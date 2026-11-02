using System.Text.Json.Nodes;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class PassengerTransferTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FrontierPassengerTests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-29T12:00:00Z");
    private static CareerEmployer Employer => CareerCatalogue.Employers.Single(x => x.Category == "Offshore");
    private DateTimeOffset utc = Start;
    private PassengerTransfersService service = null!;
    private LocalEconomyStateStore store = null!;
    private EmployerDestinationCatalogue destinations = null!;
    private PassengerTransfer transfer = null!;
    private Guid? airframe;
    private readonly FrontierAirportBuilder.TestFixtures.SyntheticPassengerAircraft physical = new();
    private IReadOnlyList<AircraftTypeBinding> mappings = [new("Synthetic Vision Jet", "SF50", "vision-jet")];
    private PassengerTransfersService NewService(EmployerDestinationCatalogue? catalogue = null, LocalEconomyStateStore? economy = null) =>
        new(economy ?? new LocalEconomyStateStore(store.StoragePath), Employer, catalogue ?? destinations, () => mappings, () => utc) {
            SelectedAirframe = () => airframe, Payload = new(economy ?? new LocalEconomyStateStore(store.StoragePath), physical, physical, () => "synthetic", physical.WaitForRetentionAsync) };
    private async Task Setup()
    {
        destinations = new([new("FYWB", "Approved", "FYWB", -23, 15, "Synthetic passenger fixture; not MSFS acceptance", Start, [])]);
        airframe = Guid.NewGuid(); store = new(Path.Combine(root, "economy-state.json")); service = NewService();
        await service.RefreshAsync(); transfer = service.Transfers.First(x => x.Destination.Name.Contains("FYAR")); service.SelectedId = transfer.Id;
    }
    private AircraftTelemetry At(int seconds, bool destination = false, bool ground = true) => new(Start.AddSeconds(seconds), "Synthetic Vision Jet", "SF50",
        destination ? transfer.Destination.Latitude : transfer.Origin.Latitude, destination ? transfer.Destination.Longitude : transfer.Origin.Longitude,
        ground ? 500 : 2000, 0, ground ? 0 : 100, ground);
    private async Task Observe(int seconds, bool destination = false, bool ground = true)
    {
        utc = Start.AddSeconds(seconds); physical.Sample = At(seconds, destination, ground);
        // These flight tests model the explicit read-only payload check after connection recovery.
        if (service.Selected?.State == PassengerTransferState.Boarded && service.Selected.AirframeId == airframe && service.SelectedTransport?.State is PassengerCarriageState.Aboard or PassengerCarriageState.Arrived)
            await service.CheckPayloadAsync();
        await service.ObserveAsync(physical.Sample);
    }
    private async Task Board()
    {
        await service.AcceptAsync("vision-jet");
        await service.SetManifestAsync([82]);
        for (var i = 0; i <= 10; i++) await Observe(i);
        Assert.True(service.CanBoard); await service.BoardAsync();
    }
    private async Task Arrive()
    {
        for (var i = 11; i <= 131; i++) await Observe(i, i > 11, false);
        for (var i = 132; i <= 142; i++) await Observe(i, true);
        Assert.True(service.CanDisembark);
    }

    [Fact] public async Task CompleteTransferPaysAgreedWageOnceWithoutAnyCargoAccounting()
    {
        await Setup(); var cargo = (await store.LoadAsync())!;
        await Board(); await service.BoardAsync(); await Arrive();
        Assert.Empty((await store.LoadAsync())!.FinancialLedger);
        await service.DisembarkAsync(); await service.DisembarkAsync();
        var paid = (await store.LoadAsync())!; var receipt = Assert.Single(paid.FinancialLedger);
        Assert.Equal(FinancialTransactionKind.PilotWage, receipt.Kind); Assert.Equal(800, receipt.Wage!.HourlyRate);
        Assert.Equal(TimeSpan.FromSeconds(120).Ticks, receipt.Wage.AirborneTicks); Assert.Equal(26.67m, receipt.Amount);
        Assert.Equal(26.67m, paid.Players.Single().CachedBalance);
        var completed = paid.PassengerTransfers.Single(x => x.Id == transfer.Id);
        Assert.Equal(PassengerTransferState.Delivered, completed.State); Assert.Equal(1, completed.PassengerCount);
        Assert.NotNull(completed.Flight!.DeliveryProof); Assert.Equal(completed.Flight.DeliveryProof!.ReadyUtc, receipt.OccurredUtc);
        Assert.Equal(cargo.Shipments, paid.Shipments); Assert.Equal(cargo.Jobs, paid.Jobs); Assert.Equal(cargo.Requirements, paid.Requirements);
        Assert.Equal(cargo.Inventories, paid.Inventories); Assert.Equal(cargo.InventoryLedger, paid.InventoryLedger); Assert.Equal(cargo.CargoTransfers, paid.CargoTransfers);
        var revision = paid.Revision; await Observe(143, true, false); Assert.Equal(revision, (await store.LoadAsync())!.Revision);
        service = NewService(); service.SelectedId = transfer.Id; await service.RefreshAsync(); await service.DisembarkAsync();
        Assert.Equal(paid.FinancialLedger, (await store.LoadAsync())!.FinancialLedger);
    }

    [Fact] public async Task DisconnectedArrivalAndRestartRetainPeopleAndFactsButResetTimersAndExcludeGapPay()
    {
        await Setup(); await Board();
        for (var i = 11; i <= 21; i++) await Observe(i, i > 11, false);
        for (var i = 22; i <= 27; i++) await Observe(i, true);
        await service.ObserveAsync(null); Assert.False(service.CanDisembark);
        var before = (await store.LoadAsync())!.PassengerTransfers.Single(x => x.Id == transfer.Id);
        service = NewService(); await service.RefreshAsync(); Assert.Equal(transfer.Id, service.SelectedId);
        utc = Start.AddHours(12); await service.RefreshAsync();
        Assert.Equal(PassengerTransferState.Boarded, service.Selected!.State); Assert.Equal(before.Flight, service.Selected.Flight);
        for (var i = 43200; i <= 43209; i++) await Observe(i, true);
        Assert.False(service.CanDisembark); await Observe(43210, true); Assert.True(service.CanDisembark);
        await service.DisembarkAsync(); var wage = Assert.Single((await store.LoadAsync())!.FinancialLedger).Wage!;
        Assert.Equal(TimeSpan.FromSeconds(10).Ticks, wage.AirborneTicks);
    }

    [Fact] public async Task QualifiedArrivalRecoversAfterFailedAtomicCompletionAndNeedsFreshDestinationSample()
    {
        await Setup(); await Board(); await Arrive(); var proven = (await store.LoadAsync())!;
        var fail = true;
        var failingStore = new LocalEconomyStateStore(store.StoragePath, _ => fail ? Task.FromException(new IOException("Injected pre-replace failure")) : Task.CompletedTask);
        service = NewService(economy: failingStore); service.SelectedId = transfer.Id;
        await service.CheckPayloadAsync(); await service.ObserveAsync(At(142, true));
        await Assert.ThrowsAsync<IOException>(() => service.DisembarkAsync());
        Assert.Empty((await store.LoadAsync())!.FinancialLedger);
        Assert.Equal(proven.PassengerTransfers.Single(x => x.Id == transfer.Id).Flight, (await store.LoadAsync())!.PassengerTransfers.Single(x => x.Id == transfer.Id).Flight);
        fail = false; service = NewService(); service.SelectedId = transfer.Id; await service.RefreshAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DisembarkAsync());
        await Observe(143); await Assert.ThrowsAsync<InvalidOperationException>(() => service.DisembarkAsync());
        await Observe(144, true); await service.DisembarkAsync(); Assert.Single((await store.LoadAsync())!.FinancialLedger);
    }

    [Theory][InlineData("invalid")][InlineData("duplicate")][InlineData("gap")][InlineData("wrong-aircraft")][InlineData("stale")]
    public async Task InvalidContinuityCannotCompleteArrivalOrEarnDisconnectedTime(string interruption)
    {
        await Setup(); await Board();
        for (var i = 11; i <= 21; i++) await Observe(i, i > 11, false);
        for (var i = 22; i <= 27; i++) await Observe(i, true);
        utc = Start.AddSeconds(interruption == "gap" ? 40 : 28);
        var sample = interruption switch
        {
            "invalid" => At(28, true) with { Latitude = double.NaN },
            "duplicate" => At(27, true),
            "wrong-aircraft" => At(28, true) with { AircraftTitle = "Another aircraft" },
            "stale" => At(22, true),
            _ => At(40, true)
        };
        await service.ObserveAsync(sample); Assert.False(service.CanDisembark);
        // A physical transfer additionally needs explicit payload reconciliation after the gap.
        // The first sample after that check starts the existing ten-second stationary timer.
        var started = interruption == "gap" ? 41 : 29;
        var nextSample = interruption == "gap" ? 41 : 29;
        for (var i = nextSample; i < started + 10; i++) await Observe(i, true);
        Assert.False(service.CanDisembark); await Observe(started + 10, true);
        await service.DisembarkAsync(); Assert.Equal(TimeSpan.FromSeconds(10).Ticks, Assert.Single((await store.LoadAsync())!.FinancialLedger).Wage!.AirborneTicks);
    }

    [Fact] public async Task BoardingRequiresExactMappingFreshOriginAndAircraftAtBothEndpoints()
    {
        await Setup(); service.SelectedId = transfer.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptAsync("h125"));
        await service.AcceptAsync("vision-jet");
        for (var i = 0; i <= 10; i++) await Observe(i, true); Assert.False(service.CanBoard);
        for (var i = 11; i <= 21; i++) await Observe(i);
        mappings = []; await Assert.ThrowsAsync<InvalidOperationException>(() => service.BoardAsync());
        Assert.Equal(PassengerTransferState.Reserved, (await store.LoadAsync())!.PassengerTransfers.Single(x => x.Id == transfer.Id).State);
        mappings = [new("Synthetic Vision Jet", "SF50", "vision-jet")];
        for (var i = 22; i <= 32; i++) await Observe(i);
        utc = Start.AddSeconds(35); await Assert.ThrowsAsync<InvalidOperationException>(() => service.BoardAsync());
        var restricted = new EmployerDestinationCatalogue([new("FYWB", "Approved", "FYWB", -23, 15, "Synthetic aircraft exclusion", Start, ["c208"])]);
        service = NewService(restricted); await service.RefreshAsync(); service.SelectedId = transfer.Id;
        Assert.DoesNotContain(service.VisibleTransfers, x => x.State == PassengerTransferState.Offered);
        for (var i = 36; i <= 46; i++) await Observe(i);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BoardAsync());
    }

    [Fact] public async Task AcceptedEndpointsAndProofSurviveLaterDestinationExclusion()
    {
        await Setup(); await Board();
        var rejected = new EmployerDestinationCatalogue([new("FYWB", "Approved", "FYWB", -23, 15, "Synthetic base", Start, []),
            new("FYAR", "Rejected", "FYAR", null, null, "Synthetic later exclusion", Start, [])]);
        service = NewService(rejected); await service.RefreshAsync();
        Assert.Equal(transfer.Origin, service.Selected!.Origin); Assert.Equal(transfer.Destination, service.Selected.Destination);
        await Arrive(); await service.DisembarkAsync(); Assert.Single((await store.LoadAsync())!.FinancialLedger);
    }

    [Fact] public async Task AirframeChangePausesWithoutMovingPassengerAndRestoringRecordedAirframeResumes()
    {
        await Setup(); airframe = Guid.NewGuid(); await Board();
        var recorded = airframe; airframe = Guid.NewGuid();
        for (var i = 11; i <= 31; i++) await Observe(i, i > 11, false);
        Assert.Null(service.Selected!.Flight!.FirstAirborneUtc); Assert.Equal(recorded, service.Selected.AirframeId);
        airframe = recorded;
        for (var i = 32; i <= 42; i++) await Observe(i, i > 32, false);
        for (var i = 43; i <= 53; i++) await Observe(i, true);
        await service.DisembarkAsync(); Assert.Single((await store.LoadAsync())!.FinancialLedger);
    }

    [Fact] public async Task OfferRefreshIsIdempotentUsesExistingUtcCadenceAndNeverExpiresReservations()
    {
        await Setup(); var count = service.Transfers.Count; Assert.NotEqual(0, count);
        var revision = (await store.LoadAsync())!.Revision; await service.RefreshAsync(); Assert.Equal(revision, (await store.LoadAsync())!.Revision);
        await service.AcceptAsync("vision-jet");
        utc = Start.AddHours(3); await service.RefreshAsync(); Assert.True(service.Transfers.Count > count);
        utc = Start.AddHours(7); await service.RefreshAsync();
        Assert.Equal(PassengerTransferState.Reserved, service.Transfers.Single(x => x.Id == transfer.Id).State);
        Assert.Contains(service.Transfers, x => x.State == PassengerTransferState.Expired);
        Assert.Empty((await store.LoadAsync())!.FinancialLedger);
    }

    [Fact] public async Task ConcurrentCompletionAndRefreshCannotDuplicatePassengersOrMoney()
    {
        await Setup(); await Board(); await Arrive();
        var other = NewService(); await other.RefreshAsync(); await other.CheckPayloadAsync(); await other.ObserveAsync(At(142, true));
        await Task.WhenAll(service.DisembarkAsync(), other.DisembarkAsync());
        var paid = (await store.LoadAsync())!; Assert.Single(paid.FinancialLedger);
        Assert.Single(paid.PassengerTransfers.Where(x => x.State == PassengerTransferState.Delivered));
        await Task.WhenAll(service.RefreshAsync(), other.RefreshAsync());
        Assert.Equal(paid.PassengerTransfers.Count, (await store.LoadAsync())!.PassengerTransfers.Count);
    }

    [Fact] public async Task PassengerAndCargoFlightsCannotBoardTogetherAndNoCargoMutationOccurs()
    {
        await Setup(); await Board();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequireNoBoardedPassengersAsync(default));
        var before = (await store.LoadAsync())!;
        var cargoService = new EmployedCargoJobsService(store, employer: Employer, destinations: destinations, utcNow: () => utc) { SelectedAircraftTypeId = "vision-jet" };
        var cargo = (await cargoService.LoadAsync()).Offers.First(); await cargoService.AcceptAsync(cargo.JobId);
        // The shared domain allows future mixed carriage. The current workflow guard still refuses it.
        EconomyKernel.MarkShipmentLoaded((await store.LoadAsync())!, cargo.JobId).World.EnsureValid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EconomyService(store).MarkShipmentLoadedAsync(cargo.JobId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequireNoBoardedPassengersAsync(default));
        Assert.Equal(before.Inventories.Sum(x => x.AvailableQuantity) - cargo.CargoMassKilograms, (await store.LoadAsync())!.Inventories.Sum(x => x.AvailableQuantity));
        Assert.Equal(PassengerTransferState.Boarded, (await store.LoadAsync())!.PassengerTransfers.Single(x => x.Id == transfer.Id).State);
    }

    [Fact] public async Task SchemaTenMigratesWithoutInventingPassengersAndCorruptFutureFilesArePreserved()
    {
        await Setup(); var world = (await store.LoadAsync())!;
        var node = JsonNode.Parse(File.ReadAllText(store.StoragePath))!; node["SchemaVersion"] = 10; node["World"]!.AsObject().Remove("PassengerTransfers");
        File.WriteAllText(store.StoragePath, node.ToJsonString());
        var migrated = (await store.LoadAsync())!; Assert.Empty(migrated.PassengerTransfers); Assert.Equal(world.Jobs, migrated.Jobs); Assert.Equal(world.FinancialLedger, migrated.FinancialLedger);
        foreach (var value in new[] { "broken", "{\"SchemaVersion\":999}" })
        {
            File.WriteAllText(store.StoragePath, value); await Assert.ThrowsAsync<IOException>(() => service.RefreshAsync()); Assert.Equal(value, File.ReadAllText(store.StoragePath));
        }
    }

    [Fact] public async Task PassengerCompletionAndLedgerTamperingFailValidationAndUnqualifiedPlatformsAreUnavailable()
    {
        await Setup(); Assert.All(service.Transfers, x => Assert.True(x.Origin.CanGenerate && x.Destination.CanGenerate));
        var platform = transfer.Destination with { Kind = PassengerEndpointKind.Platform }; Assert.False(platform.CanGenerate);
        var initial = (await store.LoadAsync())!;
        Assert.Throws<InvalidDataException>(() => (initial with { PassengerTransfers = [transfer with { Destination = platform }] }).EnsureValid());
        await Board(); await Arrive(); await service.DisembarkAsync(); var paid = (await store.LoadAsync())!; var completed = paid.PassengerTransfers.Single(x => x.Id == transfer.Id);
        Assert.Throws<InvalidDataException>(() => (paid with { FinancialLedger = [] }).EnsureValid());
        Assert.Throws<InvalidDataException>(() => (paid with { FinancialLedger = [paid.FinancialLedger.Single() with { Amount = 1000 }] }).EnsureValid());
        Assert.Throws<InvalidDataException>(() => (paid with { PassengerTransfers = paid.PassengerTransfers.Select(x => x.Id == transfer.Id ? completed with { PassengerCount = 2 } : x).ToArray() }).EnsureValid());
    }

    [Theory][InlineData("FYWB")][InlineData("FYAR")]
    public async Task NewPassengerMissionsRespectEndpointAircraftExclusions(string excluded)
    {
        await Setup();
        var reviews = new[] { new DestinationReview("FYWB", "Approved", "FYWB", -23, 15, "Synthetic base", Start, excluded == "FYWB" ? ["c208"] : []),
            new DestinationReview("FYAR", "Approved", "FYAR", -22.4, 15, "Synthetic destination", Start, excluded == "FYAR" ? ["c208"] : []) };
        var restricted = new PassengerTransfersService(new MemoryEconomyStateStore(), Employer, new(reviews), utcNow: () => utc);
        await restricted.RefreshAsync();
        Assert.DoesNotContain(restricted.Transfers, x => x.Destination.Name.Contains("FYAR"));
        if (excluded == "FYWB") Assert.Empty(restricted.Transfers);
    }

    [Fact] public async Task LoadedCargoRefusesPassengerBoardingAndWrongLocationCannotDisembark()
    {
        await Setup(); await service.AcceptAsync("vision-jet");
        var cargo = new EmployedCargoJobsService(store, employer: Employer, destinations: destinations, utcNow: () => utc) { SelectedAircraftTypeId = "vision-jet" };
        var job = (await cargo.LoadAsync()).Offers.First(); await cargo.AcceptAsync(job.JobId);
        await store.ApplyAsync(w => EconomyKernel.MarkShipmentLoaded(w, job.JobId));
        for (var t = 0; t <= 10; t++) await Observe(t);
        Assert.False(service.CanBoard); await Assert.ThrowsAsync<InvalidOperationException>(() => service.BoardAsync());
        Assert.Equal(PassengerTransferState.Reserved, (await store.LoadAsync())!.PassengerTransfers.Single(x => x.Id == transfer.Id).State);
        Assert.Empty((await store.LoadAsync())!.FinancialLedger);
    }

    [Fact] public async Task BindingRefreshKeepsSelectedTransferAndAircraftWithoutRecursiveNotifications()
    {
        await Setup(); var vm = new PassengerTransfersViewModel(service); var notifications = 0;
        vm.PropertyChanged += (_, _) => notifications++;
        vm.SelectedTransfer = transfer; vm.SelectedAircraft = vm.AircraftChoices.Single();
        var count = notifications;
        vm.SelectedTransfer = null; vm.SelectedTransfer = transfer;
        vm.SelectedAircraft = null; vm.SelectedAircraft = vm.AircraftChoices.Single();
        Assert.Equal(count, notifications);
        Assert.Equal(transfer.Id, vm.SelectedTransfer!.Id); Assert.Equal("vision-jet", vm.SelectedAircraft!.Id);
        await vm.RefreshAsync(); Assert.Equal(transfer.Id, vm.SelectedTransfer!.Id); Assert.Equal("vision-jet", vm.SelectedAircraft!.Id);
    }

    [Fact] public async Task MissionaryVisitUsesSharedTwoPersonPayloadAndExistingFourHundredHourlyWage()
    {
        var missionary = CareerCatalogue.Employers.Single(x => x.Category == "Missionary");
        var memory = new MemoryEconomyStateStore(); var catalogue = new EmployerDestinationCatalogue([
            new("FYOP", "Approved", "FYOP", -18.06, 13.85, "SYNTHETIC test route; no live scenery/facility evidence", Start, ["c208"])]);
        var id = Guid.NewGuid();
        var visiting = new PassengerTransfersService(memory, missionary, catalogue, () => [new("Synthetic Caravan", "C208", "c208")], () => utc) {
            SelectedAirframe = () => id, Payload = new(memory, physical, physical, () => "synthetic", physical.WaitForRetentionAsync) };
        await visiting.RefreshAsync(); var mission = Assert.Single(visiting.Transfers); visiting.SelectedId = mission.Id;
        Assert.Equal(2, mission.PassengerCount); Assert.Contains("Doctor", mission.Purpose); Assert.Equal(400, mission.HourlyRate);
        Assert.DoesNotContain(visiting.AircraftChoices, x => x.Id == "c152");
        await visiting.AcceptAsync("c208"); await visiting.SetManifestAsync([75, 92]);
        async Task Sample(int second, bool destination = false, bool ground = true)
        {
            utc = Start.AddSeconds(second); var endpoint = destination ? mission.Destination : mission.Origin;
            physical.Sample = new(utc, "Synthetic Caravan", "C208", endpoint.Latitude, endpoint.Longitude, ground ? 500 : 2000, 0, ground ? 0 : 100, ground);
            await visiting.ObserveAsync(physical.Sample);
        }
        for (var i = 0; i <= 10; i++) await Sample(i);
        await visiting.BoardAsync(); Assert.Equal(75, physical.Weights[2]); Assert.Equal(92, physical.Weights[3]);
        for (var i = 11; i <= 131; i++) await Sample(i, i > 11, false);
        for (var i = 132; i <= 142; i++) await Sample(i, true);
        await visiting.DisembarkAsync(); await visiting.DisembarkAsync();
        var world = (await memory.LoadAsync())!; var wage = Assert.Single(world.FinancialLedger);
        Assert.Equal(400, wage.Wage!.HourlyRate); Assert.Equal(13.33m, wage.Amount);
        Assert.Equal(PassengerCarriageState.Disembarked, Assert.Single(world.PassengerTransports).State);
        Assert.Equal(33, physical.Weights[5]); Assert.Equal(0, physical.Weights[2]); Assert.Equal(0, physical.Weights[3]);
        var unresolved = new PassengerTransfersService(new MemoryEconomyStateStore(), missionary, new(), utcNow: () => utc);
        await unresolved.RefreshAsync(); Assert.Empty(unresolved.Transfers);
    }

    [Fact] public async Task RestartRequiresExplicitPayloadCheckBeforeFlightQualification()
    {
        await Setup(); await Board(); service = NewService(); await service.RefreshAsync();
        for (var i = 11; i <= 31; i++) { utc = Start.AddSeconds(i); physical.Sample = At(i, true, false); await service.ObserveAsync(physical.Sample); }
        Assert.Null(service.Selected!.Flight!.FirstAirborneUtc); Assert.Equal(0, service.Selected.Flight.VerifiedAirborneTicks);
        await service.CheckPayloadAsync(); utc = Start.AddSeconds(32); physical.Sample = At(32, true, false); await service.ObserveAsync(physical.Sample);
        Assert.NotNull(service.Selected.Flight.FirstAirborneUtc);
        await service.ObserveAsync(null); physical.Weights[2] = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckPayloadAsync());
        Assert.Equal(PassengerTransferState.Boarded, service.Selected.State); Assert.Empty((await store.LoadAsync())!.FinancialLedger);
    }

    [Fact] public async Task SchemaElevenSimulatedAboardSaveRemainsLegacyWithoutInventingPhysicalOwnership()
    {
        await Setup(); await Board();
        var node = JsonNode.Parse(File.ReadAllText(store.StoragePath))!; node["SchemaVersion"] = 11;
        node["World"]!.AsObject().Remove("PassengerTransports");
        foreach (var t in node["World"]!["PassengerTransfers"]!.AsArray()) { t!.AsObject().Remove("TransportId"); t.AsObject().Remove("Purpose"); t.AsObject().Remove("BoardingAuthorization"); }
        File.WriteAllText(store.StoragePath, node.ToJsonString());
        service = NewService(); await service.RefreshAsync(); Assert.Null(service.Selected!.TransportId); Assert.Empty((await store.LoadAsync())!.PassengerTransports);
        await Arrive(); await service.DisembarkAsync(); Assert.Single((await store.LoadAsync())!.FinancialLedger);
        Assert.Equal(1, physical.Writes); // legacy completion does not remove payload it never owned
    }

    [Fact] public async Task VerifiedPhysicalUnloadCanSettleAfterFailedPaymentCommitWithoutAnotherSimulatorWrite()
    {
        await Setup(); await Board(); await Arrive();
        // Generic carriage reaches its durable physical receipt before mission settlement.
        var carriage = service.SelectedTransport!;
        await new PassengerPayloadCoordinator(store, physical, physical, () => "synthetic", physical.WaitForRetentionAsync).ApplyAsync(carriage.Id, "vision-jet", airframe!.Value, true, _ => { });
        service = NewService(); await service.RefreshAsync(); Assert.True(service.CanDisembark);
        await service.DisembarkAsync(); await service.DisembarkAsync();
        Assert.Equal(2, physical.Writes); Assert.Single((await store.LoadAsync())!.FinancialLedger);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
