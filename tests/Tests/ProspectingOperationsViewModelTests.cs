using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class ProspectingOperationsViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Import", OperationLogisticsDirection.Import, "rations")]
    [InlineData("Export", OperationLogisticsDirection.Export, "raw-minerals")]
    public async Task DedicatedJobFormPublishesTheSelectedDirection(string selected,
        OperationLogisticsDirection expectedDirection, string expectedResource)
    {
        var service = new RecordingOperations(); var model = new ProspectingOperationsViewModel(service);
        await model.RefreshAsync();
        model.LogisticsDirection = selected; model.LogisticsQuantity = "10"; model.LogisticsPayout = "20";

        await model.PublishLogisticsAsync();

        var call = Assert.Single(service.Publications);
        Assert.Equal(expectedDirection, call.Direction); Assert.Equal(expectedResource, call.Resource);
        Assert.Equal(10, call.Quantity); Assert.Equal(20, call.Payout);
        Assert.Empty(model.Error); Assert.Contains("published", model.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicationRefusalIsVisibleBesideThePublishAction()
    {
        var service = new RecordingOperations { PublicationError = "The destination does not have enough storage capacity." };
        var model = new ProspectingOperationsViewModel(service); await model.RefreshAsync();

        await model.PublishLogisticsAsync();

        Assert.Equal("The destination does not have enough storage capacity.", model.Error);
        Assert.Contains("refused", model.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RealOperationsServiceGeneratesValidPublicationIdentifiers()
    {
        var world = FundedCampWorld(); var store = new MemoryEconomyStateStore(); await store.InitializeAsync(world);
        var service = new ProspectingOperationsService(store, new EmptyAirportManagement(), new NoPresence(), () => T0);
        var operation = world.Operations.Single();

        await service.PublishLogisticsAsync(operation.Id, OperationLogisticsDirection.Import,
            OperationArchetypeCatalogue.RationsResourceId, 10, 20, null);
        await service.PublishLogisticsAsync(operation.Id, OperationLogisticsDirection.Export,
            OperationArchetypeCatalogue.RawMineralsResourceId, 10, 20, null);

        var saved = (await store.LoadAsync())!;
        Assert.Equal(2, saved.LogisticsContracts.Count);
        Assert.All(saved.LogisticsContracts, contract => Assert.StartsWith("publish-", contract.CommandId));
    }

    private static EconomyWorld FundedCampWorld()
    {
        var world = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        world = EconomyKernel.AcceptJob(world, "job-uis-supplies-v0", T0.AddHours(-3)).World;
        world = EconomyKernel.MarkShipmentInTransit(world, "job-uis-supplies-v0").World;
        world = EconomyKernel.CompleteDelivery(world, new("evidence-operations-service", "test", "job-uis-supplies-v0", "shipment-uis-supplies-v0",
            EmployedCargoPrototype.ErosId, EmployedCargoPrototype.UisId, T0.AddHours(-2), "TEST verified delivery.")).World;
        var candidate = new AirportSiteCandidate("83f20e8a145a44afacb8b8db3a124081", "FPLC", "Service Camp", -22.5, 17.1, true,
            [ConstructionCapability.BaseLodge]);
        var presence = new VerifiedSitePresenceEvidence("presence-operations-service", "test", candidate.AirportDefinitionId, 1,
            candidate.Latitude, candidate.Longitude, 0, 250, T0.AddHours(-1), T0.AddHours(-1));
        world = ProspectingCampKernel.Found(world, candidate, presence, "found-operations-service", T0.AddHours(-1)).World;
        world = ProspectingCampKernel.AdvanceDay(world, world.Operations.Single().Id, "day-operations-service", T0.AddMinutes(-30)).World;
        return PersistentEconomyKernel.EnsurePrototypeGatewayStock(world, new(), T0).World;
    }

    private sealed class NoPresence : ISitePresenceEvidenceProvider
    { public SitePresenceAssessment Assess(SavedAirport airport, DateTimeOffset now) => new(null, "Not required by this test."); }

    private sealed class EmptyAirportManagement : IAirportManagement
    {
        public Guid? ActiveAirportId => null; public string LibraryDirectory => "memory";
        public Task<AirportList> ListAirportsAsync(CancellationToken token) => Task.FromResult(new AirportList([], []));
        public Task<SavedAirport> SaveAirportAsync(AirstripDefinition definition, CancellationToken token) => throw new NotSupportedException();
        public Task<SavedAirport> LoadAirportAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<AirportDeletionPlan> PrepareDeleteAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<string> DeleteAirportAsync(AirportDeletionPlan plan, bool confirmed, CancellationToken token) => throw new NotSupportedException();
        public void NewAirport() { }
    }

    private sealed class RecordingOperations : IProspectingOperations
    {
        public string StoragePath => "memory";
        public string? PublicationError { get; init; }
        public List<(OperationLogisticsDirection Direction, string Resource, decimal Quantity, decimal Payout)> Publications { get; } = [];
        private static readonly OperationDisplay Operation = new("operation-test", "Test Camp", "TEST", "Operating", 1,
            "40 / 70 kg", "60 / 140 kg", "Ready", [], true, "Prospecting Camp", "location-test", -22.5, 17.1, [],
            "20 kg per day", "10 kg per day", "None")
        { ImportAvailability = "Manual Rations import: up to 30 kg now.", ExportAvailability = "Manual Raw Minerals export: up to 60 kg now." };
        private static readonly OperationsSnapshot Snapshot = new([], [Operation], 1_000, "TPC", "memory", [], []);
        public Task<OperationsSnapshot> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<OperationsSnapshot> PublishLogisticsAsync(string operationId, OperationLogisticsDirection direction,
            string resourceId, decimal quantity, decimal pilotPayout, int? fallbackMinutes, CancellationToken cancellationToken = default)
        {
            if (PublicationError is not null) throw new InvalidOperationException(PublicationError);
            Publications.Add((direction, resourceId, quantity, pilotPayout)); return Task.FromResult(Snapshot);
        }
        public Task<OperationsSnapshot> FoundAsync(Guid airportId, string commandId, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<OperationsSnapshot> AdvanceDayAsync(string operationId, string commandId, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<OperationsSnapshot> TransferPrototypeRationsAsync(string operationId, string commandId, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<OperationsSnapshot> EditLogisticsPayoutAsync(string contractId, decimal payout, int? fallbackMinutes, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<OperationsSnapshot> CancelLogisticsAsync(string contractId, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<OperationsSnapshot> ProcessNextRoadEventAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
    }
}
