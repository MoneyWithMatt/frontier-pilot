using System.Xml.Linq;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class EmployedCargoPrototypeTests
{
    private static readonly DateTimeOffset Accepted = DateTimeOffset.Parse("2026-09-09T15:00:00Z");

    [Fact]
    public void VerifiedActivatedUisAndArandisProduceTheConfiguredOffers()
    {
        var world = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        Assert.Equal(EmployedCargoPrototype.WorldId, world.Id);
        Assert.Equal(["job-arandis-supplies-v0", "job-uis-supplies-v0"], world.Jobs.Select(x => x.Id).Order().ToArray());
        var uis = Requirement(world, "job-uis-supplies-v0");
        Assert.Equal(EmployedCargoPrototype.UisId, uis.DestinationLocationId); Assert.Equal(120, uis.Quantity); Assert.Equal(240, uis.PaymentAmount);
        var arandis = Requirement(world, "job-arandis-supplies-v0");
        Assert.Equal(EmployedCargoPrototype.ArandisId, arandis.DestinationLocationId); Assert.Equal(100, arandis.Quantity); Assert.Equal(200, arandis.PaymentAmount);
        Assert.Equal(220, world.Inventories.Single(x => x.LocationId == EmployedCargoPrototype.ErosId).AvailableQuantity);
        Assert.Equal(2, world.InventoryLedger.Count(x => x.Kind == InventoryTransactionKind.SystemGatewaySupply));
        Assert.Empty(world.FinancialLedger);
    }

    [Fact]
    public void OnlyVerifiedAndActivatedDestinationsGenerateOffers()
    {
        var catalogue = NamibiaLocationCatalogue.Load(); var uis = catalogue.Get(EmployedCargoPrototype.UisId);
        var downgraded = uis with { VerificationState = LocationVerificationState.ResearchCandidate, VerificationEvidence = null,
            ActivationState = CustomerActivationState.Inactive };
        var altered = (catalogue with { Locations = catalogue.Locations.Select(x => x.StableId == uis.StableId ? downgraded : x).ToArray() }).EnsureValid();
        var world = EmployedCargoPrototype.Create(altered);
        Assert.Single(world.Jobs); Assert.Equal("job-arandis-supplies-v0", world.Jobs.Single().Id);
        Assert.DoesNotContain(world.Requirements, x => x.DestinationLocationId == EmployedCargoPrototype.UisId);
    }

    [Fact]
    public void AcceptanceReservesExactlyOnceAndBlocksAnotherActiveShipment()
    {
        var initial = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        var accepted = EconomyKernel.AcceptJob(initial, "job-uis-supplies-v0", Accepted);
        Assert.True(accepted.Changed); Assert.Single(accepted.World.Shipments);
        Assert.Equal(100, accepted.World.Inventories.Single(x => x.LocationId == EmployedCargoPrototype.ErosId).AvailableQuantity);
        var replay = EconomyKernel.AcceptJob(accepted.World, "job-uis-supplies-v0", Accepted.AddMinutes(1));
        Assert.False(replay.Changed); Assert.Single(replay.World.Shipments);
        Assert.Equal(100, replay.World.Inventories.Single(x => x.LocationId == EmployedCargoPrototype.ErosId).AvailableQuantity);
        Assert.Throws<InvalidOperationException>(() => EconomyKernel.AcceptJob(replay.World, "job-arandis-supplies-v0", Accepted.AddMinutes(2)));
    }

    private static TransportRequirement Requirement(EconomyWorld world, string jobId) =>
        world.Requirements.Single(x => x.Id == world.Jobs.Single(j => j.Id == jobId).RequirementId);
}

public sealed class EmployedCargoJobsPersistenceTests : TemporaryDirectoryTest
{
    private string FilePath => Path.Combine(Root, "economy", "economy-state.json");
    private static readonly DateTimeOffset Accepted = DateTimeOffset.Parse("2026-09-09T15:00:00Z");

    [Fact]
    public async Task RestartPreservesAcceptedJobAndReservationWithoutDuplicatingCargo()
    {
        var first = Service(); var initial = await first.LoadAsync(); Assert.Equal(2, initial.Offers.Count);
        var accepted = await first.AcceptAsync("job-uis-supplies-v0");
        Assert.Equal(ShipmentState.Reserved, accepted.ActiveJob!.ShipmentState);

        var reopened = Service(); var restored = await reopened.LoadAsync();
        Assert.Equal("job-uis-supplies-v0", restored.ActiveJob!.JobId); Assert.Single(restored.Offers);
        await reopened.AcceptAsync("job-uis-supplies-v0");
        var world = await new LocalEconomyStateStore(FilePath).LoadAsync();
        Assert.Single(world!.Shipments); Assert.Equal(100, world.Inventories.Single(x => x.LocationId == EmployedCargoPrototype.ErosId).AvailableQuantity);
        Assert.Empty(world.FinancialLedger); Assert.Equal(0, world.Players.Single().CachedBalance);
    }

    [Fact]
    public async Task ServiceRefusesSecondJobWhileShipmentIsActive()
    {
        var service = Service(); await service.LoadAsync(); await service.AcceptAsync("job-uis-supplies-v0");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptAsync("job-arandis-supplies-v0"));
        var world = await new LocalEconomyStateStore(FilePath).LoadAsync();
        Assert.Single(world!.Shipments); Assert.Equal(EconomyJobState.Offered, world.Jobs.Single(x => x.Id == "job-arandis-supplies-v0").State);
    }

    [Fact]
    public async Task ViewModelRestoresActiveShipmentAndOffersNoDeliveryOrPaymentOperation()
    {
        var first = new EmployedCargoJobsViewModel(Service(), Tracker()); await first.InitializeAsync();
        first.SelectedOffer = first.Offers.Single(x => x.JobId == "job-arandis-supplies-v0"); await first.AcceptSelectedAsync();
        var reopened = new EmployedCargoJobsViewModel(Service(), Tracker()); await reopened.InitializeAsync();
        Assert.Equal("job-arandis-supplies-v0", reopened.ActiveJob!.JobId); Assert.False(reopened.AcceptCommand.CanExecute(null));
        Assert.DoesNotContain(typeof(IEmployedCargoJobs).GetMethods(), method => method.Name.Contains("Deliver", StringComparison.OrdinalIgnoreCase) || method.Name.Contains("Complete", StringComparison.OrdinalIgnoreCase) || method.Name.Contains("Pay", StringComparison.OrdinalIgnoreCase));
        var world = await new LocalEconomyStateStore(FilePath).LoadAsync();
        Assert.Empty(world!.FinancialLedger); Assert.All(world.Inventories.Where(x => x.LocationId != EmployedCargoPrototype.ErosId), x => Assert.Equal(0, x.AvailableQuantity));
    }

    private EmployedCargoJobsService Service() => new(new LocalEconomyStateStore(FilePath), NamibiaLocationCatalogue.Load(), utcNow: () => Accepted);
    private FlightAttemptTracker Tracker() => new(new LocalEconomyStateStore(FilePath),
        new LocalFlightAttemptStore(Path.Combine(Root, "economy", "flight-attempts.json")));
}

public sealed class EmployedCargoDesktopTests
{
    [Fact]
    public void JobsPageHasExplicitLoadAndUnloadWithoutDirectDeliveryOrPaymentAction()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Desktop", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        Assert.Contains(document.Descendants(presentation + "TabItem"), x => x.Attribute("Header")?.Value == "Jobs");
        var buttons = document.Descendants(presentation + "Button").Select(x => x.Attribute("Content")?.Value ?? "").ToArray();
        Assert.Contains("Accept selected job", buttons);
        Assert.Contains("Prepare aircraft…", buttons); Assert.Contains("Unload cargo…", buttons);
        Assert.Contains(document.Descendants(presentation + "TabItem"), x => x.Attribute("Header")?.Value == "Aircraft Loading");
        Assert.Contains("Copy diagnostic summary", buttons); Assert.Contains("Export support bundle…", buttons);
        Assert.Contains(document.Descendants(presentation + "TextBlock"), x => x.Attribute("Text")?.Value == "Recently completed");
        Assert.DoesNotContain(buttons, text => text.Contains("deliver", StringComparison.OrdinalIgnoreCase) || text.Contains("payment", StringComparison.OrdinalIgnoreCase));
    }
}
