using System.ComponentModel;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

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
    public AircraftFleetViewModel(AircraftFleetStore? store, Func<IAircraftConditionSource> sourceFactory)
    {
        this.store = store; this.sourceFactory = sourceFactory;
        EnrolCommand = new(() => Run(Enrol), () => !blocked && !closed && world is not null && Registration.Trim().Length > 0 && PackageVersion.Trim().Length > 0);
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
    public IReadOnlyList<PhysicalAirframe> Aircraft => fleet.Aircraft;
    public Guid? SelectedAirframeId
    {
        get=>fleet.SelectedId;
        set { if(value is {} id && fleet.Aircraft.SingleOrDefault(a=>a.Id==id) is {} a) Selected=a; }
    }
    public PhysicalAirframe? Selected
    {
        get => fleet.Selected;
        set { Run(()=>{ if (value?.Id == fleet.SelectedId || blocked || closed || maintenanceBusy) return; EndSession("Aircraft selection changed"); bindingConfirmationRequired=false; fleet = fleet with { SelectedId = value?.Id }; dirty = true; Save(); Reconcile(); }); }
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
        Update(FleetOperations.Event(a with {LocationReconciliationRequired=false},Guid.NewGuid(),DateTimeOffset.UtcNow,
            "Relocation acknowledged","Player acknowledged external simulator location. No shipment, inventory, flight evidence or payment changed."),true);
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
    public string ConnectionStatus => world is not null && Selected is {} a && observingId==a.Id && !bindingConfirmationRequired && DateTimeOffset.UtcNow-world.Aircraft.ObservedUtc<=TimeSpan.FromSeconds(5)
        ? "LIVE · selected aircraft matches explicit binding" : "LAST KNOWN / observation-only";
    public string Overview => Selected?.Overview ?? "No physical aircraft enrolled. Enrol the loaded aircraft explicitly; historical condition remains Unknown.";
    public string Components => Selected is null ? "" : string.Join("\n", Selected.Components.Where(x => !x.Removed).Select(x => $"{x.Key} · {x.Authority} · {(x.Health is {} h ? h.ToString("P1") : "Unknown")} · {x.ObservedSeconds / 3600:F2} observed hours · {(x.InspectionRequired ? "Inspection required" : "No inspection flag")}"));
    public string History => Selected is null ? "" : string.Join("\n", Selected.History.Reverse().Select(x => $"{x.Utc:u} · {x.Kind} · {x.Detail}"));
    public string MaintenanceSummary => Selected is null ? "Select an aircraft." : string.Join("\n", Selected.Components.Where(c=>!c.Removed && c.InspectionRequired).Select(c=>c.Key + " · Inspection required · condition unknown")) + "\n" + string.Join("\n", Selected.Work.Select(w=>$"{w.Action} · {w.State} · {w.Payer} · due {w.DueUtc:u}"));
    public string Observations => Selected?.LastObservation is {} f ? $"Last condition receipt {f.CompletedUtc:u} · {f.Readings.Count} fields · compatibility unverified. Exposure requires running/unpaused simulator time; wear profiles and fault effects are not qualified." : "No condition observations yet.";
    public string ReturnInstructions => Selected?.Latest is not {} p ? "Location unknown." :
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
        EndSession("Binding changed");
        bindingConfirmationRequired=false;
        var a = Selected!; var t = world!.Aircraft;
        if (!a.Bindings.Any(b => b.Matches(t)))
            a = FleetOperations.Event(a with { Bindings = a.Bindings.Append(new(t.AircraftTitle,t.AircraftType,PackageVersion.Trim(),"standard-simvars-v0",null)).ToArray() },
                Guid.NewGuid(),DateTimeOffset.UtcNow,"Explicit binding","User associated this exact loaded title/model/package declaration with the selected physical airframe. Compatibility remains unverified.");
        Update(a,true); Reconcile();
    }
    public void ObserveTracking(TrackingWorldState? sample, AircraftLocationContext? recognized)
    {
        lock(gate)
        {
            if (closed || blocked || maintenanceBusy) return;
            try
            {
                sample?.EnsureValid(); world = sample; location = recognized;
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
