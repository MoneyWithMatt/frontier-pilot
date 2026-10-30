using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.TestFixtures;
using System.Text.Json.Nodes;

namespace FrontierAirportBuilder.Tests;

public sealed class CargoJourneyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FrontierCargoJourneyTests", Guid.NewGuid().ToString("N"));
    private readonly Guid airframe = Guid.NewGuid();
    private readonly SyntheticPassengerAircraft sim = new();
    private static readonly PassengerEndpoint Origin = new("prototype-gateway", "Synthetic Eros", -23, 15);
    private static readonly PassengerEndpoint A = new("prototype-mine", "Synthetic A", -23, 15.2);
    private static readonly PassengerEndpoint B = new("prototype-community", "Synthetic B", -23, 15.4);
    private static readonly PassengerEndpoint C = new("prototype-lodge", "Synthetic C", -23, 15.6);
    private static readonly PassengerEndpoint[] Places = [Origin, A, B, C];
    private LocalEconomyStateStore Store => new(Path.Combine(root, "economy-state.json"));
    private CargoJourneyService NewService(Guid? selected = null, Action<AircraftLoadingSnapshot, bool>? authorize = null, IEconomyStateStore? store = null) =>
        new(store ?? Store, sim, () => "", () => selected ?? airframe, (_, p) => Places.SingleOrDefault(e => WorldTracking.DistanceMeters(e.Latitude, e.Longitude, p.Latitude, p.Longitude) <= 2778), () => sim.Sample.ObservedUtc, sim.WaitForRetentionAsync) { AuthorizeHandling = authorize };
    private async Task<CargoJourneyService> Create(bool hourly = false)
    {
        var w = EconomyPrototypeSeed.Create();
        w = w with {
            Businesses = w.Businesses.Select(b => b.Id == "prototype-system-cargo" ? b with { PilotHourlyRate = hourly ? 400 : null } : b).ToArray(),
            Resources = [new("medicine", "Medical supplies", "supplies", "kg"), new("food", "Food supplies", "supplies", "kg")],
            Inventories = [new(Origin.Id, "medicine", 100), new(Origin.Id, "food", 100), new(A.Id, "food", 100)],
            Needs = [new("need-mine", A.Id, "medicine", 10), new("need-community", B.Id, "food", 25), new("need-lodge", B.Id, "food", 7)],
            Requirements = w.Requirements.Select(r => r.Id switch {
                "requirement-mine" => r with { Quantity=10, ResourceId="medicine" },
                "requirement-community" => r with { Quantity=25, ResourceId="food" },
                _ => r with { Quantity=7, ResourceId="food", OriginLocationId=A.Id, DestinationLocationId=B.Id } }).ToArray()
        };
        await Store.InitializeAsync(w.EnsureValid());
        await Store.ApplyAsync(w => EconomyKernel.AcceptJob(w, "job-mine", sim.Sample.ObservedUtc));
        await Store.ApplyAsync(w => EconomyKernel.AcceptJob(w, "job-community", sim.Sample.ObservedUtc));
        return NewService();
    }
    private async Task Handle(CargoJourneyService s, Dictionary<string, decimal> desired, params CargoStationAllocation[] allocations)
    { await s.PrepareAsync(desired, allocations); await s.ApplyAsync(); }
    private async Task Observe(CargoJourneyService s, double lat, double lon, bool ground = true, int seconds = 1)
    {
        sim.Sample = sim.Sample with { ObservedUtc = sim.Sample.ObservedUtc.AddSeconds(seconds), Latitude=lat, Longitude=lon, IsOnGround=ground, GroundSpeedKnots=ground?0:100 };
        await s.ObserveAsync(sim.Sample);
    }
    private async Task Depart(CargoJourneyService s, PassengerEndpoint next)
    {
        await s.PlanLegAsync(next);
        for (var i=0;i<=10;i++) await Observe(s, sim.Sample.Latitude, sim.Sample.Longitude);
        await s.ArmLegAsync();
    }
    private async Task Fly(CargoJourneyService s, PassengerEndpoint from, PassengerEndpoint to)
    {
        await Depart(s, to);
        for (var i=1;i<=12;i++) await Observe(s, from.Latitude, from.Longitude+(to.Longitude-from.Longitude)*i/12, false);
        for (var i=0;i<=10;i++) await Observe(s, to.Latitude, to.Longitude);
        Assert.NotNull(s.Manifest!.Legs.Last().SettledUtc);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task TwoDestinationsIntermediatePickupRestartAndIndependentSettlement(bool hourly)
    {
        var s = await Create(hourly);
        var originInventory = (await Store.LoadAsync())!.Inventories.ToArray();
        await s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10},{"job-community",25}}, [new CargoStationAllocation(6,35)]);
        Assert.Equal(33, sim.Weights[5]); Assert.Empty(s.Manifest!.Cargo);
        var proposed = CargoDispatchDocument.Render(s.World, s.Manifest, "SYNTHETIC", s.Live);
        Assert.Contains("PROPOSED / PENDING",proposed); Assert.Contains("TOTAL COMMITTED CARGO: 0",proposed);
        await s.ApplyAsync(); Assert.Equal(68,sim.Weights[5]); Assert.Equal(35,s.Manifest!.Cargo.Sum(c=>c.Kilograms));
        Assert.Equal(originInventory,(await Store.LoadAsync())!.Inventories);
        var writes=sim.Writes; await Assert.ThrowsAsync<InvalidOperationException>(()=>s.ApplyAsync()); Assert.Equal(writes,sim.Writes);
        await Fly(s, Origin, A);
        Assert.Empty((await Store.LoadAsync())!.FinancialLedger);
        s=NewService(); await s.RefreshAsync();
        await Handle(s,new(){{"job-mine",0}},new CargoStationAllocation(6,25));
        Assert.Equal(58,sim.Weights[5]); Assert.Equal("job-community",Assert.Single(s.Manifest!.Cargo).JobId);
        var w=(await Store.LoadAsync())!; Assert.Equal(EconomyJobState.Completed,w.Jobs.Single(x=>x.Id=="job-mine").State);
        Assert.Equal(EconomyJobState.Accepted,w.Jobs.Single(x=>x.Id=="job-community").State);
        Assert.Equal(10,w.Inventories.Single(x=>x.LocationId==A.Id&&x.ResourceId=="medicine").AvailableQuantity);
        var sheet=CargoDispatchDocument.Render(w,s.Manifest,"SYNTHETIC",s.Live);
        Assert.Contains("TOTAL COMMITTED CARGO: 25",sheet); Assert.Contains("RETAIN ABOARD",sheet);
        await Store.ApplyAsync(w=>EconomyKernel.AcceptJob(w,"job-lodge",sim.Sample.ObservedUtc));
        await Handle(s,new(){{"job-lodge",7}},new CargoStationAllocation(6,32)); Assert.Equal(65,sim.Weights[5]);
        Assert.Equal(93,(await Store.LoadAsync())!.Inventories.Single(x=>x.LocationId==A.Id&&x.ResourceId=="food").AvailableQuantity);
        await Fly(s,A,B);
        await Handle(s,new(){{"job-community",0}},new CargoStationAllocation(6,7)); Assert.Equal(40,sim.Weights[5]);
        await Handle(s,new(){{"job-lodge",0}}); Assert.Equal(33,sim.Weights[5]); Assert.Empty(s.Manifest!.Cargo);
        w=(await Store.LoadAsync())!; Assert.All(w.Jobs,j=>Assert.Equal(EconomyJobState.Completed,j.State));
        Assert.Equal(32,w.Inventories.Single(x=>x.LocationId==B.Id&&x.ResourceId=="food").AvailableQuantity);
        Assert.Equal(hourly?2:3,w.FinancialLedger.Count);
        Assert.Equal(hourly?2.44m:330,w.Players.Single().CachedBalance);
        Assert.Equal(new decimal[]{80,30,0,0,0,33},sim.Weights); Assert.Equal(0,sim.FuelWrites);
        var final=CargoDispatchDocument.Render(w,s.Manifest,"SYNTHETIC",s.Live);
        Assert.Contains("TOTAL COMMITTED CARGO: 0",final); Assert.DoesNotContain("PROPOSED / PENDING",final);
        Assert.False(CargoJourneyKernel.Occupied(w));
    }
    [Fact] public async Task PartialQuantitiesKeepReservedRemainderAndIndependentResourcesInOneStation()
    {
        var s=await Create();
        await Handle(s,new(){{"job-mine",4},{"job-community",9}},new CargoStationAllocation(6,13)); Assert.Equal(46,sim.Weights[5]);
        await Handle(s,new(){{"job-mine",10},{"job-community",25}},new CargoStationAllocation(6,35));
        await Fly(s,Origin,A);
        await Handle(s,new(){{"job-mine",6}},new CargoStationAllocation(6,31)); Assert.Equal(64,sim.Weights[5]);
        Assert.Empty((await Store.LoadAsync())!.FinancialLedger);
        await Handle(s,new(){{"job-mine",0}},new CargoStationAllocation(6,25)); Assert.Equal(58,sim.Weights[5]);
        Assert.Single((await Store.LoadAsync())!.FinancialLedger);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ThroughStopAndPartialDeliveryDeferWagesUntilFullDeliveryThenReleaseEachEligibleLegOnce(bool cFirst)
    {
        var s=await Create(true);await Handle(s,new(){{"job-community",25}},new CargoStationAllocation(6,25));
        await Fly(s,Origin,A);Assert.Empty((await Store.LoadAsync())!.FinancialLedger);
        await Store.ApplyAsync(w=>EconomyKernel.AcceptJob(w,"job-lodge",sim.Sample.ObservedUtc));
        await Handle(s,new(){{"job-lodge",7}},new CargoStationAllocation(6,32));await Fly(s,A,B);
        Assert.Empty((await Store.LoadAsync())!.FinancialLedger);
        if(cFirst)await Handle(s,new(){{"job-lodge",0}},new CargoStationAllocation(6,25));
        Assert.Equal(cFirst?1:0,(await Store.LoadAsync())!.FinancialLedger.Count);
        await Handle(s,new(){{"job-community",15}},new CargoStationAllocation(6,cFirst?15:22));
        Assert.Equal(cFirst?1:0,(await Store.LoadAsync())!.FinancialLedger.Count);
        await Handle(s,new(){{"job-community",0}},cFirst?[]:[new CargoStationAllocation(6,7)]);
        if(!cFirst)await Handle(s,new(){{"job-lodge",0}});
        var w=(await Store.LoadAsync())!;Assert.Equal(2,w.FinancialLedger.Count);Assert.Equal(2.44m,w.Players.Single().CachedBalance);
        Assert.Equal(2,w.FinancialLedger.Select(t=>t.EvidenceId).Distinct().Count());
    }
    [Fact] public async Task ArrivalCannotAuthorizeEarlyWageAndCompletedLegAuditCannotLoseOrChangeReceipt()
    {
        var s=await Create(true);await Handle(s,new(){{"job-mine",10}},new CargoStationAllocation(6,10));await Fly(s,Origin,A);
        var before=(await Store.LoadAsync())!;
        await Handle(s,new(){{"job-mine",0}});var w=(await Store.LoadAsync())!;var paid=w.FinancialLedger.Single();
        Assert.Throws<InvalidDataException>(()=>(before with {FinancialLedger=[paid],Players=before.Players.Select(p=>p with{CachedBalance=paid.Amount}).ToArray()}).EnsureValid());
        Assert.Throws<InvalidDataException>(()=>(w with {FinancialLedger=[]}).EnsureValid());
        Assert.Throws<InvalidDataException>(()=>(w with {FinancialLedger=[paid with{Amount=paid.Amount+1}]}).EnsureValid());
        Assert.Throws<InvalidDataException>(()=>(w with {FinancialLedger=[paid with{Wage=paid.Wage! with{HourlyRate=800}}]}).EnsureValid());
    }
    [Fact] public async Task PartialSetterDisconnectRestartCompletesOnlyRemainingTargets()
    {
        var s=await Create(); await s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10},{"job-community",25}},[new(3,10),new CargoStationAllocation(6,25)]);
        sim.FailAfterStation=1; await Assert.ThrowsAsync<IOException>(()=>s.ApplyAsync());
        Assert.Equal(10,sim.Weights[2]); Assert.Equal(33,sim.Weights[5]);
        Assert.Empty((await Store.LoadAsync())!.CargoAirframes.Single().Cargo);
        await s.ObserveAsync(null); s=NewService(); await s.ApplyAsync();
        Assert.Equal(10,sim.Weights[2]); Assert.Equal(58,sim.Weights[5]); Assert.Equal(2,sim.Writes);
        Assert.Equal(2,s.Manifest!.Cargo.Length); Assert.Equal(2,(await Store.LoadAsync())!.CargoTransfers.Count);
    }
    [Fact] public async Task SetterThrowsAfterAllTargetsRetryDoesNotDuplicateMass()
    {
        var s=await Create(); await s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10}},[new CargoStationAllocation(6,10)]);
        sim.FailAfterStation=1; await Assert.ThrowsAsync<IOException>(()=>s.ApplyAsync());
        await NewService().ApplyAsync(); Assert.Equal(43,sim.Weights[5]); Assert.Equal(1,sim.Writes);
        Assert.Equal(10,(await Store.LoadAsync())!.Shipments.Single(x=>x.JobId=="job-mine").LoadedQuantity);
    }
    private sealed class FailCargoCommitOnce(IEconomyStateStore inner) : IEconomyStateStore
    {
        private bool fail=true;
        public string StoragePath=>inner.StoragePath;
        public Task<EconomyWorld?> LoadAsync(CancellationToken token=default)=>inner.LoadAsync(token);
        public Task InitializeAsync(EconomyWorld w,CancellationToken token=default)=>inner.InitializeAsync(w,token);
        public Task<EconomyTransition> ApplyAsync(Func<EconomyWorld,EconomyTransition> mutation,CancellationToken token=default)=>inner.ApplyAsync(w=>
        {
            var result=mutation(w);
            if(fail&&w.CargoAirframes.Any(m=>m.Pending is {Verified:true})&&result.World.CargoAirframes.Any(m=>m.Pending is null))
            {fail=false;throw new IOException("Injected atomic economy replacement failure after retained payload readback");}
            return result;
        },token);
    }
    [Fact] public async Task VerifiedPayloadSurvivesFailedEconomicCommitWithoutDuplicateLoadOnRestart()
    {
        await Create();var s=NewService(store:new FailCargoCommitOnce(Store));
        await s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10}},[new CargoStationAllocation(6,10)]);
        await Assert.ThrowsAsync<IOException>(()=>s.ApplyAsync());
        Assert.Equal(43,sim.Weights[5]);var pending=(await Store.LoadAsync())!;
        Assert.True(pending.CargoAirframes.Single().Pending!.Verified);Assert.Equal(0,pending.Shipments.Single(x=>x.JobId=="job-mine").LoadedQuantity);
        s=NewService();await s.ApplyAsync();Assert.Equal(1,sim.Writes);Assert.Equal(43,sim.Weights[5]);
        Assert.Equal(10,(await Store.LoadAsync())!.Shipments.Single(x=>x.JobId=="job-mine").LoadedQuantity);
    }
    [Fact] public async Task ManualPayloadChangeAndUnrelatedPayloadConflictNeverGetOverwritten()
    {
        var s=await Create(); await s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10}},[new CargoStationAllocation(6,10)]);
        sim.Weights[5]=39; await Assert.ThrowsAsync<InvalidOperationException>(()=>s.ApplyAsync());
        Assert.Equal(39,sim.Weights[5]); Assert.Equal(0,sim.Writes); Assert.NotNull((await Store.LoadAsync())!.CargoAirframes.Single().Pending);
        sim.Weights[5]=33; sim.Weights[0]=99;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>NewService().ApplyAsync()); Assert.Equal(99,sim.Weights[0]); Assert.Equal(0,sim.Writes);
    }
    [Fact] public async Task RealStationLimitAndRejectedOrClampedReadbackDoNotCommit()
    {
        var s=await Create();
        sim.StationMaximum=85;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10}},[new(1,10)]));
        await s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10}},[new(3,10)]);
        sim.RevertReadback=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.ApplyAsync());
        Assert.Equal(0,(await Store.LoadAsync())!.Shipments.Single(x=>x.JobId=="job-mine").LoadedQuantity);
    }
    [Fact] public async Task WrongSelectedAirframeAndChangedStationLayoutAreBlocked()
    {
        var s=await Create(); await Handle(s,new(){{"job-mine",10}},new CargoStationAllocation(6,10));
        var other=NewService(Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>other.PrepareAsync(new Dictionary<string,decimal>{{"job-community",25}},[new CargoStationAllocation(6,25)]));
        sim.Sample=sim.Sample with {AircraftTitle="Another aircraft"};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.PrepareAsync(new Dictionary<string,decimal>{{"job-community",25}},[new CargoStationAllocation(6,35)]));
        Assert.Equal(1,sim.Writes);
    }
    [Fact] public async Task OperationalOverweightAndCgWarningsDoNotRefuseRealPayloadWrites()
    {
        var s=await Create(); sim.MaximumGross=1050; sim.MaximumPayload=1; sim.CurrentCg=90;
        await Handle(s,new(){{"job-mine",10}},new CargoStationAllocation(6,10));
        Assert.Equal(43,sim.Weights[5]); Assert.Equal(1,sim.Writes);
        Assert.NotEmpty(AircraftLoadPlan.EvaluateWarnings(s.Live!.Weights,s.Live.Weights.CurrentGrossWeightKilograms,sim.CurrentCg));
    }
    [Fact] public async Task StationEchoWithoutPhysicalGrossChangeCannotPassByRetryingIdenticalTargets()
    {
        var s=await Create();sim.SuppressGrossChange=true;
        await s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10}},[new CargoStationAllocation(6,10)]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.ApplyAsync());Assert.Equal(43,sim.Weights[5]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>NewService().ApplyAsync());
        Assert.Equal(1,sim.Writes);Assert.Equal(0,(await Store.LoadAsync())!.Shipments.Single(x=>x.JobId=="job-mine").LoadedQuantity);
        sim.SuppressGrossChange=false;await NewService().ApplyAsync();Assert.Equal(1,sim.Writes);
        Assert.Equal(10,(await Store.LoadAsync())!.Shipments.Single(x=>x.JobId=="job-mine").LoadedQuantity);
    }
    [Fact] public async Task CargoOwnershipBlocksPassengerBoardingAndExistingPassengerOwnershipBlocksCargo()
    {
        var s=await Create(); await s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10}},[new CargoStationAllocation(6,10)]);
        var t=PassengerTransportKernel.WithWeights(PassengerTransportKernel.Plan("cargo-conflict",1,true),[70]);
        await Store.ApplyAsync(w=>new((w with {Revision=w.Revision+1,PassengerTransports=w.PassengerTransports.Append(t).ToArray()}).EnsureValid(),true));
        var engine=new PassengerPayloadCoordinator(Store,sim,sim,()=>"",sim.WaitForRetentionAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>engine.ApplyAsync(t.Id,"fixture",airframe,false,_=>{}));
        Assert.Equal(0,sim.Writes);
        await s.CancelUnappliedPlanAsync();
        await engine.ApplyAsync(t.Id,"fixture",airframe,false,_=>{});
        Assert.Equal(70,sim.Weights[2]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10}},[new CargoStationAllocation(6,10)]));
        Assert.Equal(1,sim.Writes); Assert.Equal(70,sim.Weights[2]);
    }
    [Fact] public async Task UnappliedPreviewCanBeDiscardedButPartiallyAppliedIntentCannot()
    {
        var s=await Create(); await s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10}},[new CargoStationAllocation(6,10)]);
        await s.CancelUnappliedPlanAsync(); Assert.Null(s.Manifest!.Pending); Assert.Equal(0,sim.Writes);
        await s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10}},[new CargoStationAllocation(6,10)]);
        sim.FailAfterStation=1; await Assert.ThrowsAsync<IOException>(()=>s.ApplyAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.CancelUnappliedPlanAsync());
        await s.ApplyAsync(); Assert.Equal(43,sim.Weights[5]);
    }
    [Fact] public async Task ChangedStationNamesAndLegacyCommandsCannotAlterManagedCargo()
    {
        var s=await Create(); await Handle(s,new(){{"job-mine",10}},new CargoStationAllocation(6,10));
        sim.StationPrefix="Changed layout ";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.PrepareAsync(new Dictionary<string,decimal>{{"job-community",25}},[new CargoStationAllocation(6,35)]));
        var w=(await Store.LoadAsync())!;
        Assert.Throws<InvalidOperationException>(()=>EconomyKernel.AbandonIncompletePrototypeShipment(w,"job-mine",sim.Sample.ObservedUtc));
        Assert.Throws<InvalidOperationException>(()=>EconomyKernel.MarkShipmentLoaded(w,"job-mine"));
        Assert.Equal(43,sim.Weights[5]);
    }
    [Fact] public async Task RestartAndDisconnectedGapsDoNotCreateWagesAndNewJobsDoNotInheritEarlierEvidence()
    {
        var s=await Create(true); await Handle(s,new(){{"job-mine",10},{"job-community",25}},new CargoStationAllocation(6,35)); await Depart(s,A);
        await Observe(s,-23,15.1,false); await Observe(s,-23,15.15,false);
        await s.ObserveAsync(null); await Observe(s,-23,15.18,false,60); await Observe(s,-23,15.19,false);
        Assert.Equal(TimeSpan.FromSeconds(2).Ticks,s.Manifest!.Legs.Last().Flight!.VerifiedAirborneTicks);
        s=NewService(); await Observe(s,-23,15.2,false,3600); await Observe(s,-23,15.2,false);
        Assert.Equal(TimeSpan.FromSeconds(3).Ticks,s.Manifest!.Legs.Last().Flight!.VerifiedAirborneTicks);
        for(var i=0;i<=10;i++)await Observe(s,A.Latitude,A.Longitude);
        Assert.Empty((await Store.LoadAsync())!.FinancialLedger);
        await Handle(s,new(){{"job-mine",0}},new CargoStationAllocation(6,25));
        Assert.Single((await Store.LoadAsync())!.FinancialLedger);
        await Store.ApplyAsync(w=>EconomyKernel.AcceptJob(w,"job-lodge",sim.Sample.ObservedUtc));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.PrepareAsync(new Dictionary<string,decimal>{{"job-lodge",-1}},[new CargoStationAllocation(6,35)]));
        Assert.DoesNotContain(s.Manifest!.Legs.Last().Carried,c=>c.JobId=="job-lodge");
        Assert.Equal(.33m,(await Store.LoadAsync())!.Players.Single().CachedBalance);
    }
    [Fact] public async Task AcceptingAnotherJobInFlightDoesNotChangeTheArmedCargoOrGrantEarlierProof()
    {
        var s=await Create(true);await Handle(s,new(){{"job-mine",10}},new CargoStationAllocation(6,10));await Depart(s,A);
        await Observe(s,-23,15.1,false);
        await Store.ApplyAsync(w=>EconomyKernel.AcceptJob(w,"job-lodge",sim.Sample.ObservedUtc));
        await Observe(s,A.Latitude,A.Longitude,false);for(var i=0;i<=10;i++)await Observe(s,A.Latitude,A.Longitude);
        var w=(await Store.LoadAsync())!;Assert.Equal(EconomyJobState.Accepted,w.Jobs.Single(x=>x.Id=="job-lodge").State);
        Assert.Equal("job-mine",Assert.Single(s.Manifest!.Legs.Last().Carried).JobId);
        Assert.Null(w.Shipments.Single(x=>x.JobId=="job-lodge").DeliveryEvidenceId);
        await Handle(s,new(){{"job-mine",0}});
        await Handle(s,new(){{"job-lodge",7}},new CargoStationAllocation(6,7));
        // A later ground observation at B is not proof of a post-loading flight leg.
        sim.Sample=sim.Sample with{ObservedUtc=sim.Sample.ObservedUtc.AddSeconds(1),Latitude=B.Latitude,Longitude=B.Longitude};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.PrepareAsync(new Dictionary<string,decimal>{{"job-lodge",0}},[]));
        Assert.Equal(7,(await Store.LoadAsync())!.Shipments.Single(x=>x.JobId=="job-lodge").LoadedQuantity);
    }
    [Fact] public async Task PayloadAuthorityMustBeReauthorizedAfterExpiryAndSeatedGuardIsRequired()
    {
        var s=await Create(); var allowSeated=false;
        s=NewService(authorize:(_,outside)=>{if(!outside&&!allowSeated)throw new InvalidOperationException("Seated guard");});
        await s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10}},[new CargoStationAllocation(6,10)]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.ApplyAsync()); Assert.Equal(0,sim.Writes);
        allowSeated=true; sim.Sample=sim.Sample with{ObservedUtc=sim.Sample.ObservedUtc.AddMinutes(6)};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>s.ApplyAsync()); Assert.Equal(0,sim.Writes);
        await s.RenewAuthorizationAsync(); await s.ApplyAsync(); Assert.Equal(43,sim.Weights[5]);
    }
    [Fact] public async Task SchemaTwelvePreservesJobsBalancesAndLegacyCargoWithoutInferringOwnership()
    {
        await Create(); var w=(await Store.LoadAsync())!;
        await Store.ApplyAsync(w=>EconomyKernel.MarkShipmentLoaded(w,"job-mine"));
        var before=(await Store.LoadAsync())!;
        var json=JsonNode.Parse(File.ReadAllText(Store.StoragePath))!; json["SchemaVersion"]=12; json["World"]!.AsObject().Remove("CargoAirframes");
        File.WriteAllText(Store.StoragePath,json.ToJsonString());
        var after=(await Store.LoadAsync())!; Assert.Empty(after.CargoAirframes); Assert.Equal(before.Shipments,after.Shipments); Assert.Equal(before.Players,after.Players);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>NewService().PrepareAsync(new Dictionary<string,decimal>{{"job-community",25}},[new CargoStationAllocation(6,25)]));
        Assert.Equal(0,sim.Writes);
    }
    [Fact] public async Task GuidedLoadingAndDeliveryRetainThroughCargoWithoutManualStationArithmetic()
    {
        var s=await Create();var vm=new CargoJourneyViewModel(s,()=>Task.CompletedTask,_=>Places,()=>"SYNTHETIC");
        await vm.RefreshAsync();vm.LoadAvailableCommand.Execute(null);vm.SelectedStation=vm.Stations.Single(s=>s.Index==6);
        await vm.HandleCargoAsync();Assert.Contains("Apply cargo",vm.PrimaryActionText);Assert.False(vm.CanEditCargo);Assert.Equal(0,sim.Writes);
        await vm.HandleCargoAsync();Assert.Equal(68,sim.Weights[5]);Assert.Equal(35,s.Manifest!.Cargo.Sum(c=>c.Kilograms));
        Assert.True(vm.CanEditCargo);Assert.False(vm.PrimaryCommand.CanExecute(null));
        await Fly(s,Origin,A);await vm.RefreshAsync();vm.DeliverHereCommand.Execute(null);
        Assert.Equal(0,vm.Consignments.Single(c=>c.JobId=="job-mine").DesiredAboard);
        Assert.Equal(25,vm.Consignments.Single(c=>c.JobId=="job-community").DesiredAboard);
        await vm.HandleCargoAsync();await vm.HandleCargoAsync();Assert.Equal(58,sim.Weights[5]);Assert.Equal(25,Assert.Single(s.Manifest.Cargo).Kilograms);
    }
    [Fact] public async Task GuidedPickupRequiresExplicitCompartmentAndKeepsErrorVisibleDuringTracking()
    {
        var s=await Create();var vm=new CargoJourneyViewModel(s,()=>Task.CompletedTask,_=>Places,()=>"SYNTHETIC");
        await vm.RefreshAsync();vm.LoadAvailableCommand.Execute(null);await vm.HandleCargoAsync();
        Assert.Contains("Choose a cargo compartment",vm.Status);Assert.Null(s.Manifest);Assert.Equal(0,sim.Writes);
        await vm.ObserveAsync(sim.Sample);Assert.Contains("Choose a cargo compartment",vm.Status);
        Assert.Contains("InvalidOperationException",vm.TechnicalDetails);
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task OutsideIdentityFailureIdentifiesStageAndCannotCommitCargoOrPayment(bool tracking)
    {
        await Create();var s=NewService(authorize:(_,_)=>{if(tracking)throw new InvalidDataException("MSFS aircraft identity is incomplete (ATC MODEL missing).");});
        if(!tracking)sim.Sample=sim.Sample with{AircraftType=""};
        var before=File.ReadAllText(Store.StoragePath);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10}},[new(6,10)]));
        Assert.Contains(tracking?"walking-avatar access and live tracking identity":"reading aircraft identity and payload from MSFS",error.Message);Assert.Contains("did not write payload",error.Message);
        Assert.Equal(before,File.ReadAllText(Store.StoragePath));Assert.Equal(0,sim.Writes);
    }
    [Fact] public async Task GuidedPartialWriteRetainsPlanAndRetryDoesNotDoubleApply()
    {
        var s=await Create();var vm=new CargoJourneyViewModel(s,()=>Task.CompletedTask,_=>Places,()=>"SYNTHETIC");
        await vm.RefreshAsync();vm.LoadAvailableCommand.Execute(null);vm.SelectedStation=vm.Stations.Single(s=>s.Index==6);await vm.HandleCargoAsync();
        sim.FailAfterStation=1;await vm.HandleCargoAsync();Assert.Contains("partial payload",vm.Status);Assert.False(vm.CanEditCargo);Assert.NotNull(s.Manifest!.Pending);
        await vm.HandleCargoAsync();Assert.Equal(68,sim.Weights[5]);Assert.Equal(1,sim.Writes);Assert.Equal(35,s.Manifest.Cargo.Sum(c=>c.Kilograms));
    }
    [Fact] public async Task ReopenedPendingPlanDisplaysSavedQuantitiesWithoutClaimingThemAboard()
    {
        var s=await Create();await s.PrepareAsync(new Dictionary<string,decimal>{{"job-mine",10},{"job-community",25}},[new(6,35)]);
        var resumed=NewService();var vm=new CargoJourneyViewModel(resumed,()=>Task.CompletedTask,_=>Places,()=>"SYNTHETIC");
        await vm.RefreshAsync();Assert.Equal(35,vm.RequestedKilograms);Assert.Equal(0,vm.Consignments.Sum(c=>c.Aboard));
        Assert.Equal(35,vm.Stations.Single(s=>s.Index==6).DesiredCargo);Assert.False(vm.CanEditCargo);Assert.Equal(0,sim.Writes);
        await vm.HandleCargoAsync();Assert.Equal(68,sim.Weights[5]);Assert.Null(resumed.Manifest!.Pending);
    }
    [Fact] public async Task GuidedDepartureExplainsStationaryWaitAndStillRequiresExplicitStart()
    {
        var s=await Create();await Handle(s,new(){{"job-mine",10}},new CargoStationAllocation(6,10));
        var vm=new CargoJourneyViewModel(s,()=>Task.CompletedTask,_=>Places,()=>"SYNTHETIC");await vm.RefreshAsync();
        Assert.Contains("Cargo confirmed aboard",vm.NextStep);vm.SelectedStop=vm.Stops.Single(p=>p.Endpoint==A);
        await vm.PrepareDepartureAsync();Assert.Contains("Remain stopped",vm.NextStep);Assert.False(vm.CanEditCargo);
        Assert.Null(s.Manifest!.Legs.Last().Flight);
        for(var i=0;i<=10;i++){sim.Sample=sim.Sample with{ObservedUtc=sim.Sample.ObservedUtc.AddSeconds(1)};await vm.ObserveAsync(sim.Sample);}
        Assert.Null(s.Manifest.Legs.Last().Flight);await vm.PrepareDepartureAsync();Assert.NotNull(s.Manifest.Legs.Last().Flight);
        Assert.Contains("Flight recording active",vm.NextStep);Assert.False(vm.DepartureCommand.CanExecute(null));
    }
    public void Dispose() { if(Directory.Exists(root)) Directory.Delete(root,true); }
}
