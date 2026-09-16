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
    public decimal ActiveCargoKilograms => activeJob()?.CargoMassKilograms ?? Current.Manifest?.CargoKilograms ?? 0;
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
        if(Current.Live is not { } live)return;
        var reason=Access(live,false,checkSnapshotFreshness:false);
        Current=Current with{CanApplyCargo=reason=="Ready."&&flight.Current.CanLoadCargo,
            CargoActionReason=CargoReason(reason)};
        Notify();
    }

    private string CargoReason(string access)
    {
        if(Current.Manifest is {State:LoadManifestState.CommittedInTransit} manifest)
            return "Shipment already loaded and InTransit. " + (manifest.Reconciliation switch {
                SimulatorReconciliationState.CargoAppearsMissing => "Saved cargo is missing from MSFS. Use Restore manifest cargo; Apply cannot load this shipment again.",
                SimulatorReconciliationState.MatchesManifest => "MSFS matches the saved load. Continue the flight; no further loading is needed.",
                _ => "Read live aircraft to compare the saved load. No second load is permitted."
            });
        return access=="Ready."&&!flight.Current.CanLoadCargo?flight.Current.Reason:access;
    }

    public async Task InitializeAsync(CancellationToken token=default)
    {
        var manifests=await store.LoadManifestsAsync(token); var fuel=await store.LoadFuelStatesAsync(token);
        var attempt=flight.Current.Attempt;
        var recoverable=manifests.Where(x=>x.State is not (LoadManifestState.Completed or LoadManifestState.Abandoned)).OrderByDescending(x=>x.CreatedUtc).FirstOrDefault();
        var manifest=recoverable??(attempt is null?null:manifests.SingleOrDefault(x=>x.FlightAttemptId==attempt.Id));
        if(manifest is null&&attempt is {State:not FlightAttemptState.Delivered}&&activeJob() is {ShipmentId:not null} legacy)
        {
            manifest=new CargoLoadManifest("manifest-"+attempt.Id,legacy.JobId,legacy.ShipmentId!,attempt.Id,attempt.Aircraft,
                AircraftLoadingMode.SimulatedFrontierLoading,[],legacy.CargoMassKilograms,LoadManifestState.CommittedInTransit,
                attempt.LoadedUtc,CommittedUtc:attempt.LoadedUtc,VerificationResult:"Migrated existing simulated-loading shipment; MSFS payload was not changed.",LastValidPosition:attempt.LoadedPosition);
            await store.SaveManifestAsync(manifest,token);
        }
        if(attempt?.State==FlightAttemptState.Delivered&&manifest is {State:LoadManifestState.SimulatorUnloaded})
        {manifest=manifest with{State=LoadManifestState.Completed,CompletedUtc=attempt.DeliveredUtc,VerificationResult="Recovered after economic delivery; previously verified simulator unload is complete."};await store.SaveManifestAsync(manifest,token);}
        if(activeJob()?.ShipmentState==ShipmentState.InTransit&&manifest is {State:LoadManifestState.SimulatorApplied})
        {manifest=manifest with{State=LoadManifestState.CommittedInTransit,CommittedUtc=attempt?.LoadedUtc??utcNow(),VerificationResult="Recovered existing idempotent InTransit economy commit after verified simulator application."};await store.SaveManifestAsync(manifest,token);}
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
                CanApplyCargo=access.StartsWith("Ready.",StringComparison.Ordinal)&&flight.Current.CanLoadCargo,CanApplyFuel=fuelAccess=="Ready.",CanUnload=(flight.Current.CanUnloadCargo && manifest is not null)||manifest?.State==LoadManifestState.SimulatorUnloaded,
                FuelDecisionRequired=saved.Reconciliation==FuelReconciliationState.AwaitingDecision,Warnings=warnings};
            Current=Current with{CargoActionReason=CargoReason(access)};UpdateRestoreAvailability();Notify();
        }finally{gate.Release();}
    }

    public AircraftLoadPlan CreatePlan(IReadOnlyList<CargoStationAllocation> allocations)
    {
        var live=Current.Live??throw new InvalidOperationException("Read live aircraft loading data first.");
        var job=activeJob()??throw new InvalidOperationException("No active cargo shipment is selected.");
        var plan=AircraftLoadPlan.Create(live,job.CargoMassKilograms,allocations);Current=Current with {Plan=plan,Warnings=plan.Warnings};Notify();return plan;
    }

    public async Task ApplyCargoAsync(CancellationToken token=default)
    {
        await gate.WaitAsync(token);try
        {
            var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();
            if(Access(live,false)!="Ready."||!flight.Current.CanLoadCargo)throw new InvalidOperationException(Access(live,false)=="Ready."?flight.Current.Reason:Access(live,false));
            var job=activeJob()!; var attemptId=FlightAttemptIds.Attempt(job.ShipmentId!);
            var id="manifest-"+attemptId; var existing=(await store.LoadManifestsAsync(token)).SingleOrDefault(x=>x.Id==id);
            var plan=Current.Plan;
            if(existing is null&&plan is null)throw new InvalidOperationException("Allocate the full job cargo before applying it.");
            if(existing is null&&plan!.Stations.Any(x=>live.PayloadStations.All(s=>s.Index!=x.StationIndex)||
                Math.Abs(live.PayloadStations.Single(s=>s.Index==x.StationIndex).CurrentWeightKilograms-x.BaselineKilograms)>options.PayloadToleranceKilograms))
                throw new InvalidOperationException("Simulator payload changed after the plan was prepared. Read the aircraft again and review a new plan.");
            var manifest=existing??new CargoLoadManifest(id,job.JobId,job.ShipmentId!,attemptId,LoadingIdentityExtensions.FromSnapshot(live),
                AircraftLoadingMode.MsfsPayloadIntegration,plan!.Stations,plan.CargoKilograms,LoadManifestState.PendingApplication,utcNow(),
                VerificationToleranceKilograms:options.PayloadToleranceKilograms,LastValidPosition:live.Position);
            await store.SaveManifestAsync(manifest,token); // durable intent precedes external write
            AircraftLoadingSnapshot readBack;
            if(PayloadMatches(live,manifest.Stations.Select(x=>(x.StationIndex,x.TargetKilograms)).ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms),options.PayloadToleranceKilograms))readBack=live;
            else readBack=await simulator.WritePayloadAsync(sdkRoot(),manifest.Stations.ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms),token);
            var stations=manifest.Stations.Select(x=>x with {ReadBackKilograms=readBack.PayloadStations.Single(s=>s.Index==x.StationIndex).CurrentWeightKilograms}).ToArray();
            if(stations.Any(x=>Math.Abs(x.ReadBackKilograms!.Value-x.TargetKilograms)>options.PayloadToleranceKilograms))throw new IOException("MSFS payload read-back did not match the pending load intent.");
            manifest=manifest with {Stations=stations,State=LoadManifestState.SimulatorApplied,SimulatorAppliedUtc=utcNow(),VerificationResult="MSFS station weights written and verified.",LastReconciledUtc=utcNow(),Reconciliation=SimulatorReconciliationState.MatchesManifest};await store.SaveManifestAsync(manifest,token);
            await flight.LoadCargoAsync(AircraftLoadingMode.MsfsPayloadIntegration,token);
            manifest=manifest with {State=LoadManifestState.CommittedInTransit,CommittedUtc=utcNow(),VerificationResult="Simulator load verified; shipment committed InTransit."};await store.SaveManifestAsync(manifest,token);
            Current=Current with {Manifest=manifest,Live=readBack,CanApplyCargo=false,CanUnload=flight.Current.CanUnloadCargo,CargoActionReason="Cargo is committed InTransit."};Notify();
        }finally{gate.Release();}
    }

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
            manifest=manifest with {State=LoadManifestState.CommittedInTransit,CommittedUtc=utcNow()};await store.SaveManifestAsync(manifest,token);Current=Current with{Manifest=manifest,CanApplyCargo=false,CanUnload=flight.Current.CanUnloadCargo,CargoActionReason="Simulated cargo is committed InTransit; MSFS payload was not changed."};Notify();
        }finally{gate.Release();}
    }

    public async Task UnloadAsync(bool simulatedCompatibilityConfirmed=false,CancellationToken token=default)
    {
        await gate.WaitAsync(token);try
        {
            var manifest=Current.Manifest??throw new InvalidOperationException("No committed cargo manifest exists.");
            if(manifest.State==LoadManifestState.Completed)return;
            if(!flight.Current.CanUnloadCargo)throw new InvalidOperationException(flight.Current.Reason);
            if(manifest.Mode==AircraftLoadingMode.MsfsPayloadIntegration && !simulatedCompatibilityConfirmed && manifest.State!=LoadManifestState.SimulatorUnloaded)
            {
                var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();if(Access(live,false)!="Ready.")throw new InvalidOperationException(Access(live,false));
                var changed=manifest.State==LoadManifestState.PendingUnload?manifest.Stations.ToArray():manifest.Stations.Select(x=>{var now=live.PayloadStations.Single(s=>s.Index==x.StationIndex).CurrentWeightKilograms;var target=now-x.FrontierCargoKilograms;if(target<0)throw new InvalidOperationException($"Station {x.StationIndex} no longer contains enough weight to remove Frontier cargo.");return x with{UnloadTargetKilograms=target};}).ToArray();
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
            var manifest = Current.Manifest ?? throw new InvalidOperationException("No persistent cargo manifest exists.");
            if (manifest.State == LoadManifestState.Abandoned) return;
            if (manifest.State is not (LoadManifestState.CommittedInTransit or LoadManifestState.PendingAbandonment))
                throw new InvalidOperationException("Only a committed incomplete prototype shipment can be skipped.");
            if (manifest.Mode == AircraftLoadingMode.MsfsPayloadIntegration)
            {
                var live = (await simulator.ReadAsync(sdkRoot(), token)).EnsureValid();
                var access = Access(live, false); if (access != "Ready.") throw new InvalidOperationException(access);
                PlannedPayloadStation[] stations;
                if (manifest.State == LoadManifestState.PendingAbandonment)
                    stations = manifest.Stations.ToArray();
                else
                {
                    stations = manifest.Stations.Select(x =>
                    {
                        var current = live.PayloadStations.Single(s => s.Index == x.StationIndex).CurrentWeightKilograms;
                        var target = current - x.FrontierCargoKilograms;
                        if (target < 0) throw new InvalidOperationException($"Station {x.StationIndex} no longer contains enough weight to remove Frontier cargo.");
                        return x with { UnloadTargetKilograms = target };
                    }).ToArray();
                    manifest = manifest with { Stations = stations, State = LoadManifestState.PendingAbandonment,
                        UnloadStartedUtc = utcNow(), VerificationResult = "Prototype-skip unload intent persisted before MSFS write." };
                    await store.SaveManifestAsync(manifest, token);
                }
                var targets = stations.ToDictionary(x => x.StationIndex, x => x.UnloadTargetKilograms ??
                    throw new InvalidDataException("Prototype-skip unload target is missing."));
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
        if(flight.Current.Attempt is { } activeAttempt&&activeAttempt.Aircraft.ClearlyDiffersFrom(LoadingIdentityExtensions.FromSnapshot(live)))return "Live aircraft identity differs from the active flight attempt.";
        if(fuel){if(live.Engines.Count==0||live.Engines.Any(x=>x.IsRunning is null))return "Engine state is unavailable; fuel changes are refused.";if(live.Engines.Any(x=>x.IsRunning==true))return "Every detected engine must be off before changing fuel.";}
        return "Ready.";
    }
    private string RestoreReason(AircraftLoadingSnapshot live,PersistentAircraftFuelState saved,bool fuel)
    {var basic=Access(live,fuel);if(basic!="Ready.")return basic;if(saved.Aircraft.ClearlyDiffersFrom(LoadingIdentityExtensions.FromSnapshot(live)))return "Aircraft identity differs from the saved state.";if(DistanceNm(live.Position,saved.LastValidPosition)>options.RestorePositionRadiusNauticalMiles)return "Aircraft is more than 1.5 NM from its last persisted position.";if(live.FuelTanks.Select(x=>x.Id).Order().SequenceEqual(saved.Tanks.Select(x=>x.Id).Order())==false)return "Fuel-tank layout differs from the saved state.";return "Ready.";}
    private string RestoreReason(AircraftLoadingSnapshot live,CargoLoadManifest manifest,bool fuel)
    {var basic=Access(live,fuel);if(basic!="Ready.")return basic;if(manifest.Aircraft.ClearlyDiffersFrom(LoadingIdentityExtensions.FromSnapshot(live)))return "Aircraft identity differs from the cargo manifest.";if(manifest.LastValidPosition is null||DistanceNm(live.Position,manifest.LastValidPosition)>options.RestorePositionRadiusNauticalMiles)return "Aircraft is more than 1.5 NM from the manifest's last persisted position.";return "Ready.";}
    private void UpdateRestoreAvailability(){var l=Current.Live;var m=Current.Manifest;var f=Current.SavedFuel;var cr=l is null||m is null?"No cargo restoration is available.":RestoreReason(l,m,false);var fr=l is null||f is null?"No fuel restoration is available.":RestoreReason(l,f,true);Current=Current with{CanRestoreCargo=m?.Reconciliation==SimulatorReconciliationState.CargoAppearsMissing&&cr=="Ready.",CanRestoreFuel=f?.Reconciliation==FuelReconciliationState.AwaitingDecision&&fr=="Ready.",ReconciliationReason=$"Cargo: {cr} Fuel: {fr}"};}
    private (SimulatorReconciliationState state,string reason) ComparePayload(AircraftLoadingSnapshot live,CargoLoadManifest m){var target=m.Stations.ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms);if(PayloadMatches(live,target,options.PayloadToleranceKilograms))return(SimulatorReconciliationState.MatchesManifest,"Live payload matches the committed manifest; nothing was added.");var baseline=m.Stations.ToDictionary(x=>x.StationIndex,x=>x.BaselineKilograms);if(PayloadMatches(live,baseline,options.PayloadToleranceKilograms))return(SimulatorReconciliationState.CargoAppearsMissing,"Committed cargo appears missing from MSFS; restoration requires an explicit player action.");return(SimulatorReconciliationState.Indeterminate,"Live station weights differ from both manifest target and baseline; no automatic write occurred.");}
    private static bool PayloadMatches(AircraftLoadingSnapshot s,IReadOnlyDictionary<int,decimal> target,decimal tolerance)=>target.All(x=>s.PayloadStations.Any(p=>p.Index==x.Key&&Math.Abs(p.CurrentWeightKilograms-x.Value)<=tolerance));
    private bool ShouldCheckpoint(AircraftLoadingSnapshot s,PersistentAircraftFuelState f)=>utcNow()-(lastFuelCheckpoint??f.PersistedUtc)>=options.CheckpointInterval||s.FuelTanks.Any(x=>f.Tanks.SingleOrDefault(t=>t.Id==x.Id) is { } p&&Math.Abs(p.QuantityGallons-x.QuantityGallons)>=options.FuelToleranceGallons);
    private PersistentAircraftFuelState FuelState(AircraftLoadingSnapshot s,FuelReconciliationState state,string result)=>new PersistentAircraftFuelState("fuel-current-"+Normalize(LoadingIdentityExtensions.FromSnapshot(s).Title+"-"+LoadingIdentityExtensions.FromSnapshot(s).Type),LoadingIdentityExtensions.FromSnapshot(s),s.FuelTanks.Select(x=>new PersistedFuelTank(x.Id,x.Name,x.QuantityGallons,x.CapacityGallons)).ToArray(),s.Position,s.ObservedUtc,utcNow(),state,utcNow(),result).EnsureValid();
    private static string Normalize(string s){var chars=s.ToLowerInvariant().Select(c=>char.IsLetterOrDigit(c)?c:'-').ToArray();return new string(chars).Trim('-')[..Math.Min(40,new string(chars).Trim('-').Length)];}
    private static double DistanceNm(FlightPosition a,FlightPosition b)=>WorldTracking.DistanceMeters(a.Latitude,a.Longitude,b.Latitude,b.Longitude)/MetresPerNauticalMile;
    private void Notify()=>Changed?.Invoke(Current);
}

internal static class LoadingIdentityExtensions
{
    public static FlightAircraftIdentity FromSnapshot(AircraftLoadingSnapshot snapshot)=>snapshot.Aircraft.EnsureValid();
}





