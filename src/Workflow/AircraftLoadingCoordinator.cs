using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed record AircraftLoadingOptions(double AvatarRadiusMeters = 100, double RestorePositionRadiusNauticalMiles = 1.5,
    TimeSpan? TelemetryMaximumAge = null, TimeSpan? FuelCheckpointInterval = null, decimal PayloadToleranceKilograms = .25m,
    decimal FuelToleranceGallons = .05m)
{
    public TimeSpan MaximumAge => TelemetryMaximumAge ?? TimeSpan.FromSeconds(3);
    public TimeSpan CheckpointInterval => FuelCheckpointInterval ?? TimeSpan.FromSeconds(30);
}

public sealed record AircraftLoadingStatus(AircraftLoadingSnapshot? Live, CargoLoadManifest? Manifest,
    PersistentAircraftFuelState? SavedFuel, AircraftLoadPlan? Plan, string CargoActionReason, string FuelActionReason,
    string ReconciliationReason, bool CanApplyCargo, bool CanApplyFuel, bool CanUnload, bool CanRestoreCargo,
    bool CanRestoreFuel, bool FuelDecisionRequired, IReadOnlyList<string> Warnings)
{
    public static AircraftLoadingStatus Empty { get; } = new(null, null, null, null,
        "Connect MSFS and accept a cargo job.", "Connect MSFS to inspect fuel.", "No loading state has been observed.",
        false, false, false, false, false, false, []);
}

/// <summary>Coordinates external simulator writes with Frontier's persistent cargo authority.
/// The simulator and economy cannot commit atomically, so every write is preceded by a durable intent and followed by read-back.</summary>
public sealed class AircraftLoadingCoordinator
{
    private const double MetresPerNauticalMile = 1852;
    private readonly IAircraftLoadingStore store; private readonly IAircraftLoadingSimulator simulator;
    private readonly IFlightAttemptTracker flight; private readonly Func<string> sdkRoot;
    private readonly Func<TrackingWorldState?> tracking; private readonly Func<CargoJobDisplay?> activeJob;
    private readonly Func<DateTimeOffset> utcNow; private readonly AircraftLoadingOptions options; private readonly SemaphoreSlim gate = new(1,1);
    private bool continuouslyConnected; private DateTimeOffset? lastFuelCheckpoint;
    public AircraftLoadingStatus Current { get; private set; } = AircraftLoadingStatus.Empty;
    public event Action<AircraftLoadingStatus>? Changed;
    public string StoragePath => store.StoragePath;
    public string? ActiveShipmentId=>activeJob()?.ShipmentId;
    public decimal JobCargoKilograms=>activeJob()?.CargoMassKilograms??Current.Manifest?.ShipmentQuantityKilograms??0;
    public decimal LoadedCargoKilograms=>Current.Manifest?.CargoKilograms??activeJob()?.LoadedCargoKilograms??0;
    public decimal DeliveredCargoKilograms=>activeJob()?.DeliveredCargoKilograms??Current.Manifest?.DeliveredCargoKilograms??0;
    public decimal CargoAvailableAtCurrentLocation
    {
        get
        {
            var job=activeJob();var live=Current.Live;
            if(job is null||live is null||job.ShipmentState is not (ShipmentState.Reserved or ShipmentState.Loaded)||CurrentLocation(job,live.Position)!=job.OriginLocationId)return 0;
            return Math.Max(0,job.CargoMassKilograms-job.DeliveredCargoKilograms-LoadedCargoKilograms);
        }
    }
    public decimal ActiveCargoKilograms => activeJob() is { } job ? Math.Max(0, job.CargoMassKilograms - job.DeliveredCargoKilograms) :
        Current.Manifest is { State: not (LoadManifestState.Completed or LoadManifestState.Abandoned) } manifest ? manifest.CargoKilograms : 0;
    public bool CanAbandonPrototypeMission => Current.Manifest?.State is LoadManifestState.CommittedInTransit or LoadManifestState.PendingAbandonment;
    public object CreateDiagnosticSnapshot()
    {
        var live=Current.Live;var saved=Current.SavedFuel;var manifest=Current.Manifest;
        return new
        {
            LoadingMode=manifest?.Mode.ToString(),ManifestState=manifest?.State.ToString(),manifest?.Id,manifest?.JobId,manifest?.ShipmentId,
            AircraftIdentity=manifest?.Aircraft,CurrentAircraftIdentity=live?.Aircraft,ManifestReconciliation=manifest?.Reconciliation.ToString(),
            ManifestResult=manifest?.VerificationResult,VerificationToleranceKilograms=manifest?.VerificationToleranceKilograms,
            StationAllocation=manifest?.Stations.Select(x=>new{x.StationIndex,x.BaselineKilograms,x.FrontierCargoKilograms,x.TargetKilograms,x.ReadBackKilograms,x.UnloadTargetKilograms,x.UnloadReadBackKilograms}),
            LastPersistedAircraftPosition=manifest?.LastValidPosition??saved?.LastValidPosition,
            FuelReconciliation=saved?.Reconciliation.ToString(),LastTrustedFuelObservation=saved?.ObservedUtc,
            SavedFuel=saved?.Tanks.Select(x=>new{x.Id,x.QuantityGallons,x.CapacityGallons}),
            LiveFuel=live?.FuelTanks.Select(x=>new{x.Id,x.QuantityGallons,x.CapacityGallons}),
            Current.CargoActionReason,Current.FuelActionReason,Current.ReconciliationReason,Warnings=Current.Warnings
        };
    }

    public AircraftLoadingCoordinator(IAircraftLoadingStore store, IAircraftLoadingSimulator simulator, IFlightAttemptTracker flight,
        Func<string> sdkRoot, Func<TrackingWorldState?> tracking, Func<CargoJobDisplay?> activeJob,
        AircraftLoadingOptions? options = null, Func<DateTimeOffset>? utcNow = null)
    { this.store=store;this.simulator=simulator;this.flight=flight;this.sdkRoot=sdkRoot;this.tracking=tracking;this.activeJob=activeJob;
      this.options=options??new();this.utcNow=utcNow??(()=>DateTimeOffset.UtcNow);
      flight.Changed+=_=>RefreshEligibility(); }

    private void RefreshEligibility()
    {
        if (Current.Manifest is { State: LoadManifestState.Abandoned or LoadManifestState.Completed } terminal &&
            activeJob() is { } selected && selected.JobId != terminal.JobId)
            Current = Current with { Manifest = null, Plan = null };
        if(Current.Live is not { } live)return;
        var reason=Access(live,false,checkSnapshotFreshness:false);
        Current=Current with{CanApplyCargo=CanApplyDesiredState(live, reason),
            CargoActionReason=CargoReason(reason)};
        Notify();
    }

    private string CargoReason(string access)
    {
        if(access!="Ready.")return access;
        if(Current.Plan is { } plan && Current.Manifest is { } manifest && Math.Abs(plan.CargoKilograms-manifest.CargoKilograms)>.001m)
            return plan.CargoKilograms>manifest.CargoKilograms?"Ready to add reserved cargo and verify the requested aircraft state.":"Ready to remove Frontier cargo and verify the requested aircraft state.";
        if(Current.Manifest is {State:LoadManifestState.CommittedInTransit} saved)
            return saved.Reconciliation switch {
                SimulatorReconciliationState.CargoAppearsMissing => "Saved Frontier cargo is not present in the reported MSFS stations. Set the desired state to zero to reconcile removal, or use Restore manifest cargo.",
                SimulatorReconciliationState.Indeterminate => "Live payload differs from the saved managed state. Review the station mismatch before applying.",
                _ => $"Aircraft currently has {saved.CargoKilograms:0.###} kg of Frontier-managed cargo. Edit the desired station values to load or unload."
            };
        return activeJob() is null?"No cargo is available at this location. Fuel and current aircraft payload remain available for inspection.":flight.Current.Reason;
    }

    public async Task InitializeAsync(CancellationToken token=default)
    {
        var manifests=await store.LoadManifestsAsync(token); var fuel=await store.LoadFuelStatesAsync(token);
        var attempt=flight.Current.Attempt;
        var recoverable=manifests.Where(x=>x.State is not (LoadManifestState.Completed or LoadManifestState.Abandoned)).OrderByDescending(x=>x.CreatedUtc).FirstOrDefault();
        var manifest=recoverable;
        if(manifest is null&&attempt is {State:not FlightAttemptState.Delivered}&&activeJob() is {ShipmentId:not null} legacy)
        {
            manifest=new CargoLoadManifest("manifest-"+attempt.Id,legacy.JobId,legacy.ShipmentId!,attempt.Id,attempt.Aircraft,
                AircraftLoadingMode.SimulatedFrontierLoading,[],legacy.CargoMassKilograms,LoadManifestState.CommittedInTransit,
                attempt.LoadedUtc,CommittedUtc:attempt.LoadedUtc,VerificationResult:"Migrated existing simulated-loading shipment; MSFS payload was not changed.",LastValidPosition:attempt.LoadedPosition);
            await store.SaveManifestAsync(manifest,token);
        }
        if(attempt?.State==FlightAttemptState.Delivered&&manifest is {State:LoadManifestState.SimulatorUnloaded})
        {manifest=manifest with{State=LoadManifestState.Completed,CompletedUtc=attempt.DeliveredUtc,VerificationResult="Recovered after economic delivery; previously verified simulator unload is complete."};await store.SaveManifestAsync(manifest,token);}
        if(activeJob()?.ShipmentState is (ShipmentState.Loaded or ShipmentState.InTransit)&&manifest is {State:LoadManifestState.SimulatorApplied})
        {manifest=manifest with{State=LoadManifestState.CommittedInTransit,CommittedUtc=attempt?.LoadedUtc??utcNow(),VerificationResult="Recovered existing idempotent cargo economy commit after verified simulator application."};await store.SaveManifestAsync(manifest,token);}
        Current=Current with { Manifest=manifest, SavedFuel=fuel.OrderByDescending(x=>x.ObservedUtc).FirstOrDefault(),
            CargoActionReason=manifest is null?"No persistent cargo manifest.":"Persistent cargo manifest restored; verify simulator state before changing it.",
            ReconciliationReason=manifest is null?"No cargo reconciliation is required.":"Simulator reconciliation has not run in this session."}; Notify();
    }

    public async Task ObserveConnectionAsync(TrackingWorldState? world,CancellationToken token=default)
    {
        if(world is null && continuouslyConnected)
        {
            continuouslyConnected=false;
            if(Current.SavedFuel is { } saved)
            { await store.SaveFuelStateAsync(saved with { Reconciliation=FuelReconciliationState.FrozenAfterDisconnect, Result="Last trusted fuel snapshot frozen after disconnect." },token); Current=Current with {SavedFuel=saved with {Reconciliation=FuelReconciliationState.FrozenAfterDisconnect}}; }
            Current=Current with {Live=null,FuelDecisionRequired=Current.SavedFuel is not null,CargoActionReason="SimConnect is disconnected; persistent cargo remains authoritative.",FuelActionReason="SimConnect is disconnected; saved fuel is frozen."};Notify();
        }
        else if(world is not null&&continuouslyConnected&&utcNow()-(lastFuelCheckpoint??DateTimeOffset.MinValue)>=options.CheckpointInterval)
            await RefreshAsync(token);
    }

    public async Task RefreshAsync(CancellationToken token=default)
    {
        await gate.WaitAsync(token);try
        {
            var snapshot=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid(); var wasConnected=continuouslyConnected; continuouslyConnected=true;
            var manifest=Current.Manifest; var saved=Current.SavedFuel; var warnings=new List<string>();
            if(manifest is {Mode:AircraftLoadingMode.MsfsPayloadIntegration,State:LoadManifestState.CommittedInTransit})
            {
                var comparison=ComparePayload(snapshot,manifest);
                manifest=manifest with {Reconciliation=comparison.state,LastReconciledUtc=utcNow(),VerificationResult=comparison.reason,LastValidPosition=comparison.state==SimulatorReconciliationState.MatchesManifest?snapshot.Position:manifest.LastValidPosition};
                await store.SaveManifestAsync(manifest,token);
            }
            var identity=LoadingIdentityExtensions.FromSnapshot(snapshot);
            if(saved is null)
            {
                saved=FuelState(snapshot,FuelReconciliationState.ContinuousLive,"Initial validated live fuel snapshot.");await store.SaveFuelStateAsync(saved,token);lastFuelCheckpoint=utcNow();
            }
            else if(!wasConnected)
            {
                saved=saved with {Reconciliation=FuelReconciliationState.AwaitingDecision,Result="Reconnected; choose saved or simulator fuel before the live reading becomes authoritative."};
                await store.SaveFuelStateAsync(saved,token);
            }
            else if(saved.Reconciliation is FuelReconciliationState.ContinuousLive or FuelReconciliationState.RestoredVerified or FuelReconciliationState.KeptSimulatorFuel &&
                !saved.Aircraft.ClearlyDiffersFrom(identity) && ShouldCheckpoint(snapshot,saved))
            {
                saved=FuelState(snapshot,FuelReconciliationState.ContinuousLive,"Continuous-session validated fuel checkpoint.");await store.SaveFuelStateAsync(saved,token);lastFuelCheckpoint=utcNow();
            }
            var access=Access(snapshot,false); var fuelAccess=Access(snapshot,true);
            if(manifest is {State:LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied})
                access=PayloadMatches(snapshot,manifest.Stations.ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms),options.PayloadToleranceKilograms)
                    ? "Ready. Pending manifest weights are already present; continue will verify without adding cargo again."
                    : "Ready.";
            if(snapshot.PayloadStations.Any(x=>x.AuthoritativeMaximumKilograms is null))warnings.Add("Payload-station structural limits are unavailable from this aircraft.");
            if(snapshot.Weights.MaximumPayloadKilograms is null)warnings.Add("Authoritative maximum payload is unavailable from this aircraft.");
            if(snapshot.Engines.Any(x=>x.IsRunning==true))warnings.Add("One or more engines are running; cargo remains selectable, with a ground-handling caution.");
            Current=Current with {Live=snapshot,Manifest=manifest,SavedFuel=saved,CargoActionReason=access,FuelActionReason=fuelAccess,
                CanApplyCargo=CanApplyDesiredState(snapshot,access),CanApplyFuel=fuelAccess=="Ready.",CanUnload=(flight.Current.CanUnloadCargo && manifest is not null)||manifest?.State==LoadManifestState.SimulatorUnloaded,
                FuelDecisionRequired=saved.Reconciliation==FuelReconciliationState.AwaitingDecision,Warnings=warnings};
            Current=Current with{CargoActionReason=CargoReason(access)};UpdateRestoreAvailability();Notify();
        }finally{gate.Release();}
    }

    public AircraftLoadPlan CreatePlan(IReadOnlyList<CargoStationAllocation> allocations)
    {
        var live=Current.Live??throw new InvalidOperationException("Read live aircraft loading data first.");
        var job=activeJob(); var manifest=Current.Manifest;
        if(job is null&&manifest is null)throw new InvalidOperationException("No Frontier-managed cargo is aboard or available at this location.");
        var location=job is null?null:CurrentLocation(job,live.Position);
        var aboard=manifest?.CargoKilograms??job?.LoadedCargoKilograms??0;
        var maximum=job is not null&&location==job.OriginLocationId&&job.ShipmentState is ShipmentState.Reserved or ShipmentState.Loaded
            ? job.CargoMassKilograms-job.DeliveredCargoKilograms : aboard;
        var plan=AircraftLoadPlan.CreateDesiredState(live,allocations,manifest,Math.Max(0,maximum),toleranceKilograms:options.PayloadToleranceKilograms);
        Current=Current with {Plan=plan,Warnings=plan.Warnings,CanApplyCargo=CanApplyDesiredState(live,"Ready.")};Notify();return plan;
    }

    public async Task ApplyCargoAsync(bool confirmForfeit=false,CancellationToken token=default)
    {
        await gate.WaitAsync(token);try
        {
            var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();
            var access=Access(live,false);if(access!="Ready.")throw new InvalidOperationException(access);
            var job=activeJob();var existing=Current.Manifest;
            var plan=Current.Plan??(existing is {State:LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied}
                ? PlanFromManifest(existing) : throw new InvalidOperationException("Choose the desired Frontier cargo in each station before applying."));
            if(existing is null&&job?.ShipmentId is null)throw new InvalidOperationException("No active shipment cargo is available to load.");
            var recoveringIntent=existing?.State is LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied;
            var currentCargo=recoveringIntent?(job?.LoadedCargoKilograms??existing!.PriorCargoKilograms):(existing?.CargoKilograms??job?.LoadedCargoKilograms??0);
            var delta=plan.CargoKilograms-currentCargo;
            if(Math.Abs(delta)<=.001m)throw new InvalidOperationException("The requested Frontier cargo already matches the persistent aircraft state.");
            var location=job is null?null:CurrentLocation(job,live.Position);
            if(delta>0&&(job is null||location!=job.OriginLocationId||!flight.Current.CanLoadCargo))
                throw new InvalidOperationException(job is null?"No reserved shipment cargo is available to add.":location!=job.OriginLocationId?"Reserved cargo can only be loaded at the shipment origin.":flight.Current.Reason);
            if(delta<0&&job is not null&&location!=job.OriginLocationId&&location!=job.DestinationLocationId&&!confirmForfeit)
                throw new CargoForfeitConfirmationRequiredException("This cargo belongs to an active delivery. Unloading it here will forfeit the job and you will not receive payment.");
            if(delta<0&&job is not null&&location==job.DestinationLocationId&&!flight.Current.CanUnloadCargo)
                throw new InvalidOperationException(flight.Current.Reason);
            var attemptId=existing?.FlightAttemptId??FlightAttemptIds.Attempt(job!.ShipmentId!);
            var id=existing?.Id??"manifest-"+attemptId;
            var commandId=existing?.State is (LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied)&&existing.LastTransferCommandId is not null
                ? existing.LastTransferCommandId : "cargo-transfer-"+Guid.NewGuid().ToString("N");
            var manifest=(existing??new CargoLoadManifest(id,job!.JobId,job.ShipmentId!,attemptId,LoadingIdentityExtensions.FromSnapshot(live),
                AircraftLoadingMode.MsfsPayloadIntegration,plan.Stations,plan.CargoKilograms,LoadManifestState.PendingApplication,utcNow(),
                VerificationToleranceKilograms:options.PayloadToleranceKilograms,LastValidPosition:live.Position)
                {ShipmentQuantityKilograms=job.CargoMassKilograms,DeliveredCargoKilograms=job.DeliveredCargoKilograms,IsDesiredStateManifest=true}) with
                {Stations=plan.Stations,CargoKilograms=plan.CargoKilograms,PriorCargoKilograms=currentCargo,State=LoadManifestState.PendingApplication,
                    LastTransferCommandId=commandId,VerificationResult=$"Desired-state intent persisted. Previous managed cargo {currentCargo:0.###} kg; requested {plan.CargoKilograms:0.###} kg."};
            await store.SaveManifestAsync(manifest,token); // durable intent precedes external write
            AircraftLoadingSnapshot readBack;
            if(PayloadMatches(live,manifest.Stations.Select(x=>(x.StationIndex,x.TargetKilograms)).ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms),options.PayloadToleranceKilograms))readBack=live;
            else readBack=await simulator.WritePayloadAsync(sdkRoot(),manifest.Stations.ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms),token);
            var stations=manifest.Stations.Select(x=>x with {ReadBackKilograms=readBack.PayloadStations.Single(s=>s.Index==x.StationIndex).CurrentWeightKilograms}).ToArray();
            if(stations.Any(x=>Math.Abs(x.ReadBackKilograms!.Value-x.TargetKilograms)>options.PayloadToleranceKilograms))
            {var mismatch=string.Join("; ",stations.Where(x=>Math.Abs(x.ReadBackKilograms!.Value-x.TargetKilograms)>options.PayloadToleranceKilograms).Select(x=>$"station {x.StationIndex}: baseline {x.BaselineKilograms:0.###}, managed {x.FrontierCargoKilograms:0.###}, expected {x.TargetKilograms:0.###}, observed {x.ReadBackKilograms:0.###} kg"));throw new IOException("MSFS payload read-back did not match the desired state: "+mismatch);}
            manifest=manifest with {Stations=stations,State=LoadManifestState.SimulatorApplied,SimulatorAppliedUtc=utcNow(),VerificationResult="MSFS desired station weights written and verified; persistent cargo transfer pending.",LastReconciledUtc=utcNow(),Reconciliation=SimulatorReconciliationState.MatchesManifest};await store.SaveManifestAsync(manifest,token);
            if(job is not null)
                await flight.ApplyCargoTransferAsync("transfer-"+commandId,commandId,plan.CargoKilograms,location,live.Position.Latitude,live.Position.Longitude,confirmForfeit,AircraftLoadingMode.MsfsPayloadIntegration,token);
            var delivered=manifest.DeliveredCargoKilograms+(delta<0&&job is not null&&location==job.DestinationLocationId?-delta:0);
            manifest=manifest with {State=flight.Current.Attempt?.State==FlightAttemptState.Delivered?LoadManifestState.Completed:LoadManifestState.CommittedInTransit,
                CommittedUtc=utcNow(),CompletedUtc=flight.Current.Attempt?.State==FlightAttemptState.Delivered?utcNow():null,
                DeliveredCargoKilograms=delivered,VerificationResult=$"Desired aircraft state verified and committed. Frontier cargo aboard: {plan.CargoKilograms:0.###} kg.",
                Reconciliation=SimulatorReconciliationState.MatchesManifest,LastReconciledUtc=utcNow(),LastValidPosition=readBack.Position};await store.SaveManifestAsync(manifest,token);
            Current=Current with {Manifest=manifest,Live=readBack,Plan=null,CanApplyCargo=false,CanUnload=flight.Current.CanUnloadCargo,
                CargoActionReason=manifest.State==LoadManifestState.Completed?"Delivery completed and paid exactly once.":$"Verified aircraft state: {manifest.CargoKilograms:0.###} kg Frontier cargo aboard."};Notify();
        }finally{gate.Release();}
    }

    public Task ApplyCargoAsync(CancellationToken token)=>ApplyCargoAsync(false,token);

    private static AircraftLoadPlan PlanFromManifest(CargoLoadManifest manifest) => new(
        manifest.CargoKilograms, manifest.Stations, manifest.Stations.Sum(x => x.TargetKilograms), null, null,
        ["Recovered the exact persisted desired-state intent; MSFS read-back will be verified before persistent cargo changes are committed."]);

    public async Task ApplySimulatedCargoAsync(CancellationToken token=default)
    {
        await gate.WaitAsync(token);try
        {
            var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();
            if(Access(live,false)!="Ready."||!flight.Current.CanLoadCargo)throw new InvalidOperationException(Access(live,false)=="Ready."?flight.Current.Reason:Access(live,false));
            var job=activeJob()!;var attemptId=FlightAttemptIds.Attempt(job.ShipmentId!);var id="manifest-"+attemptId;
            var manifest=(await store.LoadManifestsAsync(token)).SingleOrDefault(x=>x.Id==id)??new CargoLoadManifest(id,job.JobId,job.ShipmentId!,attemptId,
                LoadingIdentityExtensions.FromSnapshot(live),AircraftLoadingMode.SimulatedFrontierLoading,[],job.CargoMassKilograms,LoadManifestState.PendingApplication,utcNow(),LastValidPosition:live.Position,VerificationResult:"Player explicitly selected simulated Frontier loading; MSFS payload unchanged.");
            await store.SaveManifestAsync(manifest,token);await flight.LoadCargoAsync(AircraftLoadingMode.SimulatedFrontierLoading,token);
            manifest=manifest with {State=LoadManifestState.CommittedInTransit,CommittedUtc=utcNow()};await store.SaveManifestAsync(manifest,token);Current=Current with{Manifest=manifest,CanApplyCargo=false,CanUnload=flight.Current.CanUnloadCargo,CargoActionReason="Simulated cargo is Loaded; MSFS payload was not changed."};Notify();
        }finally{gate.Release();}
    }

    public async Task UnloadAsync(bool simulatedCompatibilityConfirmed=false,CancellationToken token=default)
    {
        await gate.WaitAsync(token);try
        {
            var manifest=Current.Manifest??throw new InvalidOperationException("No committed cargo manifest exists.");
            if(manifest.State==LoadManifestState.Completed)return;
            if(manifest.State is LoadManifestState.PendingAbandonment or LoadManifestState.Abandoned)
                throw new InvalidOperationException("This job is being returned; delivery and payment are unavailable.");
            if(!flight.Current.CanUnloadCargo)throw new InvalidOperationException(flight.Current.Reason);
            if(manifest.Mode==AircraftLoadingMode.MsfsPayloadIntegration && !simulatedCompatibilityConfirmed && manifest.State!=LoadManifestState.SimulatorUnloaded)
            {
                var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();if(Access(live,false)!="Ready.")throw new InvalidOperationException(Access(live,false));
                var changed=manifest.State==LoadManifestState.PendingUnload?manifest.Stations.ToArray():manifest.Stations.Select(x=>{var now=live.PayloadStations.Single(s=>s.Index==x.StationIndex).CurrentWeightKilograms;var target=CargoRemovalTarget(now,x.FrontierCargoKilograms,options.PayloadToleranceKilograms,x.StationIndex);return x with{UnloadTargetKilograms=target};}).ToArray();
                if(manifest.State!=LoadManifestState.PendingUnload){manifest=manifest with{Stations=changed,State=LoadManifestState.PendingUnload,UnloadStartedUtc=utcNow(),VerificationResult="Unload intent persisted before MSFS write."};await store.SaveManifestAsync(manifest,token);}
                var targets=changed.ToDictionary(x=>x.StationIndex,x=>x.UnloadTargetKilograms!.Value);
                var rb=PayloadMatches(live,targets,options.PayloadToleranceKilograms)?live:await simulator.WritePayloadAsync(sdkRoot(),targets,token);
                changed=changed.Select(x=>x with{UnloadReadBackKilograms=rb.PayloadStations.Single(s=>s.Index==x.StationIndex).CurrentWeightKilograms}).ToArray();
                if(changed.Any(x=>Math.Abs(x.UnloadReadBackKilograms!.Value-x.UnloadTargetKilograms!.Value)>options.PayloadToleranceKilograms))throw new IOException("MSFS payload removal could not be verified; arrival evidence is preserved for retry.");
                manifest=manifest with{Stations=changed,State=LoadManifestState.SimulatorUnloaded,SimulatorUnloadedUtc=utcNow(),VerificationResult="Frontier allocation removed and verified; unrelated current payload was preserved."};await store.SaveManifestAsync(manifest,token);Current=Current with{Live=rb};
            }
            await flight.UnloadCargoAsync(token);manifest=manifest with{State=LoadManifestState.Completed,CompletedUtc=utcNow(),VerificationResult=manifest.Mode==AircraftLoadingMode.MsfsPayloadIntegration&&!simulatedCompatibilityConfirmed?"Simulator unload verified and economic delivery committed.":simulatedCompatibilityConfirmed?"Player explicitly confirmed simulated compatibility unload; MSFS removal was not verified and economic delivery committed.":"Simulated unload and economic delivery committed."};await store.SaveManifestAsync(manifest,token);Current=Current with{Manifest=manifest,CanUnload=false,CargoActionReason="Delivery completed; manifest is immutable."};Notify();
        }finally{gate.Release();}
    }

    public async Task ApplyFuelAsync(IReadOnlyDictionary<string,decimal> planned,CancellationToken token=default)
    {
        await gate.WaitAsync(token);try{var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();var reason=Access(live,true);if(reason!="Ready.")throw new InvalidOperationException(reason);
            foreach(var pair in planned){var tank=live.FuelTanks.SingleOrDefault(x=>x.Id==pair.Key)??throw new InvalidOperationException("Fuel plan contains an unavailable tank.");if(pair.Value<0||pair.Value>tank.CapacityGallons)throw new InvalidOperationException($"Fuel for {tank.Name} must remain within its reported capacity.");}
            var read=await simulator.WriteFuelAsync(sdkRoot(),planned,token);foreach(var pair in planned)if(Math.Abs(read.FuelTanks.Single(x=>x.Id==pair.Key).QuantityGallons-pair.Value)>options.FuelToleranceGallons)throw new IOException("MSFS fuel read-back did not match the requested quantities.");
            var saved=FuelState(read,FuelReconciliationState.ContinuousLive,"Free prototype fuel applied and verified through read-back.");await store.SaveFuelStateAsync(saved,token);Current=Current with{Live=read,SavedFuel=saved,FuelDecisionRequired=false,FuelActionReason="Fuel applied and verified. Prototype fuel has no financial cost."};Notify();
        }finally{gate.Release();}
    }

    public async Task KeepSimulatorFuelAsync(CancellationToken token=default)
    { var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();var saved=FuelState(live,FuelReconciliationState.KeptSimulatorFuel,"Player explicitly kept simulator fuel after reconciliation.");await store.SaveFuelStateAsync(saved,token);lastFuelCheckpoint=utcNow();Current=Current with{Live=live,SavedFuel=saved,FuelDecisionRequired=false};Notify(); }
    public async Task RestoreSavedFuelAsync(CancellationToken token=default)
    { var saved=Current.SavedFuel??throw new InvalidOperationException("No saved fuel state exists.");var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();var reason=RestoreReason(live,saved,true);if(reason!="Ready.")throw new InvalidOperationException(reason);
      var targets=saved.Tanks.ToDictionary(x=>x.Id,x=>x.QuantityGallons);var read=await simulator.WriteFuelAsync(sdkRoot(),targets,token);foreach(var pair in targets)if(Math.Abs(read.FuelTanks.Single(x=>x.Id==pair.Key).QuantityGallons-pair.Value)>options.FuelToleranceGallons)throw new IOException("Saved fuel restoration was not verified.");saved=FuelState(read,FuelReconciliationState.RestoredVerified,"Saved fuel restored and verified through read-back.");await store.SaveFuelStateAsync(saved,token);lastFuelCheckpoint=utcNow();Current=Current with{Live=read,SavedFuel=saved,FuelDecisionRequired=false};Notify(); }
    public async Task RestoreCargoAsync(CancellationToken token=default)
    { var manifest=Current.Manifest??throw new InvalidOperationException("No cargo manifest exists.");var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();var reason=RestoreReason(live,manifest,false);if(reason!="Ready.")throw new InvalidOperationException(reason);
      var read=await simulator.WritePayloadAsync(sdkRoot(),manifest.Stations.ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms),token);if(!PayloadMatches(read,manifest.Stations.ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms),options.PayloadToleranceKilograms))throw new IOException("Restored cargo did not pass MSFS read-back verification.");manifest=manifest with{Reconciliation=SimulatorReconciliationState.MatchesManifest,LastReconciledUtc=utcNow(),VerificationResult="Player-confirmed cargo restoration written once and verified."};await store.SaveManifestAsync(manifest,token);Current=Current with{Live=read,Manifest=manifest};UpdateRestoreAvailability();Notify(); }

    public async Task AbandonPrototypeMissionAsync(CancellationToken token=default)
    {
        await gate.WaitAsync(token); try
        {
            var displayed = Current.Manifest ?? throw new InvalidOperationException("No persistent cargo manifest exists.");
            var manifest = (await store.LoadManifestsAsync(token)).Single(x => x.Id == displayed.Id);
            if (manifest.State == LoadManifestState.Abandoned) return;
            if (flight.Current.Attempt is { State: FlightAttemptState.Delivered } completed && completed.JobId == manifest.JobId)
                throw new InvalidOperationException("Completed delivery cannot be declined.");
            if (manifest.State is not (LoadManifestState.CommittedInTransit or LoadManifestState.PendingAbandonment))
                throw new InvalidOperationException("Only a committed incomplete prototype shipment can be skipped.");
            if (manifest.Mode == AircraftLoadingMode.MsfsPayloadIntegration)
            {
                var live = (await simulator.ReadAsync(sdkRoot(), token)).EnsureValid();
                var access = Access(live, false); if (access != "Ready.") throw new InvalidOperationException(access);
                if (manifest.Aircraft.ClearlyDiffersFrom(live.Aircraft)) throw new InvalidOperationException("Aircraft identity differs from the cargo manifest.");
                PlannedPayloadStation[] stations;
                if (manifest.State == LoadManifestState.PendingAbandonment)
                    stations = manifest.Stations.ToArray();
                else
                {
                    stations = manifest.Stations.Select(x =>
                    {
                        var current = live.PayloadStations.Single(s => s.Index == x.StationIndex).CurrentWeightKilograms;
                        var target = PrototypeReturnTarget(current, x, manifest.VerificationToleranceKilograms);
                        return x with { UnloadTargetKilograms = target };
                    }).ToArray();
                    manifest = manifest with { Stations = stations, State = LoadManifestState.PendingAbandonment,
                        UnloadStartedUtc = utcNow(), VerificationResult = "Prototype-skip unload intent persisted before MSFS write." };
                    await store.SaveManifestAsync(manifest, token);
                    Current = Current with { Manifest = manifest, CanUnload = false, CanRestoreCargo = false };
                }
                var targets = stations.ToDictionary(x => x.StationIndex, x => x.UnloadTargetKilograms ??
                    throw new InvalidDataException("Prototype-skip unload target is missing."));
                foreach (var station in stations)
                {
                    var current = live.PayloadStations.Single(s => s.Index == station.StationIndex).CurrentWeightKilograms;
                    var target = station.UnloadTargetKilograms!.Value;
                    if (Math.Abs(current-target)>manifest.VerificationToleranceKilograms &&
                        Math.Abs(current-target-station.FrontierCargoKilograms)>manifest.VerificationToleranceKilograms)
                        throw new InvalidOperationException($"Station {station.StationIndex} changed after the return intent. Live {current:0.###} kg, removal target {target:0.###} kg. Payload preserved; review the station before retrying.");
                }
                var readBack = PayloadMatches(live, targets, options.PayloadToleranceKilograms)
                    ? live : await simulator.WritePayloadAsync(sdkRoot(), targets, token);
                stations = stations.Select(x => x with { UnloadReadBackKilograms = readBack.PayloadStations
                    .Single(s => s.Index == x.StationIndex).CurrentWeightKilograms }).ToArray();
                if (stations.Any(x => Math.Abs(x.UnloadReadBackKilograms!.Value - x.UnloadTargetKilograms!.Value) > options.PayloadToleranceKilograms))
                    throw new IOException("Frontier cargo removal could not be verified. The mission remains active and can be retried.");
                manifest = manifest with { Stations = stations, VerificationResult = "Frontier cargo removed from MSFS and verified before prototype mission skip." };
                await store.SaveManifestAsync(manifest, token); Current = Current with { Live = readBack, Manifest = manifest };
            }
            await flight.AbandonIncompletePrototypeShipmentAsync(manifest.JobId, token);
            manifest = manifest with { State = LoadManifestState.Abandoned, CompletedUtc = utcNow(),
                Reconciliation = SimulatorReconciliationState.NotChecked,
                VerificationResult = manifest.Mode == AircraftLoadingMode.MsfsPayloadIntegration
                    ? "Frontier cargo removal verified; incomplete prototype mission skipped without delivery or payment."
                    : "Explicit simulated-loading prototype mission skipped without delivery or payment." };
            await store.SaveManifestAsync(manifest, token);
            Current = Current with { Manifest = manifest, Plan = null, CanApplyCargo = false, CanUnload = false,
                CanRestoreCargo = false, CargoActionReason = "Prototype mission skipped. Choose a fresh offer on Jobs to retest." };
            Notify();
        }
        finally { gate.Release(); }
    }

    private string Access(AircraftLoadingSnapshot live,bool fuel,bool checkSnapshotFreshness=true)
    {
        var world=tracking();if(world is null)return "Validated live tracking is unavailable.";var now=utcNow();if(now-world.Aircraft.ObservedUtc>options.MaximumAge||(checkSnapshotFreshness&&now-live.ObservedUtc>options.MaximumAge))return "Aircraft telemetry is stale.";
        if(!live.Position.IsOnGround)return "Aircraft must be on the ground.";if(live.Position.GroundSpeedKnots>5)return $"Aircraft ground speed is {live.Position.GroundSpeedKnots:F1} kt; 5 kt or less is required.";
        if(world.PlayerMode!=PlayerControlMode.OnFoot||world.Player is null||world.CurrentObjectId!=world.AvatarObjectId||world.CurrentObjectId==world.AircraftObjectId)return "Exit the aircraft; a fresh explicit walking-avatar observation is required.";
        if(now-world.Player.ObservedUtc>options.MaximumAge)return "Walking-avatar observation is stale.";if(!world.Player.IsOnGround)return "Walking avatar must be on the ground.";if(world.DistanceFromAircraftMeters is not { } distance||distance>options.AvatarRadiusMeters)return $"Walking avatar must be within {options.AvatarRadiusMeters:F0} m of the aircraft.";
        if(FlightAircraftIdentity.From(world.Aircraft).ClearlyDiffersFrom(LoadingIdentityExtensions.FromSnapshot(live)))return "Live loading aircraft identity differs from tracked aircraft.";
        if(flight.Current.Attempt is {State:not FlightAttemptState.Delivered} activeAttempt&&activeAttempt.Aircraft.ClearlyDiffersFrom(LoadingIdentityExtensions.FromSnapshot(live)))return "Live aircraft identity differs from the active flight attempt.";
        if(fuel){if(live.Engines.Count==0||live.Engines.Any(x=>x.IsRunning is null))return "Engine state is unavailable; fuel changes are refused.";if(live.Engines.Any(x=>x.IsRunning==true))return "Every detected engine must be off before changing fuel.";}
        return "Ready.";
    }
    private bool CanApplyDesiredState(AircraftLoadingSnapshot live,string access)
    {
        if(access!="Ready.")return false;
        if(Current.Plan is not { } plan)
            return Current.Manifest?.CargoKilograms>0||activeJob() is not null&&flight.Current.CanLoadCargo;
        var current=Current.Manifest?.CargoKilograms??activeJob()?.LoadedCargoKilograms??0;
        var delta=plan.CargoKilograms-current;if(Math.Abs(delta)<=.001m)return false;
        if(delta<0)
        {
            var job=activeJob();if(job is null)return Current.Manifest is not null;
            var location=CurrentLocation(job,live.Position);
            return location==job.DestinationLocationId?flight.Current.CanUnloadCargo:true;
        }
        var active=activeJob();return active is not null&&CurrentLocation(active,live.Position)==active.OriginLocationId&&flight.Current.CanLoadCargo;
    }
    private static string? CurrentLocation(CargoJobDisplay job,FlightPosition position)
    {
        const double radiusNm=1.5;string? best=null;double distance=double.MaxValue;
        void Consider(string? id,double? latitude,double? longitude)
        {if(id is null||latitude is null||longitude is null)return;var d=WorldTracking.DistanceMeters(position.Latitude,position.Longitude,latitude.Value,longitude.Value)/MetresPerNauticalMile;if(d<=radiusNm&&d<distance){best=id;distance=d;}}
        Consider(job.OriginLocationId,job.OriginLatitude,job.OriginLongitude);Consider(job.DestinationLocationId,job.DestinationLatitude,job.DestinationLongitude);return best;
    }
    private string RestoreReason(AircraftLoadingSnapshot live,PersistentAircraftFuelState saved,bool fuel)
    {var basic=Access(live,fuel);if(basic!="Ready.")return basic;if(saved.Aircraft.ClearlyDiffersFrom(LoadingIdentityExtensions.FromSnapshot(live)))return "Aircraft identity differs from the saved state.";if(DistanceNm(live.Position,saved.LastValidPosition)>options.RestorePositionRadiusNauticalMiles)return "Aircraft is more than 1.5 NM from its last persisted position.";if(live.FuelTanks.Select(x=>x.Id).Order().SequenceEqual(saved.Tanks.Select(x=>x.Id).Order())==false)return "Fuel-tank layout differs from the saved state.";return "Ready.";}
    private string RestoreReason(AircraftLoadingSnapshot live,CargoLoadManifest manifest,bool fuel)
    {var basic=Access(live,fuel);if(basic!="Ready.")return basic;if(manifest.Aircraft.ClearlyDiffersFrom(LoadingIdentityExtensions.FromSnapshot(live)))return "Aircraft identity differs from the cargo manifest.";if(manifest.LastValidPosition is null||DistanceNm(live.Position,manifest.LastValidPosition)>options.RestorePositionRadiusNauticalMiles)return "Aircraft is more than 1.5 NM from the manifest's last persisted position.";return "Ready.";}
    private void UpdateRestoreAvailability(){var l=Current.Live;var m=Current.Manifest;var f=Current.SavedFuel;var cr=l is null||m is null?"No cargo restoration is available.":RestoreReason(l,m,false);var fr=l is null||f is null?"No fuel restoration is available.":RestoreReason(l,f,true);Current=Current with{CanRestoreCargo=m?.Reconciliation==SimulatorReconciliationState.CargoAppearsMissing&&cr=="Ready.",CanRestoreFuel=f?.Reconciliation==FuelReconciliationState.AwaitingDecision&&fr=="Ready.",ReconciliationReason=$"Cargo: {cr} Fuel: {fr}"};}
    private (SimulatorReconciliationState state,string reason) ComparePayload(AircraftLoadingSnapshot live,CargoLoadManifest m){var target=m.Stations.ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms);if(PayloadMatches(live,target,options.PayloadToleranceKilograms))return(SimulatorReconciliationState.MatchesManifest,"Live payload matches the committed manifest; nothing was added.");var baseline=m.Stations.ToDictionary(x=>x.StationIndex,x=>x.BaselineKilograms);if(PayloadMatches(live,baseline,options.PayloadToleranceKilograms))return(SimulatorReconciliationState.CargoAppearsMissing,"Committed cargo appears missing from MSFS; restoration requires an explicit player action.");return(SimulatorReconciliationState.Indeterminate,"Live station weights differ from both manifest target and baseline; no automatic write occurred.");}
    private static bool PayloadMatches(AircraftLoadingSnapshot s,IReadOnlyDictionary<int,decimal> target,decimal tolerance)=>target.All(x=>s.PayloadStations.Any(p=>p.Index==x.Key&&Math.Abs(p.CurrentWeightKilograms-x.Value)<=tolerance));
    private static decimal CargoRemovalTarget(decimal current,decimal frontierCargo,decimal tolerance,int stationIndex)
    {var target=current-frontierCargo;if(target < -tolerance)throw new InvalidOperationException($"Station {stationIndex} no longer contains enough weight to remove Frontier cargo.");return Math.Max(0,target);}
    private static decimal PrototypeReturnTarget(decimal current, PlannedPayloadStation station, decimal tolerance)
    {
        // A fresh baseline reading means MSFS has already cleared this station's Frontier cargo.
        if (station.FrontierCargoKilograms == 0 || current <= station.BaselineKilograms + tolerance) return current;
        if (current >= station.TargetKilograms - tolerance)
            return CargoRemovalTarget(current, station.FrontierCargoKilograms, tolerance, station.StationIndex);
        throw new InvalidOperationException($"Station {station.StationIndex}: live {current:0.###} kg, baseline {station.BaselineKilograms:0.###} kg, loaded target {station.TargetKilograms:0.###} kg. Partial cargo cannot be distinguished from other payload; restore the manifest before returning this job.");
    }
    private bool ShouldCheckpoint(AircraftLoadingSnapshot s,PersistentAircraftFuelState f)=>utcNow()-(lastFuelCheckpoint??f.PersistedUtc)>=options.CheckpointInterval||s.FuelTanks.Any(x=>f.Tanks.SingleOrDefault(t=>t.Id==x.Id) is { } p&&Math.Abs(p.QuantityGallons-x.QuantityGallons)>=options.FuelToleranceGallons);
    private PersistentAircraftFuelState FuelState(AircraftLoadingSnapshot s,FuelReconciliationState state,string result)=>new PersistentAircraftFuelState("fuel-current-"+Normalize(LoadingIdentityExtensions.FromSnapshot(s).Title+"-"+LoadingIdentityExtensions.FromSnapshot(s).Type),LoadingIdentityExtensions.FromSnapshot(s),s.FuelTanks.Select(x=>new PersistedFuelTank(x.Id,x.Name,x.QuantityGallons,x.CapacityGallons)).ToArray(),s.Position,s.ObservedUtc,utcNow(),state,utcNow(),result).EnsureValid();
    private static string Normalize(string s){var chars=s.ToLowerInvariant().Select(c=>char.IsLetterOrDigit(c)?c:'-').ToArray();return new string(chars).Trim('-')[..Math.Min(40,new string(chars).Trim('-').Length)];}
    private static double DistanceNm(FlightPosition a,FlightPosition b)=>WorldTracking.DistanceMeters(a.Latitude,a.Longitude,b.Latitude,b.Longitude)/MetresPerNauticalMile;
    private void Notify()=>Changed?.Invoke(Current);
}

public sealed class CargoForfeitConfirmationRequiredException(string message):InvalidOperationException(message);

internal static class LoadingIdentityExtensions
{
    public static FlightAircraftIdentity FromSnapshot(AircraftLoadingSnapshot snapshot)=>snapshot.Aircraft.EnsureValid();
}





