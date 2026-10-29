namespace FrontierAirportBuilder.Core;

public enum ComponentAuthority { Unknown, Frontier, Vendor }
public enum AirframePositionState { Unknown, LastKnown, StationaryGround, Airborne, InterruptedFlight, Uncertain, Crashed }
public enum MaintenanceAction { Inspection, Service, Repair, Overhaul, Replacement }
public enum WorkOrderState { AwaitingSettlement, InProgress, AwaitingSimulatorRepair, Completed }
public enum FaultSyncState { Pending, Applied, UnableToApply, Conflict, Cleared }
public sealed record AirframeBinding(string Title, string Model, string PackageVersion, string AdapterVersion, int? EngineCount)
{
    public bool Matches(ConditionAircraftIdentity id) => Title == id.Title && Model == id.Model && (EngineCount is null || EngineCount == id.EngineCount);
    public bool Matches(AircraftTelemetry id) => Title == id.AircraftTitle && Model == id.AircraftType;
}
public sealed record AirframePosition(AircraftTelemetry Telemetry, AircraftLocationContext? Context,
    AirframePositionState State, string Source, string Confidence, Guid SessionId);
public sealed record AirframeEvent(Guid Id, DateTimeOffset Utc, string Kind, string Detail, Guid? ComponentId = null);
public sealed record ComponentLife(Guid Id, string Key, string Type, ComponentAuthority Authority,
    double? Health, double ObservedSeconds, long Starts, double Hazard, double Threshold, string ModelVersion,
    bool InspectionRequired, bool Removed = false);
public sealed record AirframeFault(Guid Id, Guid ComponentId, string Code, string Cause, bool GroundsAircraft,
    DateTimeOffset Utc, bool Resolved = false, FaultSyncState Synchronization = FaultSyncState.UnableToApply);
public sealed record MaintenanceWork(Guid Id, Guid ComponentId, MaintenanceAction Action, string Facility,
    string Payer, decimal Quote, string QuoteVersion, DateTimeOffset StartedUtc, DateTimeOffset DueUtc,
    WorkOrderState State, string[] FaultIds)
{
    public string Payee { get; init; } = "";
    public string Currency { get; init; } = "TPC";
}
public sealed record AircraftCommandIntent(Guid Id, Guid? FaultId, string Action, bool Desired, string AdapterEvidence,
    bool Attempted, FaultSyncState State, string Detail);
public sealed record CompanyAircraftBase(string Id, string Name, string Identifier, double Latitude, double Longitude);
public sealed record AirframeTransportStop(string FlightId, string LocationId, string Name, FlightPosition Arrival);
public sealed record PhysicalAirframe(AircraftConditionRecord Identity, string Registration, string Operator,
    string? CompanyTypeId, IReadOnlyList<AirframeBinding> Bindings, IReadOnlyList<ComponentLife> Components,
    IReadOnlyList<AirframeFault> Faults, IReadOnlyList<MaintenanceWork> Work, IReadOnlyList<AirframeEvent> History)
{
    public Guid Id => Identity.AirframeId;
    public string? OwnerEntityId { get; init; }
    public Guid? AssignedPlayerId { get; init; }
    public string? AllocationKey { get; init; }
    public CompanyAircraftBase? AllocatedBase { get; init; }
    public string? AllocatedTypeName { get; init; }
    public AirframeTransportStop? LastTransportStop { get; init; }
    public AirframePosition? Latest { get; init; }
    public AirframePosition? LastStationary { get; init; }
    public Guid? OpenSession { get; init; }
    public double ObservedAirborneSeconds { get; init; }
    public long ObservedLandingCycles { get; init; }
    public ConditionFrame? LastObservation { get; init; }
    public IReadOnlyList<AircraftCommandIntent> CommandIntents { get; init; } = [];
    public IReadOnlyList<string> DispatchJobIds { get; init; } = [];
    public bool LocationReconciliationRequired { get; init; }
    public string Availability => Work.Any(x => x.State != WorkOrderState.Completed) ? "In maintenance" :
        Faults.Any(x => !x.Resolved && x.GroundsAircraft) ? "Grounded" :
        Latest is null || LocationReconciliationRequired || Components.Count==0 || Components.Any(x => !x.Removed && (x.InspectionRequired || x.Health is null)) || Faults.Any(x => !x.Resolved) ? "Attention" : "Ready";
    public string LocationLabel => Latest is null ? AllocatedBase is {} b ? $"Assigned base: {b.Name} ({b.Identifier}) · company allocation, not simulator observation" : "Unknown location" :
        $"{Latest.State} · {Latest.Context?.AirportIdentifier ?? "unrecognized location"} · {Latest.Telemetry.ObservedUtc:u}";
    public string Overview => $"{Registration} · {AllocatedTypeName ?? Identity.SimulatorModel} · {Availability}\n{LocationLabel}\n" +
        (OwnerEntityId is {} owner ? $"Owned by {owner} · assigned player {AssignedPlayerId}\nCompany property; not a personal asset.\n" : "Legacy ownership/assignment unconfirmed.\n") +
        (LastTransportStop is {} stop ? $"Last qualified transport stop: {stop.Name} · {stop.Arrival.ObservedUtc:u} (not parking/spawn authority)\n" : "") +
        $"Lifetime hours: {(Identity.AirframeHours?.ToString("F1") ?? "Unknown")} · observed airborne hours: {ObservedAirborneSeconds / 3600:F2} · landings since enrolment: {ObservedLandingCycles}";
    public override string ToString() => $"{Registration} · {AllocatedTypeName ?? Identity.SimulatorModel} · {Availability} · {LocationLabel}";
}
public sealed record AircraftFleet(int SchemaVersion, long Revision, Guid? SelectedId, IReadOnlyList<PhysicalAirframe> Aircraft)
{
    public bool RandomFailuresEnabled { get; init; }
    public bool AutomaticSimulatorEffectsEnabled { get; init; }
    public static AircraftFleet Empty => new(2, 0, null, []);
    public PhysicalAirframe? Selected => Aircraft.SingleOrDefault(x => x.Id == SelectedId);
    public AircraftFleet Validate()
    {
        if (SchemaVersion is < 1 or > 2 || Revision < 0 || Aircraft is null || Aircraft.Count > 10000 ||
            Aircraft.Select(x => x.Id).Distinct().Count() != Aircraft.Count ||
            Aircraft.Select(x => x.Registration).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Aircraft.Count ||
            SelectedId is not null && Selected is null) throw new InvalidDataException("Invalid or unsupported fleet; preserve the original.");
        foreach (var a in Aircraft)
        {
            if (a.AssignedPlayerId is not null && a.OwnerEntityId is null || a.AssignedPlayerId == Guid.Empty ||
                a.AllocationKey is not null && (SchemaVersion < 2 || a.OwnerEntityId is null || a.AssignedPlayerId is null || a.CompanyTypeId is null || a.AllocatedBase is null || string.IsNullOrWhiteSpace(a.AllocatedTypeName)))
                throw new InvalidDataException("Company aircraft ownership/assignment is incomplete.");
            if (a.OwnerEntityId is {} owner) Text(owner);
            if (a.AllocationKey is {} key) Text(key);
            if (a.AllocatedBase is {} basis && (string.IsNullOrWhiteSpace(basis.Id) || string.IsNullOrWhiteSpace(basis.Identifier) ||
                !double.IsFinite(basis.Latitude) || basis.Latitude is <= -90 or >= 90 || !double.IsFinite(basis.Longitude) || basis.Longitude is < -180 or > 180))
                throw new InvalidDataException("Invalid company allocation base.");
            a.Identity.Validate(); Text(a.Registration); Text(a.Operator);
            if(a.LastTransportStop is {} stop){Text(stop.FlightId);Text(stop.LocationId);Text(stop.Name);stop.Arrival.EnsureValid();if(!stop.Arrival.IsOnGround)throw new InvalidDataException("Transport stop is not a ground arrival.");}
            if (a.Registration != a.Registration.Trim().ToUpperInvariant() || a.Bindings is null || a.Components is null || a.Faults is null || a.Work is null || a.History is null || a.CommandIntents is null || a.DispatchJobIds is null ||
                a.Components.Count>256 || a.Bindings.Count>128 || a.DispatchJobIds.Distinct().Count()!=a.DispatchJobIds.Count || a.DispatchJobIds.Any(string.IsNullOrWhiteSpace) ||
                a.ObservedLandingCycles < 0 || !Valid(a.ObservedAirborneSeconds) || a.Components.Select(x => x.Id).Distinct().Count() != a.Components.Count ||
                a.Components.Where(x => !x.Removed).Select(x => x.Key).Distinct().Count() != a.Components.Count(x => !x.Removed) ||
                a.History.Select(x => x.Id).Distinct().Count() != a.History.Count || a.Work.Select(x => x.Id).Distinct().Count() != a.Work.Count ||
                a.Faults.Select(x => x.Id).Distinct().Count() != a.Faults.Count) throw new InvalidDataException("Invalid airframe collections.");
            foreach (var b in a.Bindings) { Text(b.Title); Text(b.Model); Text(b.PackageVersion); Text(b.AdapterVersion); if (b.EngineCount is < 0 or > 16) throw new InvalidDataException("Invalid engine configuration."); }
            foreach (var c in a.Components)
                if (c.Id == Guid.Empty || string.IsNullOrWhiteSpace(c.Key) || string.IsNullOrWhiteSpace(c.Type) || string.IsNullOrWhiteSpace(c.ModelVersion) ||
                    !Enum.IsDefined(c.Authority) || c.Health is {} h && (!Valid(h) || h > 1) || !Valid(c.ObservedSeconds) || c.Starts < 0 || !Valid(c.Hazard) || !Valid(c.Threshold) || c.Threshold <= 0)
                    throw new InvalidDataException("Invalid component life.");
            foreach (var f in a.Faults) { Text(f.Code); Text(f.Cause); if (f.Id == Guid.Empty || f.Utc == default || !Enum.IsDefined(f.Synchronization) || !a.Components.Any(c => c.Id == f.ComponentId)) throw new InvalidDataException("Invalid fault."); }
            foreach (var w in a.Work)
                if (w.Id == Guid.Empty || w.Quote < 0 || w.StartedUtc == default || w.DueUtc < w.StartedUtc || !Enum.IsDefined(w.State) || !Enum.IsDefined(w.Action) ||
                    string.IsNullOrWhiteSpace(w.Payer) || string.IsNullOrWhiteSpace(w.Facility) || string.IsNullOrWhiteSpace(w.Payee) || string.IsNullOrWhiteSpace(w.QuoteVersion) || w.FaultIds is null ||
                    w.FaultIds.Any(id=>!a.Faults.Any(f=>f.Id.ToString()==id&&f.ComponentId==w.ComponentId)) || !a.Components.Any(c => c.Id == w.ComponentId)) throw new InvalidDataException("Invalid work order.");
            if(a.CommandIntents.Select(c=>c.Id).Distinct().Count()!=a.CommandIntents.Count)throw new InvalidDataException("Duplicate command intent.");
            foreach(var command in a.CommandIntents)
                if(command.Id==Guid.Empty || string.IsNullOrWhiteSpace(command.Action) || !Enum.IsDefined(command.State) || command.FaultId is {} faultId && !a.Faults.Any(f=>f.Id==faultId))throw new InvalidDataException("Invalid command intent.");
            foreach (var e in a.History) { Text(e.Kind); Text(e.Detail); if(e.Id == Guid.Empty || e.Utc == default) throw new InvalidDataException("Invalid event."); }
            foreach (var pos in new[] { a.Latest, a.LastStationary }.OfType<AirframePosition>())
            { pos.Telemetry.EnsureValid(); pos.Context?.EnsureValid(); Text(pos.Source); Text(pos.Confidence); if(pos.SessionId == Guid.Empty || !Enum.IsDefined(pos.State) || pos.Context is {} context && context.ObservedUtc!=pos.Telemetry.ObservedUtc) throw new InvalidDataException("Invalid location."); }
        }
        if (Aircraft.Where(a=>a.AllocationKey is not null).Select(a=>a.AllocationKey).Distinct().Count()!=Aircraft.Count(a=>a.AllocationKey is not null))
            throw new InvalidDataException("Duplicate company aircraft allocation.");
        return this;
    }
    private static bool Valid(double value) => double.IsFinite(value) && value >= 0;
    private static void Text(string value) { if(string.IsNullOrWhiteSpace(value) || value.Length > 1024) throw new InvalidDataException("Missing or excessive fleet text."); }
}

public static class FleetOperations
{
    public static PhysicalAirframe EstablishBaseline(PhysicalAirframe a,DateTimeOffset utc)
    {
        if(a.Components.Count==0 || a.Components.Any(c=>c.Health is not null || c.Authority==ComponentAuthority.Vendor) || a.Faults.Any(f=>!f.Resolved) || a.Work.Any(w=>w.State!=WorkOrderState.Completed))
            throw new InvalidOperationException("Baseline requires discovered unknown components, no active fault or work, and no vendor authority. It cannot reset existing health.");
        return Event(a with {Components=a.Components.Select(c=>c with {Health=1,Authority=ComponentAuthority.Frontier,InspectionRequired=false,ModelVersion="user-declared-baseline-v1"}).ToArray()},
            Guid.NewGuid(),utc,"Explicit baseline","Player declared current components healthy for Frontier modelling. Historical hours remain Unknown; simulator state unchanged.");
    }
    public static PhysicalAirframe Enrol(string registration, string owner, AircraftTelemetry aircraft, string package, string? companyType, DateTimeOffset utc)
    {
        aircraft.EnsureValid();
        var identity = AircraftConditionRecord.Create(registration.Trim().ToUpperInvariant(), aircraft.AircraftTitle, aircraft.AircraftType, package);
        return new(identity, identity.Label, owner, companyType, [new(aircraft.AircraftTitle, aircraft.AircraftType, package, "standard-simvars-v0", null)], [], [], [],
            [new(Guid.NewGuid(), utc, "Enrolled", "Existing aircraft: historical hours, component condition and location remain unknown until observed.")]);
    }
    public static PhysicalAirframe Event(PhysicalAirframe a, Guid id, DateTimeOffset utc, string kind, string detail, Guid? component = null) =>
        a.History.Any(x => x.Id == id) ? a : a with { History = a.History.Append(new(id, utc, kind, detail, component)).ToArray() };
    public static PhysicalAirframe Rename(PhysicalAirframe a, string registration, DateTimeOffset utc) =>
        Event(a with { Registration = registration.Trim().ToUpperInvariant() }, Guid.NewGuid(), utc, "Registration", $"Changed from {a.Registration} to {registration.Trim().ToUpperInvariant()}; physical identity retained.");
    public static ComponentLife Component(string key, string type) => new(Guid.NewGuid(), key, type, ComponentAuthority.Unknown, null, 0, 0, 0,
        -Math.Log(Math.Max(double.Epsilon, Random.Shared.NextDouble())), "unqualified-v1", true);
    public static PhysicalAirframe DiscoverComponents(PhysicalAirframe a, int engines)
    {
        var components = a.Components.ToList();
        foreach (var key in Enumerable.Range(1, engines).Select(i => "engine:" + i).Concat(["brake:left", "brake:right", "gear", "electrical", "airframe"]))
            if (!components.Any(x => x.Key == key && !x.Removed)) components.Add(Component(key, key.Split(':')[0]));
        return a with { Components = components };
    }
}

/// <summary>Validated adapter output, not raw SimVar receipt. Unknown pause/surface/initialization must not qualify.</summary>
public sealed record QualifiedAircraftSample(Guid Session, AircraftTelemetry Position, AircraftLocationContext? Context,
    double SimulatorSeconds, bool Initialized, bool Paused, bool Slew, bool? DrySurface, bool HeightQualified,
    IReadOnlyList<bool?> EngineRunning, bool CrashConfirmed = false);
public sealed record ComponentWearProfile(string Version, string Type, double WearPerHour, double HazardPerHour, bool Qualified);

/// <summary>Empirically qualified package/version adapter. Standard transport receipt alone cannot implement this contract.</summary>
public interface IAircraftOperationalAdapter
{
    string EvidenceId { get; }
    bool Matches(PhysicalAirframe aircraft,ConditionConnectionInfo connection);
    IReadOnlyList<ComponentWearProfile> WearProfiles { get; }
    QualifiedAircraftSample? Qualify(ConditionFrame frame,TrackingWorldState tracking,AircraftLocationContext? location,Guid session);
}

/// <summary>One per live session. Durable counters/thresholds live on the airframe; continuity never crosses sessions.</summary>
public sealed class AircraftExposureAccumulator
{
    private QualifiedAircraftSample? previous;
    private double groundSeconds, airborneSeconds;
    private bool landingArmed;
    public void Reset() { previous = null; groundSeconds = airborneSeconds = 0; landingArmed = false; }
    public PhysicalAirframe Observe(PhysicalAirframe a, QualifiedAircraftSample s, IReadOnlyList<ComponentWearProfile> profiles, bool randomFailures)
    {
        s.Position.EnsureValid(); var p = previous;
        if(p is not null && p.Session == s.Session && s.Position.ObservedUtc <= p.Position.ObservedUtc) return a;
        previous = s;
        var dt = p is null ? 0 : s.SimulatorSeconds - p.SimulatorSeconds;
        var wall = p is null ? 0 : (s.Position.ObservedUtc - p.Position.ObservedUtc).TotalSeconds;
        if (!s.Initialized || s.Paused || s.Slew || !double.IsFinite(s.SimulatorSeconds) || p is null || !p.Initialized || p.Paused || p.Slew ||
            p.Session != s.Session || dt <= 0 || dt > 1280 || wall <= 0 || wall > 10 || dt > wall * 128 + 1)
        { groundSeconds = airborneSeconds = 0; landingArmed = false; return a; }
        if (WorldTracking.DistanceMeters(p.Position.Latitude,p.Position.Longitude,s.Position.Latitude,s.Position.Longitude) > Math.Max(1000, dt * 1000))
        { Reset(); return FleetOperations.Event(a, Guid.NewGuid(), s.Position.ObservedUtc, "Discontinuity", "Position jump; no exposure or flight evidence attributed."); }
        if (!s.Position.IsOnGround) { airborneSeconds += dt; groundSeconds = 0; if(airborneSeconds >= 10) landingArmed = true; }
        else if (s.DrySurface == true && s.HeightQualified) groundSeconds += dt;
        else { groundSeconds = 0; landingArmed = false; }
        if (landingArmed && groundSeconds >= 5)
        { a = FleetOperations.Event(a with { ObservedLandingCycles = a.ObservedLandingCycles + 1 }, Guid.NewGuid(), s.Position.ObservedUtc, "Landing", "Fresh airborne episode settled on qualified dry contact; not delivery evidence."); landingArmed = false; airborneSeconds = 0; }
        var faults = a.Faults.ToList();
        var components = a.Components.Select(c =>
        {
            if (c.Removed) return c;
            bool running = c.Key.StartsWith("engine:") && int.TryParse(c.Key.Split(':')[1], out var n) && n <= s.EngineRunning.Count && n <= p.EngineRunning.Count && s.EngineRunning[n-1] == true && p.EngineRunning[n-1] == true;
            var seconds = running || c.Type == "airframe" && !p.Position.IsOnGround && !s.Position.IsOnGround ? dt : 0;
            var starts = c.Key.StartsWith("engine:") && int.TryParse(c.Key.Split(':')[1], out var i) && i <= s.EngineRunning.Count && i <= p.EngineRunning.Count && p.EngineRunning[i-1] == false && s.EngineRunning[i-1] == true ? 1 : 0;
            c = c with { ObservedSeconds = c.ObservedSeconds + seconds, Starts = c.Starts + starts };
            var profile = profiles.SingleOrDefault(x => x.Type == c.Type && x.Qualified);
            if (profile is null || c.Authority != ComponentAuthority.Frontier || c.Health is null) return c;
            if (profile.WearPerHour < 0 || profile.HazardPerHour < 0 || !double.IsFinite(profile.WearPerHour) || !double.IsFinite(profile.HazardPerHour)) throw new InvalidDataException("Invalid wear calibration.");
            var health = Math.Max(0, c.Health.Value - seconds / 3600 * profile.WearPerHour);
            // Trapezoid integration gives frequency-independent exposure for linear normal wear.
            var activeRiskFault=faults.Any(f => f.ComponentId == c.Id && !f.Resolved && f.Code == "condition-risk");
            var risk = c.Hazard + (randomFailures && !activeRiskFault ? seconds / 3600 * profile.HazardPerHour * (1 - (health + c.Health.Value) / 2) : 0);
            if (randomFailures && risk >= c.Threshold && !faults.Any(f => f.ComponentId == c.Id && !f.Resolved && f.Code == "condition-risk"))
            {
                risk=c.Threshold;
                faults.Add(new(new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(c.Id+":"+c.Threshold.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))[..16]), c.Id, "condition-risk", "Qualified condition model " + profile.Version, true, s.Position.ObservedUtc));
            }
            return c with { Health = health, Hazard = risk, ModelVersion = profile.Version };
        }).ToArray();
        foreach(var fault in faults.Where(f=>!a.Faults.Any(old=>old.Id==f.Id)))
            a=FleetOperations.Event(a,fault.Id,s.Position.ObservedUtc,"Condition fault",fault.Cause,fault.ComponentId);
        return a with { Components = components, Faults = faults, ObservedAirborneSeconds = a.ObservedAirborneSeconds + (!p.Position.IsOnGround && !s.Position.IsOnGround ? dt : 0) };
    }
}

/// <summary>Qualified contact history; preserves altitude rather than flattening elevated locations to terrain.</summary>
public sealed class AircraftLocationAccumulator
{
    private QualifiedAircraftSample? previous;
    private double stationarySeconds;
    public void Reset(){previous=null;stationarySeconds=0;}
    public PhysicalAirframe Observe(PhysicalAirframe a,QualifiedAircraftSample sample)
    {
        var t=sample.Position.EnsureValid();var prior=previous;
        if(prior is not null && prior.Session==sample.Session && t.ObservedUtc<=prior.Position.ObservedUtc)return a;
        previous=sample;
        if(!sample.Initialized||sample.Slew){stationarySeconds=0;return a;}
        var elapsed=prior is null?0:(t.ObservedUtc-prior.Position.ObservedUtc).TotalSeconds;
        var distance=prior is null?0:WorldTracking.DistanceMeters(prior.Position.Latitude,prior.Position.Longitude,t.Latitude,t.Longitude);
        var stable=prior is not null&&prior.Initialized&&!prior.Slew&&prior.Session==sample.Session&&elapsed is >0 and <=3;
        var qualified=sample.DrySurface==true&&sample.HeightQualified&&sample.Context is not null&&t.IsOnGround&&t.GroundSpeedKnots<=1&&!sample.CrashConfirmed;
        if(qualified&&stable&&prior!.Position.IsOnGround&&prior.Position.GroundSpeedKnots<=1&&distance<5)stationarySeconds+=elapsed;else stationarySeconds=0;
        var state=sample.CrashConfirmed?AirframePositionState.Crashed:!t.IsOnGround?AirframePositionState.Airborne:
            sample.DrySurface!=true?AirframePositionState.Uncertain:stationarySeconds>=5?AirframePositionState.StationaryGround:AirframePositionState.LastKnown;
        if(stable&&distance>Math.Max(1000,elapsed*1000))
        {state=AirframePositionState.Uncertain;stationarySeconds=0;a=FleetOperations.Event(a,Guid.NewGuid(),t.ObservedUtc,"Relocation observed","Position jump; no flown journey or delivery evidence.");}
        var position=new AirframePosition(t,sample.Context,state,"qualified observation adapter","Last known; not spawn authority",sample.Session);
        return a with {Latest=position,LastStationary=state==AirframePositionState.StationaryGround?position:a.LastStationary,OpenSession=sample.Session};
    }
}
