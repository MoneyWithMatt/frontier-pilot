using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using System.Text.Json.Nodes;

namespace FrontierAirportBuilder.Tests;

public sealed class EmployedCargoJobCycleTests : TemporaryDirectoryTest
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-09-10T10:00:00Z");
    private string FilePath => Path.Combine(Root, "economy", "economy-state.json");

    [Fact]
    public void RefreshIsIdempotentAndInitialGatewaySupplyIsAuditableWithoutChangingMoney()
    {
        var initial = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        var refreshed = EconomyJobBoard.Maintain(initial, T0);
        Assert.False(refreshed.Changed); Assert.Equal(2, initial.Jobs.Count(x => x.State == EconomyJobState.Offered));
        Assert.Equal(220, initial.InventoryLedger.Where(x => x.Kind == InventoryTransactionKind.SystemGatewaySupply).Sum(x => x.QuantityDelta));
        Assert.Empty(initial.FinancialLedger); Assert.Equal(0, initial.Players.Single().CachedBalance);
        Assert.All(initial.Inventories, x => Assert.True(x.AvailableQuantity >= 0));
    }

    [Fact]
    public void DeliveryCompletesImmutableRecordsAndCreatesExactlyOneReplacement()
    {
        var initial = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        var accepted = EconomyKernel.AcceptJob(initial, "job-uis-supplies-v0", T0).World;
        var inTransit = EconomyKernel.MarkShipmentInTransit(accepted, "job-uis-supplies-v0").World;
        var evidence = Evidence("job-uis-supplies-v0", "shipment-uis-supplies-v0", EmployedCargoPrototype.UisId);
        var completed = EconomyKernel.CompleteDelivery(inTransit, evidence);
        Assert.True(completed.Changed);
        Assert.Equal(EconomyJobState.Completed, completed.World.Jobs.Single(x => x.Id == "job-uis-supplies-v0").State);
        Assert.Contains(completed.World.Jobs, x => x.Id == "job-uis-supplies-v0-000002" && x.State == EconomyJobState.Offered);
        Assert.Equal(2, completed.World.Jobs.Count(x => x.State == EconomyJobState.Offered));
        Assert.Single(completed.World.FinancialLedger); Assert.Equal(240, completed.World.Players.Single().CachedBalance);

        var replay = EconomyKernel.CompleteDelivery(completed.World, evidence);
        Assert.False(replay.Changed); Assert.Equal(completed.World, replay.World);
        Assert.Equal(3, replay.World.Jobs.Count); Assert.Single(replay.World.FinancialLedger);
    }

    [Fact]
    public void ExplicitPrototypeSkipReturnsInventoryArchivesRecordsAndCreatesOneReplacementWithoutPayment()
    {
        var initial = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        var originBefore = initial.Inventories.Single(x => x.LocationId == EmployedCargoPrototype.ErosId && x.ResourceId == EmployedCargoPrototype.ResourceId).AvailableQuantity;
        var accepted = EconomyKernel.AcceptJob(initial, "job-arandis-supplies-v0", T0).World;
        var inTransit = EconomyKernel.MarkShipmentInTransit(accepted, "job-arandis-supplies-v0").World;
        var skipped = EconomyKernel.AbandonIncompletePrototypeShipment(inTransit, "job-arandis-supplies-v0", T0.AddMinutes(5));

        Assert.True(skipped.Changed);
        Assert.Equal(EconomyJobState.Cancelled, skipped.World.Jobs.Single(x => x.Id == "job-arandis-supplies-v0").State);
        Assert.Equal(TransportRequirementState.Cancelled, skipped.World.Requirements.Single(x => x.Id == "requirement-arandis-supplies-v0").State);
        Assert.Equal(ShipmentState.Cancelled, skipped.World.Shipments.Single(x => x.Id == "shipment-arandis-supplies-v0").State);
        Assert.Equal(originBefore, skipped.World.Inventories.Single(x => x.LocationId == EmployedCargoPrototype.ErosId && x.ResourceId == EmployedCargoPrototype.ResourceId).AvailableQuantity);
        Assert.Single(skipped.World.InventoryLedger, x => x.Kind == InventoryTransactionKind.ShipmentReservationReleased && x.JobId == "job-arandis-supplies-v0");
        Assert.Empty(skipped.World.FinancialLedger); Assert.Equal(0, skipped.World.Players.Single().CachedBalance);
        Assert.Contains(skipped.World.Jobs, x => x.Id == "job-arandis-supplies-v0-000002" && x.State == EconomyJobState.Offered);

        var replay = EconomyKernel.AbandonIncompletePrototypeShipment(skipped.World, "job-arandis-supplies-v0", T0.AddMinutes(6));
        Assert.False(replay.Changed); Assert.Equal(skipped.World, replay.World);
    }

    [Fact]
    public async Task CompletingThenRestartingKeepsReplacementIdAndAllowsAnotherAcceptance()
    {
        var store = new LocalEconomyStateStore(FilePath); var economy = new EconomyService(store);
        await economy.InitializeAsync(EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load()));
        await economy.AcceptJobAsync("job-arandis-supplies-v0", T0);
        await economy.MarkShipmentInTransitAsync("job-arandis-supplies-v0");
        await economy.CompleteDeliveryAsync(Evidence("job-arandis-supplies-v0", "shipment-arandis-supplies-v0", EmployedCargoPrototype.ArandisId));
        var reopened = new EmployedCargoJobsService(new LocalEconomyStateStore(FilePath), utcNow: () => T0.AddHours(2));
        var snapshot = await reopened.LoadAsync();
        Assert.Null(snapshot.ActiveJob); Assert.Contains(snapshot.RecentCompletedJobs, x => x.JobId == "job-arandis-supplies-v0");
        Assert.Contains(snapshot.Offers, x => x.JobId == "job-arandis-supplies-v0-000002");
        var accepted = await reopened.AcceptAsync("job-uis-supplies-v0");
        Assert.Equal("job-uis-supplies-v0", accepted.ActiveJob!.JobId);
    }

    [Fact]
    public async Task PreCycleAggregateIsUpgradedOnceWithoutChangingInventoryOrHistory()
    {
        var current = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        var legacy = current with { RecurringDemand = [], InventoryLedger = [] };
        await new LocalEconomyStateStore(FilePath).InitializeAsync(legacy.EnsureValid());
        var service = new EmployedCargoJobsService(new LocalEconomyStateStore(FilePath), utcNow: () => T0);
        var first = await service.LoadAsync();
        var upgraded = await new LocalEconomyStateStore(FilePath).LoadAsync();
        Assert.Equal(2, upgraded!.RecurringDemand.Count);
        Assert.Equal(220, upgraded.Inventories.Single(x => x.LocationId == EmployedCargoPrototype.ErosId).AvailableQuantity);
        Assert.Single(upgraded.InventoryLedger, x => x.Kind == InventoryTransactionKind.LegacyOpeningBalance);
        var second = await service.LoadAsync();
        Assert.Equal(first.EconomyRevision, second.EconomyRevision);
        Assert.Equal(upgraded.Jobs, (await new LocalEconomyStateStore(FilePath).LoadAsync())!.Jobs);
    }

    [Fact]
    public async Task SchemaOneMigrationIsAtomicVersionedAndPreservesBalances()
    {
        await new LocalEconomyStateStore(FilePath).InitializeAsync(EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load()));
        var document = JsonNode.Parse(await File.ReadAllTextAsync(FilePath))!.AsObject();
        document["SchemaVersion"] = 1; document["World"]!.AsObject().Remove("InventoryLedger"); document["World"]!.AsObject().Remove("RecurringDemand");
        await File.WriteAllTextAsync(FilePath, document.ToJsonString()); var schemaOne = await File.ReadAllBytesAsync(FilePath);

        var failing = new LocalEconomyStateStore(FilePath, _ => throw new IOException("simulated migration failure"));
        await Assert.ThrowsAsync<IOException>(() => failing.LoadAsync()); Assert.Equal(schemaOne, await File.ReadAllBytesAsync(FilePath));

        var migrated = await new LocalEconomyStateStore(FilePath).LoadAsync();
        Assert.Equal(220, migrated!.Inventories.Single(x => x.LocationId == EmployedCargoPrototype.ErosId).AvailableQuantity);
        Assert.Single(migrated.InventoryLedger, x => x.Kind == InventoryTransactionKind.LegacyOpeningBalance);
        Assert.Equal(5, JsonNode.Parse(await File.ReadAllTextAsync(FilePath))!["SchemaVersion"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(EconomicLocationKind.Mine)]
    [InlineData(EconomicLocationKind.Community)]
    [InlineData(EconomicLocationKind.Lodge)]
    public void RecurringDemandUsesTheSameMechanismForEveryCustomerKind(EconomicLocationKind kind)
    {
        var world = MissingUis() with { Locations = MissingUis().Locations.Select(x => x.Id == EmployedCargoPrototype.UisId ? x with { Kind = kind } : x).ToArray() };
        var maintained = EconomyJobBoard.Maintain(world.EnsureValid(), T0);
        Assert.True(maintained.Changed); Assert.Single(maintained.World.Jobs, x => x.Id == "job-uis-supplies-v0");
    }

    [Fact]
    public async Task ConcurrentMaintenanceDoesNotOverproduce()
    {
        await new LocalEconomyStateStore(FilePath).InitializeAsync(MissingUis());
        var a = new EconomyService(new LocalEconomyStateStore(FilePath));
        var b = new EconomyService(new LocalEconomyStateStore(FilePath));
        await Task.WhenAll(a.MaintainJobBoardAsync(T0), b.MaintainJobBoardAsync(T0));
        var world = await a.LoadAsync();
        Assert.Single(world!.Jobs, x => x.RequirementId == "requirement-uis-supplies-v0");
        Assert.Single(world.InventoryLedger, x => x.Id == "inventory-gateway-shipment-uis-supplies-v0");
    }

    [Fact]
    public async Task FailedMaintenanceLeavesPreviousAggregateIntact()
    {
        await new LocalEconomyStateStore(FilePath).InitializeAsync(MissingUis());
        var before = await File.ReadAllBytesAsync(FilePath);
        var failing = new EconomyService(new LocalEconomyStateStore(FilePath, _ => throw new IOException("simulated maintenance failure")));
        await Assert.ThrowsAsync<IOException>(() => failing.MaintainJobBoardAsync(T0));
        Assert.Equal(before, await File.ReadAllBytesAsync(FilePath));
    }

    private static EconomyWorld MissingUis()
    {
        var world = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        return (world with
        {
            Requirements = world.Requirements.Where(x => x.NeedId != "need-uis-supplies-v0").ToArray(),
            Jobs = world.Jobs.Where(x => x.RequirementId != "requirement-uis-supplies-v0").ToArray(),
            Inventories = world.Inventories.Select(x => x.LocationId == EmployedCargoPrototype.ErosId ? x with { AvailableQuantity = 100 } : x).ToArray(),
            InventoryLedger = world.InventoryLedger.Where(x => !x.Id.Contains("uis", StringComparison.Ordinal)).ToArray()
        }).EnsureValid();
    }

    private static VerifiedDeliveryEvidence Evidence(string job, string shipment, string destination) =>
        new("evidence-" + job, "job-cycle-test", job, shipment, EmployedCargoPrototype.ErosId, destination, T0.AddHours(1),
            "Deterministic verified-delivery boundary fixture; no simulator claim.");
}
