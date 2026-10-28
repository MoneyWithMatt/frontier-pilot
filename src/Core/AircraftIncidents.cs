namespace FrontierAirportBuilder.Core;

public sealed record IncidentProfile(string Version,double HardLandingFeetPerSecond,bool Qualified);
public static class AircraftIncidents
{
    public static PhysicalAirframe RequireInspection(PhysicalAirframe a,Guid incidentId,DateTimeOffset utc,string evidence,bool confirmedCrash)
    {
        if(a.History.Any(e=>e.Id==incidentId))return a;
        var keys=confirmedCrash?new[]{"airframe","gear"}:new[]{"gear"};
        var components=a.Components.Select(c=>!c.Removed&&keys.Contains(c.Key)?c with {InspectionRequired=true}:c).ToArray();
        var faults=a.Faults.ToList();
        if(confirmedCrash && components.SingleOrDefault(c=>!c.Removed&&c.Key=="airframe") is {} frame)
            faults.Add(new(incidentId,frame.Id,"crash-inspection","Confirmed crash evidence; extent unknown",true,utc));
        return FleetOperations.Event(a with {Components=components,Faults=faults,
            Latest=confirmedCrash&&a.Latest is {} p?p with {State=AirframePositionState.Crashed}:a.Latest},incidentId,utc,"Incident inspection",evidence);
    }
}

/// <summary>Only fresh contact evidence supplied by a qualified adapter can evaluate an aircraft-specific landing limit.</summary>
public sealed class AircraftIncidentAccumulator
{
    private Guid session;
    private int episode;
    private bool armed;
    private double airborne, settled, worst;
    private double? previousTime;
    public PhysicalAirframe Observe(PhysicalAirframe a,QualifiedAircraftSample sample,IncidentProfile? profile,double? freshContactSinkFeetPerSecond)
    {
        if(session!=sample.Session){session=sample.Session;episode=0;armed=false;airborne=settled=worst=0;previousTime=null;}
        var delta=previousTime is {} t?sample.SimulatorSeconds-t:0;previousTime=sample.SimulatorSeconds;
        if(!sample.Initialized||sample.Paused||sample.Slew||delta<=0||delta>10){armed=false;airborne=settled=worst=0;return a;}
        Guid Id(string kind)=>new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(session+":"+episode+":"+kind))[..16]);
        if(sample.CrashConfirmed)return AircraftIncidents.RequireInspection(a,Id("crash"),sample.Position.ObservedUtc,"Qualified adapter confirmed crash; damage extent requires inspection.",true);
        if(!sample.Position.IsOnGround){airborne+=delta;settled=0;if(airborne>=10)armed=true;return a;}
        if(!armed)return a;
        if(sample.DrySurface!=true||!sample.HeightQualified){armed=false;airborne=settled=worst=0;return a;}
        if(freshContactSinkFeetPerSecond is {} sink && double.IsFinite(sink))worst=Math.Max(worst,Math.Abs(sink));
        settled+=delta;if(settled<5)return a;
        if(profile is {Qualified:true} && double.IsFinite(profile.HardLandingFeetPerSecond) && profile.HardLandingFeetPerSecond>0 && worst>=profile.HardLandingFeetPerSecond)
            a=AircraftIncidents.RequireInspection(a,Id("landing"),sample.Position.ObservedUtc,$"Fresh landing episode exceeded qualified inspection limit ({profile.Version}); no component damage percentage inferred.",false);
        episode++;armed=false;airborne=settled=worst=0;return a;
    }
}
