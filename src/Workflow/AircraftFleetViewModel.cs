using System.ComponentModel;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed record FleetAircraftRow(PhysicalAirframe Aircraft)
{
    public Guid Id=>Aircraft.Id;
    public string Registration=>Aircraft.Registration;
    public string Type=>Aircraft.AllocatedTypeName??Aircraft.Identity.SimulatorModel;
    public string Status=>CompanyAircraft.Types.SingleOrDefault(t=>t.Id==Aircraft.CompanyTypeId)?.Enabled==false ? "Coming Soon · mission capability disabled" : Aircraft.Availability;
    public string Location=>Aircraft.LocationLabel;
}
public sealed class AircraftFleetViewModel : INotifyPropertyChanged
{
    private readonly AircraftFleetStore? store;
    private readonly Func<IAircraftConditionSource> sourceFactory;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private readonly object gate = new();
    private AircraftFleet fleet = AircraftFleet.Empty;
    private TrackingWorldState? world;
    private AircraftLocationContext? location;
    private CancellationTokenSource? cancellation;
    private Task observer = Task.CompletedTask;
    private Guid? observingId;
    private Guid session;
    private uint observingObject;
    private bool bindingConfirmationRequired;
    private bool qualifiedAdapterActive;
    private DateTimeOffset stableSince, savedUtc;
    private AircraftTelemetry? previousPosition;
    private bool dirty, closed, blocked;
    private readonly AircraftExposureAccumulator exposure = new();
    private readonly AircraftLocationAccumulator qualifiedLocation = new();
    private long? discontinuity;
    private AircraftMaintenanceService? maintenance;
    private bool maintenanceBusy;
    private string status = "Select a physical aircraft. Unmatched telemetry stays observation-only.";
    private string registration = "", package = "";
    private CareerEmployer? employer;
    private Guid? playerId;
    private IReadOnlyList<string> allocationProblems=[];
    private bool Authorized(PhysicalAirframe a)=>employer is null ? a.OwnerEntityId is null :
        a.OwnerEntityId==employer.Id&&a.AssignedPlayerId==playerId&&employer.Fleet.Contains(a.CompanyTypeId!);
    public bool CanEnrolPersonal => employer is null;
    public string AllocationStatus => employer is null ? "Legacy fleet; ownership is not inferred." :
        $"{employer.Name} owns this allocation; assigned to your persistent player profile. No purchase or registration required.\n"+string.Join("\n",allocationProblems);
    public string SelectedInstructions => Selected is not {} a ? "Select an assigned aircraft." :
        a.CompanyTypeId is {} type && CompanyAircraft.Types.SingleOrDefault(x=>x.Id==type)?.Enabled==false ? "This aircraft is allocated but its mission capability is disabled / Coming Soon." :
        $"Load {a.AllocatedTypeName??a.Identity.SimulatorModel} in MSFS at {(a.Latest is null ? a.AllocatedBase?.Identifier??"its recorded location" : a.LocationLabel)}. Confirm its exact company type in Career / Settings. Selection never moves an aircraft. Unknown historical condition/hours are not assumed healthy or zero.";
    public Func<string>? MissionEligibilityProvider {get;set;}
    public string MissionEligibility=>MissionEligibilityProvider?.Invoke()??"Select a job in Jobs to inspect its eligible aircraft types.";
    public void RefreshMissionEligibility()=>Notify();
    public void ReconcileTransportArrivals(EconomyWorld state)
    {
        lock(gate)
        {
            if(blocked||closed||maintenanceBusy)return;
            try
            {
                foreach(var receipt in state.CargoAirframes.SelectMany(m=>m.Legs.Where(l=>l.Flight?.DeliveryProof is not null).Select(l=>(Id:m.AirframeId,Destination:l.Destination,Flight:l.Flight!)))
                    .Concat(state.PassengerTransfers.Where(t=>t.AirframeId is not null&&t.Flight?.DeliveryProof is not null).Select(t=>(Id:t.AirframeId!.Value,Destination:t.Destination,Flight:t.Flight!))).OrderBy(r=>r.Flight.DeliveryProof!.ReadyUtc))
                {
                    var a=fleet.Aircraft.SingleOrDefault(x=>x.Id==receipt.Id);var proof=receipt.Flight.DeliveryProof!;
                    if(a is null||!Authorized(a)||a.LastTransportStop?.Arrival.ObservedUtc>=proof.ReadyUtc || !a.Bindings.Any(b=>b.Title==receipt.Flight.Aircraft.Title&&b.Model==receipt.Flight.Aircraft.Type))continue;
                    var p=proof.ArrivalPosition;
                    a=a with{LastTransportStop=new(receipt.Flight.Id,receipt.Destination.Id,receipt.Destination.Name,p)};
                    if(a.Latest is null||a.Latest.Telemetry.ObservedUtc<=p.ObservedUtc)
                    {
                        var t=new AircraftTelemetry(p.ObservedUtc,receipt.Flight.Aircraft.Title,receipt.Flight.Aircraft.Type,p.Latitude,p.Longitude,p.AltitudeFeet,p.TrueHeadingDegrees,p.GroundSpeedKnots,p.IsOnGround);
                        a=a with{Latest=new(t,null,AirframePositionState.LastKnown,"tracker-qualified transport arrival","Last known; not qualified parking or spawn permission",a.OpenSession??Guid.NewGuid())};
                    }
                    var eventId=new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("transport-stop/"+receipt.Flight.Id))[..16]);
                    Update(FleetOperations.Event(a,eventId,p.ObservedUtc,"Transport arrival",receipt.Destination.Name+"; existing tracker proof recorded. No new flight evidence, hours, money or simulator placement."),true);
                }
            }
            catch(Exception ex){status="Transport arrival projection pending: "+ex.Message;}
        }
        Notify();
    }
    public IReadOnlyList<PhysicalAirframe> LegacyCandidates=>fleet.Aircraft.Where(a=>employer is not null&&a.AssignedPlayerId is null&&(a.OwnerEntityId is null&&a.Operator==employer.Id||a.OwnerEntityId==employer.Id)).ToArray();
    public PhysicalAirframe? SelectedLegacyCandidate {get;set;}
    public CompanyAircraftType? SelectedLegacyType {get;set;}
    public IReadOnlyList<CompanyAircraftType> EmployerTypes=>CompanyAircraft.Types.Where(t=>employer?.Fleet.Contains(t.Id)==true).ToArray();
    public void ConfigureEmployment(Guid player, CareerEmployer company, IReadOnlyList<string>? problems=null)
    {
        lock(gate)
        {
            EndSession("Employment authorization configured");playerId=player;employer=company;OperatorId=company.Id;allocationProblems=problems??[];
            if(!blocked && (fleet.Selected is not {} current || !Authorized(current)))
            {fleet=fleet with{SelectedId=Aircraft.FirstOrDefault(a=>CompanyAircraft.Types.Any(t=>t.Id==a.CompanyTypeId&&t.Enabled))?.Id??Aircraft.FirstOrDefault()?.Id};dirty=true;Save();}
        }
        Notify();
    }
    public void ConfirmLegacyAssignment()=>Run(()=>
    {
        if(employer is null||playerId is null||SelectedLegacyCandidate is not {} candidate)throw new InvalidOperationException("Choose a preserved legacy company record.");
        var updated=EmployerFleetProvisioning.ConfirmLegacy(candidate,playerId.Value,employer,DateTimeOffset.UtcNow,SelectedLegacyType?.Id);
        if(fleet.Aircraft.Any(a=>a.Id!=candidate.Id&&a.OwnerEntityId==employer.Id&&a.AssignedPlayerId==playerId&&a.CompanyTypeId==updated.CompanyTypeId))throw new InvalidOperationException("An allocated aircraft already exists for this type; nothing replaced.");
        fleet=fleet with{SchemaVersion=2,Aircraft=fleet.Aircraft.Select(a=>a.Id==updated.Id?updated:a).ToArray(),SelectedId=updated.Id};dirty=true;Save();
        var provisioned=EmployerFleetProvisioning.Provision(fleet,playerId.Value,employer,DateTimeOffset.UtcNow);allocationProblems=provisioned.Reconciliation;
        if(!fleet.Aircraft.SequenceEqual(provisioned.Fleet.Aircraft)){fleet=provisioned.Fleet;dirty=true;Save();}
        status="Legacy company assignment confirmed; all prior aircraft history retained.";
    });
    public AircraftFleetViewModel(AircraftFleetStore? store, Func<IAircraftConditionSource> sourceFactory)
    {
        this.store = store; this.sourceFactory = sourceFactory;
        EnrolCommand = new(() => Run(Enrol), () => CanEnrolPersonal && !blocked && !closed && world is not null && Registration.Trim().Length > 0 && PackageVersion.Trim().Length > 0);
        RenameCommand = new(() => Run(() => Update(FleetOperations.Rename(Selected!, Registration, DateTimeOffset.UtcNow), true)), () => !blocked && Selected is not null && Registration.Trim().Length > 0);
        BindCommand = new(() => Run(Bind), () => !blocked && Selected is not null && world is not null && PackageVersion.Trim().Length > 0);
        ReturnCommand = new(() => { status = ReturnInstructions; Notify(); }, () => Selected?.Latest is not null);
        try
        {
            fleet = store?.Load() ?? AircraftFleet.Empty;
            var recovered = fleet.Aircraft.Select(a => a.OpenSession is null ? a : FleetOperations.Event(a with { OpenSession = null,
                Latest = a.Latest is {} p ? p with { State = p.Telemetry.IsOnGround ? AirframePositionState.Uncertain : AirframePositionState.InterruptedFlight } : null },
                Guid.NewGuid(), DateTimeOffset.UtcNow, "Interrupted session", "Previous observation session did not close; saved placement remains last known.")).ToArray();
            if (!recovered.SequenceEqual(fleet.Aircraft)) { fleet = fleet with { Aircraft = recovered }; dirty = true; Save(); }
        }
        catch (Exception e) { blocked = true; status = e.Message; }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? FleetChanged;
    public IReadOnlyList<PhysicalAirframe> Aircraft => fleet.Aircraft.Where(Authorized).ToArray();
    public IReadOnlyList<FleetAircraftRow> AircraftRows=>Aircraft.Select(a=>new FleetAircraftRow(a)).ToArray();
    public Guid? SelectedAirframeId
    {
        get=>fleet.SelectedId;
        set { if(value is {} id && fleet.Aircraft.SingleOrDefault(a=>a.Id==id) is {} a) Selected=a; }
    }
    public PhysicalAirframe? Selected
    {
        get => fleet.Selected is {} selected&&Authorized(selected)?selected:null;
        set { Run(()=>{ if(value is not null&&!Authorized(value))throw new InvalidOperationException("This aircraft is not assigned by your current employer.");if (value?.Id == fleet.SelectedId || blocked || closed || maintenanceBusy) return; EndSession("Aircraft selection changed"); bindingConfirmationRequired=false; fleet = fleet with { SelectedId = value?.Id }; dirty = true; Save(); Reconcile(); }); }
    }
    public string Registration { get => registration; set { registration = value; Notify(); } }
    public string PackageVersion { get => package; set { package = value; Notify(); } }
    public string OperatorId { get; set; } = "player";
    public Func<AircraftTelemetry, string?>? ResolveCompanyType { get; set; }
    public Func<PhysicalAirframe,IAircraftOperationalAdapter?>? OperationalAdapter { get; init; }
    public bool HasLocations => Aircraft.Any(a=>a.Latest is not null);
    public bool CanEstablishBaseline => Selected is {} a && a.Components.Count>0 && a.Components.All(c=>c.Health is null&&c.Authority!=ComponentAuthority.Vendor) && !a.Faults.Any(f=>!f.Resolved);
    public bool CanBookMaintenance => !blocked && !closed && !maintenanceBusy && Selected?.Latest?.State==AirframePositionState.StationaryGround && SelectedComponent is not null && SelectedFacility is not null;
    public IReadOnlyList<MaintenanceFacility> Facilities { get; init; } = [];
    private MaintenanceFacility? selectedFacility;
    public MaintenanceFacility? SelectedFacility { get=>selectedFacility; set {selectedFacility=value;Notify();} }
    private ComponentLife? selectedComponent;
    public ComponentLife? SelectedComponent { get=>selectedComponent; set {selectedComponent=value;Notify();} }
    public IReadOnlyList<MaintenanceAction> MaintenanceActions { get; } = Enum.GetValues<MaintenanceAction>();
    private MaintenanceAction selectedAction;
    public MaintenanceAction SelectedAction { get => selectedAction; set {selectedAction=value;Notify();} }
    public string QuoteDescription => $"Prototype quote: {MaintenancePrototypePolicy.Quote(SelectedAction).Amount} credits · {MaintenancePrototypePolicy.Quote(SelectedAction).Downtime.TotalMinutes:0} real UTC minutes · payer: {OperatorId}. No facility is enabled without an authored capability profile.";
    public void ConfigureMaintenance(IEconomyStateStore economy) { if(store is not null)maintenance=new(store,economy); }
    public void EstablishBaseline() => Run(()=>Update(FleetOperations.EstablishBaseline(Selected!,DateTimeOffset.UtcNow),true));
    public void AcknowledgeRelocation() => Run(()=>
    {
        if(Selected is not {} a || !a.LocationReconciliationRequired)return;
        if(a.OwnerEntityId is not null)
        {
            if(world is null || DateTimeOffset.UtcNow-world.Aircraft.ObservedUtc>TimeSpan.FromSeconds(3) || !world.Aircraft.IsOnGround || world.Aircraft.GroundSpeedKnots>1 || ResolveCompanyType?.Invoke(world.Aircraft)!=a.CompanyTypeId)
                throw new InvalidOperationException("Developer recovery needs fresh stopped telemetry for the confirmed company type. No simulator aircraft is moved.");
            a=a with {Latest=new(world.Aircraft,location,AirframePositionState.Uncertain,"explicit developer relocation recovery","Unobserved move acknowledged; not a flown journey or spawn authority",a.OpenSession??Guid.NewGuid())};
        }
        Update(FleetOperations.Event(a with {LocationReconciliationRequired=false},Guid.NewGuid(),DateTimeOffset.UtcNow,
            a.OwnerEntityId is null?"Relocation acknowledged":"Developer relocation acknowledged","Player acknowledged external simulator location. No shipment, inventory, flight evidence or payment changed."),true);
    });
    public async Task BookMaintenanceAsync()
    {
        lock(gate)
        {
            try
            {
                if(blocked||closed||maintenanceBusy||Selected is not {} a||SelectedComponent is not {} c||SelectedFacility is not {} f||!Facilities.Contains(f))throw new InvalidOperationException("Select a component and an enabled facility first.");
                var observations=a.LastObservation;
                var stopped=observations is not null && DateTimeOffset.UtcNow-observations.CompletedUtc<=TimeSpan.FromSeconds(5) && observations.Aircraft.EngineCount>0 &&
                    Enumerable.Range(1,observations.Aircraft.EngineCount).All(i=>observations.Readings.Any(r=>r.Field.Key=="GENERAL ENG COMBUSTION:"+i&&r.Availability==ConditionAvailability.ReceivedUnverified&&r.Value==0));
                Update(AircraftMaintenance.Book(a,Guid.NewGuid(),c.Id,MaintenancePrototypePolicy.Quote(SelectedAction),f,OperatorId,DateTimeOffset.UtcNow,stopped),true);
            }
            catch(Exception e){status=e.Message;Notify();return;}
        }
        await AdvanceMaintenanceAsync(DateTimeOffset.UtcNow);
    }
    public async Task AdvanceMaintenanceAsync(DateTimeOffset utc)
    {
        lock(gate)
        {
            if(maintenance is null||maintenanceBusy||blocked||closed||!fleet.Aircraft.Any(a=>a.Work.Any(w=>w.State!=WorkOrderState.Completed)))return;
            try{Save();}catch(Exception e){status=e.Message;Notify();return;}
            maintenanceBusy=true;
        }
        try{var result=await maintenance.ReconcileAsync(utc);lock(gate){fleet=result;status="Maintenance reconciled against persisted UTC deadlines.";}}
        catch(Exception e){lock(gate){status="Maintenance pending: "+e.Message;try{fleet=store!.Load();}catch{blocked=true;}}}
        finally{lock(gate){maintenanceBusy=false;exposure.Reset();previousPosition=null;}Notify();}
    }
    public string Status => status;
    public string SaveStatus => blocked ? "Save blocked · previous durable snapshot preserved" : dirty ? "Automatic checkpoint pending (up to 10 seconds)" :
        savedUtc==default ? $"Loaded fleet revision {fleet.Revision}" : $"Saved fleet revision {fleet.Revision} · {savedUtc:u}";
    public string ConnectionStatus => Selected is {OwnerEntityId:not null,Latest:null} ? "ASSIGNED BASE · no simulator observation yet" : world is not null && Selected is {} a && observingId==a.Id && !bindingConfirmationRequired && DateTimeOffset.UtcNow-world.Aircraft.ObservedUtc<=TimeSpan.FromSeconds(5)
        ? "LIVE · selected aircraft matches explicit binding" : "LAST KNOWN / observation-only";
    public string Overview => Selected?.Overview ?? "No physical aircraft enrolled. Enrol the loaded aircraft explicitly; historical condition remains Unknown.";
    public string Components => Selected is null ? "" : string.Join("\n", Selected.Components.Where(x => !x.Removed).Select(x => $"{x.Key} · {x.Authority} · {(x.Health is {} h ? h.ToString("P1") : "Unknown")} · {x.ObservedSeconds / 3600:F2} observed hours · {(x.InspectionRequired ? "Inspection required" : "No inspection flag")}"));
    public string History => Selected is null ? "" : string.Join("\n", Selected.History.Reverse().Select(x => $"{x.Utc:u} · {x.Kind} · {x.Detail}"));
    public string MaintenanceSummary => Selected is null ? "Select an aircraft." : string.Join("\n", Selected.Components.Where(c=>!c.Removed && c.InspectionRequired).Select(c=>c.Key + " · Inspection required · condition unknown")) + "\n" + string.Join("\n", Selected.Work.Select(w=>$"{w.Action} · {w.State} · {w.Payer} · due {w.DueUtc:u}"));
    public string Observations => Selected?.LastObservation is {} f ? $"Last condition receipt {f.CompletedUtc:u} · {f.Readings.Count} fields · compatibility unverified. Exposure requires running/unpaused simulator time; wear profiles and fault effects are not qualified." : "No condition observations yet.";
    public string ReturnInstructions => Selected?.Latest is not {} p ? Selected?.AllocatedBase is {} basis ? $"Company allocation at {basis.Name} ({basis.Identifier}); load the selected type there. No simulator position was observed or written. Automatic placement is unavailable." : "Location unknown." :
        $"Return to {Selected.Registration}: {p.Telemetry.Latitude:F6}, {p.Telemetry.Longitude:F6}; altitude {p.Telemetry.AltitudeFeet:F1} ft; heading {p.Telemetry.TrueHeadingDegrees:F1}°. {p.State}.\nLoad the correct aircraft and location in MSFS, then select this airframe. Automatic placement is unavailable: no verified terrain/platform initialization adapter. An airborne or uncertain point is not a safe spawn. Cargo is not relocated.";
    public ActionCommand EnrolCommand { get; }
    public ActionCommand RenameCommand { get; }
    public ActionCommand BindCommand { get; }
    public ActionCommand ReturnCommand { get; }
    private void Run(Action action) { lock(gate) { try { if(blocked||closed||maintenanceBusy)return;action(); } catch(Exception e) { status = e.Message; } } Notify(); }
    private void Enrol()
    {
        if (world is null) throw new InvalidOperationException("Connect to the loaded aircraft first.");
        var a = FleetOperations.Enrol(Registration, OperatorId, world.Aircraft, PackageVersion.Trim(), ResolveCompanyType?.Invoke(world.Aircraft), DateTimeOffset.UtcNow);
        (fleet with { Aircraft = fleet.Aircraft.Append(a).ToArray(), SelectedId = a.Id }).Validate();
        EndSession("New airframe selected"); fleet = fleet with { Aircraft = fleet.Aircraft.Append(a).ToArray(), SelectedId = a.Id }; dirty = true; Save(); Reconcile();
    }
    private void Bind()
    {
        if(Selected is {} company && company.OwnerEntityId is not null && (ResolveCompanyType?.Invoke(world!.Aircraft)!=company.CompanyTypeId || !CompanyAircraft.Types.Any(t=>t.Id==company.CompanyTypeId&&t.Enabled)))
            throw new InvalidOperationException("The exact simulator type must match this enabled company aircraft before binding.");
        if(Selected is {} allocated && allocated.OwnerEntityId is not null && !AtRecordedLocation(allocated,world!.Aircraft))
            throw new InvalidOperationException("Load the company aircraft at its recorded location before binding. Binding cannot relocate it.");
        EndSession("Binding changed");
        bindingConfirmationRequired=false;
        var a = Selected!; var t = world!.Aircraft;
        if (!a.Bindings.Any(b => b.Matches(t)))
            a = FleetOperations.Event(a with { Bindings = a.Bindings.Append(new(t.AircraftTitle,t.AircraftType,PackageVersion.Trim(),"standard-simvars-v0",null)).ToArray() },
                Guid.NewGuid(),DateTimeOffset.UtcNow,"Explicit binding","User associated this exact loaded title/model/package declaration with the selected physical airframe. Compatibility remains unverified.");
        Update(a,true); Reconcile();
    }
    private static bool AtRecordedLocation(PhysicalAirframe a,AircraftTelemetry t)
    {
        var latitude=a.Latest?.Telemetry.Latitude??a.AllocatedBase?.Latitude;var longitude=a.Latest?.Telemetry.Longitude??a.AllocatedBase?.Longitude;
        return latitude is null || longitude is null || WorldTracking.DistanceMeters(latitude.Value,longitude.Value,t.Latitude,t.Longitude)<=2778;
    }
    public void ObserveTracking(TrackingWorldState? sample, AircraftLocationContext? recognized)
    {
        lock(gate)
        {
            if (closed || blocked || maintenanceBusy) return;
            try
            {
                sample?.EnsureValid(); world = sample; location = recognized;
                if(sample is not null && Selected is {} company && company.OwnerEntityId is not null)
                {
                    if(!CompanyAircraft.Types.Any(t=>t.Id==company.CompanyTypeId&&t.Enabled) || ResolveCompanyType?.Invoke(sample.Aircraft)!=company.CompanyTypeId)
                    {EndSession("Company type mismatch");status="Observation-only: load the selected enabled company type and confirm its exact simulator identity in Career / Settings.";Notify();return;}
                    var last=company.Latest?.Telemetry;
                    var lat=last?.Latitude??company.AllocatedBase?.Latitude;var lon=last?.Longitude??company.AllocatedBase?.Longitude;
                    if(previousPosition is null && lat is {} latitude && lon is {} longitude && WorldTracking.DistanceMeters(latitude,longitude,sample.Aircraft.Latitude,sample.Aircraft.Longitude)>2778)
                    {
                        EndSession("Unobserved company relocation");
                        if(company.Latest is not null&&!company.LocationReconciliationRequired)Update(FleetOperations.Event(Selected! with{LocationReconciliationRequired=true},Guid.NewGuid(),sample.Aircraft.ObservedUtc,"Company location conflict","Unobserved move; saved position retained pending explicit developer recovery."),true);
                        status="Company aircraft remains at its saved location. Loading another airport or selecting an airframe is not a relocation. Use only explicit developer recovery for an unobserved move.";Notify();return;
                    }
                    if(company.Bindings.Count==0 && !bindingConfirmationRequired)
                    {
                        company=FleetOperations.Event(company with {Identity=company.Identity with {SimulatorTitle=sample.Aircraft.AircraftTitle,SimulatorModel=sample.Aircraft.AircraftType},
                            Bindings=[new(sample.Aircraft.AircraftTitle,sample.Aircraft.AircraftType,"Type confirmed; package version unverified","standard-simvars-v0",null)]},
                            Guid.NewGuid(),sample.Aircraft.ObservedUtc,"Company type associated","Selected allocation matched an explicitly confirmed simulator title/model at its recorded location; no compatibility or condition inferred.");
                        Update(company,true);
                    }
                }
                if(sample is not null && observingId is not null && sample.AircraftObjectId!=observingObject)
                {EndSession("Simulator aircraft object changed");bindingConfirmationRequired=true;}
                if(bindingConfirmationRequired){status="Observation-only · aircraft object changed. Confirm the loaded livery against the selected airframe.";Notify();return;}
                if (sample is null || Selected is not {} a || !a.Bindings.Any(b => b.Matches(sample.Aircraft)))
                { EndSession("Disconnected or binding mismatch"); status = "Observation-only · select and explicitly bind the correct physical aircraft."; Notify(); return; }
                if (previousPosition is not null && (sample.Aircraft.ObservedUtc <= previousPosition.ObservedUtc || (sample.Aircraft.ObservedUtc - previousPosition.ObservedUtc).TotalSeconds > 3))
                { stableSince = sample.Aircraft.ObservedUtc; previousPosition = null; }
                Reconcile();
                a = Selected!;
                var t = sample.Aircraft;
                if (previousPosition is null)
                {
                    stableSince = t.ObservedUtc;
                    if(a.Latest is {} old && !a.LocationReconciliationRequired && WorldTracking.DistanceMeters(old.Telemetry.Latitude,old.Telemetry.Longitude,t.Latitude,t.Longitude)>2778)
                    {
                        a=FleetOperations.Event(a with {LocationReconciliationRequired=true},Guid.NewGuid(),t.ObservedUtc,"Location reconciliation required","Reconnected away from the saved location. Unobserved movement is not a flown journey.");
                        Update(a,true);
                    }
                }
                var jumped = previousPosition is {} prior && WorldTracking.DistanceMeters(prior.Latitude,prior.Longitude,t.Latitude,t.Longitude) > Math.Max(1000,(t.ObservedUtc-prior.ObservedUtc).TotalSeconds*1000);
                if (jumped) { stableSince=t.ObservedUtc; a=FleetOperations.Event(a with {LocationReconciliationRequired=true},Guid.NewGuid(),t.ObservedUtc,"Relocation observed","Position discontinuity; requires reconciliation, never flight/delivery evidence."); Update(a,true); }
                previousPosition=t;
                // Stable tracking positions can be retained as last known; ground contact alone cannot certify parking.
                if ((t.ObservedUtc-stableSince).TotalSeconds < 10) return;
                if(qualifiedAdapterActive && a.LastObservation is {} qualifiedReceipt && Math.Abs((t.ObservedUtc-qualifiedReceipt.CompletedUtc).TotalSeconds)<=3)return;
                if(a.LastObservation is {} condition && (condition.Runtime?.Running==false || condition.Runtime?.Paused==true || condition.Readings.Any(r=>r.Field.Key=="IS SLEW ACTIVE"&&r.Value==1)))
                {stableSince=t.ObservedUtc;exposure.Reset();return;}
                a=Selected! with { History=a.History };
                var pos=new AirframePosition(t,recognized,a.LocationReconciliationRequired?AirframePositionState.Uncertain:t.IsOnGround?AirframePositionState.LastKnown:AirframePositionState.Airborne,"validated Tracking", "Last known; surface/initialization not qualified",session);
                var transition=a.Latest?.Telemetry.IsOnGround!=t.IsOnGround;
                Update(a with {Latest=pos,OpenSession=session}, transition || DateTimeOffset.UtcNow-savedUtc>=TimeSpan.FromSeconds(10));
            }
            catch(Exception e) { blocked=true; cancellation?.Cancel(); status="Fleet observation stopped: "+e.Message; }
        }
        Notify();
    }
    private void Reconcile()
    {
        if (closed || blocked || bindingConfirmationRequired || world is null || Selected is not {} a || !a.Bindings.Any(b=>b.Matches(world.Aircraft))) return;
        if(a.OwnerEntityId is not null && (!CompanyAircraft.Types.Any(t=>t.Id==a.CompanyTypeId&&t.Enabled) || ResolveCompanyType?.Invoke(world.Aircraft)!=a.CompanyTypeId || previousPosition is null&&!AtRecordedLocation(a,world.Aircraft)))return;
        if (observingId==a.Id) return;
        if (!observer.IsCompleted) return; // cancellation must close old native connection before replacement
        observingId=a.Id; session=Guid.NewGuid(); cancellation?.Dispose(); cancellation=new();
        exposure.Reset(); qualifiedLocation.Reset(); discontinuity=null;
        var id=a.Id; var expected=world.AircraftObjectId; observingObject=expected; var token=cancellation.Token;
        Update(FleetOperations.Event(a with {OpenSession=session},Guid.NewGuid(),DateTimeOffset.UtcNow,"Observation started","Automatic read-only monitoring for explicitly selected airframe."),true);
        status="Automatic observation active · condition compatibility unverified";
        observer=Task.Run(async()=>
        {
            try
            {
                var source=sourceFactory();
                await foreach(var frame in source.ReadAsync(token))
                {
                    lock(gate)
                    {
                        if(token.IsCancellationRequested||fleet.SelectedId!=id||observingId!=id) break;
                        if(maintenanceBusy)continue;
                        AircraftConditionRecorder.ValidateFrame(frame,null);
                        var current=fleet.Selected!;
                        if(frame.Aircraft.ObjectId!=expected||!current.Bindings.Any(b=>b.Matches(frame.Aircraft)))throw new IOException("Condition identity mismatch.");
                        if(current.Bindings.Any(b=>b.Title==frame.Aircraft.Title&&b.Model==frame.Aircraft.Model&&b.EngineCount is null))
                            current=FleetOperations.Event(current with {Bindings=current.Bindings.Select(b=>b.Title==frame.Aircraft.Title&&b.Model==frame.Aircraft.Model?b with {EngineCount=frame.Aircraft.EngineCount}:b).ToArray()},Guid.NewGuid(),frame.CompletedUtc,"Engine configuration observed","Pinned reported engine count for this explicit binding; not a compatibility certificate.");
                        current=FleetOperations.DiscoverComponents(current,frame.Aircraft.EngineCount) with {LastObservation=frame};
                        var t=world?.Aircraft;
                        var adapter=OperationalAdapter?.Invoke(current);
                        if(adapter is not null && !string.IsNullOrWhiteSpace(adapter.EvidenceId) && adapter.Matches(current,source.ConnectionInfo) && world is not null && adapter.Qualify(frame,world,location,session) is {} qualified && qualified.Session==session)
                        {
                            qualifiedAdapterActive=true;
                            current=qualifiedLocation.Observe(current,qualified);
                            current=exposure.Observe(current,qualified,adapter.WearProfiles,fleet.RandomFailuresEnabled);
                            Update(current,current.History.Count!=Selected!.History.Count || current.Latest?.State!=Selected.Latest?.State || DateTimeOffset.UtcNow-savedUtc>=TimeSpan.FromSeconds(10));
                            Notify();
                            continue;
                        }
                        qualifiedAdapterActive=false;
                        double? Value(string key)=>frame.Readings.SingleOrDefault(r=>r.Field.Key==key && r.Availability==ConditionAvailability.ReceivedUnverified)?.Value;
                        if(frame.Runtime is {} runtime && t is not null && Math.Abs((t.ObservedUtc-frame.CompletedUtc).TotalSeconds)<=3 &&
                            Value("SIMULATION TIME") is {} simTime && Value("IS SLEW ACTIVE") is {} slew)
                        {
                            if(discontinuity!=runtime.Discontinuity){exposure.Reset();stableSince=t.ObservedUtc;discontinuity=runtime.Discontinuity;}
                            var running=Enumerable.Range(1,frame.Aircraft.EngineCount).Select(i=>Value("GENERAL ENG COMBUSTION:"+i) is {} v?(bool?)(v==1):null).ToArray();
                            var surface=Value("SURFACE TYPE");
                            current=exposure.Observe(current,new(session,t,location,simTime,
                                runtime.Running==true && runtime.Paused is not null && (t.ObservedUtc-stableSince).TotalSeconds>=10,
                                runtime.Paused!=false,slew!=0,surface is null?null:surface==2?false:null,false,running),[],false);
                        }
                        else exposure.Reset();
                        Update(current,DateTimeOffset.UtcNow-savedUtc>=TimeSpan.FromSeconds(10));
                    }
                    Notify();
                }
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested) {}
            catch(Exception e) { lock(gate) { status="Condition observations unavailable: "+e.Message; } Notify(); }
        });
    }
    private void EndSession(string reason)
    {
        cancellation?.Cancel();
        if(observingId is {} id && fleet.Aircraft.SingleOrDefault(x=>x.Id==id) is {} a)
        {
            var p=a.Latest;
            a=FleetOperations.Event(a with {OpenSession=null,Latest=p is null?null:p with {State=a.LocationReconciliationRequired?AirframePositionState.Uncertain:p.State==AirframePositionState.Crashed?AirframePositionState.Crashed:p.Telemetry.IsOnGround?AirframePositionState.LastKnown:AirframePositionState.InterruptedFlight}},
                Guid.NewGuid(),DateTimeOffset.UtcNow,"Observation ended",reason);
            Update(a,true);
        }
        observingId=null;previousPosition=null;
        qualifiedAdapterActive=false;
        exposure.Reset();qualifiedLocation.Reset();discontinuity=null;
    }
    private void Update(PhysicalAirframe a,bool save)
    { var next=fleet with {Aircraft=fleet.Aircraft.Select(x=>x.Id==a.Id?a:x).ToArray()}; next.Validate(); fleet=next;dirty=true;if(save)Save(); }
    private void Save()
    {
        if(!dirty)return;
        try { fleet=store?.Commit(fleet with {Revision=fleet.Revision+1},fleet.Revision)??fleet with {Revision=fleet.Revision+1};dirty=false;savedUtc=DateTimeOffset.UtcNow; }
        catch { blocked=true;throw; }
    }
    public string? CheckDispatch(FlightAircraftIdentity identity,IReadOnlyList<CargoJobDisplay> jobs)
        => CheckDispatchCore(identity, jobs.Select(j => new DispatchAssignment(j.JobId, j.CompanyAircraftTypeId, j.LoadedCargoKilograms > 0, j.OriginLatitude, j.OriginLongitude)).ToArray());
    public string? CheckPassengerDispatch(FlightAircraftIdentity identity, PassengerTransfer transfer)
        => CheckDispatchCore(identity, [new(transfer.Id, transfer.AircraftTypeId, false, transfer.Origin.Latitude, transfer.Origin.Longitude)]);
    private sealed record DispatchAssignment(string Id, string? TypeId, bool AlreadyAboard, double? OriginLatitude, double? OriginLongitude);
    private string? CheckDispatchCore(FlightAircraftIdentity identity, IReadOnlyList<DispatchAssignment> assignments)
    {
        lock(gate)
        {
            var a=Selected;
            if(a?.CompanyTypeId is {} type && a.OwnerEntityId is not null && (!Authorized(a) || world is null || !CompanyAircraft.Types.Any(t=>t.Id==type&&t.Enabled) || ResolveCompanyType?.Invoke(world.Aircraft)!=type))return "Selected company airframe is unauthorised, disabled or does not match the confirmed live type.";
            if(blocked||a is null||world is null||!a.Bindings.Any(b=>b.Title==identity.Title&&b.Model==identity.Type))return "Select and bind an individual aircraft in Aircraft / Fleet before a new load.";
            if(a.LocationReconciliationRequired)return "Review and acknowledge the aircraft's external relocation before new dispatch. Existing cargo is not moved by this acknowledgement.";
            if(assignments.Any(j=>j.TypeId is {} type && type!=a.CompanyTypeId))return "This physical airframe's company type does not match the assigned job.";
            if(fleet.Aircraft.Any(other=>other.Id!=a.Id && assignments.Any(j=>other.DispatchJobIds.Contains(j.Id))))return "An assignment is already associated with another physical airframe; reassignment requires explicit recovery support.";
            if(a.Availability is "Grounded" or "In maintenance")return $"{a.Registration} is {a.Availability}. Existing cargo is preserved.";
            if(a.Latest is not {} p||!p.Telemetry.IsOnGround||DateTimeOffset.UtcNow-p.Telemetry.ObservedUtc>TimeSpan.FromSeconds(15))return "Aircraft needs a fresh saved ground location before dispatch.";
            foreach(var job in assignments.Where(j=>!j.AlreadyAboard))
                if(job.OriginLatitude is not {} lat||job.OriginLongitude is not {} lon||WorldTracking.DistanceMeters(p.Telemetry.Latitude,p.Telemetry.Longitude,lat,lon)>2778)
                    return "Selected aircraft is not at the required origin (existing 1.5 NM operational loading radius).";
            var assigned=a.DispatchJobIds.Concat(assignments.Select(j=>j.Id)).Distinct().ToArray();
            if(assigned.Length!=a.DispatchJobIds.Count)
                try {Update(FleetOperations.Event(a with {DispatchJobIds=assigned},Guid.NewGuid(),DateTimeOffset.UtcNow,"Dispatch association","Selected transport assignments associated with this physical airframe; existing boarding/loading verification still required."),true);}
                catch(Exception e){return "Cannot persist airframe dispatch association: "+e.Message;}
            return null;
        }
    }
    public async Task ShutdownAsync()
    {
        Task pending;
        lock(gate){closed=true;cancellation?.Cancel();pending=observer;}
        await pending.ConfigureAwait(false);Notify();
        while(true){lock(gate){if(!maintenanceBusy)break;}await Task.Delay(20).ConfigureAwait(false);}
        lock(gate){try{EndSession("Application shutdown");Save();}catch(Exception e){status="Final fleet save failed: "+e.Message;}}
        Notify();
    }
    private void Notify()
    {
        void Publish(){PropertyChanged?.Invoke(this,new(null));FleetChanged?.Invoke();EnrolCommand.Refresh();RenameCommand.Refresh();BindCommand.Refresh();ReturnCommand.Refresh();}
        if(context is not null&&SynchronizationContext.Current!=context)context.Post(_=>Publish(),null);else Publish();
    }
}
