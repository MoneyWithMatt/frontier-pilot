using System.Runtime.CompilerServices;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.MsfsSdk;

namespace FrontierAirportBuilder.Tests;
public sealed class PhysicalAircraftTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"fleet-tests-"+Guid.NewGuid());
    private static readonly DateTimeOffset Epoch=new(2026,10,27,12,0,0,TimeSpan.Zero);
    private static AircraftTelemetry Position(int seconds=0,bool ground=true)=>new(Epoch.AddSeconds(seconds),"Test plane","Test model",-22,15,5000,90,0,ground);
    private static PhysicalAirframe Aircraft(string reg="TEST-1")=>FleetOperations.DiscoverComponents(FleetOperations.Enrol(reg,"test-company",Position(),"test-package","pa28",Epoch),1);
    private AircraftFleetStore Store()=>new(Path.Combine(root,"aircraft-fleet.json"));
    private AircraftFleet Save(PhysicalAirframe a)=>Store().Commit(new(1,1,a.Id,[a]),0);
    private static QualifiedAircraftSample Sample(Guid session,int t,bool ground=true,bool? dry=true,bool height=true)=>
        new(session,Position(t,ground),new(Epoch.AddSeconds(t),"TEST","Test airport",1,"Stand",0,0,AircraftParkingState.Parked,"synthetic"),t,true,false,false,dry,height,[true]);
    [Fact] public void IdenticalAircraftRenameAndIndependentLocationsSurviveRestart()
    {
        var a=Aircraft();var b=Aircraft("TEST-2");var id=Guid.NewGuid();
        a=a with {Latest=new(Position(),null,AirframePositionState.LastKnown,"test","unknown",id)};
        var store=Store();store.Commit(new(1,1,a.Id,[a,b]),0);
        var saved=store.Load();a=FleetOperations.Rename(saved.Selected!,"NEW-1",Epoch.AddSeconds(1));
        store.Commit(saved with {Revision=2,Aircraft=[a,b]},1);var loaded=Store().Load();
        Assert.Equal(a.Id,loaded.Selected!.Id);Assert.Equal("NEW-1",loaded.Selected.Registration);Assert.Null(loaded.Aircraft[1].Latest);
        Assert.NotEqual(a.Components[0].Id,b.Components[0].Id);Assert.Null(loaded.Selected.Identity.AirframeHours);
        Assert.Throws<IOException>(()=>store.Commit(loaded with {Revision=2},1));
    }
    [Fact] public void CorruptFutureAndInvalidSavesArePreserved()
    {
        Directory.CreateDirectory(root);var store=Store();File.WriteAllText(store.StoragePath,"broken");
        Assert.Throws<IOException>(()=>store.Load());Assert.Throws<IOException>(()=>store.Commit(new(1,1,null,[]),0));Assert.Equal("broken",File.ReadAllText(store.StoragePath));
        File.WriteAllText(store.StoragePath,"{\"SchemaVersion\":999,\"Revision\":0,\"SelectedId\":null,\"Aircraft\":[]}");Assert.Throws<IOException>(()=>store.Load());
    }
    [Fact] public void StationaryWaterElevatedAndLoadingPositionsAreDistinct()
    {
        var a=Aircraft();var id=Guid.NewGuid();var accumulator=new AircraftLocationAccumulator();
        for(var t=0;t<7;t++)a=accumulator.Observe(a,Sample(id,t));
        Assert.Equal(AirframePositionState.StationaryGround,a.Latest!.State);Assert.Equal(5000,a.LastStationary!.Telemetry.AltitudeFeet);
        a=accumulator.Observe(a,Sample(id,7,dry:false));Assert.Equal(AirframePositionState.Uncertain,a.Latest!.State);Assert.Equal(6,a.LastStationary!.Telemetry.ObservedUtc.Second);
        var last=a.Latest;a=accumulator.Observe(a,Sample(id,8) with {Initialized=false});Assert.Equal(last,a.Latest);
        a=accumulator.Observe(a,Sample(id,9,height:false));Assert.NotEqual(AirframePositionState.StationaryGround,a.Latest!.State);
    }
    [Fact] public void ExposureIgnoresPauseGapsDuplicatesAndReconnects()
    {
        var a=Aircraft();var id=Guid.NewGuid();var acc=new AircraftExposureAccumulator();
        a=acc.Observe(a,Sample(id,0,false),[],false);a=acc.Observe(a,Sample(id,2,false),[],false);
        Assert.Equal(2,a.ObservedAirborneSeconds);Assert.Equal(2,a.Components[0].ObservedSeconds);
        a=acc.Observe(a,Sample(id,2,false),[],false);Assert.Equal(2,a.ObservedAirborneSeconds);
        a=acc.Observe(a,Sample(id,3,false) with {Paused=true},[],false);
        a=acc.Observe(a,Sample(id,4,false),[],false);a=acc.Observe(a,Sample(id,30,false),[],false);
        a=acc.Observe(a,Sample(Guid.NewGuid(),31,false),[],false);Assert.Equal(2,a.ObservedAirborneSeconds);
    }
    [Fact] public void LandingEpisodesConsolidateBouncesAndExcludeWater()
    {
        var a=Aircraft();var id=Guid.NewGuid();var acc=new AircraftExposureAccumulator();
        for(var t=0;t<=12;t++)a=acc.Observe(a,Sample(id,t,false),[],false);
        a=acc.Observe(a,Sample(id,13),[],false);a=acc.Observe(a,Sample(id,14,false),[],false);
        for(var t=15;t<=25;t++)a=acc.Observe(a,Sample(id,t),[],false);Assert.Equal(1,a.ObservedLandingCycles);
        for(var t=26;t<=40;t++)a=acc.Observe(a,Sample(id,t,false),[],false);
        for(var t=41;t<=50;t++)a=acc.Observe(a,Sample(id,t,dry:false),[],false);Assert.Equal(1,a.ObservedLandingCycles);
    }
    [Fact] public void RiskIsStableAcrossSampleFrequencyAndSaveReload()
    {
        var original=FleetOperations.EstablishBaseline(Aircraft(),Epoch);
        var engine=original.Components[0] with {Health=.5,Threshold=.001};original=original with {Components=original.Components.Select(c=>c.Id==engine.Id?engine:c).ToArray()};
        PhysicalAirframe Run(int step){var a=original;var acc=new AircraftExposureAccumulator();var id=Guid.NewGuid();for(var t=0;t<=60;t+=step)a=acc.Observe(a,Sample(id,t,false),[new("test-v1","engine",.01,1,true)],true);return a;}
        var fast=Run(1);var slow=Run(2);Assert.Equal(fast.Components[0].Hazard,slow.Components[0].Hazard,10);Assert.Single(fast.Faults);Assert.Single(slow.Faults);
        Save(fast);Assert.Equal(fast.Components[0].Threshold,Store().Load().Selected!.Components[0].Threshold);
    }
    [Fact] public void InspectionDoesNotRepairAndReplacementArchivesOldComponent()
    {
        var a=FleetOperations.EstablishBaseline(Aircraft(),Epoch);var id=Guid.NewGuid();var pos=new AirframePosition(Position(),null,AirframePositionState.StationaryGround,"test","test",id);a=a with {Latest=pos,LastStationary=pos};
        var c=a.Components[0];var fault=new AirframeFault(Guid.NewGuid(),c.Id,"test","test",true,Epoch);a=a with {Faults=[fault]};
        var facility=new MaintenanceFacility("test","test",-22,15,250,Enum.GetValues<MaintenanceAction>(),"test-provider");var order=Guid.NewGuid();
        a=AircraftMaintenance.Book(a,order,c.Id,MaintenancePrototypePolicy.Quote(MaintenanceAction.Inspection),facility,"test-company",Epoch,true);
        a=AircraftMaintenance.Settled(a,order);a=AircraftMaintenance.Complete(a,order,Epoch.AddHours(1));Assert.False(a.Faults[0].Resolved);Assert.Equal(c.Health,a.Components[0].Health);
        order=Guid.NewGuid();a=AircraftMaintenance.Book(a,order,c.Id,MaintenancePrototypePolicy.Quote(MaintenanceAction.Replacement),facility,"test-company",Epoch,true);a=AircraftMaintenance.Settled(a,order);
        var pending=AircraftMaintenance.Complete(a,order,Epoch.AddHours(2));Assert.Equal(WorkOrderState.AwaitingSimulatorRepair,pending.Work.Last().State);
        a=AircraftMaintenance.Complete(pending,order,Epoch.AddHours(2),true);Assert.True(a.Components[0].Removed);Assert.Single(a.Components.Where(x=>x.Key==c.Key&&!x.Removed));Assert.True(a.Faults[0].Resolved);
        Assert.Equal(a,AircraftMaintenance.Complete(a,order,Epoch.AddHours(3),true));
    }
    [Fact] public void UnknownBaselineCannotEraseFaultOrBeRepeated()
    {
        var a=Aircraft();var b=FleetOperations.EstablishBaseline(a,Epoch);Assert.Throws<InvalidOperationException>(()=>FleetOperations.EstablishBaseline(b,Epoch));
        Assert.Throws<InvalidOperationException>(()=>FleetOperations.EstablishBaseline(a with {Faults=[new(Guid.NewGuid(),a.Components[0].Id,"fault","test",true,Epoch)]},Epoch));
    }
    [Fact] public async Task FaultSynchronizationIsOneShotAndRejectsWrongComponentEffects()
    {
        var a=Aircraft();var fault=new AirframeFault(Guid.NewGuid(),a.Components[0].Id,"engine-failed","test",true,Epoch);a=a with {Faults=[fault]};Save(a);
        var adapter=new EffectAdapter();var sync=new AircraftFaultSynchronization(Store());
        await sync.SynchronizeAsync(a.Id,fault.Id,"engine-failed",true,adapter,true,true);
        await sync.SynchronizeAsync(a.Id,fault.Id,"engine-failed",true,adapter,true,true);
        Assert.Equal(1,adapter.Writes);Assert.Equal(FaultSyncState.Pending,Store().Load().Selected!.Faults[0].Synchronization);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>sync.SynchronizeAsync(a.Id,fault.Id,"gear-failed",true,adapter,true,true));
    }
    [Fact] public async Task AutomaticObservationRequiresExplicitSelectionAndFlushesOnShutdown()
    {
        var vm=new AircraftFleetViewModel(Store(),()=>new IdleSource());var t=DateTimeOffset.UtcNow;
        TrackingWorldState W(int sec)=>new(Position() with {ObservedUtc=t.AddSeconds(sec)},42,42,null,PlayerControlMode.InAircraft,null,"test");
        vm.ObserveTracking(W(0),null);Assert.Empty(vm.Aircraft);
        vm.Registration="TEST-1";vm.PackageVersion="test-package";vm.EnrolCommand.Execute(null);
        Assert.Single(vm.Aircraft);vm.ObserveTracking(W(1),null);vm.ObserveTracking(W(3),null);vm.ObserveTracking(W(5),null);vm.ObserveTracking(W(7),null);vm.ObserveTracking(W(9),null);vm.ObserveTracking(W(11),null);
        await vm.ShutdownAsync();var a=Store().Load().Selected!;Assert.NotNull(a.Latest);Assert.Null(a.OpenSession);Assert.Equal(AirframePositionState.LastKnown,a.Latest!.State);
    }
    [Fact] public void RuntimePauseFlagsAndPositionChangesRemainReadOnly()
    {
        var events=new ConditionRuntimeEvents();Assert.Null(events.State.Paused);
        byte[] Packet(uint id,uint value){var bytes=new byte[24];BitConverter.GetBytes(4u).CopyTo(bytes,8);BitConverter.GetBytes(id).CopyTo(bytes,16);BitConverter.GetBytes(value).CopyTo(bytes,20);return bytes;}
        events.Receive(Packet(1,4));Assert.True(events.State.Paused);events.Receive(Packet(1,0));Assert.False(events.State.Paused);
        events.Receive(Packet(3,0));Assert.Equal(1,events.State.Discontinuity);
    }
    [Fact] public async Task CompanyMaintenanceSettlementRestartsWithoutPlayerDebitOrDuplicateCompletion()
    {
        var a=Aircraft();var pos=new AirframePosition(Position(),null,AirframePositionState.StationaryGround,"test","test",Guid.NewGuid());a=a with {Latest=pos,LastStationary=pos};
        var facility=new MaintenanceFacility("test","test",-22,15,250,[MaintenanceAction.Inspection],"prototype-customer-funder");var order=Guid.NewGuid();
        a=AircraftMaintenance.Book(a,order,a.Components[0].Id,MaintenancePrototypePolicy.Quote(MaintenanceAction.Inspection),facility,"prototype-system-cargo",Epoch,true);Save(a);
        var economy=new LocalEconomyStateStore(Path.Combine(root,"economy-state.json"));await economy.InitializeAsync(EconomyPrototypeSeed.Create());
        var service=new AircraftMaintenanceService(Store(),economy);await service.ReconcileAsync(Epoch.AddMinutes(1));
        Assert.Equal(WorkOrderState.InProgress,Store().Load().Selected!.Work[0].State);
        await new AircraftMaintenanceService(Store(),economy).ReconcileAsync(Epoch.AddHours(1));
        await service.ReconcileAsync(Epoch.AddHours(2));var paid=(await economy.LoadAsync())!;
        Assert.Single(paid.MaintenanceCharges);Assert.Single(paid.FinancialLedger);Assert.Equal(0,paid.Players[0].CachedBalance);
        Assert.Equal(WorkOrderState.Completed,Store().Load().Selected!.Work[0].State);
        Assert.Single(Store().Load().Selected!.History.Where(e=>e.Kind=="Maintenance completed"));
    }
    [Fact] public void MaintenanceRefusesWaterWrongFacilityAndUnfundedPlayer()
    {
        var a=Aircraft();var pos=new AirframePosition(Position(),null,AirframePositionState.Uncertain,"test","water",Guid.NewGuid());a=a with {Latest=pos,LastStationary=pos};
        var facility=new MaintenanceFacility("test","test",-22,15,250,[MaintenanceAction.Inspection],"prototype-customer-funder");
        Assert.Throws<InvalidOperationException>(()=>AircraftMaintenance.Book(a,Guid.NewGuid(),a.Components[0].Id,MaintenancePrototypePolicy.Quote(MaintenanceAction.Inspection),facility,"prototype-player",Epoch,true));
        pos=pos with {State=AirframePositionState.StationaryGround};a=a with {Latest=pos,LastStationary=pos};
        Assert.Throws<InvalidOperationException>(()=>AircraftMaintenance.Book(a,Guid.NewGuid(),a.Components[0].Id,MaintenancePrototypePolicy.Quote(MaintenanceAction.Inspection),facility with {Latitude=0},"prototype-player",Epoch,true));
        Assert.Throws<InvalidOperationException>(()=>MaintenanceEconomy.Charge(EconomyPrototypeSeed.Create(),new("work-1","airframe-1","prototype-player","prototype-customer-funder",10,"TPC",Epoch)));
    }
    [Fact] public async Task PlacementRequiresQualifiedGroundAndPreservesAltitudeWithoutFlightEvidence()
    {
        var a=Aircraft();var pos=new AirframePosition(Position(),null,AirframePositionState.Airborne,"test","test",Guid.NewGuid());a=a with {Latest=pos,LastStationary=pos};var saved=Save(a);
        var adapter=new PlacementAdapter();var service=new AircraftPlacementService(Store());var id=Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ReturnAsync(a.Id,id,adapter,true,true));Assert.Equal(0,adapter.Writes);
        pos=pos with {State=AirframePositionState.StationaryGround};a=a with {Latest=pos,LastStationary=pos};Store().Commit(saved with {Revision=2,Aircraft=[a]},1);
        await service.ReturnAsync(a.Id,id,adapter,true,true);await service.ReturnAsync(a.Id,id,adapter,true,true);
        Assert.Equal(1,adapter.Writes);Assert.Equal(5000,adapter.Altitude);Assert.Equal(0,Store().Load().Selected!.ObservedLandingCycles);
    }
    [Fact] public async Task InterruptedAirborneSessionRecoversAsUncertainAndDoesNotTransferToIdenticalAirframe()
    {
        var a=Aircraft();var b=Aircraft("SECOND");a=a with {OpenSession=Guid.NewGuid(),Latest=new(Position(0,false),null,AirframePositionState.Airborne,"test","test",Guid.NewGuid())};
        Store().Commit(new(1,1,a.Id,[a,b]),0);var vm=new AircraftFleetViewModel(Store(),()=>new IdleSource());
        Assert.Equal(AirframePositionState.InterruptedFlight,vm.Selected!.Latest!.State);vm.Selected=vm.Aircraft[1];Assert.Null(vm.Selected!.Latest);
        await vm.ShutdownAsync();Assert.Equal(a.Id,Store().Load().Aircraft[0].Id);
    }
    [Fact] public void ConfirmedCrashInspectionIsIdempotentAndDoesNotInventComponentPercentages()
    {
        var a=Aircraft();var id=Guid.NewGuid();a=AircraftIncidents.RequireInspection(a,id,Epoch,"synthetic qualified crash",true);
        Assert.Equal("Grounded",a.Availability);Assert.All(a.Components,c=>Assert.Null(c.Health));Assert.Single(a.Faults);
        Assert.Equal(a,AircraftIncidents.RequireInspection(a,id,Epoch,"duplicate",true));
    }
    [Fact] public async Task DisabledEffectsNeverReadOrWriteAndRemainIdempotent()
    {
        var a=Aircraft();var fault=new AirframeFault(Guid.NewGuid(),a.Components[0].Id,"engine-failed","test",true,Epoch);a=a with {Faults=[fault]};Save(a);
        var adapter=new EffectAdapter();var service=new AircraftFaultSynchronization(Store());
        await service.SynchronizeAsync(a.Id,fault.Id,"engine-failed",true,adapter,false,true);
        await service.SynchronizeAsync(a.Id,fault.Id,"engine-failed",true,adapter,false,true);
        Assert.Equal(0,adapter.Writes);Assert.Single(Store().Load().Selected!.CommandIntents);Assert.Equal(FaultSyncState.UnableToApply,Store().Load().Selected!.CommandIntents[0].State);
    }
    [Fact] public async Task ObjectSwitchRequiresExplicitConfirmationEvenForIdenticalModel()
    {
        var vm=new AircraftFleetViewModel(Store(),()=>new IdleSource());var t=DateTimeOffset.UtcNow;
        TrackingWorldState W(uint id,int sec)=>new(Position() with {ObservedUtc=t.AddSeconds(sec)},id,id,null,PlayerControlMode.InAircraft,null,"test");
        vm.ObserveTracking(W(42,0),null);vm.Registration="TEST-1";vm.PackageVersion="test-package";vm.EnrolCommand.Execute(null);
        vm.ObserveTracking(W(43,1),null);Assert.Contains("object changed",vm.Status);Assert.Null(vm.Selected!.Latest);
        vm.ObserveTracking(W(43,12),null);Assert.Null(vm.Selected!.Latest);await vm.ShutdownAsync();
    }
    [Fact] public void HardLandingRequiresFreshEpisodeAndQualifiedProfile()
    {
        var a=Aircraft();var id=Guid.NewGuid();var recorder=new AircraftIncidentAccumulator();var profile=new IncidentProfile("synthetic-only",10,true);
        for(var t=0;t<10;t++)a=recorder.Observe(a,Sample(id,t),profile,40);Assert.DoesNotContain(a.History,e=>e.Kind=="Incident inspection");
        for(var t=10;t<=22;t++)a=recorder.Observe(a,Sample(id,t,false),profile,null);
        a=recorder.Observe(a,Sample(id,23),profile,20);
        for(var t=24;t<=29;t++)a=recorder.Observe(a,Sample(id,t),profile,null);
        Assert.Single(a.History.Where(e=>e.Kind=="Incident inspection"));Assert.Empty(a.Faults);Assert.All(a.Components,c=>Assert.Null(c.Health));
    }
    private sealed class PlacementAdapter:IAircraftPlacementAdapter
    {
        public int Writes;public double Altitude;
        public string EvidenceId=>"synthetic";public string PackageVersion=>"test-package";
        public Task<bool> ReadyAsync(PhysicalAirframe a,AirframePosition p,CancellationToken t)=>Task.FromResult(true);
        public Task PlaceAsync(PhysicalAirframe a,AirframePosition p,CancellationToken t){Writes++;Altitude=p.Telemetry.AltitudeFeet;return Task.CompletedTask;}
        public Task<bool> VerifyAsync(PhysicalAirframe a,AirframePosition p,CancellationToken t)=>Task.FromResult(true);
    }
    private sealed class EffectAdapter:IAircraftEffectAdapter
    {
        public int Writes;
        public QualifiedCommandCapability Capability=>new("engine-failed","synthetic-test","test-package",true);
        public Task<SimulatorEffectReadback> ReadAsync(PhysicalAirframe a,CancellationToken t)=>Task.FromResult(new SimulatorEffectReadback(Writes>0,false,false));
        public Task SetAsync(PhysicalAirframe a,bool state,CancellationToken t){Writes++;return Task.CompletedTask;}
    }
    private sealed class IdleSource:IAircraftConditionSource
    {
        public ConditionConnectionInfo ConnectionInfo=>new("test","test","test");
        public async IAsyncEnumerable<ConditionFrame> ReadAsync([EnumeratorCancellation] CancellationToken token){await Task.Delay(Timeout.Infinite,token);yield break;}
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
