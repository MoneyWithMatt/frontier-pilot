using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class EmployerFleetTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"FrontierEmployerFleet",Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Epoch=DateTimeOffset.Parse("2026-10-29T12:00:00Z");
    private static CareerEmployer Employer(string category)=>CareerCatalogue.Employers.Single(x=>x.Category==category);
    private AircraftFleetStore FleetStore=>new(Path.Combine(root,"fleet","aircraft-fleet.json"));
    private CareerStore Career=>new(root);
    private CareerProfile Join(string category)
    {Career.SaveDraft(CareerStage.Opportunities,"Synthetic Pilot","namibia");return Career.AcceptEmployment(Employer(category).Id);}
    private static AircraftTelemetry At(CareerEmployer employer,DateTimeOffset? utc=null)
    {var b=CareerCatalogue.Bases.Single(x=>x.Id==employer.BaseId);return new(utc??DateTimeOffset.UtcNow,"Synthetic company aircraft","Synthetic model",b.Latitude,b.Longitude,500,0,0,true);}
    private static TrackingWorldState World(AircraftTelemetry t,uint id=42)=>new(t,id,id,null,PlayerControlMode.InAircraft,null,"Synthetic company fleet test");
    private sealed class IdleSource:IAircraftConditionSource
    {
        public ConditionConnectionInfo ConnectionInfo=>new("synthetic","synthetic","synthetic");
        public async IAsyncEnumerable<ConditionFrame> ReadAsync([EnumeratorCancellation]CancellationToken token){await Task.Delay(Timeout.Infinite,token);yield break;}
    }
    private AircraftFleetViewModel View(CareerProfile p)
    {var v=new AircraftFleetViewModel(FleetStore,()=>new IdleSource());v.ConfigureEmployment(p.PlayerId!.Value,CareerCatalogue.Employers.Single(e=>e.Id==p.Employment!.EmployerId));return v;}

    [Theory][InlineData("Missionary")][InlineData("Safari")][InlineData("Industrial")][InlineData("Offshore")]
    public void AcceptanceAllocatesEveryActualCompanyTypeWithoutInventingSimulatorEvidence(string category)
    {
        var p=Join(category);var company=Employer(category);var fleet=FleetStore.Load();
        Assert.Equal(company.Fleet.Order(),fleet.Aircraft.Select(a=>a.CompanyTypeId).Order());
        Assert.All(fleet.Aircraft,a=>{
            Assert.Equal(company.Id,a.OwnerEntityId);Assert.Equal(p.PlayerId,a.AssignedPlayerId);Assert.Equal(company.BaseId,a.AllocatedBase!.Id);
            Assert.Null(a.Latest);Assert.Null(a.Identity.AirframeHours);Assert.Empty(a.Components);Assert.Empty(a.Bindings);Assert.Equal(0,a.ObservedAirborneSeconds);
            Assert.Equal("Attention",a.Availability);Assert.Contains("not simulator observation",a.LocationLabel);
        });
        Assert.False(Directory.Exists(Path.Combine(root,"economy")));Assert.Equal(3,p.SchemaVersion);
    }
    [Fact] public void RepeatedStartupAndFreshStoreReuseIdentityRegistrationAndRevision()
    {
        var p=Join("Missionary");var before=FleetStore.Load();var bytes=File.ReadAllText(FleetStore.StoragePath);
        for(var i=0;i<5;i++)new CareerStore(root).EnsureEmployerFleet();
        Career.AcceptEmployment(Employer("Missionary").Id);
        var after=FleetStore.Load();Assert.Equal(before.Revision,after.Revision);Assert.Equal(bytes,File.ReadAllText(FleetStore.StoragePath));Assert.Equal(p.PlayerId,Career.Load().PlayerId);
    }
    [Fact] public void SeparatePlayersAndOneHundredAllocationsHaveDistinctDurableIdsAndRegistrations()
    {
        var fleet=AircraftFleet.Empty;var company=Employer("Safari");
        for(var i=0;i<100;i++)fleet=EmployerFleetProvisioning.Provision(fleet,Guid.NewGuid(),company,Epoch).Fleet;
        Assert.Equal(100*company.Fleet.Length,fleet.Aircraft.Count);Assert.Equal(fleet.Aircraft.Count,fleet.Aircraft.Select(a=>a.Id).Distinct().Count());
        Assert.Equal(fleet.Aircraft.Count,fleet.Aircraft.Select(a=>a.Registration).Distinct().Count());
        Assert.Equal(100,fleet.Aircraft.Select(a=>a.AssignedPlayerId).Distinct().Count());
        var second=new CareerStore(Path.Combine(root,"second"));second.SaveDraft(CareerStage.Opportunities,"Another Pilot","namibia");
        var p=Join("Missionary");Assert.NotEqual(p.PlayerId,second.AcceptEmployment(Employer("Missionary").Id).PlayerId);
    }
    [Fact] public void EmployerTransitionsAtAllocationBoundaryPreserveAndReuseHistoricalAirframes()
    {
        var player=Guid.NewGuid();var first=EmployerFleetProvisioning.Provision(AircraftFleet.Empty,player,Employer("Missionary"),Epoch).Fleet;
        var second=EmployerFleetProvisioning.Provision(first,player,Employer("Offshore"),Epoch.AddDays(1)).Fleet;
        var back=EmployerFleetProvisioning.Provision(second,player,Employer("Missionary"),Epoch.AddDays(2)).Fleet;
        Assert.Equal(second.Aircraft,back.Aircraft);Assert.All(first.Aircraft,a=>Assert.Equal(a,back.Aircraft.Single(x=>x.Id==a.Id)));
        Assert.All(back.Aircraft,a=>Assert.Equal(player,a.AssignedPlayerId));
    }
    [Fact] public async Task SelectionFiltersOtherPlayersAndEmployersAndCannotEnrolPersonalAssets()
    {
        var p=Join("Missionary");var f=FleetStore.Load();var other=EmployerFleetProvisioning.Provision(f,Guid.NewGuid(),Employer("Missionary"),Epoch).Fleet;
        other=EmployerFleetProvisioning.Provision(other,p.PlayerId!.Value,Employer("Offshore"),Epoch).Fleet;
        FleetStore.Commit(other with{Revision=f.Revision+1},f.Revision);
        var v=View(p);Assert.Equal(Employer("Missionary").Fleet.Length,v.Aircraft.Count);Assert.False(v.EnrolCommand.CanExecute(null));
        var original=v.Selected!.Id;v.Selected=other.Aircraft.First(a=>a.AssignedPlayerId!=p.PlayerId);Assert.Equal(original,v.Selected!.Id);Assert.Contains("not assigned",v.Status);
        v.ConfigureEmployment(p.PlayerId.Value,Employer("Offshore"));Assert.All(v.Aircraft,a=>Assert.Equal(Employer("Offshore").Id,a.OwnerEntityId));
        v.ConfigureEmployment(p.PlayerId.Value,Employer("Missionary"));Assert.Equal(f.Aircraft.Select(a=>a.Id).Order(),v.Aircraft.Select(a=>a.Id).Order());await v.ShutdownAsync();
    }
    [Fact] public void SchemaTwoCareerGetsOnePlayerIdentityWithoutChangingEmploymentBindingsOrEconomy()
    {
        var p=Join("Industrial");var path=Path.Combine(root,"career.json");var json=JsonNode.Parse(File.ReadAllText(path))!;json["SchemaVersion"]=2;json.AsObject().Remove("PlayerId");
        File.WriteAllText(path,json.ToJsonString());Directory.CreateDirectory(Path.Combine(root,"economy"));var e=Path.Combine(root,"economy","economy-state.json");File.WriteAllText(e,"preserve exact existing economy bytes");
        var migrated=Career.Load();Assert.NotNull(migrated.PlayerId);Assert.Equal(p.Employment!.EmployerId,migrated.Employment!.EmployerId);Assert.Equal(p.AircraftBindings,migrated.AircraftBindings);
        Assert.Equal(migrated.PlayerId,new CareerStore(root).Load().PlayerId);Assert.Equal("preserve exact existing economy bytes",File.ReadAllText(e));
    }
    [Fact] public void LegacyAmbiguityBlocksDuplicateAndExplicitReconciliationRetainsHistoryAndCondition()
    {
        var company=Employer("Missionary");var a=FleetOperations.Enrol("EXISTING-REG",company.Id,At(company),"synthetic-package","c208",Epoch);
        a=FleetOperations.DiscoverComponents(a,1);var c=a.Components[0] with{ObservedSeconds=123,Health=.8};a=a with{Components=a.Components.Select(x=>x.Id==c.Id?c:x).ToArray(),ObservedAirborneSeconds=321};
        Career.SaveDraft(CareerStage.Opportunities,"Synthetic Pilot","namibia");
        FleetStore.Commit(new(1,1,a.Id,[a]),0);var p=Career.AcceptEmployment(company.Id);var blocked=FleetStore.Load();
        Assert.Single(blocked.Aircraft.Where(x=>x.CompanyTypeId=="c208"));Assert.Null(blocked.Aircraft.Single(x=>x.Id==a.Id).OwnerEntityId);
        var old=blocked.Aircraft.Single(x=>x.Id==a.Id);var updated=EmployerFleetProvisioning.ConfirmLegacy(old,p.PlayerId!.Value,company,Epoch.AddHours(1));
        FleetStore.Commit(blocked with{Revision=blocked.Revision+1,Aircraft=blocked.Aircraft.Select(x=>x.Id==a.Id?updated:x).ToArray()},blocked.Revision);
        Career.EnsureEmployerFleet();var reused=FleetStore.Load().Aircraft.Single(x=>x.Id==a.Id);
        Assert.Equal(a.Registration,reused.Registration);Assert.Equal(a.Components,reused.Components);Assert.Equal(321,reused.ObservedAirborneSeconds);
        Assert.Equal(a.History,reused.History.Take(a.History.Count));Assert.Equal(p.PlayerId,reused.AssignedPlayerId);
    }
    [Fact] public void MissingOnlyOneTypeRepairsWithoutOverwritingValidAircraftHoursLocationOrMaintenance()
    {
        var p=Join("Industrial");var f=FleetStore.Load();var a=f.Aircraft[0];
        var discovered=FleetOperations.DiscoverComponents(a,1);
        var observed=discovered with{ObservedAirborneSeconds=400,Latest=new(At(Employer("Industrial")),null,AirframePositionState.LastKnown,"synthetic","unverified",Guid.NewGuid()),
            Work=[new(Guid.NewGuid(),discovered.Components[0].Id,MaintenanceAction.Inspection,"synthetic facility",Employer("Industrial").Id,10,"synthetic",Epoch,Epoch.AddHours(1),WorkOrderState.InProgress,[]){Payee="synthetic-payee"}]};
        FleetStore.Commit(f with{Revision=f.Revision+1,Aircraft=f.Aircraft.Select(x=>x.Id==a.Id?observed:x).ToArray()},f.Revision);
        var saved=FleetStore.Load();var bytes=File.ReadAllText(FleetStore.StoragePath);Career.EnsureEmployerFleet();Assert.Equal(bytes,File.ReadAllText(FleetStore.StoragePath));
        var partial=saved with{Aircraft=[observed]};var repaired=EmployerFleetProvisioning.Provision(partial,p.PlayerId!.Value,Employer("Industrial"),Epoch).Fleet;
        Assert.Equal(Employer("Industrial").Fleet.Length,repaired.Aircraft.Count);Assert.Equal(observed,repaired.Aircraft.Single(x=>x.Id==a.Id));
    }
    [Fact] public async Task ConfirmedTypeAtBaseAutomaticallyAssociatesAllocatedAircraftWithoutRegistration()
    {
        var p=Join("Missionary");var v=View(p);v.ResolveCompanyType=_=>v.Selected!.CompanyTypeId;var t=At(Employer("Missionary"));
        for(var i=0;i<=11;i++)v.ObserveTracking(World(t with{ObservedUtc=t.ObservedUtc.AddSeconds(i)}),null);
        Assert.Single(v.Selected!.Bindings);Assert.NotNull(v.Selected.Latest);Assert.Null(v.CheckDispatch(FlightAircraftIdentity.From(t),[]));
        var id=v.Selected.Id;await v.ShutdownAsync();var restarted=View(p);Assert.Equal(id,restarted.Selected!.Id);Assert.NotNull(restarted.Selected.Latest);await restarted.ShutdownAsync();
    }
    [Theory][InlineData("", "Synthetic model")][InlineData("Synthetic company aircraft", "")][InlineData("Unknown aircraft", "Synthetic model")]
    public async Task IncompleteIdentityWaitsWithoutBindingAndCompleteObservationRecovers(string title,string model)
    {
        var p=Join("Missionary");var v=View(p);v.Selected=v.Aircraft.Single(a=>a.CompanyTypeId=="c208");
        var t=At(Employer("Missionary"),DateTimeOffset.UtcNow.AddSeconds(-12));
        v.ResolveCompanyType=sample=>CompanyAircraft.Resolve(FlightAircraftIdentity.From(sample),[new(t.AircraftTitle,t.AircraftType,"c208")]);
        var bytes=File.ReadAllText(FleetStore.StoragePath);
        v.ObserveTracking(World(t with{AircraftTitle=title,AircraftType=model}),null);
        Assert.Contains("Waiting for MSFS",v.Status);Assert.Empty(v.Selected!.Bindings);Assert.Null(v.Selected.Latest);
        Assert.Contains("Waiting",v.CheckDispatch(FlightAircraftIdentity.From(t),[])!);Assert.Equal(bytes,File.ReadAllText(FleetStore.StoragePath));
        for(var i=0;i<=11;i++)v.ObserveTracking(World(t with{ObservedUtc=t.ObservedUtc.AddSeconds(i)}),null);
        Assert.Single(v.Selected.Bindings);Assert.NotNull(v.Selected.Latest);Assert.Null(v.CheckDispatch(FlightAircraftIdentity.From(t),[]));
        await v.ShutdownAsync();
    }
    [Fact] public async Task IdentityGapResetsStationaryContinuityWithoutReplacingIdentityOrLocation()
    {
        var p=Join("Missionary");var v=View(p);var t=At(Employer("Missionary"));
        v.ResolveCompanyType=sample=>CompanyAircraft.Resolve(FlightAircraftIdentity.From(sample),[new(t.AircraftTitle,t.AircraftType,v.Selected!.CompanyTypeId!)]);
        v.ObserveTracking(World(t),null);v.ObserveTracking(World(t with{ObservedUtc=t.ObservedUtc.AddSeconds(9)}),null);
        v.ObserveTracking(World(t with{ObservedUtc=t.ObservedUtc.AddSeconds(10),AircraftType=""}),null);
        Assert.Null(v.Selected!.Latest);Assert.NotNull(v.CheckDispatch(FlightAircraftIdentity.From(t),[]));
        for(var i=11;i<=20;i++)v.ObserveTracking(World(t with{ObservedUtc=t.ObservedUtc.AddSeconds(i)}),null);
        Assert.Null(v.Selected.Latest);
        v.ObserveTracking(World(t with{ObservedUtc=t.ObservedUtc.AddSeconds(21)}),null);Assert.NotNull(v.Selected.Latest);
        Assert.Single(v.Selected.Bindings);await v.ShutdownAsync();
    }
    [Fact] public async Task DurableFleetRevisionFailureStaysBlockedAfterCompleteTelemetry()
    {
        var p=Join("Missionary");var v=View(p);v.ResolveCompanyType=sample=>{FlightAircraftIdentity.From(sample);return v.Selected!.CompanyTypeId;};
        var f=FleetStore.Load();FleetStore.Commit(f with{Revision=f.Revision+1},f.Revision);var bytes=File.ReadAllText(FleetStore.StoragePath);
        var t=At(Employer("Missionary"));v.ObserveTracking(World(t),null);Assert.Contains("revision conflict",v.Status);
        v.ObserveTracking(World(t with{AircraftType=""}),null);v.ObserveTracking(World(t with{ObservedUtc=t.ObservedUtc.AddSeconds(1)}),null);
        Assert.Contains("Fleet is blocked",v.CheckDispatch(FlightAircraftIdentity.From(t),[])!);
        Assert.Equal(bytes,File.ReadAllText(FleetStore.StoragePath));await v.ShutdownAsync();
    }
    [Fact] public async Task WrongTypeWrongLocationAndDisabledAircraftRemainUnavailable()
    {
        var p=Join("Offshore");var v=View(p);var enabled=v.Aircraft.Single(a=>a.CompanyTypeId=="vision-jet");v.Selected=enabled;
        v.ResolveCompanyType=_=>"c208";var t=At(Employer("Offshore"));v.ObserveTracking(World(t),null);Assert.Empty(v.Selected!.Bindings);
        Assert.NotNull(v.CheckDispatch(FlightAircraftIdentity.From(t),[]));
        v.ResolveCompanyType=_=>"vision-jet";v.ObserveTracking(World(t with{Latitude=t.Latitude+3}),null);Assert.Null(v.Selected.Latest);Assert.Empty(v.Selected.Bindings);
        v.PackageVersion="synthetic";v.BindCommand.Execute(null);Assert.Empty(v.Selected.Bindings);Assert.Contains("recorded location",v.Status);
        v.Selected=v.Aircraft.Single(a=>a.CompanyTypeId=="h125");v.ResolveCompanyType=_=>"h125";v.ObserveTracking(World(t),null);
        Assert.Empty(v.Selected!.Bindings);Assert.Contains("disabled",v.SelectedInstructions);Assert.NotNull(v.CheckPassengerDispatch(FlightAircraftIdentity.From(t),new("synthetic","employer","player",new("a","A",-23,15),new("b","B",-23,16),1,["h125"],800,Epoch,Epoch.AddHours(1))));
        await v.ShutdownAsync();
    }
    [Fact] public async Task DisconnectReconnectRetainsGuidAndRejectsIncompatibleObject()
    {
        var p=Join("Missionary");var v=View(p);v.ResolveCompanyType=_=>v.Selected!.CompanyTypeId;var t=At(Employer("Missionary"));v.ObserveTracking(World(t),null);var id=v.Selected!.Id;
        v.ObserveTracking(null,null);v.ObserveTracking(World(t with{ObservedUtc=t.ObservedUtc.AddSeconds(1)},99),null);Assert.Equal(id,v.Selected!.Id);
        v.ResolveCompanyType=_=>"pa28";v.ObserveTracking(World(t with{AircraftTitle="Incompatible"},100),null);Assert.Equal(id,v.Selected!.Id);Assert.DoesNotContain(v.Selected.Bindings,b=>b.Title=="Incompatible");
        await v.ShutdownAsync();
    }
    [Fact] public async Task UnobservedCompanyMoveNeedsExplicitDeveloperRecoveryAndCannotStartObservationByBinding()
    {
        var p=Join("Missionary");var v=View(p);v.ResolveCompanyType=_=>v.Selected!.CompanyTypeId;var t=At(Employer("Missionary"),DateTimeOffset.UtcNow.AddSeconds(-12));
        for(var i=0;i<=11;i++)v.ObserveTracking(World(t with{ObservedUtc=t.ObservedUtc.AddSeconds(i)}),null);
        var original=v.Selected!.Latest!;v.ObserveTracking(null,null);
        var moved=t with{ObservedUtc=DateTimeOffset.UtcNow,Latitude=t.Latitude+3};v.ObserveTracking(World(moved),null);
        Assert.True(v.Selected!.LocationReconciliationRequired);Assert.Equal(original.Telemetry.Latitude,v.Selected.Latest!.Telemetry.Latitude);
        Assert.NotNull(v.CheckDispatch(FlightAircraftIdentity.From(moved),[]));v.AcknowledgeRelocation();
        Assert.False(v.Selected.LocationReconciliationRequired);Assert.Equal(moved.Latitude,v.Selected.Latest!.Telemetry.Latitude);
        Assert.Equal(AirframePositionState.Uncertain,v.Selected.Latest.State);Assert.Contains(v.Selected.History,e=>e.Kind=="Developer relocation acknowledged");
        Assert.Equal(0,v.Selected.ObservedAirborneSeconds);await v.ShutdownAsync();
    }
    [Fact] public void CorruptFleetAndFutureCareerArePreservedWithoutReplacement()
    {
        Join("Missionary");var path=FleetStore.StoragePath;File.WriteAllText(path,"corrupt fleet fixture");
        Assert.Throws<IOException>(()=>Career.EnsureEmployerFleet());Assert.Equal("corrupt fleet fixture",File.ReadAllText(path));
        var career=Path.Combine(root,"career.json");var json=JsonNode.Parse(File.ReadAllText(career))!;json["SchemaVersion"]=999;File.WriteAllText(career,json.ToJsonString());
        Assert.Throws<IOException>(()=>Career.Load());Assert.Contains("999",File.ReadAllText(career));
    }
    [Fact] public async Task ConcurrentStartupConvergesOnOneAllocationAndPlayerIdentity()
    {
        var p=Join("Missionary");await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(()=>new CareerStore(root).EnsureEmployerFleet())));
        Assert.Equal(Employer("Missionary").Fleet.Length,FleetStore.Load().Aircraft.Count);Assert.Equal(p.PlayerId,Career.Load().PlayerId);
    }
    [Fact] public async Task AllocatedGuidWorksThroughExistingCargoAndPassengerPhysicalPayloadCoordinators()
    {
        var p=Join("Missionary");var company=Employer("Missionary");var v=View(p);v.Selected=v.Aircraft.Single(a=>a.CompanyTypeId=="c208");
        var sim=new FrontierAirportBuilder.TestFixtures.SyntheticPassengerAircraft{Sample=At(company,DateTimeOffset.UtcNow.AddSeconds(-12))};
        v.ResolveCompanyType=sample=>CompanyAircraft.Resolve(FlightAircraftIdentity.From(sample),[new(sim.Sample.AircraftTitle,sim.Sample.AircraftType,"c208")]);
        v.ObserveTracking(World(sim.Sample with{AircraftType=""}),null);
        Assert.Contains("Waiting",v.CheckDispatch(FlightAircraftIdentity.From(sim.Sample),[])!);
        for(var i=0;i<=11;i++)v.ObserveTracking(World(sim.Sample with{ObservedUtc=sim.Sample.ObservedUtc.AddSeconds(i)}),null);
        sim.Sample=sim.Sample with{ObservedUtc=sim.Sample.ObservedUtc.AddSeconds(11)};
        var cargo=new MemoryEconomyStateStore();var seed=EconomyPrototypeSeed.Create();seed=seed with{Resources=seed.Resources.Select(r=>r with{UnitName="kg"}).ToArray()};
        await cargo.InitializeAsync(seed);await cargo.ApplyAsync(w=>EconomyKernel.AcceptJob(w,"job-mine",sim.Sample.ObservedUtc));
        var loading=new CargoJourneyService(cargo,sim,()=>"synthetic",()=>v.Selected!.Id,(_,_)=>new("prototype-gateway","Synthetic base",sim.Sample.Latitude,sim.Sample.Longitude),()=>sim.Sample.ObservedUtc,sim.WaitForRetentionAsync)
        {DispatchGuard=(id,_)=>v.CheckDispatch(id,[]),WorldObserved=v.ReconcileTransportArrivals};
        await loading.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",2}},[new(6,2)]);await loading.ApplyAsync();
        Assert.Equal(v.Selected!.Id,loading.Manifest!.AirframeId);Assert.Equal(35,sim.Weights[5]);
        var passengerStore=new MemoryEconomyStateStore();var transport=PassengerTransportKernel.WithWeights(PassengerTransportKernel.Plan("synthetic-person",1,true),[70]);
        await passengerStore.InitializeAsync(seed with{PassengerTransports=[transport]});
        var transfer=new PassengerTransfer("synthetic-transfer",company.Id,"prototype-player",new("origin","Synthetic base",sim.Sample.Latitude,sim.Sample.Longitude),new("destination","Synthetic destination",sim.Sample.Latitude+1,sim.Sample.Longitude),1,["c208"],400,Epoch,Epoch.AddHours(1),AircraftTypeId:"c208");
        Assert.Null(v.CheckPassengerDispatch(FlightAircraftIdentity.From(sim.Sample),transfer));
        var passengerSim=new FrontierAirportBuilder.TestFixtures.SyntheticPassengerAircraft{Sample=sim.Sample};
        var engine=new PassengerPayloadCoordinator(passengerStore,passengerSim,passengerSim,()=>"synthetic",passengerSim.WaitForRetentionAsync);
        var boarded=await engine.ApplyAsync(transport.Id,"c208",v.Selected.Id,false,_=>{});
        Assert.Equal(v.Selected.Id,boarded.Payload!.AirframeId);Assert.Equal(70,passengerSim.Weights[2]);Assert.Equal(35,sim.Weights[5]);
        // Separate isolated worlds test the adapters; this does not enable mixed transport in one career.
        var originLat=sim.Sample.Latitude;var originLon=sim.Sample.Longitude;var destination=new PassengerEndpoint("prototype-mine","Synthetic delivery airport",originLat+1,originLon);
        await loading.PlanLegAsync(destination);
        async Task Observe(double latitude,bool ground)
        {sim.Sample=sim.Sample with{ObservedUtc=sim.Sample.ObservedUtc.AddSeconds(1),Latitude=latitude,IsOnGround=ground,GroundSpeedKnots=ground?0:100};await loading.ObserveAsync(sim.Sample);}
        for(var i=0;i<=10;i++)await Observe(originLat,true);await loading.ArmLegAsync();
        for(var i=1;i<=12;i++)await Observe(originLat+i/12d,false);
        for(var i=0;i<=10;i++)await Observe(destination.Latitude,true);
        Assert.Equal(destination.Name,v.Selected.LastTransportStop!.Name);Assert.Equal(destination.Latitude,v.Selected.Latest!.Telemetry.Latitude);
        var historyCount=v.Selected.History.Count;v.ReconcileTransportArrivals(loading.World!);Assert.Equal(historyCount,v.Selected.History.Count);
        Assert.Null(v.Selected.Identity.AirframeHours); // Flight proof is not condition/exposure evidence.
        await v.ShutdownAsync();
    }
    [Fact] public void EvidenceDrivenHoursAndLocationSurviveCompanyProvisioningWithoutInventedHealth()
    {
        var p=Join("Missionary");var f=FleetStore.Load();var original=f.Aircraft[0];var a=FleetOperations.DiscoverComponents(original,1);var session=Guid.NewGuid();
        var t=At(Employer("Missionary"),Epoch) with{IsOnGround=false,GroundSpeedKnots=100};var exposure=new AircraftExposureAccumulator();
        QualifiedAircraftSample Sample(int seconds)=>new(session,t with{ObservedUtc=Epoch.AddSeconds(seconds)},null,seconds,true,false,false,true,true,[true]);
        a=exposure.Observe(a,Sample(0),[],false);a=exposure.Observe(a,Sample(1),[],false);
        a=new AircraftLocationAccumulator().Observe(a,Sample(1));
        FleetStore.Commit(f with{Revision=f.Revision+1,Aircraft=f.Aircraft.Select(x=>x.Id==a.Id?a:x).ToArray()},f.Revision);Career.EnsureEmployerFleet();
        var restored=FleetStore.Load().Aircraft.Single(x=>x.Id==original.Id);
        Assert.Equal(1,restored.ObservedAirborneSeconds);Assert.Equal(1,restored.Components.Single(c=>c.Key=="engine:1").ObservedSeconds);
        Assert.Null(restored.Identity.AirframeHours);Assert.All(restored.Components,c=>Assert.Null(c.Health));Assert.Equal(AirframePositionState.Airborne,restored.Latest!.State);
        Assert.Equal(original.Registration,restored.Registration);Assert.Equal(p.PlayerId,restored.AssignedPlayerId);
    }
    [Fact] public void InterruptedAcceptanceWithOnlyDurableEmploymentRepairsAllocationOnce()
    {
        var company=Employer("Missionary");var id=Guid.NewGuid();Directory.CreateDirectory(root);
        var profile=new CareerProfile(3,CareerStage.Employed,"Synthetic Pilot","namibia"){PlayerId=id,Employment=new(company.Id,company.Role,company.BaseId,company.Fleet)};
        File.WriteAllText(Path.Combine(root,"career.json"),System.Text.Json.JsonSerializer.Serialize(profile));
        Career.EnsureEmployerFleet();var bytes=File.ReadAllText(FleetStore.StoragePath);Career.EnsureEmployerFleet();
        Assert.Equal(bytes,File.ReadAllText(FleetStore.StoragePath));Assert.All(FleetStore.Load().Aircraft,a=>Assert.Equal(id,a.AssignedPlayerId));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
