using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class ProspectingOperationsViewModelTests
{
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
