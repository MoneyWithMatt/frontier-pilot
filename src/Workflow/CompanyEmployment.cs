using FrontierAirportBuilder.Core;
using System.Security.Cryptography;
using System.Text;

namespace FrontierAirportBuilder.Workflow;

public sealed record CompanyAircraftType(string Id,string Name,decimal PrototypeDispatchKilograms,bool Enabled=true)
{
    public override string ToString()=>Name+(Enabled?"":" (Coming Soon)");
}
public sealed record AircraftTypeBinding(string Title,string Model,string AircraftTypeId);
public static class CompanyAircraft
{
    // Dispatch fixture budgets, NOT certified payload capacity or performance limits.
    public static IReadOnlyList<CompanyAircraftType> Types {get;}=[new("c152","Cessna 152",10),new("c208","Cessna 208 Caravan",200),
        new("baron-g58","Beechcraft Baron G58",80),new("bonanza","Beechcraft Bonanza",60),new("pa28","PA-28",40),
        new("savage-cub","Savage Cub",10),new("vision-jet","Cirrus Vision Jet",40),new("h125","H125",0,false)];
    public static string? Resolve(FlightAircraftIdentity identity,IEnumerable<AircraftTypeBinding> bindings)
    {
        identity.EnsureValid();
        return bindings.SingleOrDefault(x=>x.Title==identity.Title&&x.Model==identity.Type)?.AircraftTypeId;
    }
    public static IReadOnlyList<CompanyAircraftType> Choices(CareerEmployer employer,DestinationDiagnostic destination,decimal kilograms)=>
        // Airport runway/performance suitability belongs to the pilot. Retained review lists
        // describe historical observations, not company/type or physical loading authority.
        Types.Where(x=>x.Enabled&&employer.Fleet.Contains(x.Id)).ToArray();
    public static string? CheckDispatch(FlightAircraftIdentity identity,IReadOnlyList<CargoJobDisplay> jobs,IEnumerable<AircraftTypeBinding> bindings)
    {
        var required=jobs.Where(x=>x.CompanyAircraftTypeId is not null).Select(x=>x.CompanyAircraftTypeId).Distinct().ToArray();
        if(required.Length==0)return null;
        var actual=Resolve(identity,bindings);
        if(required.Length!=1||required[0]!=actual)return "Company dispatch requires "+string.Join(", ",required)+". In Career / Settings, confirm the exact live aircraft's supported type. Mixed aircraft assignments cannot share a load.";
        return null;
    }
}

/// <summary>Employer configuration feeds the existing recurring-demand board and ledger. No mission lifecycle lives here.</summary>
public static class CompanyEmployment
{
    public static EconomyWorld Create(CareerEmployer employer,EmployerDestinationCatalogue destinations,DateTimeOffset now)
    {
        var eros=NamibiaLocationCatalogue.Load().Get(EmployedCargoPrototype.ErosId);
        var world=new EconomyWorld(EmployedCargoPrototype.WorldId,1,[eros.ToEconomicLocation()],
            [new(employer.Id,employer.Name,EconomicOwnerKind.System),new(EmployedCargoPrototype.EmployerId,"Frontier Regional Cargo (system prototype)",EconomicOwnerKind.System)],
            [new(EmployedCargoPrototype.ResourceId,"General Supplies","general-supplies","kg")],[],[],[],
            [new(EmployedCargoPrototype.PlayerId,employer.Id,EmployedCargoPrototype.Currency,0)],[],[],[]);
        return Configure(world,employer,destinations,now,false).World;
    }
    public static EconomyTransition Configure(EconomyWorld world,CareerEmployer employer,EmployerDestinationCatalogue destinations,DateTimeOffset now,bool increment=true)
    {
        world.EnsureValid();
        if(world.Players.Single(x=>x.Id==EmployedCargoPrototype.PlayerId).EmployerBusinessId!=employer.Id)
            throw new IOException("Career employer differs from the saved economy. Both files were preserved.");
        var origin=destinations.Base(employer);
        var businesses=world.Businesses.Select(x=>x.Id==employer.Id?x with {PilotHourlyRate=employer.HourlyWage}:x).ToArray();
        var locations=world.Locations.ToList();var resources=world.Resources.ToList();var needs=world.Needs.ToList();
        var definitions=world.RecurringDemand.ToList();
        var eligible=destinations.Diagnose(employer.Category).Where(x=>x.Enabled).ToArray();
        if(origin is not null)
        {
            if(!locations.Any(x=>x.Id==origin.StableId))locations.Add(origin.ToEconomicLocation());
            eligible=SelectDestinations(employer,eligible,SelectionEpoch(world,now)).ToArray();
        }
        var desired=new HashSet<string>(StringComparer.Ordinal);
        foreach(var destination in eligible)
        {
            var place=destination.Resolved!;
            if(!locations.Any(x=>x.Id==place.StableId))locations.Add(place.ToEconomicLocation() with {Status=place.VerificationState==LocationVerificationState.MsfsVerified?place.ToEconomicLocation().Status:EconomicLocationStatus.ExploratoryAirport});
            for(var i=0;i<employer.CargoMissions.Length;i++)
            {
                var resourceId=$"company-{employer.Category.ToLowerInvariant()}-{i}";
                if(!resources.Any(x=>x.Id==resourceId))resources.Add(new(resourceId,employer.CargoMissions[i],employer.Category.ToLowerInvariant(),"kg"));
                var stem=employer.Category.ToLowerInvariant()+"-"+destination.Research.Key.ToLowerInvariant()+"-"+i;
                var mass=employer.PrototypeCargoKilograms[i];
                if(CompanyAircraft.Choices(employer,destination,mass).Count==0)continue;
                var need="need-"+stem;var id="demand-"+stem;desired.Add(id);
                if(!needs.Any(x=>x.Id==need))needs.Add(new(need,place.StableId,resourceId,mass));
                var definition=new RecurringDemandDefinition(id,need,stem,origin!.StableId,place.StableId,resourceId,mass,0,
                    EmployedCargoPrototype.Currency,TimePenalty(origin,place,employer)==0?2:1,employer.Id,employer.Id,EmployedCargoPrototype.PlayerId,true);
                var index=definitions.FindIndex(x=>x.Id==id);
                if(index<0)definitions.Add(definition);else definitions[index]=definition;
            }
        }
        for(var i=0;i<definitions.Count;i++)definitions[i]=definitions[i] with {IsEnabled=desired.Contains(definitions[i].Id)};
        if(businesses.SequenceEqual(world.Businesses)&&locations.SequenceEqual(world.Locations)&&resources.SequenceEqual(world.Resources)&&needs.SequenceEqual(world.Needs)&&definitions.SequenceEqual(world.RecurringDemand))return new(world,false);
        var updated=world with {Businesses=businesses,Locations=locations,Resources=resources,Needs=needs,RecurringDemand=definitions};
        // Existing jobs, accepted shipments, inventory and financial history are untouched.
        updated=EconomyJobBoard.Maintain(updated,now,false,scheduledGeneration:true).World;
        return new((updated with {Revision=increment?world.Revision+1:world.Revision}).EnsureValid(),true);
    }
    public static double TimePenalty(NamibiaLocation origin,NamibiaLocation destination,CareerEmployer employer)
    {
        var a=origin.ApproximateCoordinates!;var b=destination.ApproximateCoordinates!;
        var minutes=Geodesy.Inverse(new(a.Latitude,a.Longitude),new(b.Latitude,b.Longitude)).DistanceMeters/1852/120*60;
        return minutes<employer.PreferredMinimumMinutes?employer.PreferredMinimumMinutes-minutes:Math.Max(0,minutes-employer.PreferredMaximumMinutes);
    }
    public static IReadOnlyList<DestinationDiagnostic> SelectDestinations(CareerEmployer employer, IEnumerable<DestinationDiagnostic> candidates, DateTimeOffset utc, int count=6)
    {
        var rows=candidates.Where(x=>x.Enabled).ToArray();
        var priority=employer.Category switch {
            "Missionary" => new[]{"FYAM","NA-0114","NA-0014"}, "Industrial" => new[]{"FYUS","FYAR"},
            "Safari" => new[]{"FYKN","FYME","FYSO","FYMO","FYDN"}, "Offshore" => count<=4?new[]{"FYAR","FYWE","FYWH"}:new[]{"FYAR","FYWE","FYWH","FYLZ","FYOG"}, _ => Array.Empty<string>() };
        var bucket=utc.UtcTicks/TimeSpan.FromHours(3).Ticks;
        string Rank(DestinationDiagnostic d)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(employer.Id+":"+bucket+":"+d.Research.Key)));
        var anchors=rows.Where(x=>priority.Contains(CuratedNamibiaDestinations.Find(x.Research.Key)?.Id??x.Research.Key));
        if(employer.Category=="Safari")anchors=anchors.OrderBy(Rank).Take(3);
        return anchors.Concat(rows.Where(x=>x.Resolved?.VerificationState==LocationVerificationState.MsfsVerified)).Concat(rows.OrderBy(Rank)).DistinctBy(x=>x.Research.Key).Take(count).ToArray();
    }
    public static DateTimeOffset SelectionEpoch(EconomyWorld world,DateTimeOffset now)
    {
        if(world.RealTime is not {} clock)return now;
        var interval=TimeSpan.FromHours(clock.OfferIntervalHours);
        return now<clock.NextOffersUtc?clock.NextOffersUtc-interval:clock.NextOffersUtc.AddTicks((now-clock.NextOffersUtc).Ticks/interval.Ticks*interval.Ticks);
    }
}
