using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class OffshoreRoundTripTests
{
    private static DateTimeOffset Epoch => FrontierAirportBuilder.TestFixtures.SyntheticOffshoreRoundTrip.Epoch;
    private static OffshorePlatform Platform => FrontierAirportBuilder.TestFixtures.SyntheticOffshoreRoundTrip.Platform;
    private static PassengerEndpoint Origin => FrontierAirportBuilder.TestFixtures.SyntheticOffshoreRoundTrip.Origin;
    private static ConditionFrame Frame(int second, string? problem = null) => FrontierAirportBuilder.TestFixtures.SyntheticOffshoreRoundTrip.Frame(second, problem);
    private static void Observe(ExperimentalOffshoreRoundTrip t, int i, string? problem = null) { var f = Frame(i, problem); t.Observe(f, f.CompletedUtc); }
    private static void Through(ExperimentalOffshoreRoundTrip t, int end, int start = 0)
    { for (int i = start; i <= end; i++) Observe(t, i); }
    [Fact]
    public void CompleteVisitRequiresOwnOutboundTimedDeckDepartureAndReturnOnce()
    {
        var test = new ExperimentalOffshoreRoundTrip(Origin, Platform);
        Through(test, 330); Assert.Equal(OffshoreTestStage.EstablishingDeck, test.Current.Stage);
        Observe(test, 331); Assert.Equal(OffshoreTestStage.Inspecting, test.Current.Stage);
        Through(test, 390, 332); Assert.False(test.Current.Completed);
        Observe(test, 391); Assert.Equal(OffshoreTestStage.ReadyToDepart, test.Current.Stage);
        Through(test, 701, 392); Assert.False(test.Current.Completed);
        Observe(test, 702); Assert.True(test.Current.Completed); Assert.True(test.Current.CurrentBasePresence);
        Assert.Equal(Enum.GetValues<OffshoreTestMilestone>(), test.Current.Events.Select(e => e.Milestone));
        Through(test, 710, 703); Assert.Equal(6, test.Current.Events.Count);
        var historical = test.Observe(null, Epoch.AddSeconds(711));
        Assert.True(historical.Completed); Assert.False(historical.CurrentBasePresence); Assert.False(historical.CurrentDeckPresence);
        Assert.Contains("No delivery", historical.Authority);
        Assert.False(Platform.CanOperate(PlatformOperation.InspectionVisit, "H125 Passengers", "H125", "1.9.21.0", "unknown"));
    }
    [Theory]
    [InlineData("water")]
    [InlineData("hover")]
    public void PlatformProximityAndWaterNeverEstablishAVisit(string problem)
    {
        var test = new ExperimentalOffshoreRoundTrip(Origin, Platform); Through(test, 310);
        for (int i = 311; i <= 391; i++) Observe(test, i, problem);
        Assert.DoesNotContain(test.Current.Events, e => e.Milestone == OffshoreTestMilestone.InspectionObserved);
        Assert.False(test.Current.Completed);
    }
    [Theory]
    [InlineData("pause")]
    [InlineData("freeze")]
    [InlineData("slew")]
    [InlineData("rate")]
    [InlineData("diagnostic")]
    [InlineData("disconnect")]
    [InlineData("gap")]
    [InlineData("identity")]
    public void InterruptedSessionRetainsHistoryButCannotStitchACompletion(string problem)
    {
        var test = new ExperimentalOffshoreRoundTrip(Origin, Platform); Through(test, 391);
        if (problem == "disconnect") test.Observe(null, Epoch.AddSeconds(392));
        else if (problem == "gap") Observe(test, 395);
        else if (problem == "identity") { var frame = Frame(392) with { Aircraft = new(43, "H125 Passengers", "H125", 1) }; test.Observe(frame, frame.CompletedUtc); }
        else Observe(test, 392, problem);
        Through(test, 702, 396);
        Assert.Equal(OffshoreTestStage.Interrupted, test.Current.Stage); Assert.Equal(4, test.Current.Events.Count);
        Assert.False(test.Current.CurrentDeckPresence); Assert.False(test.Current.CurrentBasePresence);
    }
    [Fact]
    public void StaticDeckStartOrRestartCannotBorrowEarlierFlightHistory()
    {
        var test = new ExperimentalOffshoreRoundTrip(Origin, Platform); Through(test, 391, 311);
        Assert.Empty(test.Current.Events); Assert.False(test.Current.Completed);
        Through(test, 702, 392); Assert.DoesNotContain(test.Current.Events, e => e.Milestone == OffshoreTestMilestone.BaseReturn);
    }
    [Fact]
    public void RepositioningAfterOriginIsNotAnOutboundFlight()
    {
        var test = new ExperimentalOffshoreRoundTrip(Origin, Platform); Through(test, 10);
        var jump = Frame(311) with { StartedUtc = Epoch.AddSeconds(11), CompletedUtc = Epoch.AddSeconds(11),
            Readings = Frame(311).Readings.Select(r => r with { ObservedUtc = Epoch.AddSeconds(11) }).ToArray() };
        test.Observe(jump, jump.CompletedUtc); Assert.Equal(OffshoreTestStage.Interrupted, test.Current.Stage);
    }
    private sealed class Source(IEnumerable<ConditionFrame> frames) : IAircraftConditionSource
    {
        public ConditionConnectionInfo ConnectionInfo => new("SYNTHETIC", "test-sim", "test-hash");
        public async IAsyncEnumerable<ConditionFrame> ReadAsync([EnumeratorCancellation] CancellationToken token)
        { foreach (var f in frames) { token.ThrowIfCancellationRequested(); yield return f; await Task.Yield(); } }
    }
    [Fact]
    public void LeavingDeckResetsUnconfirmedInspectionAndDoesNotPermitAnEarlyReturn()
    {
        var test = new ExperimentalOffshoreRoundTrip(Origin, Platform); Through(test, 360);
        Assert.Equal(29, test.Current.InspectionSeconds);
        Observe(test, 361, "hover"); Assert.Equal(0, test.Current.InspectionSeconds);
        Through(test, 391, 362); Assert.Equal(9, test.Current.InspectionSeconds);
        Through(test, 702, 392); Assert.False(test.Current.Completed);
        Assert.DoesNotContain(test.Current.Events, e => e.Milestone == OffshoreTestMilestone.InspectionObserved);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateTakeoffAfterGroundOrWaterTravelCannotReplaceObservedDeparture(bool returnLeg)
    {
        var test = new ExperimentalOffshoreRoundTrip(Origin, Platform);
        Through(test, returnLeg ? 391 : 10);
        for (int i = returnLeg ? 392 : 11; i <= (returnLeg ? 450 : 308); i++)
        {
            var f = Frame(i);
            f = f with { Readings = f.Readings.Select(r => r.Field.Key == "SIM ON GROUND" ? r.Field.Observe(1, r.ObservedUtc) : r).ToArray() };
            test.Observe(f, f.CompletedUtc);
        }
        Observe(test, returnLeg ? 451 : 309);
        Assert.Equal(OffshoreTestStage.Interrupted, test.Current.Stage); Assert.False(test.Current.Completed);
    }
    [Fact]
    public void DiscoveredOrUnresolvedOriginsDoNotBecomeRoundTripTestDestinations()
    {
        Assert.Throws<InvalidOperationException>(() => new ExperimentalOffshoreRoundTrip(Origin with { Kind = PassengerEndpointKind.Platform }, Platform));
        Assert.Throws<InvalidOperationException>(() => new ExperimentalOffshoreRoundTrip(Origin, Platform with { Stage = PlatformQualificationStage.Discovered }));
    }
    [Fact]
    public void StandaloneDiagnosticReadsOnlyResolvedDestinationMetadataAndCannotInventABase()
    {
        var root = Path.Combine(Path.GetTempPath(), "OffshoreBase", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<IOException>(() => ExperimentalOffshoreRoundTripRecording.ResolveBase(root));
            Assert.Empty(Directory.GetFiles(root));
            new DestinationReviewStore(root).Save(new("FYWB", "Approved", "FYWB", Origin.Latitude, Origin.Longitude,
                "SYNTHETIC reviewed base fixture only", Epoch, ["vision-jet"]));
            var files = Directory.GetFiles(root).ToDictionary(f => f, File.ReadAllBytes);
            var endpoint = ExperimentalOffshoreRoundTripRecording.ResolveBase(root);
            Assert.Equal(Origin.Latitude, endpoint.Latitude); Assert.Equal(Origin.Longitude, endpoint.Longitude);
            Assert.Equal(files.Keys.Order(), Directory.GetFiles(root).Order());
            foreach (var f in files) Assert.Equal(f.Value, File.ReadAllBytes(f.Key));
            Assert.DoesNotContain(Directory.GetFiles(root), f => Path.GetFileName(f).Contains("economy"));
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("missing-end")]
    [InlineData("duplicate")]
    [InlineData("trailing")]
    public async Task InterruptedAndCorruptRoundTripRecordingsAreNeverLivePermission(string problem)
    {
        var root = Path.Combine(Path.GetTempPath(), "OffshoreRoundTrip", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "partial.jsonl"); var clock = Epoch;
            IEnumerable<ConditionFrame> Frames() { for (int i = 0; i <= 20; i++) { var f = Frame(i); clock = f.CompletedUtc; yield return f; } }
            await ExperimentalOffshoreRoundTripRecording.RecordAsync(new Source(Frames()), Origin, Platform, path, CancellationToken.None, clock: () => clock);
            if (problem == "missing-end") File.WriteAllLines(path, File.ReadAllLines(path).SkipLast(1));
            else if (problem == "duplicate") File.WriteAllText(path, File.ReadAllText(path).Replace("\"Sequence\":2,", "\"Sequence\":1,"));
            else File.AppendAllText(path, "{}\n");
            var bytes = File.ReadAllBytes(path);
            if (problem == "missing-end")
            { var review = ExperimentalOffshoreRoundTripRecording.Review(path); Assert.False(review.RecordingClosed); Assert.False(review.HistoricalStatus.Completed); Assert.False(review.HistoricalStatus.CurrentBasePresence); }
            else Assert.ThrowsAny<Exception>(() => ExperimentalOffshoreRoundTripRecording.Review(path));
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task NativeRecordingContextAndReplayAreCreateOnlyAndHistoricalWithoutCallbacks()
    {
        var root = Path.Combine(Path.GetTempPath(), "OffshoreRoundTrip", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "roundtrip.jsonl"); var clock = Epoch;
            IEnumerable<ConditionFrame> Frames() { for (int i = 0; i <= 702; i++) { var f = Frame(i); clock = f.CompletedUtc; yield return f; } }
            var result = await ExperimentalOffshoreRoundTripRecording.RecordAsync(new Source(Frames()), Origin, Platform, path, CancellationToken.None, clock: () => clock);
            Assert.True(result.Completed); Assert.False(result.CurrentBasePresence);
            var review = ExperimentalOffshoreRoundTripRecording.Review(path);
            Assert.True(review.RecordingClosed); Assert.Equal(703, review.Frames); Assert.True(review.HistoricalStatus.Completed);
            Assert.False(review.HistoricalStatus.CurrentBasePresence); Assert.Equal(6, review.HistoricalStatus.Events.Count);
            var bytes = File.ReadAllBytes(path);
            await Assert.ThrowsAsync<IOException>(() => ExperimentalOffshoreRoundTripRecording.RecordAsync(new Source(Frames()), Origin, Platform, path, CancellationToken.None));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            var context = JsonNode.Parse(File.ReadAllText(path + ".mission.json"))!; context["SchemaVersion"] = 99;
            File.WriteAllText(path + ".mission.json", context.ToJsonString()); var contextBytes = File.ReadAllBytes(path + ".mission.json");
            Assert.Throws<InvalidDataException>(() => ExperimentalOffshoreRoundTripRecording.Review(path));
            Assert.Equal(contextBytes, File.ReadAllBytes(path + ".mission.json"));
        }
        finally { Directory.Delete(root, true); }
    }
}

