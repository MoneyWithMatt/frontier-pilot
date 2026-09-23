using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

[Collection("Community installation")]
public sealed class PersistentEconomyTests : TemporaryDirectoryTest
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-09-19T10:00:00Z");
    private const string AirportId = "22222222222222222222222222222222";

    [Fact]
    public void GatewayOpeningStockIsAuditedIdempotentAndDoesNotAffectMoney()
    {
        var world = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        var balance = EconomyLedger.Balance(world, EmployedCargoPrototype.PlayerId, EmployedCargoPrototype.Currency);
        var first = PersistentEconomyKernel.EnsurePrototypeGatewayStock(world, new(), T0);
        Assert.True(first.Changed);
        Assert.Equal(300, Inventory(first.World, EmployedCargoPrototype.ErosId, EmployedCargoPrototype.ResourceId));
        Assert.Equal(200, Inventory(first.World, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RationsResourceId));
        Assert.Equal(0, Inventory(first.World, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RawMineralsResourceId));
        Assert.Equal(2, first.World.InventoryLedger.Count(x => x.Kind == InventoryTransactionKind.PersistentGatewayOpeningStock));
        Assert.Equal(balance, EconomyLedger.Balance(first.World, EmployedCargoPrototype.PlayerId, EmployedCargoPrototype.Currency));
        var replay = PersistentEconomyKernel.EnsurePrototypeGatewayStock(first.World, new(), T0.AddDays(1));
        Assert.False(replay.Changed); Assert.Same(first.World, replay.World);
        var corrupted = first.World with { Inventories = first.World.Inventories.Select(x =>
            x.LocationId == EmployedCargoPrototype.ErosId && x.ResourceId == OperationArchetypeCatalogue.RationsResourceId
                ? x with { AvailableQuantity = x.AvailableQuantity + 1 } : x).ToArray() };
        Assert.Throws<InvalidDataException>(() => corrupted.EnsureValid());
    }

    [Fact]
    public async Task GatewayAndOperationInventorySurviveRestartAndTransferUsesRealAuditLedger()
    {
        var path = Path.Combine(Root, "economy", "economy-state.json"); var firstStore = new LocalEconomyStateStore(path);
        var seeded = PersistentEconomyKernel.EnsurePrototypeGatewayStock(FundedWorld(), new(), T0).World;
        var founded = ProspectingCampKernel.Found(seeded, Candidate(), Presence(), "found-transfer-camp", T0).World;
        var operation = founded.Operations.Single(); var site = founded.Sites.Single();
        founded = ProspectingCampKernel.AdvanceDay(founded, operation.Id, "day-before-transfer", T0.AddDays(1)).World;
        await firstStore.InitializeAsync(founded);
        var service = new EconomyService(firstStore);
        var request = new PersistentInventoryTransferRequest("transfer-test-rations", "transfer-test-rations-command",
            EmployedCargoPrototype.ErosId, site.LocationId, OperationArchetypeCatalogue.RationsResourceId, 5,
            T0.AddDays(1).AddMinutes(1), "TEST ONLY persistent location transfer.");
        var applied = await service.TransferPersistentInventoryAsync(request); Assert.True(applied.Changed);
        Assert.Equal(195, Inventory(applied.World, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RationsResourceId));
        Assert.Equal(65, Inventory(applied.World, site.LocationId, OperationArchetypeCatalogue.RationsResourceId));
        Assert.Single(applied.World.InventoryLedger, x => x.Kind == InventoryTransactionKind.PersistentTransferOut && x.CommandId == request.CommandId && x.QuantityDelta == -5);
        Assert.Single(applied.World.InventoryLedger, x => x.Kind == InventoryTransactionKind.PersistentTransferIn && x.CommandId == request.CommandId && x.QuantityDelta == 5);
        Assert.False((await service.TransferPersistentInventoryAsync(request)).Changed);
        var restarted = await new LocalEconomyStateStore(path).LoadAsync();
        Assert.NotNull(restarted); Assert.Equal(195, Inventory(restarted!, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RationsResourceId));
        Assert.Equal(65, Inventory(restarted!, site.LocationId, OperationArchetypeCatalogue.RationsResourceId));
    }

    [Fact]
    public void TransferRejectsAbstractSystemLocationsInsufficientStockAndOperationOverCapacity()
    {
        var world = PersistentEconomyKernel.EnsurePrototypeGatewayStock(FundedWorld(), new(), T0).World;
        var founded = ProspectingCampKernel.Found(world, Candidate(), Presence(), "found-capacity-camp", T0).World;
        var site = founded.Sites.Single();
        Assert.Throws<InvalidOperationException>(() => PersistentEconomyKernel.Transfer(founded,
            Request("abstract", EmployedCargoPrototype.ErosId, EmployedCargoPrototype.UisId, EmployedCargoPrototype.ResourceId, 1)));
        Assert.Throws<InvalidOperationException>(() => PersistentEconomyKernel.Transfer(founded,
            Request("capacity", EmployedCargoPrototype.ErosId, site.LocationId, OperationArchetypeCatalogue.RationsResourceId, 1)));
        var afterDay = ProspectingCampKernel.AdvanceDay(founded, founded.Operations.Single().Id, "capacity-day", T0.AddDays(1)).World;
        Assert.Throws<InvalidOperationException>(() => PersistentEconomyKernel.Transfer(afterDay,
            Request("stock", site.LocationId, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RawMineralsResourceId, 21)));
    }

    [Fact]
    public async Task OperationsSnapshotUsesCatalogueSavedAirportsAndCombinedOperationMarker()
    {
        var definition = Fixtures.Strip with { Identifier = "FPM1", Name = "Map Camp", Latitude = -22.5, Longitude = 14.5,
            PlacedObjects = [new(1, SceneryAssets.PrototypeBaseLodgeId, 50, 50, 0)] };
        var airport = new SavedAirport(1, Guid.ParseExact(AirportId, "N"), 1, T0, definition, null!, [], "not built", "not installed");
        var store = new MemoryEconomyStateStore();
        var world = ProspectingCampKernel.Found(FundedWorld(), Candidate(), Presence(), "found-map-camp", T0).World;
        await store.InitializeAsync(world);
        var service = new ProspectingOperationsService(store, new FakeAirportManagement([airport]), new FixedPresence(Presence()), () => T0.AddHours(1));
        var snapshot = await service.LoadAsync();
        var eros = Assert.Single(snapshot.MapLocations, x => x.Kind == OperationsMapLocationKind.Gateway);
        Assert.Equal("FYWE", eros.Identifier); Assert.Contains(eros.Inventory, x => x.ResourceId == OperationArchetypeCatalogue.RationsResourceId && x.Quantity == 200);
        Assert.Contains(snapshot.MapLocations, x => x.Kind == OperationsMapLocationKind.SystemLocation && x.Identifier == "FYUS");
        var operationMarker = Assert.Single(snapshot.MapLocations, x => x.Kind == OperationsMapLocationKind.PlayerOperation);
        Assert.Contains("Player-created site", operationMarker.Concepts); Assert.NotNull(operationMarker.OperationId);
        Assert.DoesNotContain(snapshot.MapLocations, x => x.Kind == OperationsMapLocationKind.PlayerAirport);
        Assert.Contains(operationMarker.Inventory, x => x.ResourceId == OperationArchetypeCatalogue.RationsResourceId);
    }

    [Fact]
    public async Task DeveloperTransferAcceptsTheRealLongUiCommandAndRefreshesTheSnapshot()
    {
        var definition = Fixtures.Strip with { Identifier = "FPM1", Name = "Map Camp", Latitude = -22.5, Longitude = 14.5,
            PlacedObjects = [new(1, SceneryAssets.PrototypeBaseLodgeId, 50, 50, 0)] };
        var airport = new SavedAirport(1, Guid.ParseExact(AirportId, "N"), 1, T0, definition, null!, [], "not built", "not installed");
        var world = PersistentEconomyKernel.EnsurePrototypeGatewayStock(FundedWorld(), new(), T0).World;
        world = ProspectingCampKernel.Found(world, Candidate(), Presence(), "found-ui-transfer-camp", T0).World;
        var operation = world.Operations.Single(); var site = world.Sites.Single();
        world = ProspectingCampKernel.AdvanceDay(world, operation.Id, "ui-transfer-capacity", T0.AddDays(1)).World;
        var store = new MemoryEconomyStateStore(); await store.InitializeAsync(world);
        var service = new ProspectingOperationsService(store, new FakeAirportManagement([airport]), new FixedPresence(Presence()), () => T0.AddDays(1).AddMinutes(1));
        var uiCommand = "dev-transfer-rations-" + operation.Id + "-" + Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee").ToString("N");

        var result = await service.TransferPrototypeRationsAsync(operation.Id, uiCommand);

        Assert.Equal(61, result.Operations.Single().Inventory.Single(x => x.ResourceId == OperationArchetypeCatalogue.RationsResourceId).Quantity);
        var persisted = (await store.LoadAsync())!;
        Assert.Equal(199, Inventory(persisted, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RationsResourceId));
        Assert.Equal(61, Inventory(persisted, site.LocationId, OperationArchetypeCatalogue.RationsResourceId));
        Assert.Equal(2, persisted.InventoryLedger.Count(x => x.Kind is InventoryTransactionKind.PersistentTransferOut or InventoryTransactionKind.PersistentTransferIn));
        Assert.All(persisted.InventoryLedger.Where(x => x.Kind is InventoryTransactionKind.PersistentTransferOut or InventoryTransactionKind.PersistentTransferIn),
            x => Assert.InRange(x.CommandId!.Length, 1, 64));
    }

    private static PersistentInventoryTransferRequest Request(string id, string origin, string destination, string resource, decimal quantity) =>
        new("transfer-" + id, "command-" + id, origin, destination, resource, quantity, T0.AddDays(2), "TEST ONLY transfer.");
    private static decimal Inventory(EconomyWorld world, string location, string resource) =>
        world.Inventories.Single(x => x.LocationId == location && x.ResourceId == resource).AvailableQuantity;
    private static AirportSiteCandidate Candidate() => new(AirportId, "FPM1", "Map Camp", -22.5, 14.5, true, [ConstructionCapability.BaseLodge]);
    private static VerifiedSitePresenceEvidence Presence() => new("map-site-presence", "test-provider", AirportId, 42,
        -22.5, 14.5, 0, 250, T0, T0);
    private static EconomyWorld FundedWorld()
    {
        var world = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        world = EconomyKernel.AcceptJob(world, "job-uis-supplies-v0", T0.AddHours(-2)).World;
        world = EconomyKernel.MarkShipmentInTransit(world, "job-uis-supplies-v0").World;
        return EconomyKernel.CompleteDelivery(world, new("persistent-test-payment", "test-boundary", "job-uis-supplies-v0",
            "shipment-uis-supplies-v0", EmployedCargoPrototype.ErosId, EmployedCargoPrototype.UisId,
            T0.AddHours(-1), "TEST ONLY verified delivery.")).World;
    }
    private sealed class FixedPresence(VerifiedSitePresenceEvidence evidence) : ISitePresenceEvidenceProvider
    { public SitePresenceAssessment Assess(SavedAirport airport, DateTimeOffset now) => new(evidence, "verified"); }
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
