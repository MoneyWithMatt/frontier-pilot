using System.Text.Json.Nodes;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class EconomyKernelTests
{
    private static readonly DateTimeOffset Accepted = DateTimeOffset.Parse("2026-09-09T10:00:00Z");
    private static readonly DateTimeOffset Delivered = DateTimeOffset.Parse("2026-09-09T11:15:00Z");

    [Fact]
    public void PrototypeDataIsClearlyTemporarySystemEmploymentAndSupportsAllCustomerKinds()
    {
        var world = EconomyPrototypeSeed.Create();
        Assert.All(world.Locations, location => Assert.Equal(EconomicLocationStatus.PrototypeOnly, location.Status));
        Assert.All(world.Businesses, business => Assert.Equal(EconomicOwnerKind.System, business.Owner));
        Assert.Contains(world.Businesses, business => business.Id == "prototype-system-cargo" && business.Owner == EconomicOwnerKind.System);
        Assert.Contains(EconomicLocationKind.Mine, world.Locations.Select(x => x.Kind));
        Assert.Contains(EconomicLocationKind.Community, world.Locations.Select(x => x.Kind));
        Assert.Contains(EconomicLocationKind.Lodge, world.Locations.Select(x => x.Kind));
        Assert.Equal("TPC", EconomyPrototypeSeed.Currency);
    }

    [Fact]
    public void DemandExistsIndependentlyOfFleetRecommendation()
    {
        var world = EconomyPrototypeSeed.Create(); var need = world.Needs.Single(x => x.Id == "need-mine");
        var before = EconomyDemand.UnmetQuantity(world, need);
        var assessment = EconomyDispatch.AssessCargoCapacity(world, "job-mine", new("prototype-too-small-aircraft", 1));
        Assert.False(assessment.FleetCompatible); Assert.Contains("need remains", assessment.Explanation);
        Assert.Equal(before, EconomyDemand.UnmetQuantity(world, need)); Assert.Equal(5, before);
    }

    [Fact]
    public void AcceptReservesExactlyOneShipmentAndIsIdempotent()
    {
        var initial = EconomyPrototypeSeed.Create();
        var accepted = EconomyKernel.AcceptJob(initial, "job-community", Accepted);
        Assert.True(accepted.Changed); Assert.Equal(18, Inventory(accepted.World, "prototype-gateway"));
        Assert.Equal(ShipmentState.Reserved, Assert.Single(accepted.World.Shipments).State);
        Assert.Equal(EconomyJobState.Accepted, accepted.World.Jobs.Single(x => x.Id == "job-community").State);
        var replay = EconomyKernel.AcceptJob(accepted.World, "job-community", Accepted.AddMinutes(1));
        Assert.False(replay.Changed); Assert.Same(accepted.World, replay.World); Assert.Single(replay.World.Shipments);
        Assert.Equal(18, Inventory(replay.World, "prototype-gateway"));
    }

    [Theory]
    [InlineData("mine")]
    [InlineData("community")]
    [InlineData("lodge")]
    public void TheSameDataDrivenKernelDeliversToMineCommunityAndLodge(string suffix)
    {
        var accepted = EconomyKernel.AcceptJob(EconomyPrototypeSeed.Create(), "job-" + suffix, Accepted).World;
        var result = EconomyKernel.CompleteDelivery(accepted, Evidence(suffix));
        Assert.True(result.Changed); Assert.Equal(2, Inventory(result.World, "prototype-" + suffix));
        Assert.Equal(ShipmentState.Delivered, result.World.Shipments.Single().State);
    }

    [Fact]
    public void DeliveryUpdatesCargoInventoryAndAuthoritativeLedgerExactlyOnce()
    {
        var accepted = EconomyKernel.AcceptJob(EconomyPrototypeSeed.Create(), "job-community", Accepted).World;
        var completed = EconomyKernel.CompleteDelivery(accepted, Evidence("community"));
        Assert.True(completed.Changed); Assert.Equal(2, Inventory(completed.World, "prototype-community"));
        var payment = Assert.Single(completed.World.FinancialLedger);
        Assert.Equal("payment-job-community", payment.Id); Assert.Equal(110, payment.Amount);
        Assert.Equal(110, EconomyLedger.Balance(completed.World, "prototype-player", EconomyPrototypeSeed.Currency));
        Assert.Equal(110, completed.World.Players.Single().CachedBalance);
        var replay = EconomyKernel.CompleteDelivery(completed.World, Evidence("community"));
        Assert.False(replay.Changed); Assert.Single(replay.World.FinancialLedger); Assert.Equal(2, Inventory(replay.World, "prototype-community"));
    }

    [Fact]
    public void WrongShipmentRouteOrEvidenceReplayIsRefused()
    {
        var initial = EconomyPrototypeSeed.Create();
        Assert.Throws<InvalidOperationException>(() => EconomyKernel.CompleteDelivery(initial, Evidence("community")));
        var accepted = EconomyKernel.AcceptJob(initial, "job-community", Accepted).World;
        Assert.Throws<InvalidOperationException>(() => EconomyKernel.CompleteDelivery(accepted, Evidence("community") with { DestinationLocationId = "prototype-lodge" }));
        Assert.Throws<InvalidOperationException>(() => EconomyKernel.CompleteDelivery(accepted, Evidence("community") with { ShipmentId = "shipment-mine" }));
        var completed = EconomyKernel.CompleteDelivery(accepted, Evidence("community")).World;
        Assert.Throws<InvalidOperationException>(() => EconomyKernel.CompleteDelivery(completed, Evidence("community") with { Id = "evidence-different" }));
        Assert.Throws<InvalidOperationException>(() => EconomyKernel.CompleteDelivery(completed, Evidence("community") with { DestinationLocationId = "prototype-lodge" }));
    }

    [Fact]
    public void CachedBalanceCannotDisagreeWithLedger()
    {
        var completed = EconomyKernel.CompleteDelivery(EconomyKernel.AcceptJob(EconomyPrototypeSeed.Create(), "job-mine", Accepted).World, Evidence("mine")).World;
        var tampered = completed with { Players = completed.Players.Select(x => x with { CachedBalance = x.CachedBalance + 1 }).ToArray() };
        var error = Assert.Throws<InvalidDataException>(() => tampered.EnsureValid());
        Assert.Contains("authoritative financial ledger", error.Message);
    }

    [Fact]
    public void PaymentWithoutDeliveryAndDeliveryWithoutPaymentAreInvalid()
    {
        var accepted = EconomyKernel.AcceptJob(EconomyPrototypeSeed.Create(), "job-mine", Accepted).World;
        var premature = new FinancialTransaction("payment-job-mine", FinancialTransactionKind.JobPayment, "job-mine", "evidence-mine",
            "prototype-customer-funder", "prototype-player", 100, EconomyPrototypeSeed.Currency, Delivered);
        var paidEarly = accepted with { FinancialLedger = [premature], Players = [accepted.Players.Single() with { CachedBalance = 100 }] };
        Assert.Throws<InvalidDataException>(() => paidEarly.EnsureValid());
        var completed = EconomyKernel.CompleteDelivery(accepted, Evidence("mine")).World;
        var missingPayment = completed with { FinancialLedger = [], Players = [completed.Players.Single() with { CachedBalance = 0 }] };
        Assert.Throws<InvalidDataException>(() => missingPayment.EnsureValid());
    }

    [Fact]
    public void TwoJobsCannotAllocateTheSameFundedRequirement()
    {
        var world = EconomyPrototypeSeed.Create(); var first = world.Jobs[0];
        var duplicate = first with { Id = "job-duplicate", ShipmentId = "shipment-duplicate" };
        Assert.Throws<InvalidDataException>(() => (world with { Jobs = [.. world.Jobs, duplicate] }).EnsureValid());
    }

    private static VerifiedDeliveryEvidence Evidence(string suffix) => new("evidence-" + suffix, "prototype-test-delivery-provider",
        "job-" + suffix, "shipment-" + suffix, "prototype-gateway", "prototype-" + suffix, Delivered,
        "Deterministic external confirmation fixture; no flight-validity claim.");
    private static decimal Inventory(EconomyWorld world, string location) => world.Inventories.Single(x => x.LocationId == location && x.ResourceId == "prototype-general-supplies").AvailableQuantity;
}

public sealed class EconomyPersistenceTests : TemporaryDirectoryTest
{
    private string FilePath => Path.Combine(Root, "economy", "economy-state.json");
    private static readonly DateTimeOffset Accepted = DateTimeOffset.Parse("2026-09-09T10:00:00Z");
    private static VerifiedDeliveryEvidence Evidence => new("evidence-community", "prototype-test-delivery-provider", "job-community",
        "shipment-community", "prototype-gateway", "prototype-community", DateTimeOffset.Parse("2026-09-09T11:15:00Z"),
        "Deterministic external confirmation fixture; no flight-validity claim.");

    [Fact]
    public async Task NoSavedEconomyReturnsEmptyAndInitializePersistsAcrossInstances()
    {
        Assert.Null(await new LocalEconomyStateStore(FilePath).LoadAsync());
        await new LocalEconomyStateStore(FilePath).InitializeAsync(EconomyPrototypeSeed.Create());
        var loaded = await new LocalEconomyStateStore(FilePath).LoadAsync();
        Assert.NotNull(loaded); Assert.Equal("prototype-world-v0", loaded!.Id); Assert.Equal(1, loaded.Revision);
    }

    [Fact]
    public async Task AcceptedShipmentSurvivesRestartAndCannotReserveAgain()
    {
        var first = new EconomyService(new LocalEconomyStateStore(FilePath)); await first.InitializeAsync(EconomyPrototypeSeed.Create());
        Assert.True((await first.AcceptJobAsync("job-community", Accepted)).Changed);
        var reopened = new EconomyService(new LocalEconomyStateStore(FilePath));
        Assert.False((await reopened.AcceptJobAsync("job-community", Accepted.AddMinutes(2))).Changed);
        var world = await reopened.LoadAsync(); Assert.Single(world!.Shipments);
        Assert.Equal(18, world.Inventories.Single(x => x.LocationId == "prototype-gateway").AvailableQuantity);
    }

    [Fact]
    public async Task CompletedDeliveryAndPaymentAreIdempotentAcrossRestart()
    {
        var first = new EconomyService(new LocalEconomyStateStore(FilePath)); await first.InitializeAsync(EconomyPrototypeSeed.Create());
        await first.AcceptJobAsync("job-community", Accepted); Assert.True((await first.CompleteDeliveryAsync(Evidence)).Changed);
        var reopened = new EconomyService(new LocalEconomyStateStore(FilePath));
        Assert.False((await reopened.CompleteDeliveryAsync(Evidence)).Changed);
        var world = await reopened.LoadAsync(); Assert.Single(world!.FinancialLedger); Assert.Equal(110, world.Players.Single().CachedBalance);
        Assert.Equal(2, world.Inventories.Single(x => x.LocationId == "prototype-community").AvailableQuantity);
    }

    [Fact]
    public async Task FailureAfterFlushedTemporaryWriteLeavesPreviousCompleteRevision()
    {
        await new LocalEconomyStateStore(FilePath).InitializeAsync(EconomyPrototypeSeed.Create());
        var original = await File.ReadAllBytesAsync(FilePath);
        var failing = new EconomyService(new LocalEconomyStateStore(FilePath, _ => throw new IOException("simulated failure before atomic replacement")));
        var error = await Assert.ThrowsAsync<IOException>(() => failing.AcceptJobAsync("job-community", Accepted));
        Assert.Contains("simulated failure", error.Message); Assert.Equal(original, await File.ReadAllBytesAsync(FilePath));
        var world = await new LocalEconomyStateStore(FilePath).LoadAsync(); Assert.Equal(1, world!.Revision); Assert.Empty(world.Shipments);
        Assert.Empty(world.FinancialLedger); Assert.Equal(0, world.Players.Single().CachedBalance);
    }

    [Fact]
    public async Task CorruptDocumentIsReportedAndNeverReinitializedOver()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); await File.WriteAllTextAsync(FilePath, "{bad json");
        var original = await File.ReadAllTextAsync(FilePath); var store = new LocalEconomyStateStore(FilePath);
        var load = await Assert.ThrowsAsync<IOException>(() => store.LoadAsync()); Assert.Contains("corrupt", load.Message, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<IOException>(() => store.InitializeAsync(EconomyPrototypeSeed.Create()));
        Assert.Equal(original, await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task UnsupportedSchemaIsReportedAndPreserved()
    {
        var store = new LocalEconomyStateStore(FilePath); await store.InitializeAsync(EconomyPrototypeSeed.Create());
        var document = JsonNode.Parse(await File.ReadAllTextAsync(FilePath))!.AsObject(); document["SchemaVersion"] = 99;
        await File.WriteAllTextAsync(FilePath, document.ToJsonString()); var original = await File.ReadAllTextAsync(FilePath);
        var error = await Assert.ThrowsAsync<IOException>(() => store.LoadAsync()); Assert.Contains("unsupported schema", error.Message);
        Assert.Equal(original, await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task InconsistentPersistedLedgerIsRejectedAndPreserved()
    {
        var service = new EconomyService(new LocalEconomyStateStore(FilePath)); await service.InitializeAsync(EconomyPrototypeSeed.Create());
        await service.AcceptJobAsync("job-community", Accepted); await service.CompleteDeliveryAsync(Evidence);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(FilePath))!.AsObject();
        document["World"]!["Players"]![0]!["CachedBalance"] = 999;
        await File.WriteAllTextAsync(FilePath, document.ToJsonString()); var original = await File.ReadAllTextAsync(FilePath);
        var error = await Assert.ThrowsAsync<IOException>(() => new LocalEconomyStateStore(FilePath).LoadAsync());
        Assert.Contains("inconsistent", error.Message); Assert.Equal(original, await File.ReadAllTextAsync(FilePath));
    }
}
