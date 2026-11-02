using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

// Synthetic airport coordinates and telemetry only. These tests do not qualify scenery or aircraft.
public sealed class OffshoreCareerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FrontierOffshoreTests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-29T12:00:00Z");
    private static CareerEmployer Employer => CareerCatalogue.Employers.Single(x => x.Category == "Offshore");
    private static DestinationReview Review(string code, params string[] aircraft) => new(code, "Approved", code,
        code == "FYWB" ? -23 : -22, 15, "Synthetic automated fixture; no live acceptance", Now, aircraft);
    private EmployerDestinationCatalogue Airports()
    {
        new SimulatorAirportStore(root).Save(new(Now, "Synthetic completed scan", [new("FYWB", -23, 15), new("FYWE", -22.6, 17), new("FYAR", -22.4, 15)]));
        return new DestinationReviewStore(root).Load();
    }
    private static EmployedCargoJobsService Service(IEconomyStateStore store, EmployerDestinationCatalogue catalogue) =>
        new(store, utcNow: () => Now, employer: Employer, destinations: catalogue);

    [Fact]
    public async Task AirportDispatchUsesWalvisBayVisionJetAndActualRoutePlanningTime()
    {
        var catalogue = Airports();
        var service = Service(new MemoryEconomyStateStore(), catalogue);
        var board = await service.LoadAsync();
        Assert.NotEmpty(board.Offers);
        Assert.All(board.Offers, job =>
        {
            Assert.Contains("FYWB", job.Origin);
            Assert.NotEqual(job.OriginLocationId, job.DestinationLocationId);
            Assert.Equal("vision-jet", Assert.Single(service.AircraftChoices(job)).Id);
            Assert.Equal(800m, job.HourlyWage);
            var minutes = Math.Max(1, (int)Math.Round(Geodesy.Inverse(new(job.OriginLatitude!.Value, job.OriginLongitude!.Value),
                new(job.DestinationLatitude!.Value, job.DestinationLongitude!.Value)).DistanceMeters / 1852 / 120 * 60));
            Assert.Contains($"about {minutes} min direct from {job.Origin} at 120 kt (planning estimate)", job.KnownDestinationInformation);
        });
        var eros = board.Offers.First(x => x.Destination.Contains("FYWE"));
        Assert.DoesNotContain("about 1 min", eros.KnownDestinationInformation);
        service.SelectedAircraftTypeId = "h125";
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptAsync(eros.JobId));
        service.SelectedAircraftTypeId = "vision-jet";
        Assert.Equal("vision-jet", Assert.Single((await service.AcceptAsync(eros.JobId)).ReservedJobs).CompanyAircraftTypeId);
    }

    [Fact]
    public async Task BaseAircraftExclusionBlocksOffshoreOffersAndAcceptanceWithoutChangingReservation()
    {
        var catalogue = Airports();
        var store = new LocalEconomyStateStore(Path.Combine(root, "economy-state.json"));
        var service = Service(store, catalogue);
        var offer = (await service.LoadAsync()).Offers.First();
        service.SelectedAircraftTypeId = "vision-jet";
        await service.AcceptAsync(offer.JobId);
        var before = (await store.LoadAsync())!;
        var reviews = new DestinationReviewStore(root);
        reviews.Save(new("FYWB", "Approved", "FYWB", -23, 15, "Synthetic base restriction", Now, ["c208"]));
        var restricted = reviews.Load();
        Assert.All(restricted.Diagnose("Offshore"), x => Assert.False(x.Enabled));
        Assert.Contains("both origin and destination", restricted.Diagnose("Offshore").First(x => x.Identifier == "FYWE").Reason);
        var restarted = Service(new LocalEconomyStateStore(store.StoragePath), restricted);
        var board = await restarted.LoadAsync();
        Assert.Empty(board.Offers);
        Assert.Equal(offer.JobId, Assert.Single(board.ReservedJobs).JobId);
        var unaccepted = before.Jobs.First(x => x.State == EconomyJobState.Offered);
        restarted.SelectedAircraftTypeId = "vision-jet";
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.AcceptAsync(unaccepted.Id));
        var after = (await store.LoadAsync())!;
        Assert.Equal(before.Shipments, after.Shipments);
        Assert.Equal(before.FinancialLedger, after.FinancialLedger);
        Assert.Equal(before.Inventories, after.Inventories);
    }

    [Fact]
    public void EndpointRestrictionsRequireOneCommonEnabledCompanyType()
    {
        var safari = CareerCatalogue.Employers.Single(x => x.Category == "Safari");
        var catalogue = new EmployerDestinationCatalogue([Review("FYWE", "bonanza", "pa28"), Review("FYNG", "pa28", "savage-cub")]);
        var destination = catalogue.Diagnose("Safari").Single(x => x.Identifier == "FYNG");
        Assert.True(destination.Enabled);
        Assert.Equal("pa28", Assert.Single(CompanyAircraft.Choices(safari, destination, 20)).Id);
        Assert.Equal("pa28", Assert.Single(CompanyAircraft.Choices(safari, destination, 50)).Id);
        var disjoint = new EmployerDestinationCatalogue([Review("FYWE", "bonanza"), Review("FYNG", "pa28")]);
        Assert.False(disjoint.Diagnose("Safari").Single(x => x.Identifier == "FYNG").Enabled);
        var offshore = new EmployerDestinationCatalogue([Review("FYWB", "vision-jet")]);
        Assert.All(offshore.Diagnose("Offshore").Where(x => x.Enabled), x =>
            Assert.Equal("vision-jet", Assert.Single(CompanyAircraft.Choices(Employer, x, 20)).Id));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("moved")]
    public async Task LaterBaseLookupProblemBlocksNewWorkAndPreservesAcceptedEndpoints(string problem)
    {
        var catalogue = Airports();
        var store = new LocalEconomyStateStore(Path.Combine(root, "economy-state.json"));
        var service = Service(store, catalogue);
        var offer = (await service.LoadAsync()).Offers.First();
        service.SelectedAircraftTypeId = "vision-jet";
        await service.AcceptAsync(offer.JobId);
        var baseRows = problem switch
        {
            "missing" => Array.Empty<SimulatorAirport>(),
            "ambiguous" => [new("FYWB", -23, 15), new SimulatorAirport("FYWB", -24, 15)],
            _ => [new SimulatorAirport("FYWB", -24, 15)]
        };
        new SimulatorAirportStore(root).Save(new(Now.AddMinutes(1), "Synthetic later scan", [..baseRows, new("FYWE", -22.6, 17), new("FYAR", -22.4, 15)]));
        var restarted = Service(new LocalEconomyStateStore(store.StoragePath), new DestinationReviewStore(root).Load());
        var board = await restarted.LoadAsync();
        Assert.Empty(board.Offers);
        var reserved = Assert.Single(board.ReservedJobs);
        Assert.Equal(offer.OriginLatitude, reserved.OriginLatitude);
        Assert.Equal(offer.OriginLongitude, reserved.OriginLongitude);
        Assert.Equal(offer.DestinationLatitude, reserved.DestinationLatitude);
        Assert.Equal(offer.DestinationLongitude, reserved.DestinationLongitude);
        Assert.Equal("vision-jet", reserved.CompanyAircraftTypeId);
        var tracker = new FlightAttemptTracker(store, new MemoryFlightAttemptStore(), utcNow: () => Now.AddMinutes(1)) { AdditionalLocationResolver = new DestinationReviewStore(root).Load().Find };
        await tracker.InitializeAsync();
        await tracker.SelectJobsAsync([offer.JobId]);
        for (var t = 0; t <= 10; t++) await tracker.ObserveAsync(new(Now.AddSeconds(t), "Synthetic Vision Jet", "SF50",
            reserved.OriginLatitude!.Value, reserved.OriginLongitude!.Value, 500, 0, 0, true));
        Assert.True(tracker.Current.CanLoadCargo);
        Assert.Empty((await store.LoadAsync())!.FinancialLedger);
    }

    [Fact]
    public async Task GroupedOffshoreDeliveryPersistsProofPartialUnloadAndOneWageAcrossRestart()
    {
        var catalogue = Airports();
        var store = new LocalEconomyStateStore(Path.Combine(root, "economy-state.json"));
        var attemptsPath = Path.Combine(root, "flight-attempts.json");
        var service = Service(store, catalogue);
        var offers = (await service.LoadAsync()).Offers.Where(x => x.Destination.Contains("FYAR")).Take(2).ToArray();
        Assert.Equal(2, offers.Length);
        Assert.True(offers.Sum(x => x.CargoMassKilograms) <= 40);
        service.SelectedAircraftTypeId = "vision-jet";
        foreach (var offer in offers) await service.AcceptAsync(offer.JobId);
        var tracker = new FlightAttemptTracker(store, new LocalFlightAttemptStore(attemptsPath), utcNow: () => Now.AddHours(1)) { AdditionalLocationResolver = catalogue.Find };
        await tracker.InitializeAsync();
        await tracker.SelectJobsAsync(offers.Select(x => x.JobId).ToArray());
        var first = offers[0];
        AircraftTelemetry At(int second, bool destination = false, bool ground = true) => new(Now.AddSeconds(second), "Synthetic Vision Jet", "SF50",
            (destination ? first.DestinationLatitude : first.OriginLatitude)!.Value,
            (destination ? first.DestinationLongitude : first.OriginLongitude)!.Value, ground ? 500 : 1500, 0, ground ? 0 : 100, ground);
        for (var t = 0; t <= 10; t++) await tracker.ObserveAsync(At(t));
        var load = offers.ToDictionary(x => x.JobId, x => x.CargoMassKilograms);
        Assert.Null(CompanyAircraft.CheckDispatch(new("Synthetic Vision Jet", "SF50"),
            (await service.LoadAsync()).ReservedJobs, [new("Synthetic Vision Jet", "SF50", "vision-jet")]));
        await tracker.ApplyCargoTransfersAsync("offshore-load", "offshore-load", load, first.OriginLocationId,
            first.OriginLatitude!.Value, first.OriginLongitude!.Value, false, AircraftLoadingMode.MsfsPayloadIntegration);
        for (var t = 11; t <= 131; t++) await tracker.ObserveAsync(At(t, t > 11, false));
        for (var t = 132; t <= 143; t++) await tracker.ObserveAsync(At(t, true));
        Assert.True(tracker.Current.CanUnloadCargo);
        var partial = load.ToDictionary(x => x.Key, x => x.Value / 2);
        await tracker.ApplyCargoTransfersAsync("offshore-partial", "offshore-partial", partial, first.DestinationLocationId,
            first.DestinationLatitude!.Value, first.DestinationLongitude!.Value, false, AircraftLoadingMode.MsfsPayloadIntegration);
        var partlyDelivered = (await store.LoadAsync())!;
        Assert.Empty(partlyDelivered.FinancialLedger);
        Assert.All(partlyDelivered.Shipments, x =>
        {
            Assert.Equal(x.Quantity / 2, x.DeliveredQuantity);
            Assert.Equal(x.Quantity / 2, x.LoadedQuantity);
        });
        tracker = new(new LocalEconomyStateStore(store.StoragePath), new LocalFlightAttemptStore(attemptsPath), utcNow: () => Now.AddHours(1)) { AdditionalLocationResolver = catalogue.Find };
        await tracker.InitializeAsync();
        await tracker.SelectJobsAsync(offers.Select(x => x.JobId).ToArray());
        for (var t = 144; t <= 155; t++) await tracker.ObserveAsync(At(t, true));
        await tracker.ApplyCargoTransfersAsync("offshore-unload", "offshore-unload", load.ToDictionary(x => x.Key, _ => 0m), first.DestinationLocationId,
            first.DestinationLatitude.Value, first.DestinationLongitude.Value, false, AircraftLoadingMode.MsfsPayloadIntegration);
        var paid = (await store.LoadAsync())!;
        var wage = Assert.Single(paid.FinancialLedger);
        Assert.Equal(26.67m, wage.Amount);
        Assert.Equal(800m, wage.Wage!.HourlyRate);
        Assert.All(paid.Shipments, x => Assert.Equal(ShipmentState.Delivered, x.State));
        foreach (var shipment in paid.Shipments)
            Assert.Equal(shipment.Quantity, paid.Inventories.Single(x => x.LocationId == shipment.DestinationLocationId && x.ResourceId == shipment.ResourceId).AvailableQuantity);
        Assert.Equal(2, paid.Jobs.Count(x => x.State == EconomyJobState.Completed));
        Assert.All(await new LocalFlightAttemptStore(attemptsPath).LoadAsync(), x => Assert.NotNull(x.DeliveryProof));
        var restarted = Service(new LocalEconomyStateStore(store.StoragePath), catalogue);
        Assert.Equal(2, (await restarted.LoadAsync()).RecentCompletedJobs.Count);
        tracker = new(new LocalEconomyStateStore(store.StoragePath), new LocalFlightAttemptStore(attemptsPath)) { AdditionalLocationResolver = catalogue.Find };
        await tracker.InitializeAsync();
        await tracker.UnloadCargoAsync();
        var reloaded = (await store.LoadAsync())!;
        Assert.Equal(paid.FinancialLedger, reloaded.FinancialLedger);
        Assert.Equal(paid.Inventories, reloaded.Inventories);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
