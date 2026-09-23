using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class ProspectingOperationsViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, EmployedCargoPrototype.ErosId, "location-test", "rations")]
    [InlineData(true, "location-test", EmployedCargoPrototype.ErosId, "raw-minerals")]
    public async Task DedicatedJobFormPublishesTheSelectedRoute(bool reverse,
        string expectedOrigin, string expectedDestination, string expectedResource)
    {
        var service = new RecordingOperations(); var model = new ProspectingOperationsViewModel(service);
        await model.RefreshAsync();
        if(reverse)
        {
            model.SelectedLogisticsOrigin=model.LogisticsOrigins.Single(x=>x.LocationId=="location-test");
            model.SelectedLogisticsDestination=model.LogisticsDestinations.Single(x=>x.LocationId==EmployedCargoPrototype.ErosId);
        }
        model.LogisticsQuantity = "10"; model.LogisticsPayout = "20";

        await model.PublishLogisticsAsync();

        var call = Assert.Single(service.Publications);
        Assert.Equal(expectedOrigin, call.Origin);Assert.Equal(expectedDestination,call.Destination); Assert.Equal(expectedResource, call.Resource);
        Assert.Equal(10, call.Quantity); Assert.Equal(20, call.Payout);
        Assert.Contains(reverse?"Expected cargo sale":"Cargo purchase",model.CargoEconomics,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reserves",model.LogisticsCommitment,StringComparison.OrdinalIgnoreCase);
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

        var site=world.Sites.Single();
        await service.PublishLogisticsAsync(operation.Id, EmployedCargoPrototype.ErosId,site.LocationId,
            OperationArchetypeCatalogue.RationsResourceId, 10, 20, null);
        await service.PublishLogisticsAsync(operation.Id,site.LocationId,EmployedCargoPrototype.ErosId,
            OperationArchetypeCatalogue.RawMineralsResourceId, 10, 20, null);

        var saved = (await store.LoadAsync())!;
        Assert.Equal(2, saved.LogisticsContracts.Count);
        Assert.All(saved.LogisticsContracts, contract => Assert.StartsWith("publish-", contract.CommandId));
        var import=Assert.Single(saved.LogisticsContracts,x=>x.Direction==OperationLogisticsDirection.Import);
        Assert.Equal(EmployedCargoPrototype.ErosId,import.OriginLocationId);Assert.Equal(site.LocationId,import.DestinationLocationId);
        var export=Assert.Single(saved.LogisticsContracts,x=>x.Direction==OperationLogisticsDirection.Export);
        Assert.Equal(site.LocationId,export.OriginLocationId);Assert.Equal(EmployedCargoPrototype.ErosId,export.DestinationLocationId);
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
        public List<(string Origin,string Destination, string Resource, decimal Quantity, decimal Payout)> Publications { get; } = [];
        private static readonly IReadOnlyList<EconomicInventoryDisplay> GatewayInventory=[new("rations","Rations",30,"kg"),new("raw-minerals","Raw Minerals",0,"kg")];
        private static readonly IReadOnlyList<EconomicInventoryDisplay> OperationInventory=[new("rations","Rations",40,"kg",70),new("raw-minerals","Raw Minerals",60,"kg",140)];
        private static readonly OperationDisplay Operation = new("operation-test", "Test Camp", "TEST", "Operating", 1,
            "40 / 70 kg", "60 / 140 kg", "Ready", [], true, "Prospecting Camp", "location-test", -22.5, 17.1, OperationInventory,
            "20 kg per day", "10 kg per day", "None")
        { ImportAvailability = "Manual Rations import: up to 30 kg now.", ExportAvailability = "Manual Raw Minerals export: up to 60 kg now.",
          LogisticsLocations=[new("location-test","Test Camp","Player operation",false,-22.5,17.1,OperationInventory),new(EmployedCargoPrototype.ErosId,"Test Gateway","Gateway",true,-22.6,17.2,GatewayInventory)],
          LogisticsCargo=[new("rations","Rations","kg"),new("raw-minerals","Raw Minerals","kg")] };
        private static readonly OperationsSnapshot Snapshot = new([], [Operation], 1_000, "TPC", "memory", [], []);
        public Task<OperationsSnapshot> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<OperationsSnapshot> PublishLogisticsAsync(string operationId, string originLocationId,string destinationLocationId,
            string resourceId, decimal quantity, decimal pilotPayout, int? fallbackMinutes, CancellationToken cancellationToken = default)
        {
            if (PublicationError is not null) throw new InvalidOperationException(PublicationError);
            Publications.Add((originLocationId,destinationLocationId, resourceId, quantity, pilotPayout)); return Task.FromResult(Snapshot);
        }
        public Task<OperationsSnapshot> FoundAsync(Guid airportId, string commandId, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<OperationsSnapshot> AdvanceDayAsync(string operationId, string commandId, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<OperationsSnapshot> TransferPrototypeRationsAsync(string operationId, string commandId, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<OperationsSnapshot> EditLogisticsPayoutAsync(string contractId, decimal payout, int? fallbackMinutes, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<OperationsSnapshot> CancelLogisticsAsync(string contractId, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<OperationsSnapshot> ProcessNextRoadEventAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
    }
}
