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

    [Fact] public void Desktop_uses_compact_manual_loading_controls_without_duplicate_workflow_actions()
    {
        var document=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"Desktop","MainWindow.xaml"));XNamespace p="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var buttons=document.Descendants(p+"Button").Select(x=>x.Attribute("Content")?.Value).ToArray();
        Assert.DoesNotContain("Distribute evenly",buttons);Assert.DoesNotContain("Authorize / restore manifest cargo…",buttons);Assert.DoesNotContain("Apply authorized cargo after boarding",buttons);Assert.Contains("Set cargo to zero",buttons);Assert.Contains("0",buttons);
        Assert.Single(document.Descendants(p+"Button"),x=>(x.Attribute("Content")?.Value??"").Contains("PrimaryCargoActionText"));
        var textBindings=document.Descendants(p+"TextBox").Select(x=>x.Attribute("Text")?.Value??"").ToArray();
        Assert.Contains(textBindings,x=>x.Contains("AssignedKilogramsText")&&x.Contains("Mode=TwoWay")&&x.Contains("UpdateSourceTrigger=PropertyChanged"));
        Assert.Contains(textBindings,x=>x.Contains("PlannedGallonsText")&&x.Contains("Mode=TwoWay")&&x.Contains("UpdateSourceTrigger=PropertyChanged"));
        var labels=document.Descendants(p+"TextBlock").Select(x=>x.Attribute("Text")?.Value??"").ToArray();
        Assert.Contains("SHIPMENT",labels);Assert.Contains("OTHER",labels);Assert.Contains("TOTAL",labels);Assert.Contains("Available here",labels);Assert.Contains("Aboard",labels);Assert.Contains("Requested",labels);Assert.Contains("MSFS",labels);
        Assert.DoesNotContain(document.Descendants(p+"ProgressBar"),x=>(x.Attribute("Value")?.Value??"").Contains("AllocationProgressValue"));
        Assert.Contains(document.Descendants(p+"Path"),x=>(x.Attribute("Data")?.Value??"").StartsWith("M170,10"));Assert.Contains(document.Descendants(p+"Ellipse"),x=>(x.Attribute("Canvas.Top")?.Value??"").Contains("CurrentCgMarkerTop"));
    }

    [Theory][InlineData(119)][InlineData(121)] public void Under_or_over_allocation_is_refused(decimal assigned)
    {Assert.Throws<InvalidOperationException>(()=>AircraftLoadPlan.Create(Snapshot(),120,[new(1,assigned)]));}

    [Fact] public void Authoritative_station_limit_is_hard_but_missing_limit_is_reported()
    {var limited=Snapshot(stations:[new(1,"A",10,null,100)]);Assert.Throws<InvalidOperationException>(()=>AircraftLoadPlan.Create(limited,100,[new(1,100)]));var plan=AircraftLoadPlan.Create(Snapshot(),120,[new(1,120)]);Assert.Contains(plan.Warnings,x=>x.Contains("structural station limits"));}

    [Fact] public void Maximum_gross_and_CG_limits_warn_without_blocking()
    {var w=new AircraftWeightReading(700,1000,950,null,25,20,30);var plan=AircraftLoadPlan.Create(Snapshot(weights:w),120,[new(1,120)],35);Assert.Contains(plan.Warnings,x=>x.Contains("maximum gross"));Assert.Contains(plan.Warnings,x=>x.Contains("aft"));}

    [Fact] public void Desired_state_plan_supports_partial_loads_unloads_and_preserves_non_frontier_payload()
    {
        var live=Snapshot(stations:[new(1,"Front",86),new(5,"Baggage",0)]);
        var first=AircraftLoadPlan.CreateDesiredState(live,[new(1,0),new(5,60)],null,100);
        Assert.Equal(60,first.CargoKilograms);Assert.Equal(86,first.Stations.Single(x=>x.StationIndex==1).TargetKilograms);Assert.Equal(60,first.Stations.Single(x=>x.StationIndex==5).TargetKilograms);
        var manifest=new CargoLoadManifest("manifest-test","job-test","shipment-test","flight-test",live.Aircraft,AircraftLoadingMode.MsfsPayloadIntegration,
            first.Stations,60,LoadManifestState.CommittedInTransit,live.ObservedUtc,Reconciliation:SimulatorReconciliationState.MatchesManifest)
            {ShipmentQuantityKilograms=100,IsDesiredStateManifest=true};
        var loaded=live with{PayloadStations=[new(1,"Front",86),new(5,"Baggage",60)]};
        var full=AircraftLoadPlan.CreateDesiredState(loaded,[new(1,10),new(5,90)],manifest,100);
        Assert.Equal(96,full.Stations.Single(x=>x.StationIndex==1).TargetKilograms);Assert.Equal(90,full.Stations.Single(x=>x.StationIndex==5).TargetKilograms);
        var partialUnload=AircraftLoadPlan.CreateDesiredState(loaded,[new(1,0),new(5,40)],manifest,100);
        Assert.Equal(86,partialUnload.Stations.Single(x=>x.StationIndex==1).TargetKilograms);Assert.Equal(40,partialUnload.Stations.Single(x=>x.StationIndex==5).TargetKilograms);
        Assert.Throws<InvalidOperationException>(()=>AircraftLoadPlan.CreateDesiredState(loaded,[new(5,101)],manifest,100));
    }

    [Fact] public void Desired_state_enforces_authoritative_station_payload_and_gross_limits()
    {
        var stationLimited=Snapshot(stations:[new(1,"Cargo",20,null,50)]);
        Assert.Throws<InvalidOperationException>(()=>AircraftLoadPlan.CreateDesiredState(stationLimited,[new(1,40)],null,100));
        var payloadLimited=Snapshot(weights:new(700,1400,900,50,25,20,30));
        Assert.Throws<InvalidOperationException>(()=>AircraftLoadPlan.CreateDesiredState(payloadLimited,[new(1,40),new(2,20)],null,100));
        var grossLimited=Snapshot(weights:new(700,940,900,null,25,20,30));
        Assert.Throws<InvalidOperationException>(()=>AircraftLoadPlan.CreateDesiredState(grossLimited,[new(1,50)],null,100));
    }

    [Fact] public void Uncommitted_reset_restores_managed_baselines_and_preserves_current_unmanaged_payload()
    {
        var live=Snapshot(stations:[new(1,"Forward Baggage",40.823m),new(2,"Pilot",63.5m),new(8,"Rear Baggage",0)]);
        var manifest=new CargoLoadManifest("manifest-test","job-test","shipment-test","flight-test",live.Aircraft,
            AircraftLoadingMode.MsfsPayloadIntegration,
            [new(1,0,90,90),new(2,63.5m,0,63.5m),new(8,18.143m,0,18.143m)],90,
            LoadManifestState.PendingApplication,live.ObservedUtc,VerificationResult:"Unverified Duke intent")
            {ShipmentQuantityKilograms=100,PriorCargoKilograms=0,IsDesiredStateManifest=true};

        var reset=AircraftLoadPlan.CreateUncommittedReset(live,manifest);

        Assert.Equal(0,reset.CargoKilograms);
        Assert.Equal(0,reset.Stations.Single(x=>x.StationIndex==1).TargetKilograms);
        Assert.Equal(63.5m,reset.Stations.Single(x=>x.StationIndex==2).TargetKilograms);
        Assert.Equal(0,reset.Stations.Single(x=>x.StationIndex==8).TargetKilograms);
    }

    [Fact] public void Economy_tracks_partial_loading_delivery_and_exactly_once_payment()
    {
        var now=DateTimeOffset.Parse("2026-09-16T12:00:00Z");var world=AcceptedWorld(now,out var job,out var shipment);
        world=EconomyKernel.ApplyCargoTransfer(world,Transfer("load-60",job.Id,60,shipment.OriginLocationId,now)).World;
        Assert.Equal(60,world.Shipments.Single(x=>x.Id==shipment.Id).LoadedQuantity);
        world=EconomyKernel.ApplyCargoTransfer(world,Transfer("load-100",job.Id,100,shipment.OriginLocationId,now.AddMinutes(1))).World;
        Assert.Equal(100,world.Shipments.Single(x=>x.Id==shipment.Id).LoadedQuantity);
        world=EconomyKernel.MarkShipmentInTransit(world,job.Id).World;
        world=EconomyKernel.ApplyCargoTransfer(world,Transfer("deliver-60",job.Id,40,shipment.DestinationLocationId,now.AddHours(1))).World;
        Assert.Equal(60,world.Shipments.Single(x=>x.Id==shipment.Id).DeliveredQuantity);Assert.Empty(world.FinancialLedger);
        var evidence=new VerifiedDeliveryEvidence("evidence-test","test",job.Id,shipment.Id,shipment.OriginLocationId,shipment.DestinationLocationId,now.AddHours(2),"test proof");
        world=EconomyKernel.ApplyCargoTransfer(world,Transfer("deliver-final",job.Id,0,shipment.DestinationLocationId,now.AddHours(2),evidence:evidence)).World;
        Assert.Equal(ShipmentState.Delivered,world.Shipments.Single(x=>x.Id==shipment.Id).State);Assert.Single(world.FinancialLedger.Where(x=>x.JobId==job.Id));
        var replay=EconomyKernel.ApplyCargoTransfer(world,Transfer("deliver-final",job.Id,0,shipment.DestinationLocationId,now.AddHours(2),evidence:evidence));
        Assert.False(replay.Changed);Assert.Single(replay.World.FinancialLedger.Where(x=>x.JobId==job.Id));
    }

    [Fact] public void Wrong_location_unload_requires_confirmation_and_forfeits_without_payment()
    {
        var now=DateTimeOffset.Parse("2026-09-16T12:00:00Z");var world=AcceptedWorld(now,out var job,out var shipment);
        world=EconomyKernel.ApplyCargoTransfer(world,Transfer("load-full",job.Id,shipment.Quantity,shipment.OriginLocationId,now)).World;
        world=EconomyKernel.MarkShipmentInTransit(world,job.Id).World;
        Assert.Throws<InvalidOperationException>(()=>EconomyKernel.ApplyCargoTransfer(world,Transfer("wrong",job.Id,40,null,now.AddHours(1))));
        var forfeited=EconomyKernel.ApplyCargoTransfer(world,Transfer("wrong",job.Id,40,null,now.AddHours(1),confirm:true)).World;
        Assert.Equal(EconomyJobState.Forfeited,forfeited.Jobs.Single(x=>x.Id==job.Id).State);Assert.Equal(ShipmentState.Forfeited,forfeited.Shipments.Single(x=>x.Id==shipment.Id).State);
        Assert.Empty(forfeited.FinancialLedger.Where(x=>x.JobId==job.Id));
        Assert.Throws<InvalidOperationException>(()=>EconomyKernel.CompleteDelivery(forfeited,new("evidence-late","test",job.Id,shipment.Id,shipment.OriginLocationId,shipment.DestinationLocationId,now.AddHours(2),"late")));
    }

    [Fact] public async Task Opening_and_initializing_does_not_touch_simulator()
    {var h=Harness();await h.coordinator.InitializeAsync();Assert.Equal(0,h.sim.ReadCount);Assert.Empty(await h.store.LoadManifestsAsync());}

    [Fact] public async Task Pending_intent_precedes_write_and_apply_is_idempotent()
    {var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();Assert.Equal(1,h.sim.PayloadWrites);Assert.Equal(AircraftLoadingMode.MsfsPayloadIntegration,h.tracker.Mode);Assert.Equal(1,h.tracker.Loads);Assert.Equal(LoadManifestState.CommittedInTransit,h.coordinator.Current.Manifest!.State);await Assert.ThrowsAsync<InvalidOperationException>(()=>h.coordinator.ApplyCargoAsync());Assert.Equal(1,h.sim.PayloadWrites);}

    [Fact] public async Task Maximum_length_player_contract_ids_create_a_bounded_manifest_before_simulator_write()
    {
        var jobId="job-"+new string('a',60);var shipmentId="shipment-"+new string('b',55);
        var job=new CargoJobDisplay(jobId,"Player operation","Prospecting Strip","Windhoek Eros","Export","Raw Minerals",20,50,"TPC","Test",
            shipmentId,ShipmentState.Reserved,"origin-test","destination-test",-22.5,15,-21,15);
        var h=Harness(cargoJob:job);await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(1,0),new(2,20)]);await h.coordinator.ApplyCargoAsync();
        var manifest=Assert.Single(await h.store.LoadManifestsAsync());
        Assert.True(manifest.Id.Length<=64);Assert.StartsWith("manifest-",manifest.Id);Assert.Equal(shipmentId,manifest.ShipmentId);Assert.Equal(1,h.sim.PayloadWrites);
    }

    [Fact] public async Task Simulator_applied_intent_recovers_without_writing_or_loading_twice()
    {
        var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);
        h.tracker.FailNextTransfer=true;
        await Assert.ThrowsAsync<IOException>(()=>h.coordinator.ApplyCargoAsync());
        Assert.Equal(1,h.sim.PayloadWrites);Assert.Equal(LoadManifestState.SimulatorApplied,(await h.store.LoadManifestsAsync()).Single().State);
        await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();await h.coordinator.ApplyCargoAsync();
        Assert.Equal(1,h.sim.PayloadWrites);Assert.Equal(1,h.tracker.Loads);Assert.Equal(LoadManifestState.CommittedInTransit,h.coordinator.Current.Manifest!.State);
    }

    [Fact] public async Task Setting_all_cargo_to_zero_resets_failed_uncommitted_intent_and_allows_a_clean_reload()
    {
        var live=Snapshot(stations:[new(1,"Forward Baggage",40.823m),new(2,"Pilot",63.5m)]);
        var h=Harness(snapshot:live);
        await h.store.SaveManifestAsync(new CargoLoadManifest("manifest-flight-shipment-test","job-test","shipment-test","flight-shipment-test",
            live.Aircraft,AircraftLoadingMode.MsfsPayloadIntegration,[new(1,0,90,90),new(2,63.5m,0,63.5m)],90,
            LoadManifestState.PendingApplication,live.ObservedUtc,VerificationResult:"MSFS total-weight verification failed")
            {ShipmentQuantityKilograms=120,PriorCargoKilograms=0,IsDesiredStateManifest=true,LastTransferCommandId="failed-command"});
        await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();

        h.coordinator.CreatePlan([new(1,0),new(2,0)]);await h.coordinator.ApplyCargoAsync();

        Assert.Equal(0,h.sim.Value.PayloadStations.Single(x=>x.Index==1).CurrentWeightKilograms);
        Assert.Equal(63.5m,h.sim.Value.PayloadStations.Single(x=>x.Index==2).CurrentWeightKilograms);
        Assert.Equal(1,h.tracker.Resets);Assert.Equal(0,h.tracker.Loads);
        Assert.Equal(LoadManifestState.ResetAtOrigin,h.coordinator.Current.Manifest!.State);
        Assert.Equal(0,h.coordinator.Current.Manifest.CargoKilograms);

        await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();
        Assert.Equal(LoadManifestState.ResetAtOrigin,h.coordinator.Current.Manifest!.State);
        h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();
        Assert.Equal(1,h.tracker.Loads);Assert.Equal(LoadManifestState.CommittedInTransit,h.coordinator.Current.Manifest!.State);
        Assert.Equal(120,h.coordinator.Current.Manifest.CargoKilograms);
    }

    [Fact] public async Task Coordinator_applies_partial_zero_to_sixty_to_full_without_replacing_baseline()
    {
        var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(1,40),new(2,20)]);await h.coordinator.ApplyCargoAsync();
        Assert.Equal(50,h.sim.Value.PayloadStations[0].CurrentWeightKilograms);Assert.Equal(40,h.sim.Value.PayloadStations[1].CurrentWeightKilograms);Assert.Equal(60,h.coordinator.Current.Manifest!.CargoKilograms);
        h.tracker.SetReady(true);h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();
        Assert.Equal(80,h.sim.Value.PayloadStations[0].CurrentWeightKilograms);Assert.Equal(70,h.sim.Value.PayloadStations[1].CurrentWeightKilograms);Assert.Equal(120,h.coordinator.Current.Manifest!.CargoKilograms);
    }

    [Fact] public async Task Persisted_zero_reset_intent_reuses_its_recorded_targets_after_restart()
    {
        var live=Snapshot(stations:[new(1,"Forward Baggage",40.823m),new(2,"Pilot",63.5m)]);var h=Harness(snapshot:live);
        await h.store.SaveManifestAsync(new CargoLoadManifest("manifest-flight-shipment-test","job-test","shipment-test","flight-shipment-test",
            live.Aircraft,AircraftLoadingMode.MsfsPayloadIntegration,[new(1,0,0,0),new(2,63.5m,0,63.5m)],0,
            LoadManifestState.PendingApplication,live.ObservedUtc,VerificationResult:"Persisted reset intent")
            {ShipmentQuantityKilograms=120,PriorCargoKilograms=0,IsDesiredStateManifest=true});
        await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();

        var recovered=h.coordinator.CreatePlan([new(1,0),new(2,0)]);

        Assert.Equal(0,recovered.Stations.Single(x=>x.StationIndex==1).TargetKilograms);
        Assert.Equal(63.5m,recovered.Stations.Single(x=>x.StationIndex==2).TargetKilograms);
    }

    [Fact] public async Task Station_five_missing_cargo_reconciles_to_zero_without_subtracting_baseline_again()
    {
        var h=Harness(snapshot:Snapshot(stations:[new(5,"Baggage",0)]));await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(5,120)]);await h.coordinator.ApplyCargoAsync();h.sim.Value=Snapshot(stations:[new(5,"Baggage",0)]);
        await h.coordinator.RefreshAsync();Assert.Equal(SimulatorReconciliationState.CargoAppearsMissing,h.coordinator.Current.Manifest!.Reconciliation);
        h.coordinator.CreatePlan([new(5,0)]);await h.coordinator.ApplyCargoAsync();
        Assert.Equal(0,h.sim.Value.PayloadStations.Single().CurrentWeightKilograms);Assert.Equal(0,h.coordinator.Current.Manifest!.CargoKilograms);
    }

    [Fact] public async Task Terminal_manifest_does_not_present_stale_job_cargo_as_available()
    {
        var h=Harness(noActiveJob:true);var t=DateTimeOffset.Parse("2026-09-16T12:00:00Z");
        await h.store.SaveManifestAsync(new CargoLoadManifest("manifest-old","job-test","shipment-test","flight-test",Snapshot().Aircraft,
            AircraftLoadingMode.MsfsPayloadIntegration,[new(5,0,100,100,100)],100,LoadManifestState.Completed,t,CompletedUtc:t));
        await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();
        Assert.Null(h.coordinator.Current.Manifest);Assert.Equal(0,h.coordinator.ActiveCargoKilograms);
    }

    [Fact] public async Task Apply_refreshes_telemetry_at_write_time_instead_of_reusing_an_expired_table_snapshot()
    {var now=new DateTimeOffset(2026,9,15,12,0,0,TimeSpan.Zero);var h=Harness(snapshot:Snapshot(now:now.AddSeconds(-10)),world:World(now:now));await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);h.sim.Value=Snapshot(now:now);await h.coordinator.ApplyCargoAsync();Assert.Equal(LoadManifestState.CommittedInTransit,h.coordinator.Current.Manifest!.State);}

    [Fact] public async Task Matching_payload_on_reconnect_is_not_added_again()
    {var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();var writes=h.sim.PayloadWrites;await h.coordinator.ObserveConnectionAsync(null);await h.coordinator.RefreshAsync();Assert.Equal(writes,h.sim.PayloadWrites);Assert.Equal(SimulatorReconciliationState.MatchesManifest,h.coordinator.Current.Manifest!.Reconciliation);}

    [Fact] public async Task Missing_cargo_is_reported_and_never_automatically_restored()
    {var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();h.sim.Value=Snapshot();var writes=h.sim.PayloadWrites;await h.coordinator.ObserveConnectionAsync(null);await h.coordinator.RefreshAsync();Assert.Equal(writes,h.sim.PayloadWrites);Assert.Equal(SimulatorReconciliationState.CargoAppearsMissing,h.coordinator.Current.Manifest!.Reconciliation);}

    [Fact] public async Task Delayed_verification_refuses_a_payload_write_that_the_aircraft_resets()
    {
        var h=Harness(snapshot:Snapshot(stations:[new(1,"Pilot",86),new(5,"Baggage",0)]));h.sim.ResetPayloadAfterWrite=true;
        await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,0),new(5,120)]);
        var error=await Assert.ThrowsAsync<IOException>(()=>h.coordinator.ApplyCargoAsync());
        Assert.Contains("did not retain",error.Message);Assert.Equal(0,h.tracker.Loads);
        Assert.Equal(LoadManifestState.PendingApplication,h.coordinator.Current.Manifest!.State);
        Assert.Equal(SimulatorReconciliationState.CargoAppearsMissing,h.coordinator.Current.Manifest.Reconciliation);
        Assert.Equal(0,h.sim.Value.PayloadStations.Single(x=>x.Index==5).CurrentWeightKilograms);
        var vm=new AircraftLoadingViewModel(h.coordinator);Assert.Equal(0,vm.LoadedCargoKilograms);
        Assert.Contains("cargo missing",vm.SimulatorCargoStatus,StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public async Task Unrelated_occupant_change_does_not_hide_missing_managed_station_cargo()
    {
        var h=Harness(snapshot:Snapshot(stations:[new(1,"Pilot",0),new(2,"Co-Pilot",86),new(5,"Baggage",0)]));
        await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,0),new(2,0),new(5,120)]);await h.coordinator.ApplyCargoAsync();
        h.sim.Value=Snapshot(stations:[new(1,"Pilot",86),new(2,"Co-Pilot",86),new(5,"Baggage",0)]);
        await h.coordinator.RefreshAsync();
        Assert.Equal(SimulatorReconciliationState.CargoAppearsMissing,h.coordinator.Current.Manifest!.Reconciliation);
        Assert.True(h.coordinator.Current.CanRestoreCargo);
        Assert.Contains("unrelated station changes were ignored",h.coordinator.Current.Manifest.VerificationResult);
    }

    [Fact] public async Task Explicit_restore_must_also_survive_delayed_verification()
    {
        var h=Harness(snapshot:Snapshot(stations:[new(5,"Baggage",0)]));await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(5,120)]);await h.coordinator.ApplyCargoAsync();h.sim.Value=Snapshot(stations:[new(5,"Baggage",0)]);await h.coordinator.RefreshAsync();
        h.sim.ResetPayloadAfterWrite=true;var error=await Assert.ThrowsAsync<IOException>(()=>h.coordinator.RestoreCargoAsync());
        Assert.Contains("did not retain",error.Message);Assert.Equal(SimulatorReconciliationState.CargoAppearsMissing,h.coordinator.Current.Manifest!.Reconciliation);
        Assert.Equal(1,h.tracker.Loads);Assert.Equal(0,h.sim.Value.PayloadStations.Single().CurrentWeightKilograms);
    }

    [Fact] public async Task Rejected_unload_retains_the_previous_logical_and_detected_cargo()
    {
        var h=Harness(snapshot:Snapshot(stations:[new(5,"Baggage",0)]));await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(5,120)]);await h.coordinator.ApplyCargoAsync();h.tracker.SetReady(true);
        h.sim.ResetPayloadAfterWrite=true;h.coordinator.CreatePlan([new(5,0)]);
        var error=await Assert.ThrowsAsync<IOException>(()=>h.coordinator.ApplyCargoAsync());
        Assert.Contains("did not retain",error.Message);Assert.Equal(120,h.coordinator.LoadedCargoKilograms);
        Assert.Equal(120,h.coordinator.SimulatorDetectedCargoKilograms);Assert.Equal(0,h.tracker.Unloads);
        Assert.Equal(LoadManifestState.PendingApplication,h.coordinator.Current.Manifest!.State);
    }

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

    [Fact] public async Task Weight_and_balance_diagnostics_expose_direct_dynamic_msfs_values()
    {
        var observed=new DateTimeOffset(2026,9,15,11,59,59,345,TimeSpan.Zero);
        var weights=new AircraftWeightReading(2065,3175,2573.6123m,null,30.257,25,40);
        var h=Harness(snapshot:Snapshot(observed,
            [new(1,"Forward Baggage",12.345m),new(8,"Rear Baggage",118.14m)],weights:weights));
        var vm=new AircraftLoadingViewModel(h.coordinator);await vm.InitializeAsync();await h.coordinator.RefreshAsync();

        Assert.Equal(observed,vm.LiveObservedUtc);
        Assert.Equal(2573.6123m,vm.LiveTotalWeightKilograms);
        Assert.Equal(LoadingUnits.KilogramsToPounds(2573.6123m),vm.LiveTotalWeightPounds);
        Assert.Equal(30.257,vm.LiveCgPercent);Assert.Equal(25,vm.LiveForwardCgLimitPercent);Assert.Equal(40,vm.LiveAftCgLimitPercent);
        Assert.Equal("2573.612 kg  /  5673.84 lb",vm.LiveTotalWeightText);
        Assert.Equal("30.257%",vm.LiveCgText);Assert.Equal("25.000%",vm.LiveForwardCgLimitText);Assert.Equal("40.000%",vm.LiveAftCgLimitText);
        Assert.Collection(vm.LivePayloadStations,
            station=>{Assert.Equal(1,station.Index);Assert.Equal("Forward Baggage",station.Name);Assert.Equal(12.345m,station.Kilograms);},
            station=>{Assert.Equal(8,station.Index);Assert.Equal("Rear Baggage",station.Name);Assert.Equal(118.14m,station.Kilograms);});
    }

    [Fact] public async Task Live_station_diagnostics_do_not_subtract_frontier_manifest_cargo()
    {
        var h=Harness();var vm=new AircraftLoadingViewModel(h.coordinator);await vm.InitializeAsync();await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();

        Assert.Equal(10,vm.PayloadStations.Single(x=>x.Index==1).CurrentKilograms);
        Assert.Equal(80,vm.LivePayloadStations.Single(x=>x.Index==1).Kilograms);
        Assert.Equal(20,vm.PayloadStations.Single(x=>x.Index==2).CurrentKilograms);
        Assert.Equal(70,vm.LivePayloadStations.Single(x=>x.Index==2).Kilograms);
    }

    [Fact] public async Task Unavailable_live_weight_and_cg_values_are_reported_honestly()
    {
        var weights=new AircraftWeightReading(700,1300,null,null,null,null,null);
        var h=Harness(snapshot:Snapshot(weights:weights));var vm=new AircraftLoadingViewModel(h.coordinator);
        await vm.InitializeAsync();await h.coordinator.RefreshAsync();

        Assert.Equal("Unavailable from aircraft",vm.LiveTotalWeightText);
        Assert.Equal("Unavailable from aircraft",vm.LiveCgText);
        Assert.Equal("Unavailable from aircraft",vm.LiveForwardCgLimitText);
        Assert.Equal("Unavailable from aircraft",vm.LiveAftCgLimitText);
    }
    [Fact] public async Task Valid_manual_draft_can_be_submitted_while_live_qualification_is_pending()
    {
        var h=Harness();h.tracker.SetReady(false);var vm=new AircraftLoadingViewModel(h.coordinator);
        await vm.InitializeAsync();await h.coordinator.RefreshAsync();vm.PayloadStations[0].AssignedKilogramsText="120";
        Assert.True(vm.ApplyCargoCommand.CanExecute(null));Assert.Contains("DESIRED STATE",vm.AllocationState);
        await vm.ApplyDesiredCargoAsync(false);
        Assert.Contains("Ready",vm.Error);Assert.Equal(0,h.sim.PayloadWrites);Assert.Equal(0,h.tracker.Loads);
        h.tracker.SetReady(true);
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
        Assert.Contains("Restore manifest cargo",vm.CargoReason);Assert.Contains("DESIRED STATE",vm.AllocationState);
        Assert.True(vm.CanEditCargo);Assert.False(vm.ApplyCargoCommand.CanExecute(null));
        h.tracker.SetReady(false);Assert.Contains("Restore manifest cargo",vm.CargoReason);
        Assert.Equal(60,vm.PayloadStations[0].AssignedKilograms);Assert.Equal(1,h.tracker.Loads);
    }

    [Fact] public async Task Live_status_updates_do_not_replace_a_zero_unload_draft_with_the_manifest_load()
    {
        var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();
        var vm=new AircraftLoadingViewModel(h.coordinator);await h.coordinator.RefreshAsync();
        foreach(var row in vm.PayloadStations)row.AssignedKilogramsText="0";
        Assert.Equal(0,vm.AssignedCargo);Assert.True(vm.CanApplyCargo);
        h.tracker.SetReady(false);h.tracker.SetReady(true);await h.coordinator.RefreshAsync();
        Assert.Equal(0,vm.AssignedCargo);Assert.All(vm.PayloadStations,row=>Assert.Equal("0",row.AssignedKilogramsText));Assert.True(vm.CanApplyCargo);
        await vm.ApplyDesiredCargoAsync(false);
        Assert.Equal(10,h.sim.Value.PayloadStations[0].CurrentWeightKilograms);Assert.Equal(20,h.sim.Value.PayloadStations[1].CurrentWeightKilograms);
        Assert.Equal(0,h.coordinator.Current.Manifest!.CargoKilograms);
    }

    [Fact] public async Task Summary_separates_job_available_loaded_and_requested_cargo()
    {
        var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();var vm=new AircraftLoadingViewModel(h.coordinator);await h.coordinator.RefreshAsync();
        Assert.Equal(120,vm.JobCargoKilograms);Assert.Equal(120,vm.CargoAvailableKilograms);Assert.Equal(0,vm.LoadedCargoKilograms);
        vm.PayloadStations[0].AssignedKilogramsText="40";vm.PayloadStations[1].AssignedKilogramsText="20";await vm.ApplyDesiredCargoAsync(false);
        Assert.Equal(60,vm.LoadedCargoKilograms);Assert.Equal(60,vm.CargoAvailableKilograms);Assert.Equal(60,vm.AssignedCargo);
    }

    [Fact] public async Task Outside_station_write_without_total_weight_change_authorizes_one_seated_apply()
    {
        var h=Harness(requireBoarded:true);h.sim.UpdateGrossOnPayloadWrite=false;await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(1,70),new(2,50)]);
        await h.coordinator.ApplyCargoAsync();Assert.Equal(0,h.tracker.Loads);
        Assert.Equal(SimulatorReconciliationState.RestoreAvailable,h.coordinator.Current.Manifest!.Reconciliation);
        h.sim.Value=Snapshot();h.sim.UpdateGrossOnPayloadWrite=true;h.setWorld(World(seated:true));await h.coordinator.RefreshAsync();
        Assert.True(h.coordinator.CanApplyAuthorizedCargoAfterBoarding);
        await h.coordinator.ApplyAuthorizedCargoAfterBoardingAsync();
        Assert.Equal(1,h.tracker.Loads);Assert.Equal(LoadManifestState.CommittedInTransit,h.coordinator.Current.Manifest!.State);
        Assert.Equal(1020,h.sim.Value.Weights.CurrentGrossWeightKilograms);
        Assert.Contains("Total aircraft weight changed",h.coordinator.Current.Manifest.VerificationResult);
    }

    [Fact] public async Task Boarded_apply_does_not_commit_when_total_aircraft_weight_still_ignores_payload()
    {
        var h=Harness(requireBoarded:true);h.sim.UpdateGrossOnPayloadWrite=false;await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();
        h.sim.Value=Snapshot();h.setWorld(World(seated:true));await h.coordinator.RefreshAsync();
        var error=await Assert.ThrowsAsync<IOException>(()=>h.coordinator.ApplyAuthorizedCargoAfterBoardingAsync());
        Assert.Contains("total weight verification failed",error.Message);Assert.Equal(0,h.tracker.Loads);
        Assert.Equal(LoadManifestState.PendingApplication,h.coordinator.Current.Manifest!.State);
    }

    [Fact] public async Task Refresh_after_boarding_preserves_outside_authorization()
    {
        var h=Harness(requireBoarded:true);h.sim.UpdateGrossOnPayloadWrite=false;await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();
        h.sim.Value=Snapshot();h.setWorld(World(seated:true));await h.coordinator.RefreshAsync();await h.coordinator.RefreshAsync();
        Assert.Equal(SimulatorReconciliationState.RestoreAvailable,h.coordinator.Current.Manifest!.Reconciliation);
        Assert.True(h.coordinator.CanApplyAuthorizedCargoAfterBoarding);
    }

    [Fact] public async Task Outside_plan_does_not_write_until_one_verified_boarded_application()
    {
        var h=Harness(requireBoarded:true);await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();Assert.Equal(0,h.sim.PayloadWrites);
        h.setWorld(World(seated:true));await h.coordinator.RefreshAsync();await h.coordinator.ApplyAuthorizedCargoAfterBoardingAsync();
        Assert.Equal(1,h.sim.PayloadWrites);Assert.Equal(1,h.tracker.Loads);Assert.Equal(LoadManifestState.CommittedInTransit,h.coordinator.Current.Manifest!.State);
    }

    [Fact] public async Task Missing_committed_cargo_is_authorized_outside_and_written_only_after_boarding()
    {
        var h=Harness(requireBoarded:true);await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,70),new(2,50)]);await h.coordinator.ApplyCargoAsync();
        h.setWorld(World(seated:true));await h.coordinator.RefreshAsync();await h.coordinator.ApplyAuthorizedCargoAfterBoardingAsync();
        h.sim.Value=Snapshot();h.setWorld(World());await h.coordinator.RefreshAsync();Assert.True(h.coordinator.Current.CanRestoreCargo);
        var writes=h.sim.PayloadWrites;await h.coordinator.RestoreCargoAsync();Assert.Equal(writes,h.sim.PayloadWrites);
        Assert.Equal(SimulatorReconciliationState.RestoreAvailable,h.coordinator.Current.Manifest!.Reconciliation);
        Assert.Equal(1020,h.coordinator.Current.Manifest.BoardedExpectedGrossWeightKilograms);
        h.setWorld(World(seated:true));await h.coordinator.RefreshAsync();await h.coordinator.ApplyAuthorizedCargoAfterBoardingAsync();
        Assert.Equal(writes+1,h.sim.PayloadWrites);
    }

    [Fact] public async Task Primary_cargo_action_changes_label_and_command_across_outside_and_boarded_steps()
    {
        var h=Harness(requireBoarded:true);var vm=new AircraftLoadingViewModel(h.coordinator);
        await vm.InitializeAsync();await h.coordinator.RefreshAsync();vm.PayloadStations[0].AssignedKilogramsText="70";vm.PayloadStations[1].AssignedKilogramsText="50";
        Assert.True(vm.CanUsePrimaryCargoAction);Assert.Equal("Confirm loading plan",vm.PrimaryCargoActionText);
        await vm.ApplyPrimaryCargoAsync(false);Assert.Equal(0,h.sim.PayloadWrites);Assert.False(vm.CanUsePrimaryCargoAction);Assert.Equal("Board aircraft to continue",vm.PrimaryCargoActionText);
        h.setWorld(World(seated:true));await h.coordinator.RefreshAsync();
        Assert.True(vm.CanUsePrimaryCargoAction);Assert.Equal("Apply cargo to aircraft",vm.PrimaryCargoActionText);
        await vm.ApplyPrimaryCargoAsync(false);Assert.Equal(1,h.sim.PayloadWrites);Assert.Equal(1,h.tracker.Loads);
    }

    [Fact] public async Task Committed_load_display_does_not_count_frontier_cargo_twice()
    {var h=Harness();var vm=new AircraftLoadingViewModel(h.coordinator);await vm.InitializeAsync();await h.coordinator.RefreshAsync();vm.PayloadStations[0].AssignedKilogramsText="70";vm.PayloadStations[1].AssignedKilogramsText="50";vm.ApplyCargoCommand.Execute(null);Assert.Equal(10,vm.PayloadStations[0].CurrentKilograms);Assert.Equal(80,vm.PayloadStations[0].PlannedKilograms);Assert.Equal(20,vm.PayloadStations[1].CurrentKilograms);Assert.Equal(70,vm.PayloadStations[1].PlannedKilograms);Assert.Contains("Existing non-Frontier payload: 30.0 kg",vm.WeightAndBalance);Assert.Contains("Total planned payload: 150.0 kg",vm.WeightAndBalance);}

    [Fact] public async Task Unmanaged_seat_change_is_not_reported_as_detected_frontier_cargo()
    {
        var h=Harness();await h.coordinator.InitializeAsync();await h.coordinator.RefreshAsync();h.coordinator.CreatePlan([new(1,0),new(2,120)]);await h.coordinator.ApplyCargoAsync();
        h.sim.Value=h.sim.Value with{PayloadStations=[new(1,"Front",96.1825503m),new(2,"Rear",140)]};await h.coordinator.RefreshAsync();
        Assert.Equal(120,h.coordinator.SimulatorDetectedCargoKilograms);
    }

    [Fact] public async Task Return_job_with_cargo_already_cleared_preserves_baseline_and_never_writes_payload()
    {
        var h=Harness(); await h.coordinator.InitializeAsync(); await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(1,70),new(2,50)]); await h.coordinator.ApplyCargoAsync();
        h.sim.Value=Snapshot(); var writes=h.sim.PayloadWrites;
        await h.coordinator.AbandonPrototypeMissionAsync();
        Assert.Equal(writes,h.sim.PayloadWrites); Assert.Equal(10,h.sim.Value.PayloadStations[0].CurrentWeightKilograms);
        Assert.Equal(20,h.sim.Value.PayloadStations[1].CurrentWeightKilograms); Assert.Equal(1,h.tracker.Abandons);
        Assert.Equal(0,h.tracker.Unloads); Assert.Equal(LoadManifestState.Abandoned,h.coordinator.Current.Manifest!.State);
    }

    [Fact] public async Task Return_job_handles_one_cleared_station_without_subtracting_its_cargo_again()
    {
        var h=Harness(); await h.coordinator.InitializeAsync(); await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(1,70),new(2,50)]); await h.coordinator.ApplyCargoAsync();
        h.sim.Value=Snapshot(stations:[new(1,"Front",10),new(2,"Rear",75)]);
        await h.coordinator.AbandonPrototypeMissionAsync();
        Assert.Equal(10,h.sim.Value.PayloadStations[0].CurrentWeightKilograms); Assert.Equal(25,h.sim.Value.PayloadStations[1].CurrentWeightKilograms);
    }

    [Fact] public async Task Return_job_refuses_ambiguous_partial_cargo_without_changing_payload_or_economy()
    {
        var h=Harness(); await h.coordinator.InitializeAsync(); await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(1,70),new(2,50)]); await h.coordinator.ApplyCargoAsync();
        h.sim.Value=Snapshot(stations:[new(1,"Front",35),new(2,"Rear",70)]); var writes=h.sim.PayloadWrites;
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>h.coordinator.AbandonPrototypeMissionAsync());
        Assert.Contains("Partial cargo",error.Message);Assert.Equal(writes,h.sim.PayloadWrites);Assert.Equal(0,h.tracker.Abandons);
    }

    [Fact] public async Task Return_job_retries_persisted_targets_after_restart_without_subtracting_twice()
    {
        var h=Harness(); await h.coordinator.InitializeAsync(); await h.coordinator.RefreshAsync();
        h.coordinator.CreatePlan([new(1,70),new(2,50)]); await h.coordinator.ApplyCargoAsync();
        var m=h.coordinator.Current.Manifest!;
        await h.store.SaveManifestAsync(m with {State=LoadManifestState.PendingAbandonment,
            Stations=m.Stations.Select(x=>x with{UnloadTargetKilograms=x.BaselineKilograms}).ToArray()});
        h.sim.Value=Snapshot(); var writes=h.sim.PayloadWrites;
        await h.coordinator.InitializeAsync(); await h.coordinator.AbandonPrototypeMissionAsync();
        Assert.Equal(writes,h.sim.PayloadWrites);Assert.Equal(1,h.tracker.Abandons);
    }

    private static EconomyWorld AcceptedWorld(DateTimeOffset now,out EconomyJob job,out Shipment shipment)
    {
        var world=EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());var offered=world.Jobs.First(x=>x.State==EconomyJobState.Offered);
        world=EconomyKernel.AcceptJob(world,offered.Id,now).World;job=world.Jobs.Single(x=>x.Id==offered.Id);var shipmentId=job.ShipmentId;shipment=world.Shipments.Single(x=>x.Id==shipmentId);return world;
    }
    private static ShipmentCargoTransferRequest Transfer(string id,string jobId,decimal desired,string? location,DateTimeOffset time,bool confirm=false,VerifiedDeliveryEvidence? evidence=null)=>
        new("transfer-"+id,"command-"+id,jobId,desired,location,-22.5,17.1,time,confirm,evidence);
    private static (AircraftLoadingCoordinator coordinator,MemoryAircraftLoadingStore store,FakeSimulator sim,FakeTracker tracker,Action<TrackingWorldState?> setWorld) Harness(AircraftLoadingSnapshot? snapshot=null,TrackingWorldState? world=null,bool noActiveJob=false,bool requireBoarded=false,CargoJobDisplay? cargoJob=null)
    {var now=new DateTimeOffset(2026,9,15,12,0,0,TimeSpan.Zero);snapshot??=Snapshot(now:now);world??=World(now:now);var currentWorld=world;var sim=new FakeSimulator(snapshot);var tracker=new FakeTracker();var store=new MemoryAircraftLoadingStore();var job=cargoJob??new CargoJobDisplay("job-test","Employer","Origin","Destination","Purpose","General Supplies",120,240,"TPC","Test","shipment-test",ShipmentState.Reserved,"origin-test","destination-test",-22.5,15,-21,15);var c=new AircraftLoadingCoordinator(store,sim,tracker,()=>"sdk",()=>currentWorld,()=>noActiveJob?null:job,new(TelemetryMaximumAge:TimeSpan.FromHours(1),RequirePostBoardingCargoVerification:requireBoarded),()=>now,(_,_)=>Task.CompletedTask);return(c,store,sim,tracker,value=>currentWorld=value);}
    private static AircraftLoadingSnapshot Snapshot(DateTimeOffset? now=null,IReadOnlyList<PayloadStationReading>? stations=null,IReadOnlyList<FuelTankReading>? fuel=null,IReadOnlyList<AircraftEngineReading>? engines=null,AircraftWeightReading? weights=null)
    {var t=now??new DateTimeOffset(2026,9,15,12,0,0,TimeSpan.Zero);return new AircraftLoadingSnapshot(t,new("Piper PA28","PA28"),new(t,-22.5,15,100,90,0,true),stations??[new(1,"Front",10),new(2,"Rear",20)],fuel??[new("left","Left",20,50)],engines??[new(1,false)],2.72m,weights??new(700,1300,900,null,25,20,30),"test adapter").EnsureValid();}
    private static TrackingWorldState World(DateTimeOffset? now=null,double avatarLat=-22.5,bool seated=false)
    {var t=now??new DateTimeOffset(2026,9,15,12,0,0,TimeSpan.Zero);var a=new AircraftTelemetry(t,"Piper PA28","PA28",-22.5,15,100,90,0,true);var p=seated?null:new PlayerTelemetry(t,2,avatarLat,15,100,90,true);return new TrackingWorldState(a,1,seated?1u:2u,2,seated?PlayerControlMode.InAircraft:PlayerControlMode.OnFoot,p,"verified").EnsureValid();}
    private sealed class FakeSimulator(AircraftLoadingSnapshot value):IAircraftLoadingSimulator
    {private AircraftLoadingSnapshot? beforePayloadWrite;private bool resetOnRead;public bool ResetPayloadAfterWrite{get;set;}public bool UpdateGrossOnPayloadWrite{get;set;}=true;public AircraftLoadingSnapshot Value{get;set;}=value;public int ReadCount{get;private set;}public int PayloadWrites{get;private set;}public int FuelWrites{get;private set;}public Task<AircraftLoadingSnapshot> ReadAsync(string s,CancellationToken c=default){ReadCount++;if(resetOnRead){Value=beforePayloadWrite!;resetOnRead=false;}return Task.FromResult(Value);}public Task<AircraftLoadingSnapshot> WritePayloadAsync(string s,IReadOnlyDictionary<int,decimal> target,CancellationToken c=default){PayloadWrites++;beforePayloadWrite=Value;var before=Value.PayloadStations.Sum(x=>x.CurrentWeightKilograms);var stations=Value.PayloadStations.Select(x=>target.TryGetValue(x.Index,out var v)?x with{CurrentWeightKilograms=v}:x).ToArray();var delta=stations.Sum(x=>x.CurrentWeightKilograms)-before;var weights=UpdateGrossOnPayloadWrite&&Value.Weights.CurrentGrossWeightKilograms is { } gross?Value.Weights with{CurrentGrossWeightKilograms=gross+delta}:Value.Weights;Value=Value with{PayloadStations=stations,Weights=weights};resetOnRead=ResetPayloadAfterWrite;return Task.FromResult(Value);}public Task<AircraftLoadingSnapshot> WriteFuelAsync(string s,IReadOnlyDictionary<string,decimal> target,CancellationToken c=default){FuelWrites++;Value=Value with{FuelTanks=Value.FuelTanks.Select(x=>target.TryGetValue(x.Id,out var v)?x with{QuantityGallons=v}:x).ToArray()};return Task.FromResult(Value);}}
    private sealed class FakeTracker:IFlightAttemptTracker
    {public bool FailNextTransfer{get;set;}public void SetReady(bool ready){Current=Current with{CanLoadCargo=ready};Changed?.Invoke(Current);}public string StoragePath=>"memory";public FlightAttemptStatus Current{get;private set;}=new("Ready to load","Ready",true,true,false,0,10,null,0,"TPC");public event Action<FlightAttemptStatus>? Changed;public int Loads{get;private set;}public int Unloads{get;private set;}public int Abandons{get;private set;}public int Resets{get;private set;}public AircraftLoadingMode? Mode{get;private set;}public Task InitializeAsync(CancellationToken c=default)=>Task.CompletedTask;public Task RefreshEconomyAsync(CancellationToken c=default)=>Task.CompletedTask;public Task ObserveAsync(AircraftTelemetry? t,CancellationToken c=default)=>Task.CompletedTask;public Task LoadCargoAsync(CancellationToken c=default)=>LoadCargoAsync(AircraftLoadingMode.SimulatedFrontierLoading,c);public Task LoadCargoAsync(AircraftLoadingMode mode,CancellationToken c=default){Loads++;Mode=mode;Current=Current with{CanLoadCargo=false,CanUnloadCargo=true};Changed?.Invoke(Current);return Task.CompletedTask;}public Task ApplyCargoTransferAsync(string transferId,string commandId,decimal desiredLoadedKilograms,string? currentLocationId,double latitude,double longitude,bool confirmForfeit,AircraftLoadingMode mode,CancellationToken c=default){if(FailNextTransfer){FailNextTransfer=false;throw new IOException("Injected economy commit failure.");}return desiredLoadedKilograms>0?LoadCargoAsync(mode,c):UnloadCargoAsync(c);}public Task UnloadCargoAsync(CancellationToken c=default){if(!Current.CanUnloadCargo)return Task.CompletedTask;Unloads++;Current=Current with{CanUnloadCargo=false};return Task.CompletedTask;}public Task ResetUncommittedCargoLoadAsync(CancellationToken c=default){Resets++;Current=Current with{CanLoadCargo=true,CanUnloadCargo=false};Changed?.Invoke(Current);return Task.CompletedTask;}public Task AbandonIncompletePrototypeShipmentAsync(string jobId,CancellationToken c=default){Abandons++;Current=Current with{CanUnloadCargo=false};Changed?.Invoke(Current);return Task.CompletedTask;}public Task<string> ExportDeveloperDiagnosticAsync(CancellationToken c=default)=>Task.FromResult("{}");public Task<string>CreateDiagnosticSummaryAsync(CancellationToken c=default)=>Task.FromResult("");public Task ResetIncompletePrototypeProgressAsync(CancellationToken c=default)=>Task.CompletedTask;}
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
    [Fact] public async Task Schema_two_document_migrates_to_four_without_inventing_boarded_authorization()
    {var t=DateTimeOffset.Parse("2026-09-15T12:00:00Z");var p=new FlightPosition(t,-22.5,15,100,90,0,true);var manifest=new CargoLoadManifest("manifest-flight-test","job-test","shipment-test","flight-test",new("Piper PA28","PA28"),AircraftLoadingMode.MsfsPayloadIntegration,[new(1,10,40,50,50)],40,LoadManifestState.CommittedInTransit,t,CommittedUtc:t,LastValidPosition:p){ShipmentQuantityKilograms=40,IsDesiredStateManifest=true};var store=new LocalAircraftLoadingStore(FilePath);await store.SaveManifestAsync(manifest);var json=await File.ReadAllTextAsync(FilePath);await File.WriteAllTextAsync(FilePath,json.Replace("\"SchemaVersion\": 5","\"SchemaVersion\": 2"));var loaded=Assert.Single(await new LocalAircraftLoadingStore(FilePath).LoadManifestsAsync());Assert.Null(loaded.BoardedAuthorizationUtc);Assert.Single(loaded.Shipments);Assert.Contains("\"SchemaVersion\": 5",await File.ReadAllTextAsync(FilePath));}
}


