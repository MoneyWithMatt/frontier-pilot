using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

[Collection("Community installation")]
public sealed class ProspectingCampKernelTests : TemporaryDirectoryTest
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-09-11T10:00:00Z");
    private const string AirportId = "11111111111111111111111111111111";
    private static AirportSiteCandidate Candidate(bool runway = true, bool lodge = true) => new(AirportId, "FPC1", "Frontier Prospect Camp",
        -22.5, 14.5, runway, lodge ? [ConstructionCapability.BaseLodge] : []);
    private static VerifiedSitePresenceEvidence Presence(DateTimeOffset? observed = null, double distance = 10)
    {
        var avatar = LocalCoordinates.ToGeographic(new(-22.5, 14.5), new(0, distance));
        return new("site-presence-proof", "test-guarded-avatar-provider", AirportId, 42,
            avatar.Latitude, avatar.Longitude, distance, 250, observed ?? T0, observed ?? T0);
    }

    [Fact]
    public void ValidAirportAndBaseLodgeFoundOnceAndDebitExactlyOnce()
    {
        Assert.True(ConstructionObjectCatalogue.AssetProvides(SceneryAssets.PrototypeBaseLodgeId, ConstructionCapability.BaseLodge));
        Assert.False(ConstructionObjectCatalogue.AssetProvides(SceneryAssets.GenericHangar56Id, ConstructionCapability.BaseLodge));
        var funded = EarnedWorld(); var founded = ProspectingCampKernel.Found(funded, Candidate(), Presence(), "found-camp", T0);
        Assert.True(founded.Changed); var world = founded.World; var site = Assert.Single(world.Sites); var operation = Assert.Single(world.Operations);
        Assert.Equal(AirportId, site.AirportDefinitionId); Assert.Equal(site.Id, operation.SiteId); Assert.Equal(0, operation.PrototypeDay);
        var debit = Assert.Single(world.FinancialLedger, x => x.Kind == FinancialTransactionKind.SiteFounding);
        Assert.Equal(150, debit.Amount); Assert.Equal(site.Id, debit.SiteId); Assert.Equal(90, world.Players.Single().CachedBalance);
        Assert.Equal(70, Inventory(world, site.LocationId, OperationArchetypeCatalogue.RationsResourceId));
        Assert.Single(world.InventoryLedger, x => x.Kind == InventoryTransactionKind.OperationInitialStock);
        var replay = ProspectingCampKernel.Found(world, Candidate(), Presence(T0.AddHours(1)), "different-replay-command", T0.AddHours(1));
        Assert.False(replay.Changed); Assert.Single(replay.World.Sites); Assert.Single(replay.World.FinancialLedger, x => x.Kind == FinancialTransactionKind.SiteFounding);
    }

    [Theory]
    [InlineData(false, true, "runway")]
    [InlineData(true, false, "Base Lodge")]
    public void MissingAirportInfrastructureIsRefused(bool runway, bool lodge, string expected)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ProspectingCampKernel.Found(EarnedWorld(), Candidate(runway, lodge), Presence(), "found-camp", T0));
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaleDistantOrWrongSitePresenceIsRefused()
    {
        Assert.ThrowsAny<Exception>(() => ProspectingCampKernel.Found(EarnedWorld(), Candidate(), Presence(T0.AddSeconds(-4)), "found-stale", T0));
        Assert.ThrowsAny<Exception>(() => ProspectingCampKernel.Found(EarnedWorld(), Candidate(), Presence(distance: 251), "found-distant", T0));
        var wrong = Presence() with { AirportDefinitionId = "22222222222222222222222222222222" };
        Assert.Throws<InvalidOperationException>(() => ProspectingCampKernel.Found(EarnedWorld(), Candidate(), wrong, "found-wrong", T0));
    }

    [Fact]
    public void InsufficientAuthoritativeFundsRefusesFounding()
    {
        var initial = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        var error = Assert.Throws<InvalidOperationException>(() => ProspectingCampKernel.Found(initial, Candidate(), Presence(), "found-poor", T0));
        Assert.Contains("authoritative ledger", error.Message); Assert.Empty(initial.Sites); Assert.Empty(initial.FinancialLedger);
    }

    [Fact]
    public async Task FoundingIsAtomicAndIdempotentAcrossRestart()
    {
        var path = Path.Combine(Root, "economy", "economy-state.json"); var store = new LocalEconomyStateStore(path);
        await store.InitializeAsync(EarnedWorld()); var before = await File.ReadAllBytesAsync(path);
        var failing = new EconomyService(new LocalEconomyStateStore(path, _ => throw new IOException("before replace")));
        await Assert.ThrowsAsync<IOException>(() => failing.FoundProspectingCampAsync(Candidate(), Presence(), "found-camp", T0));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        var service = new EconomyService(new LocalEconomyStateStore(path));
        Assert.True((await service.FoundProspectingCampAsync(Candidate(), Presence(), "found-camp", T0)).Changed);
        Assert.False((await new EconomyService(new LocalEconomyStateStore(path)).FoundProspectingCampAsync(Candidate(), Presence(T0.AddSeconds(1)), "found-replay", T0.AddSeconds(1))).Changed);
        var world = await service.LoadAsync(); Assert.Single(world!.Sites); Assert.Single(world.FinancialLedger, x => x.Kind == FinancialTransactionKind.SiteFounding);
    }

    [Fact]
    public void PrototypeDaysAreAuditedIdempotentAndSupplyConstrained()
    {
        var world = Founded(); var operation = world.Operations.Single(); var site = world.Sites.Single();
        var day1 = ProspectingCampKernel.AdvanceDay(world, operation.Id, "day-1", T0.AddDays(1));
        Assert.Equal(60, Inventory(day1.World, site.LocationId, OperationArchetypeCatalogue.RationsResourceId));
        Assert.Equal(20, Inventory(day1.World, site.LocationId, OperationArchetypeCatalogue.RawMineralsResourceId));
        Assert.Single(day1.World.InventoryLedger, x => x.Kind == InventoryTransactionKind.OperationConsumption && x.CommandId == "day-1");
        Assert.Single(day1.World.InventoryLedger, x => x.Kind == InventoryTransactionKind.OperationProduction && x.CommandId == "day-1");
        Assert.False(ProspectingCampKernel.AdvanceDay(day1.World, operation.Id, "day-1", T0.AddDays(2)).Changed);
        var depleted = day1.World;
        for (var day = 2; day <= 8; day++) depleted = ProspectingCampKernel.AdvanceDay(depleted, operation.Id, "day-" + day, T0.AddDays(day)).World;
        var last = depleted.OperationTicks.Single(x => x.CommandId == "day-8");
        Assert.Equal(0, last.InputConsumed); Assert.Equal(0, last.OutputProduced); Assert.Contains("no partial", last.Result);
        Assert.Equal(8, depleted.Operations.Single().PrototypeDay);
    }

    [Fact]
    public void MineralCapacityConstrainsAFullSuppliedDay()
    {
        var world = Founded(); var operation = world.Operations.Single(); var site = world.Sites.Single();
        for (var day = 1; day <= 6; day++) world = ProspectingCampKernel.AdvanceDay(world, operation.Id, "day-" + day, T0.AddDays(day)).World;
        var balances = world.Inventories.Select(x => x.LocationId == site.LocationId && x.ResourceId == OperationArchetypeCatalogue.RawMineralsResourceId
            ? x with { AvailableQuantity = x.AvailableQuantity + 15 } : x).ToArray();
        var setup = new InventoryTransaction("inventory-test-capacity-setup", InventoryTransactionKind.OperationProduction,
            site.LocationId, OperationArchetypeCatalogue.RawMineralsResourceId, 15, null, T0.AddDays(6).AddHours(1),
            "TEST ONLY capacity fixture.", operation.Id, "test-capacity-setup");
        world = (world with { Inventories = balances, InventoryLedger = world.InventoryLedger.Append(setup).ToArray() }).EnsureValid();
        var constrained = ProspectingCampKernel.AdvanceDay(world, operation.Id, "day-7", T0.AddDays(7)).World;
        var tick = constrained.OperationTicks.Single(x => x.CommandId == "day-7");
        Assert.Equal(5, tick.OutputProduced); Assert.Contains("Storage constrained", tick.Result);
        Assert.Equal(140, Inventory(constrained, site.LocationId, OperationArchetypeCatalogue.RawMineralsResourceId));
    }

    [Fact]
    public void ThresholdsCreateOneInboundAndOneOutboundOfferAcrossRefresh()
    {
        var world = Founded(); var operation = world.Operations.Single();
        for (var day = 1; day <= 4; day++) world = ProspectingCampKernel.AdvanceDay(world, operation.Id, "day-" + day, T0.AddDays(day)).World;
        Assert.Single(world.Requirements, x => x.NeedId == ProspectingCampKernel.InboundNeed(operation.Id) && x.State == TransportRequirementState.Open);
        Assert.Single(world.Requirements, x => x.NeedId == ProspectingCampKernel.OutboundNeed(operation.Id) && x.State == TransportRequirementState.Open);
        var replay = EconomyJobBoard.Maintain(world, T0.AddDays(5)); Assert.False(replay.Changed);
        Assert.Equal(world.Jobs, replay.World.Jobs);
        var gatewaySupply = world.InventoryLedger.Where(x => x.Kind == InventoryTransactionKind.SystemGatewaySupply && x.ResourceId == OperationArchetypeCatalogue.RationsResourceId).ToArray();
        Assert.Single(gatewaySupply); Assert.Equal(40, gatewaySupply[0].QuantityDelta);
    }

    [Fact]
    public void InboundAndOutboundDeliveryMoveInventoryAndCreditExactlyOnce()
    {
        var inboundWorld = Founded(); var operation = inboundWorld.Operations.Single(); var site = inboundWorld.Sites.Single();
        for (var day = 1; day <= 4; day++) inboundWorld = ProspectingCampKernel.AdvanceDay(inboundWorld, operation.Id, "in-day-" + day, T0.AddDays(day)).World;
        var inboundJob = JobForNeed(inboundWorld, ProspectingCampKernel.InboundNeed(operation.Id));
        inboundWorld = EconomyKernel.AcceptJob(inboundWorld, inboundJob.Id, T0.AddDays(5)).World;
        var inboundEvidence = Delivery(inboundWorld, inboundJob.Id, T0.AddDays(5).AddHours(1));
        var inbound = EconomyKernel.CompleteDelivery(inboundWorld, inboundEvidence);
        Assert.Equal(70, Inventory(inbound.World, site.LocationId, OperationArchetypeCatalogue.RationsResourceId));
        Assert.Equal(170, inbound.World.Players.Single().CachedBalance);
        Assert.False(EconomyKernel.CompleteDelivery(inbound.World, inboundEvidence).Changed);

        var outboundWorld = Founded(); operation = outboundWorld.Operations.Single(); site = outboundWorld.Sites.Single();
        for (var day = 1; day <= 2; day++) outboundWorld = ProspectingCampKernel.AdvanceDay(outboundWorld, operation.Id, "out-day-" + day, T0.AddDays(day)).World;
        var outboundJob = JobForNeed(outboundWorld, ProspectingCampKernel.OutboundNeed(operation.Id));
        outboundWorld = EconomyKernel.AcceptJob(outboundWorld, outboundJob.Id, T0.AddDays(3)).World;
        Assert.Equal(0, Inventory(outboundWorld, site.LocationId, OperationArchetypeCatalogue.RawMineralsResourceId));
        var outboundEvidence = Delivery(outboundWorld, outboundJob.Id, T0.AddDays(3).AddHours(1));
        var outbound = EconomyKernel.CompleteDelivery(outboundWorld, outboundEvidence);
        Assert.Equal(40, Inventory(outbound.World, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RawMineralsResourceId));
        Assert.Equal(250, outbound.World.Players.Single().CachedBalance);
        Assert.False(EconomyKernel.CompleteDelivery(outbound.World, outboundEvidence).Changed);
    }

    [Fact]
    public void ExistingErosOffersAndSingleActiveShipmentRuleRemain()
    {
        var world = Founded(); Assert.Contains(world.Jobs, x => x.Id == "job-uis-supplies-v0"); Assert.Contains(world.Jobs, x => x.Id == "job-arandis-supplies-v0");
        world = EconomyKernel.AcceptJob(world, "job-arandis-supplies-v0", T0.AddHours(1)).World;
        var other = world.Jobs.Single(x => x.Id == "job-uis-supplies-v0-000002");
        Assert.Throws<InvalidOperationException>(() => EconomyKernel.AcceptJob(world, other.Id, T0.AddHours(2)));
    }

    [Fact]
    public void AcceptingAnOperationJobLocksEveryOtherOffer()
    {
        var world = Founded(); var operation = world.Operations.Single();
        for (var day = 1; day <= 2; day++) world = ProspectingCampKernel.AdvanceDay(world, operation.Id, "lock-day-" + day, T0.AddDays(day)).World;
        var operationJob = JobForNeed(world, ProspectingCampKernel.OutboundNeed(operation.Id));
        world = EconomyKernel.AcceptJob(world, operationJob.Id, T0.AddDays(3)).World;
        Assert.Throws<InvalidOperationException>(() => EconomyKernel.AcceptJob(world, "job-arandis-supplies-v0", T0.AddDays(3).AddMinutes(1)));
    }

    [Fact]
    public async Task OperationStateAndOfferIdentifiersSurviveRestartWithoutDuplication()
    {
        var world = Founded(); var operation = world.Operations.Single();
        for (var day = 1; day <= 4; day++) world = ProspectingCampKernel.AdvanceDay(world, operation.Id, "restart-day-" + day, T0.AddDays(day)).World;
        var path = Path.Combine(Root, "economy", "economy-state.json"); await new LocalEconomyStateStore(path).InitializeAsync(world);
        var beforeJobs = world.Jobs.Select(x => x.Id).ToArray();
        var reopened = new EconomyService(new LocalEconomyStateStore(path));
        Assert.False((await reopened.MaintainJobBoardAsync(T0.AddDays(10))).Changed);
        var loaded = await reopened.LoadAsync(); Assert.Equal(beforeJobs, loaded!.Jobs.Select(x => x.Id));
        Assert.Equal(4, loaded.Operations.Single().PrototypeDay); Assert.Equal(4, loaded.OperationTicks.Count);
        Assert.Equal(world.Sites.Single(), loaded.Sites.Single());
    }

    [Fact]
    public async Task SchemaTwoMigratesAtomicallyWithoutChangingExistingCargoHistory()
    {
        var path = Path.Combine(Root, "economy", "economy-state.json"); var world = EarnedWorld();
        await new LocalEconomyStateStore(path).InitializeAsync(world);
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject(); json["SchemaVersion"] = 2;
        json["World"]!.AsObject().Remove("Sites"); json["World"]!.AsObject().Remove("Operations"); json["World"]!.AsObject().Remove("OperationTicks");
        await File.WriteAllTextAsync(path, json.ToJsonString());
        var schemaTwo = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<IOException>(() => new LocalEconomyStateStore(path, _ => throw new IOException("migration replace failure")).LoadAsync());
        Assert.Equal(schemaTwo, await File.ReadAllBytesAsync(path));
        var loaded = await new LocalEconomyStateStore(path).LoadAsync();
        Assert.Empty(loaded!.Sites); Assert.Empty(loaded.Operations); Assert.Empty(loaded.OperationTicks);
        Assert.Single(loaded.FinancialLedger); Assert.Equal(240, loaded.Players.Single().CachedBalance);
        Assert.Equal(3, System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!["SchemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task FlightAttemptQualifiesAtTheConfiguredOperationOrigin()
    {
        var world = Founded(); var operation = world.Operations.Single(); var site = world.Sites.Single();
        for (var day = 1; day <= 2; day++) world = ProspectingCampKernel.AdvanceDay(world, operation.Id, "flight-day-" + day, T0.AddDays(day)).World;
        var job = JobForNeed(world, ProspectingCampKernel.OutboundNeed(operation.Id));
        world = EconomyKernel.AcceptJob(world, job.Id, T0.AddDays(3)).World;
        var economy = new MemoryEconomyStateStore(); await economy.InitializeAsync(world);
        var tracker = new FlightAttemptTracker(economy, new MemoryFlightAttemptStore(), utcNow: () => T0.AddDays(3));
        await tracker.InitializeAsync();
        var sample = new AircraftTelemetry(T0.AddDays(3), "Cessna 208B", "C208", site.Latitude, site.Longitude, 100, 0, 0, true);
        for (var second = 0; second <= 10; second++) await tracker.ObserveAsync(sample with { ObservedUtc = sample.ObservedUtc.AddSeconds(second) });
        Assert.True(tracker.Current.CanLoadCargo); await tracker.LoadCargoAsync();
        Assert.Equal(site.LocationId, tracker.Current.Attempt!.OriginLocationId);
        Assert.Equal(EmployedCargoPrototype.ErosId, tracker.Current.Attempt.DestinationLocationId);
    }

    [Fact]
    public async Task StockAirportCannotBeClaimedAndUiBoundaryDoesNotAcceptEvidence()
    {
        var store = new MemoryEconomyStateStore(); await store.InitializeAsync(EarnedWorld());
        var management = new FakeAirportManagement([]); var presence = new FixedPresence(Presence());
        var service = new ProspectingOperationsService(store, management, presence, () => T0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FoundAsync(Guid.ParseExact(AirportId, "N"), "found-stock"));
        Assert.DoesNotContain(typeof(IProspectingOperations).GetMethods().SelectMany(x => x.GetParameters()), x => x.ParameterType == typeof(VerifiedSitePresenceEvidence));
    }

    [Fact]
    public async Task OperationsServicePresentsAndFoundsOnlyTheSavedCapableAirport()
    {
        var id = Guid.ParseExact(AirportId, "N");
        var definition = Fixtures.Strip with { Identifier = "FPC1", Name = "Frontier Prospect Camp", Latitude = -22.5, Longitude = 14.5,
            PlacedObjects = [new(1, SceneryAssets.PrototypeBaseLodgeId, 50, 50, 0)] };
        var airport = new SavedAirport(1, id, 1, T0, definition, null!, [], "not built", "not installed");
        var store = new MemoryEconomyStateStore(); await store.InitializeAsync(EarnedWorld());
        var service = new ProspectingOperationsService(store, new FakeAirportManagement([airport]), new FixedPresence(Presence()), () => T0);
        var before = await service.LoadAsync(); Assert.True(Assert.Single(before.Airports).CanFound);
        var after = await service.FoundAsync(id, "found-through-service");
        Assert.Single(after.Operations); Assert.True(Assert.Single(after.Airports).IsLinked); Assert.Equal(90, after.PlayerBalance);
        var restarted = new ProspectingOperationsService(store, new FakeAirportManagement([airport]), new RefusingPresence(), () => T0.AddDays(1));
        var replay = await restarted.FoundAsync(id, "found-through-service");
        Assert.Single(replay.Operations); Assert.Equal(90, replay.PlayerBalance);
    }

    [Fact]
    public async Task LinkedEconomicAirportDeletionIsRefusedAndDefinitionIsPreserved()
    {
        var builder = BuilderServices.CreateDryRun(); var vm = new BuilderViewModel(builder, "");
        await vm.CheckAsync(); await vm.CaptureAsync(); vm.AirportName = "Linked camp"; vm.Identifier = "FPC1"; vm.SiteConfirmed = true;
        vm.Layout.AddObject(new(50, 50), vm.Survey!.TrueHeadingDegrees, SceneryAssets.PrototypeBaseLodgeId); await vm.SaveAirportAsync();
        var saved = Assert.Single(vm.Airports); var candidate = ProspectingOperationsService.Candidate(saved);
        var evidence = Presence() with { AirportDefinitionId = saved.Id.ToString("N"), AvatarLatitude = candidate.Latitude,
            AvatarLongitude = candidate.Longitude, DistanceMeters = 0 };
        var economyPath = Path.Combine(Root, "economy", "economy-state.json"); var economy = new LocalEconomyStateStore(economyPath);
        await economy.InitializeAsync(ProspectingCampKernel.Found(EarnedWorld(), candidate, evidence, "found-linked", T0).World);
        builder.SetEconomyDependencyStore(economy);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.PrepareDeleteAsync(saved.Id, default));
        Assert.Contains("depends on it", error.Message); Assert.Single((await builder.ListAirportsAsync(default)).Airports);
    }

    private static EconomyWorld Founded() => ProspectingCampKernel.Found(EarnedWorld(), Candidate(), Presence(), "found-camp", T0).World;
    private static EconomyWorld EarnedWorld()
    {
        var world = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        world = EconomyKernel.AcceptJob(world, "job-uis-supplies-v0", T0.AddHours(-2)).World;
        world = EconomyKernel.MarkShipmentInTransit(world, "job-uis-supplies-v0").World;
        return EconomyKernel.CompleteDelivery(world, new("evidence-earned-funds", "test-flight-boundary", "job-uis-supplies-v0",
            "shipment-uis-supplies-v0", EmployedCargoPrototype.ErosId, EmployedCargoPrototype.UisId, T0.AddHours(-1), "TEST ONLY verified delivery.")).World;
    }
    private static EconomyJob JobForNeed(EconomyWorld world, string needId)
    { var requirement = world.Requirements.Single(x => x.NeedId == needId && x.State == TransportRequirementState.Open); return world.Jobs.Single(x => x.RequirementId == requirement.Id); }
    private static VerifiedDeliveryEvidence Delivery(EconomyWorld world, string jobId, DateTimeOffset time)
    { var job = world.Jobs.Single(x => x.Id == jobId); var shipment = world.Shipments.Single(x => x.JobId == jobId); return new("evidence-operation-test", "test-flight-boundary", jobId, shipment.Id, shipment.OriginLocationId, shipment.DestinationLocationId, time, "TEST ONLY verified operation delivery."); }
    private static decimal Inventory(EconomyWorld world, string location, string resource) => world.Inventories.Single(x => x.LocationId == location && x.ResourceId == resource).AvailableQuantity;

    private sealed class FixedPresence(VerifiedSitePresenceEvidence evidence) : ISitePresenceEvidenceProvider
    { public SitePresenceAssessment Assess(SavedAirport airport, DateTimeOffset now) => new(evidence, "verified"); }
    private sealed class RefusingPresence : ISitePresenceEvidenceProvider
    { public SitePresenceAssessment Assess(SavedAirport airport, DateTimeOffset now) => throw new InvalidOperationException("Presence must not be requested for an idempotent replay."); }
    private sealed class FakeAirportManagement(IReadOnlyList<SavedAirport> values) : IAirportManagement
    {
        public Guid? ActiveAirportId => null; public string LibraryDirectory => "memory://airports";
        public Task<AirportList> ListAirportsAsync(CancellationToken token) => Task.FromResult(new AirportList(values, []));
        public Task<SavedAirport> SaveAirportAsync(AirstripDefinition definition, CancellationToken token) => throw new NotSupportedException();
        public Task<SavedAirport> LoadAirportAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<AirportDeletionPlan> PrepareDeleteAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<string> DeleteAirportAsync(AirportDeletionPlan plan, bool confirmed, CancellationToken token) => throw new NotSupportedException();
        public void NewAirport() { }
    }
}

public sealed class SitePresenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-11T12:00:00Z");
    private static SavedAirport Airport => new(1, Guid.ParseExact("11111111111111111111111111111111", "N"), 1, Now,
        Fixtures.Strip with { Identifier = "FPC1", Name = "Camp", Latitude = -22.5, Longitude = 14.5 }, null!, [], "not built", "not installed");
    [Fact]
    public void OnlyFreshNearbyGuardedOnFootAvatarCanProducePresenceEvidence()
    {
        var avatar = new PlayerTelemetry(Now, 22, -22.5, 14.5, 100, 90, true);
        var aircraft = new AircraftTelemetry(Now, "Test", "Test", -22.6, 14.6, 100, 0, 0, true);
        var onFoot = new TrackingWorldState(aircraft, 11, 22, 22, PlayerControlMode.OnFoot, avatar, "verified");
        var accepted = new GuardedAvatarSitePresence(() => onFoot).Assess(Airport, Now);
        Assert.True(accepted.IsVerified); Assert.Equal((uint)22, accepted.Evidence!.AvatarObjectId);
        Assert.False(new GuardedAvatarSitePresence(() => onFoot with { PlayerMode = PlayerControlMode.InAircraft, CurrentObjectId = 11, Player = null }).Assess(Airport, Now).IsVerified);
        Assert.False(new GuardedAvatarSitePresence(() => onFoot with { Player = avatar with { ObservedUtc = Now.AddSeconds(-4) }, Aircraft = aircraft with { ObservedUtc = Now.AddSeconds(-4) } }).Assess(Airport, Now).IsVerified);
        Assert.False(new GuardedAvatarSitePresence(() => onFoot with { Player = avatar with { Latitude = -22.49 } }).Assess(Airport, Now).IsVerified);
        Assert.False(new GuardedAvatarSitePresence(() => null).Assess(Airport, Now).IsVerified);
    }
}
