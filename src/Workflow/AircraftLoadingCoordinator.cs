using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed record AircraftLoadingOptions(double AvatarRadiusMeters = 100, double RestorePositionRadiusNauticalMiles = 1.5,
    TimeSpan? TelemetryMaximumAge = null, TimeSpan? FuelCheckpointInterval = null, decimal PayloadToleranceKilograms = .25m,
    decimal FuelToleranceGallons = .05m, TimeSpan? PayloadRetentionDelay = null, decimal GrossWeightToleranceKilograms = 1,
    TimeSpan? BoardedAuthorizationDuration = null, bool RequirePostBoardingCargoVerification = true)
{
    public TimeSpan MaximumAge => TelemetryMaximumAge ?? TimeSpan.FromSeconds(3);
    public TimeSpan CheckpointInterval => FuelCheckpointInterval ?? TimeSpan.FromSeconds(30);
    public TimeSpan RetentionDelay => PayloadRetentionDelay ?? TimeSpan.FromSeconds(3);
    public TimeSpan AuthorizationDuration => BoardedAuthorizationDuration ?? TimeSpan.FromMinutes(5);
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
    private readonly Func<TrackingWorldState?> tracking; private readonly Func<IReadOnlyList<CargoJobDisplay>> selectedJobs;
    private readonly Func<DateTimeOffset> utcNow; private readonly Func<TimeSpan,CancellationToken,Task> delay;
    private readonly AircraftLoadingOptions options; private readonly SemaphoreSlim gate = new(1,1);
    private bool continuouslyConnected; private DateTimeOffset? lastFuelCheckpoint;
    public AircraftLoadingStatus Current { get; private set; } = AircraftLoadingStatus.Empty;
    public event Action<AircraftLoadingStatus>? Changed;
    public string StoragePath => store.StoragePath;
    private IReadOnlyList<CargoJobDisplay> ActiveJobs()=>selectedJobs();
    private CargoJobDisplay? activeJob()=>ActiveJobs().FirstOrDefault();
    public string? ActiveShipmentId=>activeJob()?.ShipmentId;
    public decimal JobCargoKilograms=>ActiveJobs().Count>0?ActiveJobs().Sum(x=>x.CargoMassKilograms):Current.Manifest?.ShipmentQuantityKilograms??0;
    public decimal LoadedCargoKilograms=>Current.Manifest switch
    {
        {State:LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied} pending => pending.PriorCargoKilograms,
        { } committed => committed.CargoKilograms,
        _ => ActiveJobs().Sum(x=>x.LoadedCargoKilograms)
    };
    public decimal? SimulatorDetectedCargoKilograms=>Current.Manifest switch
    {
        null or {Mode:AircraftLoadingMode.SimulatedFrontierLoading} => null,
        {Reconciliation:SimulatorReconciliationState.CargoAppearsMissing} => 0,
        {State:LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied,Reconciliation:SimulatorReconciliationState.MatchesManifest} pending when Current.Live is { } live =>
            Math.Min(pending.PriorCargoKilograms,pending.Stations.Sum(x=>Math.Max(0,live.PayloadStations.Single(p=>p.Index==x.StationIndex).CurrentWeightKilograms-x.BaselineKilograms))),
        {Reconciliation:SimulatorReconciliationState.MatchesManifest} manifest when Current.Live is { } live =>
            DetectedCargo(manifest,live),
        _ => null
    };
    public decimal DeliveredCargoKilograms=>ActiveJobs().Count>0?ActiveJobs().Sum(x=>x.DeliveredCargoKilograms):Current.Manifest?.DeliveredCargoKilograms??0;
    public decimal CargoAvailableAtCurrentLocation
    {
        get
        {
            var jobs=ActiveJobs();var live=Current.Live;
            if(jobs.Count==0||live is null||jobs.Any(job=>job.ShipmentState is not (ShipmentState.Reserved or ShipmentState.Loaded)||CurrentLocation(job,live.Position)!=job.OriginLocationId))return 0;
            return Math.Max(0,jobs.Sum(job=>job.CargoMassKilograms-job.DeliveredCargoKilograms)-LoadedCargoKilograms);
        }
    }
    public decimal ActiveCargoKilograms => ActiveJobs().Count>0 ? Math.Max(0, ActiveJobs().Sum(job=>job.CargoMassKilograms-job.DeliveredCargoKilograms)) :
        Current.Manifest is { State: not (LoadManifestState.Completed or LoadManifestState.Abandoned) } manifest ? manifest.CargoKilograms : 0;
    public bool CanAbandonPrototypeMission => Current.Manifest?.State is LoadManifestState.CommittedInTransit or LoadManifestState.PendingAbandonment;
    public bool CanApplyAuthorizedCargoAfterBoarding=>Current.Live is { } live&&Current.Manifest is { } manifest&&BoardedApplicationReason(live,manifest)=="Ready.";
    public string AuthorizedBoardedCargoReason=>Current.Live is { } live&&Current.Manifest is { } manifest?BoardedApplicationReason(live,manifest):"No boarded cargo application is authorized.";
    public object CreateDiagnosticSnapshot()
    {
        var live=Current.Live;var saved=Current.SavedFuel;var manifest=Current.Manifest;
        return new
        {
            LoadingMode=manifest?.Mode.ToString(),ManifestState=manifest?.State.ToString(),manifest?.Id,manifest?.JobId,manifest?.ShipmentId,
            AircraftIdentity=manifest?.Aircraft,CurrentAircraftIdentity=live?.Aircraft,ManifestReconciliation=manifest?.Reconciliation.ToString(),
            ManifestResult=manifest?.VerificationResult,VerificationToleranceKilograms=manifest?.VerificationToleranceKilograms,
            PayloadRetentionDelaySeconds=options.RetentionDelay.TotalSeconds,
            BoardedAuthorizationDurationSeconds=options.AuthorizationDuration.TotalSeconds,
            manifest?.BoardedAuthorizationUtc,manifest?.BoardedAuthorizationPosition,manifest?.BoardedBaselineGrossWeightKilograms,manifest?.BoardedExpectedGrossWeightKilograms,
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
        AircraftLoadingOptions? options = null, Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan,CancellationToken,Task>? delay = null)
        : this(store,simulator,flight,sdkRoot,tracking,()=>activeJob() is { } job?[job]:[],options,utcNow,delay) { }

    public AircraftLoadingCoordinator(IAircraftLoadingStore store, IAircraftLoadingSimulator simulator, IFlightAttemptTracker flight,
        Func<string> sdkRoot, Func<TrackingWorldState?> tracking, Func<IReadOnlyList<CargoJobDisplay>> selectedJobs,
        AircraftLoadingOptions? options = null, Func<DateTimeOffset>? utcNow = null,
        Func<TimeSpan,CancellationToken,Task>? delay = null)
    { this.store=store;this.simulator=simulator;this.flight=flight;this.sdkRoot=sdkRoot;this.tracking=tracking;this.selectedJobs=selectedJobs;
      this.options=options??new();this.utcNow=utcNow??(()=>DateTimeOffset.UtcNow);this.delay=delay??Task.Delay;
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
        if(Current.Plan is { } changedPlan && Current.Manifest is { } changedManifest &&
            Math.Abs(changedPlan.CargoKilograms-changedManifest.CargoKilograms)>.001m && access=="Ready.")
            return changedPlan.CargoKilograms>changedManifest.CargoKilograms?"Ready to add reserved cargo and verify the requested aircraft state.":"Ready to remove Frontier cargo and verify the requested aircraft state.";
        if(Current.Manifest is {Reconciliation:SimulatorReconciliationState.RestoreAvailable} authorized)
            return BoardedApplicationReason(Current.Live!,authorized)=="Ready."
                ? "Outside loading checks passed. Apply the authorized cargo now while seated in the same stationary aircraft."
                : AuthorizedBoardedCargoReason;
        if(access!="Ready.")return access;
        if(activeJob() is not null&&(Current.Manifest?.CargoKilograms??0)<=.001m&&!flight.Current.CanLoadCargo)
            return flight.Current.Reason;
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
            manifest=new CargoLoadManifest(AircraftLoadingIds.Manifest(attempt.Id),legacy.JobId,legacy.ShipmentId!,attempt.Id,attempt.Aircraft,
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
        else if(world is not null&&Current.Manifest?.Reconciliation==SimulatorReconciliationState.RestoreAvailable)
            RefreshEligibility();
    }

    public async Task RefreshAsync(CancellationToken token=default)
    {
        await gate.WaitAsync(token);try
        {
            var snapshot=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid(); var wasConnected=continuouslyConnected; continuouslyConnected=true;
            var manifest=Current.Manifest; var saved=Current.SavedFuel; var warnings=new List<string>();
            if(manifest is {Mode:AircraftLoadingMode.MsfsPayloadIntegration,State:LoadManifestState.CommittedInTransit,BoardedAuthorizationUtc:null})
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
        var jobs=ActiveJobs();var job=jobs.FirstOrDefault(); var manifest=Current.Manifest;
        if(job is null&&manifest is null)throw new InvalidOperationException("No Frontier-managed cargo is aboard or available at this location.");
        var location=job is null?null:CurrentLocation(job,live.Position);
        var aboard=manifest?.CargoKilograms??job?.LoadedCargoKilograms??0;
        var maximum=jobs.Count>0&&location==job!.OriginLocationId&&jobs.All(x=>x.ShipmentState is ShipmentState.Reserved or ShipmentState.Loaded)
            ? jobs.Sum(x=>x.CargoMassKilograms-x.DeliveredCargoKilograms) : aboard;
        var requested=allocations.Sum(x=>x.CargoKilograms);
        var plan=CanResetUncommittedIntent(job,manifest)&&requested<=.001m
            ? manifest!.CargoKilograms<=.001m ? PlanFromManifest(manifest) : AircraftLoadPlan.CreateUncommittedReset(live,manifest)
            : AircraftLoadPlan.CreateDesiredState(live,allocations,manifest,Math.Max(0,maximum),toleranceKilograms:options.PayloadToleranceKilograms);
        Current=Current with {Plan=plan,Warnings=plan.Warnings,CanApplyCargo=CanApplyDesiredState(live,"Ready.")};Notify();return plan;
    }

    public async Task ApplyCargoAsync(bool confirmForfeit=false,CancellationToken token=default)
    {
        await gate.WaitAsync(token);try
        {
            var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();
            var access=Access(live,false);if(access!="Ready.")throw new InvalidOperationException(access);
            var jobs=ActiveJobs();var job=jobs.FirstOrDefault();var existing=Current.Manifest;
            var plan=Current.Plan??(existing is {State:LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied}
                ? PlanFromManifest(existing) : throw new InvalidOperationException("Choose the desired Frontier cargo in each station before applying."));
            if(existing is null&&job?.ShipmentId is null)throw new InvalidOperationException("No active shipment cargo is available to load.");
            var recoveringIntent=existing?.State is LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied;
            var previouslyVerifiedSimulatorApplication=existing?.State==LoadManifestState.SimulatorApplied;
            var currentCargo=recoveringIntent?(jobs.Count>0?jobs.Sum(x=>x.LoadedCargoKilograms):existing!.PriorCargoKilograms):(existing?.CargoKilograms??jobs.Sum(x=>x.LoadedCargoKilograms));
            var delta=plan.CargoKilograms-currentCargo;
            var resettingUncommitted=CanResetUncommittedIntent(job,existing)&&plan.CargoKilograms<=.001m;
            if(Math.Abs(delta)<=.001m&&!resettingUncommitted)throw new InvalidOperationException("The requested Frontier cargo already matches the persistent aircraft state.");
            var location=job is null?null:CurrentLocation(job,live.Position);
            if(delta>0&&(job is null||location!=job.OriginLocationId||!flight.Current.CanLoadCargo))
                throw new InvalidOperationException(job is null?"No reserved shipment cargo is available to add.":location!=job.OriginLocationId?"Reserved cargo can only be loaded at the shipment origin.":flight.Current.Reason);
            if(delta<0&&job is not null&&location!=job.DestinationLocationId&&
                (location!=job.OriginLocationId||jobs.Any(x=>x.ShipmentState==ShipmentState.InTransit))&&!confirmForfeit)
                throw new CargoForfeitConfirmationRequiredException("This cargo belongs to an active delivery. Unloading it here will forfeit the job and you will not receive payment.");
            if(delta<0&&job is not null&&location==job.DestinationLocationId&&!flight.Current.CanUnloadCargo)
                throw new InvalidOperationException(flight.Current.Reason);
            var attemptId=existing?.FlightAttemptId??FlightAttemptIds.Attempt(job!.ShipmentId!);
            var id=existing?.Id??AircraftLoadingIds.Manifest(attemptId);
            var commandId=existing?.State is (LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied)&&existing.LastTransferCommandId is not null
                ? existing.LastTransferCommandId : "cargo-transfer-"+Guid.NewGuid().ToString("N");
            var shipmentLoads=AllocateShipmentLoads(existing,jobs,plan.CargoKilograms,location==job?.DestinationLocationId);
            var manifest=(existing??new CargoLoadManifest(id,job!.JobId,job.ShipmentId!,attemptId,LoadingIdentityExtensions.FromSnapshot(live),
                AircraftLoadingMode.MsfsPayloadIntegration,plan.Stations,plan.CargoKilograms,LoadManifestState.PendingApplication,utcNow(),
                VerificationToleranceKilograms:options.PayloadToleranceKilograms,LastValidPosition:live.Position)
                {ShipmentQuantityKilograms=jobs.Sum(x=>x.CargoMassKilograms),DeliveredCargoKilograms=jobs.Sum(x=>x.DeliveredCargoKilograms),
                    Shipments=shipmentLoads,IsDesiredStateManifest=true}) with
                {Stations=plan.Stations,CargoKilograms=plan.CargoKilograms,PriorCargoKilograms=currentCargo,State=LoadManifestState.PendingApplication,
                    Shipments=shipmentLoads,LastTransferCommandId=commandId,SimulatorAppliedUtc=null,CompletedUtc=null,
                    VerificationResult=$"Desired-state intent persisted. Previous managed cargo {currentCargo:0.###} kg; requested {plan.CargoKilograms:0.###} kg."};
            await store.SaveManifestAsync(manifest,token); // durable intent precedes external write
            // The outside step proves access and durably records the exact plan. The physical
            // payload write happens after boarding, because some aircraft clear station writes
            // during the walkaround-to-cockpit transition.
            if(delta>options.PayloadToleranceKilograms&&!previouslyVerifiedSimulatorApplication&&options.RequirePostBoardingCargoVerification)
            {
                manifest=AuthorizeBoardedApplication(manifest,live,delta,"Outside loading plan authorized; MSFS payload was not changed.");
                await store.SaveManifestAsync(manifest,token);
                Current=Current with{Live=live,Manifest=manifest,
                    CargoActionReason="Loading plan saved. Board the same aircraft, then use the main cargo action to apply and verify it."};
                UpdateRestoreAvailability();Notify();return;
            }
            var targets=manifest.Stations.ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms);
            var verificationTargets=manifest.Stations.Where(x=>x.FrontierCargoKilograms>options.PayloadToleranceKilograms||
                Math.Abs(live.PayloadStations.Single(p=>p.Index==x.StationIndex).CurrentWeightKilograms-x.TargetKilograms)>options.PayloadToleranceKilograms)
                .ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms);
            var immediateReadBack=PayloadMatches(live,targets,options.PayloadToleranceKilograms)
                ? live : await simulator.WritePayloadAsync(sdkRoot(),targets,token);
            var stations=manifest.Stations.Select(x=>x with {ReadBackKilograms=immediateReadBack.PayloadStations.Single(s=>s.Index==x.StationIndex).CurrentWeightKilograms}).ToArray();
            if(stations.Any(x=>Math.Abs(x.ReadBackKilograms!.Value-x.TargetKilograms)>options.PayloadToleranceKilograms))
            {var mismatch=string.Join("; ",stations.Where(x=>Math.Abs(x.ReadBackKilograms!.Value-x.TargetKilograms)>options.PayloadToleranceKilograms).Select(x=>$"station {x.StationIndex}: baseline {x.BaselineKilograms:0.###}, managed {x.FrontierCargoKilograms:0.###}, expected {x.TargetKilograms:0.###}, observed {x.ReadBackKilograms:0.###} kg"));throw new IOException("MSFS payload read-back did not match the desired state: "+mismatch);}
            await delay(options.RetentionDelay,token);
            var readBack=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();
            stations=stations.Select(x=>x with {ReadBackKilograms=readBack.PayloadStations.Single(s=>s.Index==x.StationIndex).CurrentWeightKilograms}).ToArray();
            if(!PayloadMatches(readBack,verificationTargets,options.PayloadToleranceKilograms))
            {
                var comparison=ComparePayload(readBack,existing??manifest with{Stations=stations});var mismatch=PayloadMismatch(stations,readBack);
                manifest=manifest with{Stations=stations,Reconciliation=comparison.state,LastReconciledUtc=utcNow(),
                    VerificationResult="MSFS accepted the requested payload initially but did not retain it: "+mismatch};
                await store.SaveManifestAsync(manifest,token);Current=Current with{Live=readBack,Manifest=manifest,
                    CargoActionReason="MSFS did not retain the requested Frontier cargo. No shipment inventory was committed; review or retry explicitly."};
                UpdateRestoreAvailability();Notify();
                throw new IOException("MSFS accepted the payload write but did not retain it after the verification delay: "+mismatch);
            }
            if(resettingUncommitted)
            {
                await flight.ResetUncommittedCargoLoadAsync(token);
                manifest=manifest with{Stations=stations,CargoKilograms=0,PriorCargoKilograms=0,State=LoadManifestState.ResetAtOrigin,
                    CompletedUtc=utcNow(),BoardedAuthorizationUtc=null,BoardedAuthorizationPosition=null,
                    BoardedBaselineGrossWeightKilograms=null,BoardedExpectedGrossWeightKilograms=null,
                    Reconciliation=SimulatorReconciliationState.MatchesManifest,LastReconciledUtc=utcNow(),LastValidPosition=readBack.Position,
                    VerificationResult="Uncommitted Frontier payload intent cleared and verified; shipment remains reserved at its origin."};
                await store.SaveManifestAsync(manifest,token);
                Current=Current with{Manifest=manifest,Live=readBack,Plan=null,CanApplyCargo=false,CanUnload=false,
                    CargoActionReason="Aircraft cargo reset. The shipment remains reserved at the origin; complete loading qualification and load it again."};
                Notify();return;
            }
            manifest=manifest with {Stations=stations,State=LoadManifestState.SimulatorApplied,SimulatorAppliedUtc=utcNow(),VerificationResult=$"MSFS desired station weights remained present after {options.RetentionDelay.TotalSeconds:0.#} seconds; persistent cargo transfer pending.",LastReconciledUtc=utcNow(),Reconciliation=SimulatorReconciliationState.MatchesManifest};await store.SaveManifestAsync(manifest,token);
            if(job is not null)
                await flight.ApplyCargoTransfersAsync("transfer-"+commandId,commandId,shipmentLoads.ToDictionary(x=>x.JobId,x=>x.LoadedKilograms),location,live.Position.Latitude,live.Position.Longitude,confirmForfeit,AircraftLoadingMode.MsfsPayloadIntegration,token);
            var delivered=shipmentLoads.Sum(x=>x.DeliveredKilograms);
            manifest=manifest with {State=flight.Current.Attempt?.State==FlightAttemptState.Delivered?LoadManifestState.Completed:LoadManifestState.CommittedInTransit,
                CommittedUtc=utcNow(),CompletedUtc=flight.Current.Attempt?.State==FlightAttemptState.Delivered?utcNow():null,
                DeliveredCargoKilograms=delivered,VerificationResult=$"Desired aircraft state verified and committed. Frontier cargo aboard: {plan.CargoKilograms:0.###} kg.",
                Reconciliation=SimulatorReconciliationState.MatchesManifest,LastReconciledUtc=utcNow(),LastValidPosition=readBack.Position};await store.SaveManifestAsync(manifest,token);
            Current=Current with {Manifest=manifest,Live=readBack,Plan=null,CanApplyCargo=false,CanUnload=flight.Current.CanUnloadCargo,
                CargoActionReason=manifest.State==LoadManifestState.Completed?"Delivery completed and paid exactly once.":$"Verified aircraft state: {manifest.CargoKilograms:0.###} kg Frontier cargo aboard."};Notify();
        }finally{gate.Release();}
    }

    public Task ApplyCargoAsync(CancellationToken token)=>ApplyCargoAsync(false,token);

    private static bool CanResetUncommittedIntent(CargoJobDisplay? job,CargoLoadManifest? manifest)=>
        job is {ShipmentState:ShipmentState.Reserved,LoadedCargoKilograms:<=.001m}&&
        manifest is {State:LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied,PriorCargoKilograms:<=.001m};

    private static AircraftLoadPlan PlanFromManifest(CargoLoadManifest manifest) => new(
        manifest.CargoKilograms, manifest.Stations, manifest.Stations.Sum(x => x.TargetKilograms), null, null,
        ["Recovered the exact persisted desired-state intent; MSFS read-back will be verified before persistent cargo changes are committed."]);

    private static IReadOnlyList<ManifestShipmentCargo> AllocateShipmentLoads(CargoLoadManifest? existing,
        IReadOnlyList<CargoJobDisplay> jobs,decimal requestedTotal,bool delivering)
    {
        var source=existing?.Shipments.Count>0?existing.Shipments:
            jobs.Select(x=>new ManifestShipmentCargo(x.JobId,x.ShipmentId!,x.CargoMassKilograms,x.LoadedCargoKilograms,x.DeliveredCargoKilograms)).ToArray();
        if(source.Count==0)throw new InvalidOperationException("No selected shipment cargo is available.");
        var remaining=Math.Max(0,requestedTotal);var result=new List<ManifestShipmentCargo>();
        foreach(var item in source)
        {
            var capacity=item.QuantityKilograms-item.DeliveredKilograms;
            var loaded=Math.Min(capacity,remaining);remaining-=loaded;
            var delivered=item.DeliveredKilograms+(delivering?Math.Max(0,item.LoadedKilograms-loaded):0);
            result.Add(item with{LoadedKilograms=loaded,DeliveredKilograms=delivered});
        }
        if(remaining>.001m)throw new InvalidOperationException("Requested cargo exceeds the selected reserved shipments.");
        return result;
    }

    public async Task ApplySimulatedCargoAsync(CancellationToken token=default)
    {
        await gate.WaitAsync(token);try
        {
            var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();
            if(Access(live,false)!="Ready."||!flight.Current.CanLoadCargo)throw new InvalidOperationException(Access(live,false)=="Ready."?flight.Current.Reason:Access(live,false));
            var job=activeJob()!;var attemptId=FlightAttemptIds.Attempt(job.ShipmentId!);var id=AircraftLoadingIds.Manifest(attemptId);
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
    {
        await gate.WaitAsync(token);try
        {
            var manifest=Current.Manifest??throw new InvalidOperationException("No cargo manifest exists.");
            if(manifest.State is not (LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied or LoadManifestState.CommittedInTransit))
                throw new InvalidOperationException("This cargo manifest cannot be authorized for aircraft restoration.");
            var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();var reason=RestoreReason(live,manifest,false);
            if(reason!="Ready.")throw new InvalidOperationException(reason);
            if(!options.RequirePostBoardingCargoVerification)
            {
                var targets=ManagedTargets(manifest);var immediate=await simulator.WritePayloadAsync(sdkRoot(),targets,token);
                if(!PayloadMatches(immediate,targets,options.PayloadToleranceKilograms))throw new IOException("Restored cargo did not pass immediate MSFS read-back verification.");
                await delay(options.RetentionDelay,token);var read=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();
                if(!PayloadMatches(read,targets,options.PayloadToleranceKilograms))
                {
                    var comparison=ComparePayload(read,manifest);manifest=manifest with{Reconciliation=comparison.state,LastReconciledUtc=utcNow(),VerificationResult="Player-confirmed restoration was accepted initially but not retained: "+PayloadMismatch(manifest.Stations,read)};
                    await store.SaveManifestAsync(manifest,token);Current=Current with{Live=read,Manifest=manifest};UpdateRestoreAvailability();Notify();
                    throw new IOException("MSFS accepted the restored cargo but did not retain it after the verification delay: "+PayloadMismatch(manifest.Stations,read));
                }
                manifest=manifest with{Reconciliation=SimulatorReconciliationState.MatchesManifest,LastReconciledUtc=utcNow(),VerificationResult=$"Player-confirmed cargo restoration remained present after {options.RetentionDelay.TotalSeconds:0.#} seconds."};
                await store.SaveManifestAsync(manifest,token);Current=Current with{Live=read,Manifest=manifest};UpdateRestoreAvailability();Notify();return;
            }
            var expectedDelta=manifest.State is LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied
                ? Math.Max(0,manifest.CargoKilograms-manifest.PriorCargoKilograms)
                : Math.Max(0,manifest.CargoKilograms-DetectedCargo(manifest,live));
            if(expectedDelta<=options.PayloadToleranceKilograms)
                throw new InvalidOperationException("The reported aircraft already contains the saved Frontier cargo; no restoration is required.");
            manifest=AuthorizeBoardedApplication(manifest,live,expectedDelta,"Outside cargo restoration authorized; MSFS payload was not changed.");
            await store.SaveManifestAsync(manifest,token);
            Current=Current with{Live=live,Manifest=manifest,
                CargoActionReason="Cargo restoration authorized. Board the same aircraft, then use the main cargo action to apply and verify it."};
            UpdateRestoreAvailability();Notify();
        }finally{gate.Release();}
    }

    public async Task ApplyAuthorizedCargoAfterBoardingAsync(CancellationToken token=default)
    {
        await gate.WaitAsync(token);try
        {
            var manifest=Current.Manifest??throw new InvalidOperationException("No outside cargo authorization exists.");
            var live=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();
            var reason=BoardedApplicationReason(live,manifest);if(reason!="Ready.")throw new InvalidOperationException(reason);
            if(manifest.CargoKilograms<=options.PayloadToleranceKilograms)throw new InvalidOperationException("The authorized cargo application has no positive aircraft-weight change to verify.");
            var baselineGross=live.Weights.CurrentGrossWeightKilograms??throw new InvalidOperationException("MSFS did not report current total aircraft weight; boarded cargo application cannot be verified.");
            var targets=ManagedTargets(manifest);if(targets.Count==0)throw new InvalidOperationException("The authorized manifest contains no positive Frontier cargo allocation.");
            var retainedAcrossBoarding=PayloadMatches(live,targets,options.PayloadToleranceKilograms)&&manifest.BoardedExpectedGrossWeightKilograms is { } outsideExpected&&Math.Abs(baselineGross-outsideExpected)<=options.GrossWeightToleranceKilograms;
            if(!retainedAcrossBoarding&&PayloadMatches(live,targets,options.PayloadToleranceKilograms))
            {
                // A previous write may have succeeded before verification/commit failed. Never
                // expect another cargo-sized increase from writing identical station weights.
                // Re-establish a measured baseline using only this authorized allocation.
                var baselineTargets=manifest.Stations.Where(x=>targets.ContainsKey(x.StationIndex)).ToDictionary(x=>x.StationIndex,x=>x.BaselineKilograms);
                manifest=manifest with{VerificationResult="Authorized retry: verify managed station baseline and gross-weight removal before reapplying the persisted targets."};
                await store.SaveManifestAsync(manifest,token);
                await simulator.WritePayloadAsync(sdkRoot(),baselineTargets,token);await delay(options.RetentionDelay,token);
                var cleared=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();
                if(!PayloadMatches(cleared,baselineTargets,options.PayloadToleranceKilograms)||
                    !GrossChangedBy(live,cleared,-manifest.CargoKilograms,out _))
                    throw new IOException("Retry could not verify removal of the authorized allocation in station and total weight readings. Shipment remains pending; refresh and retry.");
                live=cleared;baselineGross=live.Weights.CurrentGrossWeightKilograms!.Value;
            }
            var physicalDelta=targets.Sum(x=>x.Value-live.PayloadStations.Single(p=>p.Index==x.Key).CurrentWeightKilograms);
            if(!retainedAcrossBoarding&&physicalDelta<=options.PayloadToleranceKilograms)
                throw new InvalidOperationException("Live managed station weights do not provide a positive cargo delta; reconcile the aircraft before retrying.");
            var expectedGross=retainedAcrossBoarding?baselineGross:baselineGross+physicalDelta;
            manifest=manifest with{BoardedBaselineGrossWeightKilograms=baselineGross,BoardedExpectedGrossWeightKilograms=expectedGross,
                VerificationResult=retainedAcrossBoarding?$"Post-boarding verification intent persisted; MSFS still reports the authorized station and total weight {baselineGross:0.###} kg.":$"Boarded apply intent persisted. Expected total aircraft weight change: +{physicalDelta:0.###} kg, from {baselineGross:0.###} to {expectedGross:0.###} kg."};
            await store.SaveManifestAsync(manifest,token);
            if(!retainedAcrossBoarding){await simulator.WritePayloadAsync(sdkRoot(),targets,token);await delay(options.RetentionDelay,token);}
            var read=(await simulator.ReadAsync(sdkRoot(),token)).EnsureValid();
            if(!PayloadMatches(read,targets,options.PayloadToleranceKilograms))
                throw new IOException("MSFS did not retain the authorized station values while seated: "+PayloadMismatch(manifest.Stations,read));
            if(read.Weights.CurrentGrossWeightKilograms is not { } observedGross||Math.Abs(observedGross-expectedGross)>options.GrossWeightToleranceKilograms)
            {
                manifest=manifest with{LastReconciledUtc=utcNow(),VerificationResult=$"Boarded station write retained, but total weight verification failed. Baseline {baselineGross:0.###} kg; expected {expectedGross:0.###} kg; observed {(read.Weights.CurrentGrossWeightKilograms is { } g?g.ToString("0.###"):"unavailable")} kg."};
                await store.SaveManifestAsync(manifest,token);Current=Current with{Live=read,Manifest=manifest};Notify();
                throw new IOException(manifest.VerificationResult);
            }
            var stations=manifest.Stations.Select(x=>x with{ReadBackKilograms=read.PayloadStations.Single(p=>p.Index==x.StationIndex).CurrentWeightKilograms}).ToArray();
            if(manifest.State is LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied)
            {
                var job=activeJob()??throw new InvalidOperationException("The authorized shipment is no longer active; persistent intent was preserved.");
                var commandId=manifest.LastTransferCommandId??throw new InvalidOperationException("The authorized load is missing its persistent transfer command ID.");
                var location=CurrentLocation(job,read.Position);
                var shipmentLoads=manifest.Shipments.Count>0?manifest.Shipments:
                    [new ManifestShipmentCargo(manifest.JobId,manifest.ShipmentId,manifest.ShipmentQuantityKilograms,manifest.CargoKilograms,manifest.DeliveredCargoKilograms)];
                await flight.ApplyCargoTransfersAsync("transfer-"+commandId,commandId,shipmentLoads.ToDictionary(x=>x.JobId,x=>x.LoadedKilograms),location,read.Position.Latitude,read.Position.Longitude,false,AircraftLoadingMode.MsfsPayloadIntegration,token);
            }
            manifest=manifest with{Stations=stations,State=LoadManifestState.CommittedInTransit,SimulatorAppliedUtc=utcNow(),CommittedUtc=manifest.CommittedUtc??utcNow(),
                Reconciliation=SimulatorReconciliationState.MatchesManifest,LastReconciledUtc=utcNow(),LastValidPosition=read.Position,
                BoardedAuthorizationUtc=null,BoardedAuthorizationPosition=null,
                VerificationResult=$"Authorized seated application verified. Total aircraft weight changed from {baselineGross:0.###} to {observedGross:0.###} kg; {manifest.CargoKilograms:0.###} kg Frontier cargo is aboard."};
            await store.SaveManifestAsync(manifest,token);Current=Current with{Live=read,Manifest=manifest,Plan=null,CanApplyCargo=false,
                CargoActionReason="Cargo station values and total aircraft weight were verified while seated."};UpdateRestoreAvailability();Notify();
        }finally{gate.Release();}
    }

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
    private CargoLoadManifest AuthorizeBoardedApplication(CargoLoadManifest manifest,AircraftLoadingSnapshot outside,decimal expectedDelta,string result)
    {
        var now=utcNow();var baseline=outside.Weights.CurrentGrossWeightKilograms;return manifest with{Reconciliation=SimulatorReconciliationState.RestoreAvailable,
            BoardedAuthorizationUtc=now,BoardedAuthorizationPosition=outside.Position,
            BoardedBaselineGrossWeightKilograms=baseline,BoardedExpectedGrossWeightKilograms=baseline+expectedDelta,
            LastReconciledUtc=now,LastValidPosition=outside.Position,
            VerificationResult=result+$" Outside checks authorize one explicit seated application for {options.AuthorizationDuration.TotalMinutes:0.#} minutes."};
    }
    private string BoardedApplicationReason(AircraftLoadingSnapshot live,CargoLoadManifest manifest)
    {
        if(manifest.Reconciliation!=SimulatorReconciliationState.RestoreAvailable||manifest.BoardedAuthorizationUtc is not { } authorized||manifest.BoardedAuthorizationPosition is not { } position)
            return "No outside cargo authorization exists.";
        var now=utcNow();if(now-authorized>options.AuthorizationDuration)return "The outside cargo authorization expired; exit near the aircraft and authorize the load again.";
        if(now-live.ObservedUtc>options.MaximumAge)return "Aircraft loading data is stale; read the aircraft again.";
        if(manifest.Aircraft.ClearlyDiffersFrom(LoadingIdentityExtensions.FromSnapshot(live)))return "The seated aircraft differs from the authorized loading aircraft.";
        var world=tracking();if(world is null)return "Validated live tracking is unavailable.";
        if(now-world.Aircraft.ObservedUtc>options.MaximumAge)return "Aircraft telemetry is stale.";
        if(world.PlayerMode!=PlayerControlMode.InAircraft||world.CurrentObjectId!=world.AircraftObjectId)return "Board the same aircraft before applying the authorized cargo.";
        if(FlightAircraftIdentity.From(world.Aircraft).ClearlyDiffersFrom(manifest.Aircraft))return "The controlled aircraft differs from the authorized loading aircraft.";
        if(!live.Position.IsOnGround)return "Aircraft must remain on the ground.";
        if(live.Position.GroundSpeedKnots>5)return $"Aircraft ground speed is {live.Position.GroundSpeedKnots:F1} kt; 5 kt or less is required.";
        if(DistanceNm(live.Position,position)>options.RestorePositionRadiusNauticalMiles)return "Aircraft moved more than 1.5 NM from the outside authorization position.";
        if(flight.Current.Attempt is {State:not FlightAttemptState.Delivered} attempt&&attempt.Aircraft.ClearlyDiffersFrom(manifest.Aircraft))return "The active flight attempt aircraft differs from the authorized loading aircraft.";
        return "Ready.";
    }
    private bool GrossChangedBy(AircraftLoadingSnapshot before,AircraftLoadingSnapshot after,decimal expectedDelta,out string reason)
    {
        if(before.Weights.CurrentGrossWeightKilograms is not { } first||after.Weights.CurrentGrossWeightKilograms is not { } second)
        {reason="MSFS did not report total aircraft weight, so the physical weight change could not be verified.";return false;}
        var actual=second-first;if(Math.Abs(actual-expectedDelta)<=options.GrossWeightToleranceKilograms)
        {reason=$"total aircraft weight increased by {actual:0.###} kg.";return true;}
        reason=$"total aircraft weight changed by {actual:0.###} kg; {expectedDelta:0.###} kg was required (before {first:0.###}, after {second:0.###} kg).";return false;
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
    private void UpdateRestoreAvailability(){var l=Current.Live;var m=Current.Manifest;var f=Current.SavedFuel;var cr=l is null||m is null?"No cargo restoration is available.":RestoreReason(l,m,false);var fr=l is null||f is null?"No fuel restoration is available.":RestoreReason(l,f,true);var recoverable=m?.State is LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied or LoadManifestState.CommittedInTransit;Current=Current with{CanRestoreCargo=recoverable&&(m?.Reconciliation is SimulatorReconciliationState.CargoAppearsMissing or SimulatorReconciliationState.RestoreAvailable)&&cr=="Ready.",CanRestoreFuel=f?.Reconciliation==FuelReconciliationState.AwaitingDecision&&fr=="Ready.",ReconciliationReason=$"Cargo: {cr} Fuel: {fr}"};}
    private (SimulatorReconciliationState state,string reason) ComparePayload(AircraftLoadingSnapshot live,CargoLoadManifest m){var managed=m.Stations.Where(x=>x.FrontierCargoKilograms>options.PayloadToleranceKilograms).ToArray();if(managed.Length==0)return(SimulatorReconciliationState.MatchesManifest,"The manifest contains no Frontier cargo to reconcile.");var target=managed.ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms);if(PayloadMatches(live,target,options.PayloadToleranceKilograms))return(SimulatorReconciliationState.MatchesManifest,"Live MSFS payload contains the committed Frontier cargo allocation.");var baseline=managed.ToDictionary(x=>x.StationIndex,x=>x.BaselineKilograms);if(PayloadMatches(live,baseline,options.PayloadToleranceKilograms))return(SimulatorReconciliationState.CargoAppearsMissing,"Committed Frontier cargo is missing from its assigned MSFS stations; unrelated station changes were ignored and restoration requires an explicit player action.");return(SimulatorReconciliationState.Indeterminate,"A Frontier-managed station differs from both its committed target and non-Frontier baseline: "+PayloadMismatch(managed,live));}
    private static bool PayloadMatches(AircraftLoadingSnapshot s,IReadOnlyDictionary<int,decimal> target,decimal tolerance)=>target.All(x=>s.PayloadStations.Any(p=>p.Index==x.Key&&Math.Abs(p.CurrentWeightKilograms-x.Value)<=tolerance));
    private static IReadOnlyDictionary<int,decimal> ManagedTargets(CargoLoadManifest manifest)=>manifest.Stations.Where(x=>x.FrontierCargoKilograms>manifest.VerificationToleranceKilograms).ToDictionary(x=>x.StationIndex,x=>x.TargetKilograms);
    private static decimal DetectedCargo(CargoLoadManifest manifest,AircraftLoadingSnapshot live)=>manifest.Stations.Sum(x=>
        Math.Clamp((live.PayloadStations.SingleOrDefault(p=>p.Index==x.StationIndex)?.CurrentWeightKilograms??x.BaselineKilograms)-x.BaselineKilograms,0,x.FrontierCargoKilograms));
    private static string PayloadMismatch(IEnumerable<PlannedPayloadStation> stations,AircraftLoadingSnapshot? live=null)=>string.Join("; ",stations.Where(x=>x.FrontierCargoKilograms>0||live is not null&&Math.Abs((live.PayloadStations.SingleOrDefault(p=>p.Index==x.StationIndex)?.CurrentWeightKilograms??x.TargetKilograms)-x.TargetKilograms)>.001m).Select(x=>$"station {x.StationIndex}: baseline {x.BaselineKilograms:0.###}, Frontier {x.FrontierCargoKilograms:0.###}, expected {x.TargetKilograms:0.###}, observed {(live?.PayloadStations.SingleOrDefault(p=>p.Index==x.StationIndex)?.CurrentWeightKilograms??x.ReadBackKilograms):0.###} kg"));
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





