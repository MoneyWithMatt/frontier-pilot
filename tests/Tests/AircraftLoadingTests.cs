using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using System.Xml.Linq;

namespace FrontierAirportBuilder.Tests;

public sealed class AircraftLoadingTests
{
    [Fact] public void Unit_conversions_are_explicit_and_round_trip()
    { Assert.Equal(220.46226218487757m,LoadingUnits.KilogramsToPounds(100));Assert.Equal(100,LoadingUnits.PoundsToKilograms(LoadingUnits.KilogramsToPounds(100)));Assert.Equal(3.785411784m,LoadingUnits.GallonsToLitres(1)); }

    [Fact] public void Exact_cargo_can_be_distributed_without_overwriting_baseline()
    {var p=AircraftLoadPlan.Create(Snapshot(),120,[new(1,70),new(2,50)]);Assert.Equal(120,p.Stations.Sum(x=>x.FrontierCargoKilograms));Assert.Equal(80,p.Stations[0].TargetKilograms);Assert.Equal(70,p.Stations[1].TargetKilograms);}

    [Fact] public void Manual_station_entry_changes_only_the_station_the_player_selected()
    {var first=new PayloadStationViewModel{Index=1,Name="Front",CurrentKilograms=10,AssignmentMaximumKilograms=120};var second=new PayloadStationViewModel{Index=2,Name="Rear",CurrentKilograms=20,AssignmentMaximumKilograms=120};first.AssignedKilograms=35;Assert.Equal(35,first.AssignedKilograms);Assert.Equal(45,first.PlannedKilograms);Assert.Equal(0,second.AssignedKilograms);Assert.Equal(20,second.PlannedKilograms);}

    [Fact] public void Exact_manual_text_entry_accepts_zero_and_decimals_for_payload_and_fuel()
    {var payload=new PayloadStationViewModel{Index=1,Name="Front",CurrentKilograms=10,AssignmentMaximumKilograms=120,AssignedKilograms=35};payload.AssignedKilogramsText="";Assert.Equal(35,payload.AssignedKilograms);payload.AssignedKilogramsText="12.5";Assert.Equal(12.5m,payload.AssignedKilograms);payload.ResetCommand.Execute(null);Assert.Equal(0,payload.AssignedKilograms);Assert.Equal("0",payload.AssignedKilogramsText);var fuel=new FuelTankViewModel{Id="left",Name="Left",CurrentGallons=20,CapacityGallons=50,PlannedGallons=20};fuel.PlannedGallonsText="7.25";Assert.Equal(7.25m,fuel.PlannedGallons);fuel.ResetCommand.Execute(null);Assert.Equal(0,fuel.PlannedGallons);Assert.Equal("0",fuel.PlannedGallonsText);}

    [Fact] public void Desktop_uses_explicit_manual_payload_and_fuel_controls_without_auto_balance()
    {var document=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"Desktop","MainWindow.xaml"));XNamespace p="http://schemas.microsoft.com/winfx/2006/xaml/presentation";var buttons=document.Descendants(p+"Button").Select(x=>x.Attribute("Content")?.Value).ToArray();Assert.DoesNotContain("Distribute evenly",buttons);Assert.Contains("0",buttons);var textBindings=document.Descendants(p+"TextBox").Select(x=>x.Attribute("Text")?.Value??"").ToArray();Assert.Contains(textBindings,x=>x.Contains("AssignedKilogramsText")&&x.Contains("Mode=TwoWay")&&x.Contains("UpdateSourceTrigger=PropertyChanged"));Assert.Contains(textBindings,x=>x.Contains("PlannedGallonsText")&&x.Contains("Mode=TwoWay")&&x.Contains("UpdateSourceTrigger=PropertyChanged"));Assert.DoesNotContain("Use simulated loading�",buttons);var progress=Assert.Single(document.Descendants(p+"ProgressBar"),x=>(x.Attribute("Value")?.Value??"").Contains("AllocationProgressValue"));Assert.Contains("Mode=OneWay",progress.Attribute("Value")!.Value);Assert.Contains("Mode=OneWay",progress.Attribute("Maximum")!.Value);Assert.Contains(document.Descendants(p+"Path"),x=>(x.Attribute("Data")?.Value??"").StartsWith("M62,8"));Assert.Contains(document.Descendants(p+"Ellipse"),x=>(x.Attribute("Canvas.Top")?.Value??"").Contains("CurrentCgMarkerTop"));}

    [Theory][InlineData(119)][InlineData(121)] public void Under_or_over_allocation_is_refused(decimal assigned)
    {Assert.Throws<InvalidOperationException>(()=>AircraftLoadPlan.Create(Snapshot(),120,[new(1,assigned)]));}

    [Fact] public void Authoritative_station_limit_is_hard_but_missing_limit_is_reported()
    {var limited=Snapshot(stations:[new(1,"A",10,null,100)]);Assert.Throws<InvalidOperationException>(()=>AircraftLoadPlan.Create(limited,100,[new(1,100)]));var plan=AircraftLoadPlan.Create(Snapshot(),120,[new(1,120)]);Assert.Contains(plan.Warnings,x=>x.Contains("structural station limits"));}

    [Fact] public void Maximum_gross_and_CG_limits_warn_without_blocking()
    {var w=new AircraftWeightReading(700,1000,950,null,25,20,30);var plan=AircraftLoadPlan.Create(Snapshot(weights:w),120,[new(1,120)],35);Assert.Contains(plan.Warnings,x=>x.Contains("maximum gross"));Assert.Contains(plan.Warnings,x=>x.Contains("aft"));}

    [Fact] public async Task Opening_and_initializing_does_not_touch_simulator()
    {var h=Harness();await h.coordinator.InitializeAsync();Assert.Equal(0,h.sim.ReadCount);Assert.Empty(await h.store.LoadManifestsAsync());}

    [Fact] public async Task Pending_intent_precedes_write_and_apply_is_idempotent()
    {var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();Assert.Equal(1,h.sim.PayloadWrites);Assert.Equal(AircraftLoadingMode.MsfsPayloadIntegration,h.tracker.Mode);Assert.Equal(1,h.tracker.Loads);Assert.Equal(LoadManifestState.CommittedInTransit,h.coordinator.Current.Manifest!.State);await Assert.ThrowsAsync<InvalidOperationException>(()=>h.coordinator.ApplyCargoAsync());Assert.Equal(1,h.sim.PayloadWrites);}

    [Fact] public async Task Apply_refreshes_telemetry_at_write_time_instead_of_reusing_an_expired_table_snapshot()
    {var now=new DateTimeOffset(2026,9,15,12,0,0,TimeSpan.Zero);var h=Harness(snapshot:Snapshot(now:now.AddSeconds(-10)),world:World(now:now));await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);h.sim.Value=Snapshot(now:now);await h.coordinator.ApplyCargoAsync();Assert.Equal(LoadManifestState.CommittedInTransit,h.coordinator.Current.Manifest!.State);}

    [Fact] public async Task Matching_payload_on_reconnect_is_not_added_again()
    {var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();var writes=h.sim.PayloadWrites;await h.coordinator.ObserveConnectionAsync(null);await h.coordinator.RefreshAsync();Assert.Equal(writes,h.sim.PayloadWrites);Assert.Equal(SimulatorReconciliationState.MatchesManifest,h.coordinator.Current.Manifest!.Reconciliation);}

    [Fact] public async Task Missing_cargo_is_reported_and_never_automatically_restored()
    {var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();h.sim.Value=Snapshot();var writes=h.sim.PayloadWrites;await h.coordinator.ObserveConnectionAsync(null);await h.coordinator.RefreshAsync();Assert.Equal(writes,h.sim.PayloadWrites);Assert.Equal(SimulatorReconciliationState.CargoAppearsMissing,h.coordinator.Current.Manifest!.Reconciliation);}

    [Fact] public async Task Unload_removes_only_frontier_allocation_and_cannot_pay_twice()
    {var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();h.sim.Value=h.sim.Value with{PayloadStations=[new(1,"Front",85),new(2,"Rear",70)]};await h.coordinator.RefreshAsync();await h.coordinator.UnloadAsync();Assert.Equal(15,h.sim.Value.PayloadStations[0].CurrentWeightKilograms);Assert.Equal(20,h.sim.Value.PayloadStations[1].CurrentWeightKilograms);Assert.Equal(1,h.tracker.Unloads);await h.coordinator.UnloadAsync();Assert.Equal(1,h.tracker.Unloads);}

    [Fact] public async Task Prototype_skip_removes_only_manifest_cargo_and_never_invokes_delivery()
    {var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();h.sim.Value=h.sim.Value with{PayloadStations=[new(1,"Front",85),new(2,"Rear",70)]};await h.coordinator.RefreshAsync();await h.coordinator.AbandonPrototypeMissionAsync();Assert.Equal(15,h.sim.Value.PayloadStations[0].CurrentWeightKilograms);Assert.Equal(20,h.sim.Value.PayloadStations[1].CurrentWeightKilograms);Assert.Equal(1,h.tracker.Abandons);Assert.Equal(0,h.tracker.Unloads);Assert.Equal(LoadManifestState.Abandoned,h.coordinator.Current.Manifest!.State);await h.coordinator.AbandonPrototypeMissionAsync();Assert.Equal(1,h.tracker.Abandons);Assert.Equal(2,h.sim.PayloadWrites);}

    [Fact] public async Task Prototype_skip_tolerates_simconnect_conversion_drift_just_below_cargo_mass()
    {var h=Harness(snapshot:Snapshot(stations:[new(1,"Front",0),new(2,"Rear",20)]));await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,20),new(2,100)]);await h.coordinator.ApplyCargoAsync();h.sim.Value=h.sim.Value with{PayloadStations=[new(1,"Front",19.999999999999993m),new(2,"Rear",120)]};await h.coordinator.RefreshAsync();await h.coordinator.AbandonPrototypeMissionAsync();Assert.Equal(0,h.sim.Value.PayloadStations[0].CurrentWeightKilograms);Assert.Equal(20,h.sim.Value.PayloadStations[1].CurrentWeightKilograms);Assert.Equal(LoadManifestState.Abandoned,h.coordinator.Current.Manifest!.State);}

    [Fact] public async Task Engine_running_blocks_fuel_but_not_cargo()
    {var h=Harness(snapshot:Snapshot(engines:[new(1,true)]));await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();Assert.True(h.coordinator.Current.CanApplyCargo);Assert.False(h.coordinator.Current.CanApplyFuel);Assert.Contains(h.coordinator.Current.Warnings,x=>x.Contains("engines are running"));}

    [Fact] public async Task Unknown_engine_state_refuses_fuel()
    {var h=Harness(snapshot:Snapshot(engines:[new(1,null)]));await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();Assert.False(h.coordinator.Current.CanApplyFuel);Assert.Contains("Engine state is unavailable",h.coordinator.Current.FuelActionReason);}

    [Fact] public async Task Exact_fuel_plan_is_written_read_back_and_persisted()
    {var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();await h.coordinator.ApplyFuelAsync(new Dictionary<string,decimal>{{"left",7.25m}});Assert.Equal(1,h.sim.FuelWrites);Assert.Equal(7.25m,h.sim.Value.FuelTanks.Single().QuantityGallons);Assert.Equal(7.25m,h.coordinator.Current.SavedFuel!.Tanks.Single().QuantityGallons);Assert.Contains("applied and verified",h.coordinator.Current.FuelActionReason);}

    [Fact] public async Task First_fuel_reading_after_reconnect_cannot_overwrite_saved_snapshot()
    {var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();var original=h.coordinator.Current.SavedFuel!.Tanks[0].QuantityGallons;await h.coordinator.ObserveConnectionAsync(null);h.sim.Value=Snapshot(fuel:[new("left","Left",1,50)]);await h.coordinator.RefreshAsync();Assert.Equal(original,h.coordinator.Current.SavedFuel!.Tanks[0].QuantityGallons);Assert.True(h.coordinator.Current.FuelDecisionRequired);}

    [Fact] public async Task Explicit_keep_simulator_fuel_replaces_snapshot_once()
    {var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();await h.coordinator.ObserveConnectionAsync(null);h.sim.Value=Snapshot(fuel:[new("left","Left",7,50)]);await h.coordinator.RefreshAsync();await h.coordinator.KeepSimulatorFuelAsync();Assert.Equal(7,h.coordinator.Current.SavedFuel!.Tanks[0].QuantityGallons);Assert.False(h.coordinator.Current.FuelDecisionRequired);}

    [Fact] public async Task Distant_or_missing_avatar_blocks_restore_and_cargo_remains_persistent()
    {var h=Harness(world:World(avatarLat:-20));await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();Assert.False(h.coordinator.Current.CanApplyCargo);Assert.Contains("within 100",h.coordinator.Current.CargoActionReason);}

    [Fact] public void Route_sensitive_distance_handles_short_long_and_tiny_routes()
    {Assert.Equal(3.27,FlightAttemptTracker.RequiredDistanceForRoute(6.54),6);Assert.Equal(10,FlightAttemptTracker.RequiredDistanceForRoute(40));Assert.Equal(.5,FlightAttemptTracker.RequiredDistanceForRoute(.2));}

    [Fact] public void Sandy_dirt_uses_visible_native_dirt_fallback()
    {var p=RunwaySurfacePresets.Get(RunwaySurfacePresets.SandyDirtId);Assert.Equal("DIRT",p.SdkSurfaceReference);Assert.DoesNotContain("SAND",p.SdkSurfaceReference);Assert.Equal(SurfacePresetAvailability.TemporaryFallback,p.Availability);}


    [Fact] public async Task Refresh_preserves_editing_rows_drafts_and_fuel_plan()
    {
        var h=Harness();var vm=new AircraftLoadingViewModel(h.coordinator);
        await vm.InitializeAsync();await h.coordinator.RefreshAsync();
        var row=vm.PayloadStations[0];var fuel=vm.FuelTanks[0];
        row.AssignedKilogramsText="12.";fuel.PlannedGallonsText="7.25";
        await h.coordinator.RefreshAsync();
        Assert.Same(row,vm.PayloadStations[0]);Assert.Same(fuel,vm.FuelTanks[0]);
        Assert.Equal("12.",row.AssignedKilogramsText);Assert.Equal(7.25m,fuel.PlannedGallons);
        row.AssignedKilogramsText="120";Assert.True(vm.ApplyCargoCommand.CanExecute(null));
        row.AssignedKilogramsText="";Assert.False(vm.ApplyCargoCommand.CanExecute(null));
        row.ResetCommand.Execute(null);Assert.Equal("0",row.AssignedKilogramsText);
    }
    [Fact] public async Task Qualification_changes_refresh_apply_without_replacing_manual_values()
    {
        var h=Harness();h.tracker.SetReady(false);var vm=new AircraftLoadingViewModel(h.coordinator);
        await vm.InitializeAsync();await h.coordinator.RefreshAsync();vm.PayloadStations[0].AssignedKilogramsText="120";
        Assert.False(vm.ApplyCargoCommand.CanExecute(null));Assert.Contains("WAITING",vm.AllocationState);h.tracker.SetReady(true);
        Assert.True(vm.ApplyCargoCommand.CanExecute(null));Assert.Equal("120",vm.PayloadStations[0].AssignedKilogramsText);
    }

    [Fact] public async Task Manual_command_writes_exact_allocation_and_commits_once()
    {
        var h=Harness();var vm=new AircraftLoadingViewModel(h.coordinator);await vm.InitializeAsync();await h.coordinator.RefreshAsync();
        vm.PayloadStations[0].AssignedKilogramsText="75.25";vm.PayloadStations[1].AssignedKilogramsText="44.75";
        Assert.True(vm.ApplyCargoCommand.CanExecute(null));vm.ApplyCargoCommand.Execute(null);
        Assert.Equal("",vm.Error);Assert.Equal(1,h.sim.PayloadWrites);Assert.Equal(1,h.tracker.Loads);
        Assert.Equal(85.25m,h.sim.Value.PayloadStations[0].CurrentWeightKilograms);
        Assert.Equal(64.75m,h.sim.Value.PayloadStations[1].CurrentWeightKilograms);
        vm.ApplyCargoCommand.Execute(null);Assert.Equal(1,h.sim.PayloadWrites);Assert.Equal(1,h.tracker.Loads);
    }

    [Fact] public async Task Reading_loading_requests_tracking_reconnection()
    {
        var h=Harness();int requests=0;var vm=new AircraftLoadingViewModel(h.coordinator,()=>requests++);
        await vm.InitializeAsync();vm.RefreshCommand.Execute(null);
        Assert.Equal(1,requests);Assert.NotEmpty(vm.PayloadStations);
    }

    [Fact] public async Task Existing_committed_load_reports_recovery_instead_of_ready_or_generic_telemetry()
    {
        var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(1,60),new(2,60)]);await h.coordinator.ApplyCargoAsync();
        h.sim.Value=Snapshot();var vm=new AircraftLoadingViewModel(h.coordinator);await h.coordinator.RefreshAsync();
        Assert.Contains("Restore manifest cargo",vm.CargoReason);Assert.Equal("CARGO ALREADY LOADED",vm.AllocationState);
        Assert.False(vm.CanEditCargo);Assert.False(vm.ApplyCargoCommand.CanExecute(null));
        h.tracker.SetReady(false);Assert.Contains("Restore manifest cargo",vm.CargoReason);
        Assert.Equal(60,vm.PayloadStations[0].AssignedKilograms);Assert.Equal(1,h.tracker.Loads);
    }

    [Fact] public async Task Committed_load_display_does_not_count_frontier_cargo_twice()
    {var h=Harness();var vm=new AircraftLoadingViewModel(h.coordinator);await vm.InitializeAsync();await h.coordinator.RefreshAsync();vm.PayloadStations[0].AssignedKilogramsText="70";vm.PayloadStations[1].AssignedKilogramsText="50";vm.ApplyCargoCommand.Execute(null);Assert.Equal(10,vm.PayloadStations[0].CurrentKilograms);Assert.Equal(80,vm.PayloadStations[0].PlannedKilograms);Assert.Equal(20,vm.PayloadStations[1].CurrentKilograms);Assert.Equal(70,vm.PayloadStations[1].PlannedKilograms);Assert.Contains("Existing non-Frontier payload: 30.0 kg",vm.WeightAndBalance);Assert.Contains("Total planned payload: 150.0 kg",vm.WeightAndBalance);}

    private static (AircraftLoadingCoordinator coordinator,MemoryAircraftLoadingStore store,FakeSimulator sim,FakeTracker tracker) Harness(AircraftLoadingSnapshot? snapshot=null,TrackingWorldState? world=null)
    {var now=new DateTimeOffset(2026,9,15,12,0,0,TimeSpan.Zero);snapshot??=Snapshot(now:now);world??=World(now:now);var sim=new FakeSimulator(snapshot);var tracker=new FakeTracker();var store=new MemoryAircraftLoadingStore();var job=new CargoJobDisplay("job-test","Employer","Origin","Destination","Purpose","General Supplies",120,240,"TPC","Test","shipment-test",ShipmentState.Reserved);var c=new AircraftLoadingCoordinator(store,sim,tracker,()=>"sdk",()=>world,()=>job,new(TelemetryMaximumAge:TimeSpan.FromHours(1)),()=>now);return(c,store,sim,tracker);}
    private static AircraftLoadingSnapshot Snapshot(DateTimeOffset? now=null,IReadOnlyList<PayloadStationReading>? stations=null,IReadOnlyList<FuelTankReading>? fuel=null,IReadOnlyList<AircraftEngineReading>? engines=null,AircraftWeightReading? weights=null)
    {var t=now??new DateTimeOffset(2026,9,15,12,0,0,TimeSpan.Zero);return new AircraftLoadingSnapshot(t,new("Piper PA28","PA28"),new(t,-22.5,15,100,90,0,true),stations??[new(1,"Front",10),new(2,"Rear",20)],fuel??[new("left","Left",20,50)],engines??[new(1,false)],2.72m,weights??new(700,1300,900,null,25,20,30),"test adapter").EnsureValid();}
    private static TrackingWorldState World(DateTimeOffset? now=null,double avatarLat=-22.5)
    {var t=now??new DateTimeOffset(2026,9,15,12,0,0,TimeSpan.Zero);var a=new AircraftTelemetry(t,"Piper PA28","PA28",-22.5,15,100,90,0,true);var p=new PlayerTelemetry(t,2,avatarLat,15,100,90,true);return new TrackingWorldState(a,1,2,2,PlayerControlMode.OnFoot,p,"verified").EnsureValid();}
    private sealed class FakeSimulator(AircraftLoadingSnapshot value):IAircraftLoadingSimulator
    {public AircraftLoadingSnapshot Value{get;set;}=value;public int ReadCount{get;private set;}public int PayloadWrites{get;private set;}public int FuelWrites{get;private set;}public Task<AircraftLoadingSnapshot> ReadAsync(string s,CancellationToken c=default){ReadCount++;return Task.FromResult(Value);}public Task<AircraftLoadingSnapshot> WritePayloadAsync(string s,IReadOnlyDictionary<int,decimal> target,CancellationToken c=default){PayloadWrites++;Value=Value with{PayloadStations=Value.PayloadStations.Select(x=>target.TryGetValue(x.Index,out var v)?x with{CurrentWeightKilograms=v}:x).ToArray()};return Task.FromResult(Value);}public Task<AircraftLoadingSnapshot> WriteFuelAsync(string s,IReadOnlyDictionary<string,decimal> target,CancellationToken c=default){FuelWrites++;Value=Value with{FuelTanks=Value.FuelTanks.Select(x=>target.TryGetValue(x.Id,out var v)?x with{QuantityGallons=v}:x).ToArray()};return Task.FromResult(Value);}}
    private sealed class FakeTracker:IFlightAttemptTracker
    {public void SetReady(bool ready){Current=Current with{CanLoadCargo=ready};Changed?.Invoke(Current);}public string StoragePath=>"memory";public FlightAttemptStatus Current{get;private set;}=new("Ready to load","Ready",true,true,false,0,10,null,0,"TPC");public event Action<FlightAttemptStatus>? Changed;public int Loads{get;private set;}public int Unloads{get;private set;}public int Abandons{get;private set;}public AircraftLoadingMode? Mode{get;private set;}public Task InitializeAsync(CancellationToken c=default)=>Task.CompletedTask;public Task RefreshEconomyAsync(CancellationToken c=default)=>Task.CompletedTask;public Task ObserveAsync(AircraftTelemetry? t,CancellationToken c=default)=>Task.CompletedTask;public Task LoadCargoAsync(CancellationToken c=default)=>LoadCargoAsync(AircraftLoadingMode.SimulatedFrontierLoading,c);public Task LoadCargoAsync(AircraftLoadingMode mode,CancellationToken c=default){Loads++;Mode=mode;Current=Current with{CanLoadCargo=false,CanUnloadCargo=true};Changed?.Invoke(Current);return Task.CompletedTask;}public Task UnloadCargoAsync(CancellationToken c=default){if(!Current.CanUnloadCargo)return Task.CompletedTask;Unloads++;Current=Current with{CanUnloadCargo=false};return Task.CompletedTask;}public Task AbandonIncompletePrototypeShipmentAsync(string jobId,CancellationToken c=default){Abandons++;Current=Current with{CanUnloadCargo=false};Changed?.Invoke(Current);return Task.CompletedTask;}public Task<string> ExportDeveloperDiagnosticAsync(CancellationToken c=default)=>Task.FromResult("{}");public Task<string>CreateDiagnosticSummaryAsync(CancellationToken c=default)=>Task.FromResult("");public Task ResetIncompletePrototypeProgressAsync(CancellationToken c=default)=>Task.CompletedTask;}
}

public sealed class AircraftLoadingPersistenceTests : TemporaryDirectoryTest
{
    private string FilePath=>Path.Combine(Root,"economy","aircraft-loading.json");
    [Fact] public async Task Cargo_and_fuel_round_trip_across_store_instances()
    {var t=DateTimeOffset.Parse("2026-09-15T12:00:00Z");var position=new FlightPosition(t,-22.5,15,100,90,0,true);var manifest=new CargoLoadManifest("manifest-flight-test","job-test","shipment-test","flight-test",new("Piper PA28","PA28"),AircraftLoadingMode.MsfsPayloadIntegration,[new(1,10,40,50,50)],40,LoadManifestState.CommittedInTransit,t,t,t,LastValidPosition:position);var fuel=new PersistentAircraftFuelState("fuel-pa28",new("Piper PA28","PA28"),[new("left","Left",20,50)],position,t,t,FuelReconciliationState.ContinuousLive);var first=new LocalAircraftLoadingStore(FilePath);await first.SaveManifestAsync(manifest);await first.SaveFuelStateAsync(fuel);var reopened=new LocalAircraftLoadingStore(FilePath);var loadedManifest=Assert.Single(await reopened.LoadManifestsAsync());Assert.Equal(manifest.Id,loadedManifest.Id);Assert.Equal(manifest.State,loadedManifest.State);Assert.Equal(manifest.Stations,loadedManifest.Stations);var loadedFuel=Assert.Single(await reopened.LoadFuelStatesAsync());Assert.Equal(fuel.Id,loadedFuel.Id);Assert.Equal(fuel.Tanks,loadedFuel.Tanks);}
    [Fact] public async Task Corrupt_and_future_documents_are_preserved()
    {Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);await File.WriteAllTextAsync(FilePath,"{bad");var before=await File.ReadAllTextAsync(FilePath);await Assert.ThrowsAsync<IOException>(()=>new LocalAircraftLoadingStore(FilePath).LoadManifestsAsync());Assert.Equal(before,await File.ReadAllTextAsync(FilePath));await File.WriteAllTextAsync(FilePath,"{\"SchemaVersion\":99,\"Manifests\":[],\"FuelStates\":[]}");before=await File.ReadAllTextAsync(FilePath);await Assert.ThrowsAsync<IOException>(()=>new LocalAircraftLoadingStore(FilePath).LoadFuelStatesAsync());Assert.Equal(before,await File.ReadAllTextAsync(FilePath));}
    [Fact] public async Task Completed_manifest_is_immutable()
    {var t=DateTimeOffset.Parse("2026-09-15T12:00:00Z");var p=new FlightPosition(t,-22.5,15,100,90,0,true);var complete=new CargoLoadManifest("manifest-flight-test","job-test","shipment-test","flight-test",new("Piper PA28","PA28"),AircraftLoadingMode.SimulatedFrontierLoading,[],40,LoadManifestState.Completed,t,CommittedUtc:t,CompletedUtc:t,LastValidPosition:p);var store=new LocalAircraftLoadingStore(FilePath);await store.SaveManifestAsync(complete);await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveManifestAsync(complete with{VerificationResult="rewritten"}));}
}


