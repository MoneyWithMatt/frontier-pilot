using System.Text.Json.Nodes;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class FlightAttemptTrackerTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-10T08:00:00Z");

    [Fact]
    public async Task LoadingIsRefusedWhenDisconnectedAirborneMovingOrOutsideEros()
    {
        var h = await Harness.Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Tracker.LoadCargoAsync());
        await ObserveSeconds(h.Tracker, At(EmployedCargoPrototype.ErosId, Start, onGround: false), 11);
        Assert.False(h.Tracker.Current.CanLoadCargo); Assert.Contains("airborne", h.Tracker.Current.Reason, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Tracker.LoadCargoAsync());
        await ObserveSeconds(h.Tracker, At(EmployedCargoPrototype.ErosId, Start.AddMinutes(1), speed: 6), 11);
        Assert.False(h.Tracker.Current.CanLoadCargo); Assert.Contains("ground speed", h.Tracker.Current.Reason, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Tracker.LoadCargoAsync());
        await ObserveSeconds(h.Tracker, At(EmployedCargoPrototype.UisId, Start.AddMinutes(2)), 11);
        Assert.False(h.Tracker.Current.CanLoadCargo); Assert.Contains("from Eros", h.Tracker.Current.Reason, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Tracker.LoadCargoAsync());
    }

    [Fact]
    public async Task TenContinuousSecondsAtErosEnablesIdempotentLoadingAndMovesShipmentOnce()
    {
        var h = await Harness.Create(); var atEros = At(EmployedCargoPrototype.ErosId, Start);
        await ObserveSeconds(h.Tracker, atEros, 9); Assert.False(h.Tracker.Current.CanLoadCargo);
        await h.Tracker.ObserveAsync(atEros with { ObservedUtc = Start.AddSeconds(10) }); Assert.True(h.Tracker.Current.CanLoadCargo);
        await h.Tracker.LoadCargoAsync(); await h.Tracker.LoadCargoAsync();
        var world = await h.Economy.LoadAsync();
        Assert.Equal(3, world!.Revision); Assert.Single(world.Shipments);
        Assert.Equal(ShipmentState.InTransit, world.Shipments.Single().State); Assert.Null(world.Shipments.Single().CurrentLocationId);
        Assert.Single(await h.Attempts.LoadAsync());
    }

    [Fact]
    public async Task LoadingAndProgressSurviveRestartWithoutASecondTransition()
    {
        using var temp = new TestDirectory();
        var economyPath = Path.Combine(temp.Root, "economy", "economy-state.json");
        var attemptPath = Path.Combine(temp.Root, "economy", "flight-attempts.json");
        var jobs = new EmployedCargoJobsService(new LocalEconomyStateStore(economyPath), utcNow: () => Start);
        await jobs.LoadAsync(); await jobs.AcceptAsync("job-uis-supplies-v0");
        var first = new FlightAttemptTracker(new LocalEconomyStateStore(economyPath), new LocalFlightAttemptStore(attemptPath));
        await first.InitializeAsync(); await ObserveSeconds(first, At(EmployedCargoPrototype.ErosId, Start), 10); await first.LoadCargoAsync();
        var reopened = new FlightAttemptTracker(new LocalEconomyStateStore(economyPath), new LocalFlightAttemptStore(attemptPath));
        await reopened.InitializeAsync(); await reopened.LoadCargoAsync();
        var world = await new LocalEconomyStateStore(economyPath).LoadAsync();
        Assert.Equal(3, world!.Revision); Assert.Equal(ShipmentState.InTransit, world.Shipments.Single().State);
        Assert.Equal(FlightAttemptState.CargoLoaded, reopened.Current.Attempt!.State);
    }

    [Fact]
    public async Task ReconnectingAtDestinationWithoutAirborneEvidenceCannotCreateProof()
    {
        var h = await Loaded(); await h.Tracker.ObserveAsync(null);
        await ObserveSeconds(h.Tracker, At(h.DestinationId, Start.AddMinutes(1)), 20);
        Assert.Null(h.Tracker.Current.Attempt!.FirstAirborneUtc); Assert.Null(h.Tracker.Current.Attempt.DeliveryProof);
        Assert.False(h.Tracker.Current.CanUnloadCargo); Assert.Equal("Cargo loaded", h.Tracker.Current.Stage);
    }

    [Theory]
    [InlineData(EmployedCargoPrototype.UisId)]
    [InlineData(EmployedCargoPrototype.ArandisId)]
    public async Task AirborneMovementAndStationaryDestinationProduceEvidenceForBothDestinations(string destinationId)
    {
        var h = await Loaded(destinationId); await CompleteFlightEvidence(h);
        Assert.True(h.Tracker.Current.CanUnloadCargo); Assert.Equal("Ready to unload", h.Tracker.Current.Stage);
        var attempt = h.Tracker.Current.Attempt!;
        Assert.NotNull(attempt.FirstAirborneUtc); Assert.NotNull(attempt.MovementObservedUtc);
        Assert.True(attempt.MaximumDistanceFromOriginNauticalMiles >= 10); Assert.NotNull(attempt.DeliveryProof);
        Assert.Equal(destinationId, attempt.DestinationLocationId);
        var world = await h.Economy.LoadAsync(); Assert.Equal(ShipmentState.InTransit, world!.Shipments.Single().State); Assert.Empty(world.FinancialLedger);
    }

    [Fact]
    public async Task ArrivalStationaryTimerCannotSpanDisconnectInvalidSampleOrLongGap()
    {
        var h = await Loaded(); await RecordDeparture(h);
        var arrival = At(h.DestinationId, Start.AddMinutes(1));
        await ObserveSeconds(h.Tracker, arrival, 5); await h.Tracker.ObserveAsync(null);
        await ObserveSeconds(h.Tracker, arrival with { ObservedUtc = Start.AddMinutes(2) }, 5); Assert.False(h.Tracker.Current.CanUnloadCargo);
        await h.Tracker.ObserveAsync(arrival with { ObservedUtc = Start.AddMinutes(2).AddSeconds(20) });
        await ObserveSeconds(h.Tracker, arrival with { ObservedUtc = Start.AddMinutes(2).AddSeconds(21) }, 8); Assert.False(h.Tracker.Current.CanUnloadCargo);
        await h.Tracker.ObserveAsync(arrival with { ObservedUtc = Start.AddMinutes(2).AddSeconds(32) }); Assert.False(h.Tracker.Current.CanUnloadCargo);
        await h.Tracker.ObserveAsync(arrival with { ObservedUtc = default }); Assert.False(h.Tracker.Current.CanUnloadCargo);
        await ObserveSeconds(h.Tracker, arrival with { ObservedUtc = Start.AddMinutes(3) }, 5); Assert.False(h.Tracker.Current.CanUnloadCargo);
    }

    [Fact]
    public async Task WrongAircraftAndWrongDestinationCannotProduceEvidence()
    {
        var h = await Loaded();
        await h.Tracker.ObserveAsync(At(EmployedCargoPrototype.ErosId, Start.AddSeconds(20), onGround: false, title: "Clearly Different Aircraft"));
        Assert.Null(h.Tracker.Current.Attempt!.FirstAirborneUtc); Assert.Contains("differs", h.Tracker.Current.Reason, StringComparison.OrdinalIgnoreCase);
        await RecordDeparture(h);
        await ObserveSeconds(h.Tracker, At(EmployedCargoPrototype.ArandisId, Start.AddMinutes(1)), 12);
        Assert.False(h.Tracker.Current.CanUnloadCargo); Assert.Null(h.Tracker.Current.Attempt!.DeliveryProof);
    }

    [Fact]
    public async Task TamperedShipmentJobOrRouteIsRefusedDuringReconciliation()
    {
        foreach (var mutation in new Func<FlightAttempt, FlightAttempt>[]
        {
            x => x with { ShipmentId = "shipment-wrong" }, x => x with { JobId = "job-wrong" },
            x => x with { DestinationLocationId = EmployedCargoPrototype.ArandisId }
        })
        {
            var h = await Loaded(); var original = h.Tracker.Current.Attempt!; var tampered = mutation(original);
            var store = new MemoryFlightAttemptStore(); await store.SaveAsync(tampered);
            var tracker = new FlightAttemptTracker(h.Economy, store); await Assert.ThrowsAnyAsync<Exception>(() => tracker.InitializeAsync());
        }
    }

    [Fact]
    public async Task UnloadCommitsInventoryLedgerAndBalanceExactlyOnceAcrossRestart()
    {
        using var temp = new TestDirectory();
        var economyPath = Path.Combine(temp.Root, "economy", "economy-state.json"); var attemptPath = Path.Combine(temp.Root, "economy", "flight-attempts.json");
        var jobs = new EmployedCargoJobsService(new LocalEconomyStateStore(economyPath), utcNow: () => Start);
        await jobs.LoadAsync(); await jobs.AcceptAsync("job-uis-supplies-v0");
        var tracker = new FlightAttemptTracker(new LocalEconomyStateStore(economyPath), new LocalFlightAttemptStore(attemptPath));
        await tracker.InitializeAsync(); await ObserveSeconds(tracker, At(EmployedCargoPrototype.ErosId, Start), 10); await tracker.LoadCargoAsync();
        var h = new Harness(new LocalEconomyStateStore(economyPath), new LocalFlightAttemptStore(attemptPath), tracker, EmployedCargoPrototype.UisId);
        await CompleteFlightEvidence(h); await tracker.UnloadCargoAsync(); await tracker.UnloadCargoAsync();
        var reopened = new FlightAttemptTracker(new LocalEconomyStateStore(economyPath), new LocalFlightAttemptStore(attemptPath));
        await reopened.InitializeAsync(); await reopened.UnloadCargoAsync();
        var world = await new LocalEconomyStateStore(economyPath).LoadAsync();
        Assert.Equal(4, world!.Revision);
        Assert.Equal(EconomyJobState.Completed, world.Jobs.Single(x => x.Id == "job-uis-supplies-v0").State);
        Assert.Equal(TransportRequirementState.Fulfilled, world.Requirements.Single(x => x.Id == "requirement-uis-supplies-v0").State);
        Assert.Equal(ShipmentState.Delivered, world.Shipments.Single().State);
        Assert.Equal(120, world.Inventories.Single(x => x.LocationId == EmployedCargoPrototype.UisId).AvailableQuantity);
        Assert.Single(world.FinancialLedger); Assert.Equal(240, world.Players.Single().CachedBalance);
        Assert.Equal(240, EconomyLedger.Balance(world, EmployedCargoPrototype.PlayerId, EmployedCargoPrototype.Currency));
        Assert.Equal("Delivered and paid", reopened.Current.Stage);
    }

    [Fact]
    public async Task DeveloperDiagnosticExportsStateAndResetClearsOnlyIncompleteFlightObservations()
    {
        var h = await Loaded(); await RecordDeparture(h);
        var before = await h.Economy.LoadAsync();
        var report = await h.Tracker.ExportDeveloperDiagnosticAsync();
        Assert.Contains("job-uis-supplies-v0", report); Assert.Contains("flight-shipment-uis-supplies-v0", report);
        await h.Tracker.ResetIncompletePrototypeProgressAsync();
        var reset = h.Tracker.Current.Attempt!;
        Assert.Equal(FlightAttemptState.CargoLoaded, reset.State); Assert.Null(reset.FirstAirborneUtc);
        Assert.Null(reset.MovementObservedUtc); Assert.Equal(0, reset.MaximumDistanceFromOriginNauticalMiles);
        Assert.Null(reset.DeliveryProof); Assert.False(h.Tracker.Current.IsConnected);
        var after = await h.Economy.LoadAsync();
        Assert.Equal(before, after); Assert.Equal(ShipmentState.InTransit, after!.Shipments.Single().State);
        Assert.Empty(after.FinancialLedger);
    }

    [Fact]
    public async Task DeveloperResetRefusesCompletedDeliveryAndProtectsInventoryLedgerAndBalance()
    {
        var h = await Loaded(); await CompleteFlightEvidence(h); await h.Tracker.UnloadCargoAsync();
        var before = await h.Economy.LoadAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Tracker.ResetIncompletePrototypeProgressAsync());
        Assert.Contains("completed", error.Message, StringComparison.OrdinalIgnoreCase);
        var after = await h.Economy.LoadAsync();
        Assert.Equal(before, after); Assert.Equal(4, after!.Revision); Assert.Single(after.FinancialLedger);
        Assert.Equal(240, after.Players.Single().CachedBalance);
        Assert.Equal(FlightAttemptState.Delivered, Assert.Single(await h.Attempts.LoadAsync()).State);
    }

    [Fact]
    public async Task SignificantEventsAreRecordedOnceAndRepeatedSamplesDoNotFloodTimeline()
    {
        var h = await Harness.Create();
        await ObserveSeconds(h.Tracker, At(EmployedCargoPrototype.ErosId, Start), 20);
        var beforeLoad = await h.Attempts.LoadDiagnosticTimelineAsync();
        Assert.Single(beforeLoad, x => x.Type == FlightDiagnosticEventType.SimConnectConnected);
        Assert.Single(beforeLoad, x => x.Type == FlightDiagnosticEventType.OriginQualificationStarted);
        Assert.Single(beforeLoad, x => x.Type == FlightDiagnosticEventType.ReadyToLoad);
        await h.Tracker.LoadCargoAsync(); await RecordDeparture(h);
        var events = await h.Attempts.LoadDiagnosticTimelineAsync();
        Assert.Single(events, x => x.Type == FlightDiagnosticEventType.FlightAttemptCreated);
        Assert.Single(events, x => x.Type == FlightDiagnosticEventType.CargoLoaded);
        Assert.Single(events, x => x.Type == FlightDiagnosticEventType.ShipmentEnteredInTransit);
        Assert.Single(events, x => x.Type == FlightDiagnosticEventType.AirborneEvidenceRecorded);
        Assert.Single(events, x => x.Type == FlightDiagnosticEventType.PostDepartureMovementRecorded);
        Assert.Single(events, x => x.Type == FlightDiagnosticEventType.RequiredMaximumDistanceReached);
        Assert.True(events.Count < 12);
    }

    [Fact]
    public async Task QualificationInterruptionAndConnectionCycleContainFactualCause()
    {
        var h = await Harness.Create(); await ObserveSeconds(h.Tracker, At(EmployedCargoPrototype.ErosId, Start), 3);
        await h.Tracker.ObserveAsync(null); await h.Tracker.ObserveAsync(At(EmployedCargoPrototype.ErosId, Start.AddMinutes(1)));
        var events = await h.Attempts.LoadDiagnosticTimelineAsync();
        Assert.Equal(2, events.Count(x => x.Type == FlightDiagnosticEventType.SimConnectConnected));
        Assert.Single(events, x => x.Type == FlightDiagnosticEventType.SimConnectDisconnected);
        var interrupted = Assert.Single(events, x => x.Type == FlightDiagnosticEventType.OriginQualificationInterrupted);
        Assert.Contains("disconnected", interrupted.Details["reason"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArrivalInterruptionAndRefusedUnloadRecordReasonsWithoutCreatingProof()
    {
        var h = await Loaded(); await RecordDeparture(h);
        await ObserveSeconds(h.Tracker, At(h.DestinationId, Start.AddMinutes(1)), 3); await h.Tracker.ObserveAsync(null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Tracker.UnloadCargoAsync());
        var events = await h.Attempts.LoadDiagnosticTimelineAsync();
        var interrupted = Assert.Single(events, x => x.Type == FlightDiagnosticEventType.ArrivalQualificationInterrupted);
        Assert.Contains("disconnected", interrupted.Details["reason"], StringComparison.OrdinalIgnoreCase);
        var refused = Assert.Single(events, x => x.Type == FlightDiagnosticEventType.DeliveryRefused);
        Assert.Contains("not ready", refused.Details["reason"], StringComparison.OrdinalIgnoreCase);
        Assert.Null(h.Tracker.Current.Attempt!.DeliveryProof);
    }

    [Fact]
    public async Task RestartRecoveryIsRecordedOnceAndDoesNotDuplicateMilestones()
    {
        using var temp = new TestDirectory();
        var economyPath = Path.Combine(temp.Root, "economy", "economy-state.json"); var attemptPath = Path.Combine(temp.Root, "economy", "flight-attempts.json");
        var jobs = new EmployedCargoJobsService(new LocalEconomyStateStore(economyPath), utcNow: () => Start);
        await jobs.LoadAsync(); await jobs.AcceptAsync("job-uis-supplies-v0");
        var first = new FlightAttemptTracker(new LocalEconomyStateStore(economyPath), new LocalFlightAttemptStore(attemptPath), utcNow: () => Start.AddMinutes(1));
        await first.InitializeAsync(); await ObserveSeconds(first, At(EmployedCargoPrototype.ErosId, Start), 10); await first.LoadCargoAsync();
        var reopened = new FlightAttemptTracker(new LocalEconomyStateStore(economyPath), new LocalFlightAttemptStore(attemptPath), utcNow: () => Start.AddMinutes(2));
        await reopened.InitializeAsync(); await reopened.InitializeAsync();
        var events = await new LocalFlightAttemptStore(attemptPath).LoadDiagnosticTimelineAsync();
        Assert.Single(events, x => x.Type == FlightDiagnosticEventType.ApplicationRecovery);
        Assert.Single(events, x => x.Type == FlightDiagnosticEventType.CargoLoaded);
    }

    [Fact]
    public async Task DiagnosticTimelineCannotSatisfyDeliveryEvidence()
    {
        var h = await Loaded(); var attempt = h.Tracker.Current.Attempt!;
        await h.Attempts.AppendDiagnosticEventAsync(new FlightDiagnosticEvent("forged-diagnostic-event", attempt.JobId, attempt.ShipmentId,
            Start.AddMinutes(1), FlightDiagnosticEventType.ReadyToUnload, new Dictionary<string, string> { ["note"] = "diagnostic only" }));
        await h.Tracker.RefreshEconomyAsync();
        Assert.Null(h.Tracker.Current.Attempt!.DeliveryProof); Assert.False(h.Tracker.Current.CanUnloadCargo);
        Assert.Equal("Cargo loaded", h.Tracker.Current.Stage);
    }

    [Fact]
    public async Task SupportBundleAndSummaryAreReadOnlyAndExcludePathsAndUsername()
    {
        var h = await Loaded(); var beforeWorld = await h.Economy.LoadAsync(); var beforeAttempt = await h.Attempts.LoadAsync();
        var beforeEvents = await h.Attempts.LoadDiagnosticTimelineAsync();
        var bundle = await h.Tracker.ExportDeveloperDiagnosticAsync(); var summary = await h.Tracker.CreateDiagnosticSummaryAsync();
        Assert.Contains("SupportBundleSchemaVersion", bundle); Assert.Contains("DiagnosticTimeline", bundle); Assert.Contains("Thresholds", bundle);
        Assert.Contains("Frontier Pilot Flight Attempt diagnostic", summary);
        Assert.DoesNotContain(Environment.UserName, bundle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetPathRoot(Environment.CurrentDirectory) + "\\", bundle, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(beforeWorld, await h.Economy.LoadAsync()); Assert.Equal(beforeAttempt, await h.Attempts.LoadAsync());
        Assert.Equal(beforeEvents, await h.Attempts.LoadDiagnosticTimelineAsync());
    }

    [Fact]
    public async Task TelemetryAfterDeliveryUpdatesLiveValuesWithoutAppendingProgressionEvents()
    {
        var h = await Loaded(); await CompleteFlightEvidence(h); await h.Tracker.UnloadCargoAsync();
        var worldBefore = await h.Economy.LoadAsync(); var eventsBefore = await h.Attempts.LoadDiagnosticTimelineAsync();
        var progressionBefore = eventsBefore.Count(x => IsProgressionEvent(x.Type));
        var live = At(h.DestinationId, Start.AddMinutes(2), speed: 3.2, title: "Post-delivery observed aircraft");
        await h.Tracker.ObserveAsync(live);
        for (var second = 1; second <= 20; second++) await h.Tracker.ObserveAsync(live with { ObservedUtc = live.ObservedUtc.AddSeconds(second), GroundSpeedKnots = 3.2 + second / 10d });

        var eventsAfter = await h.Attempts.LoadDiagnosticTimelineAsync();
        Assert.Equal(progressionBefore, eventsAfter.Count(x => IsProgressionEvent(x.Type)));
        var priorIds = eventsBefore.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(eventsAfter.Where(x => !priorIds.Contains(x.Id)), x => x.Type is FlightDiagnosticEventType.OriginQualificationStarted or FlightDiagnosticEventType.OriginQualificationInterrupted or FlightDiagnosticEventType.ArrivalQualificationStarted or FlightDiagnosticEventType.ArrivalQualificationInterrupted);
        Assert.Equal("Delivered and paid", h.Tracker.Current.Stage);
        Assert.Equal(live.ObservedUtc.AddSeconds(20), h.Tracker.Current.Diagnostic.LastValidSampleUtc);
        Assert.Equal("5.2 kt", h.Tracker.Current.Diagnostic.GroundSpeed);
        Assert.Contains("Post-delivery observed aircraft", h.Tracker.Current.Diagnostic.CurrentAircraftIdentity);
        Assert.Equal(worldBefore, await h.Economy.LoadAsync());
    }

    [Fact]
    public async Task RestartThenTelemetryForCompletedAttemptCannotResumeQualificationOrDuplicatePayment()
    {
        using var temp = new TestDirectory();
        var economyPath = Path.Combine(temp.Root, "economy", "economy-state.json"); var attemptPath = Path.Combine(temp.Root, "economy", "flight-attempts.json");
        var jobs = new EmployedCargoJobsService(new LocalEconomyStateStore(economyPath), utcNow: () => Start);
        await jobs.LoadAsync(); await jobs.AcceptAsync("job-uis-supplies-v0");
        var first = new FlightAttemptTracker(new LocalEconomyStateStore(economyPath), new LocalFlightAttemptStore(attemptPath), utcNow: () => Start);
        await first.InitializeAsync(); await ObserveSeconds(first, At(EmployedCargoPrototype.ErosId, Start), 10); await first.LoadCargoAsync();
        var harness = new Harness(new LocalEconomyStateStore(economyPath), new LocalFlightAttemptStore(attemptPath), first, EmployedCargoPrototype.UisId);
        await CompleteFlightEvidence(harness); await first.UnloadCargoAsync();

        var reopenedStore = new LocalFlightAttemptStore(attemptPath);
        var reopened = new FlightAttemptTracker(new LocalEconomyStateStore(economyPath), reopenedStore, utcNow: () => Start.AddMinutes(3));
        await reopened.InitializeAsync(); var before = await reopenedStore.LoadDiagnosticTimelineAsync();
        var live = At(EmployedCargoPrototype.UisId, Start.AddMinutes(4), speed: 1.5, title: "Restart live aircraft");
        await ObserveSeconds(reopened, live, 15); await reopened.UnloadCargoAsync();

        var after = await reopenedStore.LoadDiagnosticTimelineAsync();
        Assert.Equal(before.Count(IsProgression), after.Count(IsProgression));
        var priorIds = before.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(after.Where(x => !priorIds.Contains(x.Id)), IsProgression);
        Assert.Equal(live.ObservedUtc.AddSeconds(15), reopened.Current.Diagnostic.LastValidSampleUtc);
        Assert.Equal("Delivered and paid", reopened.Current.Stage);
        var world = await new LocalEconomyStateStore(economyPath).LoadAsync();
        Assert.Single(world!.FinancialLedger); Assert.Equal(240, world.Players.Single().CachedBalance);
        Assert.Equal(120, world.Inventories.Single(x => x.LocationId == EmployedCargoPrototype.UisId).AvailableQuantity);
    }

    [Fact]
    public void ObservabilityDoesNotChangeFlightAttemptThresholds()
    {
        var options = new FlightAttemptTrackerOptions();
        Assert.Equal(1.5, options.AirportRadiusNauticalMiles); Assert.Equal(5, options.MaximumGroundSpeedKnots);
        Assert.Equal(TimeSpan.FromSeconds(10), options.RequiredStationaryDuration); Assert.Equal(2, options.MeaningfulMovementNauticalMiles);
        Assert.Equal(TimeSpan.FromSeconds(2), options.MaximumSampleGap);
        Assert.Equal(3.27, FlightAttemptTracker.RequiredDistanceForRoute(6.54), 6);
        Assert.Equal(10, FlightAttemptTracker.RequiredDistanceForRoute(25));
        Assert.Equal(.5, FlightAttemptTracker.RequiredDistanceForRoute(.2));
    }

    [Fact]
    public void UiBoundaryCannotSubmitOrManufactureVerifiedDeliveryEvidence()
    {
        Assert.DoesNotContain(typeof(IFlightAttemptTracker).GetMethods(), method =>
            method.GetParameters().Any(parameter => parameter.ParameterType == typeof(VerifiedDeliveryEvidence)) || method.ReturnType == typeof(VerifiedDeliveryEvidence));
        Assert.DoesNotContain(typeof(EmployedCargoJobsViewModel).GetMethods(), method =>
            method.GetParameters().Any(parameter => parameter.ParameterType == typeof(VerifiedDeliveryEvidence)) || method.ReturnType == typeof(VerifiedDeliveryEvidence));
    }

    private static async Task<Harness> Loaded(string destinationId = EmployedCargoPrototype.UisId)
    {
        var h = await Harness.Create(destinationId); await ObserveSeconds(h.Tracker, At(EmployedCargoPrototype.ErosId, Start), 10); await h.Tracker.LoadCargoAsync(); return h;
    }
    private static async Task RecordDeparture(Harness h)
    {
        await h.Tracker.ObserveAsync(OffsetFromEros(0.5, Start.AddSeconds(20), false));
        await h.Tracker.ObserveAsync(OffsetFromEros(3, Start.AddSeconds(21), false));
        await h.Tracker.ObserveAsync(OffsetFromEros(11, Start.AddSeconds(22), false));
    }
    private static async Task CompleteFlightEvidence(Harness h)
    { await RecordDeparture(h); await ObserveSeconds(h.Tracker, At(h.DestinationId, Start.AddMinutes(1)), 10); }
    private static bool IsProgression(FlightDiagnosticEvent value) => IsProgressionEvent(value.Type);
    private static bool IsProgressionEvent(FlightDiagnosticEventType type) => type is
        FlightDiagnosticEventType.FlightAttemptCreated or FlightDiagnosticEventType.OriginQualificationStarted or
        FlightDiagnosticEventType.OriginQualificationInterrupted or FlightDiagnosticEventType.ReadyToLoad or
        FlightDiagnosticEventType.CargoLoaded or FlightDiagnosticEventType.ShipmentEnteredInTransit or
        FlightDiagnosticEventType.AirborneEvidenceRecorded or FlightDiagnosticEventType.PostDepartureMovementRecorded or
        FlightDiagnosticEventType.RequiredMaximumDistanceReached or FlightDiagnosticEventType.ArrivalQualificationStarted or
        FlightDiagnosticEventType.ArrivalQualificationInterrupted or FlightDiagnosticEventType.ReadyToUnload;
    private static async Task ObserveSeconds(FlightAttemptTracker tracker, AircraftTelemetry first, int throughSecond)
    { for (int second = 0; second <= throughSecond; second++) await tracker.ObserveAsync(first with { ObservedUtc = first.ObservedUtc.AddSeconds(second) }); }
    private static AircraftTelemetry At(string locationId, DateTimeOffset time, bool onGround = true, double speed = 0, string title = "Cessna 208B Grand Caravan")
    {
        var point = NamibiaLocationCatalogue.Load().Get(locationId).ApproximateCoordinates!;
        return new(time, title, "C208B", point.Latitude, point.Longitude, 1000, 90, speed, onGround);
    }
    private static AircraftTelemetry OffsetFromEros(double nauticalMiles, DateTimeOffset time, bool onGround)
    {
        var eros = NamibiaLocationCatalogue.Load().Get(EmployedCargoPrototype.ErosId).ApproximateCoordinates!;
        return new(time, "Cessna 208B Grand Caravan", "C208B", eros.Latitude + nauticalMiles / 60d, eros.Longitude, 2000, 0, 90, onGround);
    }

    private sealed record Harness(IEconomyStateStore Economy, IFlightAttemptStore Attempts, FlightAttemptTracker Tracker, string DestinationId)
    {
        public static async Task<Harness> Create(string destinationId = EmployedCargoPrototype.UisId)
        {
            var economy = new MemoryEconomyStateStore(); var attempts = new MemoryFlightAttemptStore();
            var jobs = new EmployedCargoJobsService(economy, utcNow: () => Start); await jobs.LoadAsync();
            await jobs.AcceptAsync(destinationId == EmployedCargoPrototype.UisId ? "job-uis-supplies-v0" : "job-arandis-supplies-v0");
            var tracker = new FlightAttemptTracker(economy, attempts); await tracker.InitializeAsync();
            return new(economy, attempts, tracker, destinationId);
        }
    }
}

public sealed class FlightAttemptPersistenceTests : TemporaryDirectoryTest
{
    private string FilePath => Path.Combine(Root, "economy", "flight-attempts.json");

    [Fact]
    public async Task CorruptAndUnsupportedDocumentsAreReportedAndPreserved()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); await File.WriteAllTextAsync(FilePath, "{bad json");
        var original = await File.ReadAllTextAsync(FilePath); var store = new LocalFlightAttemptStore(FilePath);
        var corrupt = await Assert.ThrowsAsync<IOException>(() => store.LoadAsync()); Assert.Contains("corrupt", corrupt.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, await File.ReadAllTextAsync(FilePath));
        await File.WriteAllTextAsync(FilePath, "{\"SchemaVersion\":99,\"Attempts\":[]}"); original = await File.ReadAllTextAsync(FilePath);
        var unsupported = await Assert.ThrowsAsync<IOException>(() => store.LoadAsync()); Assert.Contains("unsupported schema", unsupported.Message);
        Assert.Equal(original, await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task SchemaOneAttemptMigratesWithoutLosingAuthoritativeProgress()
    {
        var loaded = DateTimeOffset.Parse("2026-09-10T08:00:00Z");
        var attempt = new FlightAttempt("flight-shipment-test", "job-test", "shipment-test", "origin-test", "destination-test",
            FlightAttemptState.CargoLoaded, loaded, new("Cessna 208B", "C208B"), new(loaded, -22, 17, 1000, 90, 0, true));
        var ready = loaded.AddMinutes(30); var proof = new FlightDeliveryProof("delivery-shipment-complete", ready,
            new(ready, -21.9, 16.8, 900, 180, 0, true), loaded.AddMinutes(1), loaded.AddMinutes(2), 12);
        var completed = new FlightAttempt("flight-shipment-complete", "job-complete", "shipment-complete", "origin-test", "destination-test",
            FlightAttemptState.Delivered, loaded, new("Cessna 208B", "C208B"), new(loaded, -22, 17, 1000, 90, 0, true),
            loaded.AddMinutes(1), loaded.AddMinutes(2), 12, loaded.AddMinutes(3), proof, ready);
        var store = new LocalFlightAttemptStore(FilePath); await store.SaveAsync(attempt); await store.SaveAsync(completed);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(FilePath))!.AsObject(); document["SchemaVersion"] = 1;
        document.Remove("DiagnosticTimeline"); await File.WriteAllTextAsync(FilePath, document.ToJsonString());
        var original = await new LocalFlightAttemptStore(FilePath).LoadAsync();
        Assert.Contains(attempt, original); Assert.Contains(completed, original); Assert.Empty(await new LocalFlightAttemptStore(FilePath).LoadDiagnosticTimelineAsync());
        Assert.Equal(3, JsonNode.Parse(await File.ReadAllTextAsync(FilePath))!["SchemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task SchemaTwoAttemptKeepsItsHistoricalTenNauticalMileRequirement()
    {
        var loaded=DateTimeOffset.Parse("2026-09-10T08:00:00Z");
        var attempt=new FlightAttempt("flight-shipment-legacy","job-legacy","shipment-legacy","origin-test","destination-test",
            FlightAttemptState.CargoLoaded,loaded,new("Cessna 208B","C208B"),new(loaded,-22,17,1000,90,0,true),RequiredMaximumDistanceNauticalMiles:3.27);
        var store=new LocalFlightAttemptStore(FilePath);await store.SaveAsync(attempt);
        var document=JsonNode.Parse(await File.ReadAllTextAsync(FilePath))!.AsObject();document["SchemaVersion"]=2;await File.WriteAllTextAsync(FilePath,document.ToJsonString());
        var migrated=Assert.Single(await new LocalFlightAttemptStore(FilePath).LoadAsync());Assert.Equal(10,migrated.RequiredMaximumDistanceNauticalMiles);
    }

    [Fact]
    public void TimelineRetentionIsBoundedWithoutRemovingLifecycleMilestones()
    {
        var start = DateTimeOffset.Parse("2026-09-10T08:00:00Z");
        var events = Enumerable.Range(0, 300).Select(index => new FlightDiagnosticEvent($"event-{index:D4}", "job-test", "shipment-test",
            start.AddSeconds(index), FlightDiagnosticEventType.SimConnectConnected, new Dictionary<string, string>())).ToList();
        var milestone = new FlightDiagnosticEvent("event-milestone", "job-test", "shipment-test", start.AddSeconds(150),
            FlightDiagnosticEventType.CargoLoaded, new Dictionary<string, string>()); events.Add(milestone);
        var bounded = LocalFlightAttemptStore.Bound(events, "shipment-test");
        Assert.True(bounded.Count <= 256); Assert.Contains(milestone, bounded);
    }
}

internal sealed class TestDirectory : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "FrontierFlightAttemptTests", Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(Root);
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
