using System.Runtime.CompilerServices;
using System.Text.Json;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class ExperimentalPlatformVisitTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static ConditionFrame Frame(int second, double height = .75, bool airborne = false,
        params (string Key, double Value)[] changes)
    {
        var identity = new ConditionAircraftIdentity(42, "H125 Passengers", "H125", 1);
        var data = new Dictionary<string, double>
        {
            ["PLANE LATITUDE"] = ExperimentalPlatformContactProfile.Latitude,
            ["PLANE LONGITUDE"] = ExperimentalPlatformContactProfile.Longitude,
            ["PLANE ALTITUDE"] = (ExperimentalPlatformContactProfile.DeckMeters + height) / .3048,
            ["GROUND ALTITUDE"] = ExperimentalPlatformContactProfile.DeckMeters / .3048,
            ["SIM ON GROUND"] = airborne ? 0 : 1,
            ["CONTACT POINT IS ON GROUND:8"] = airborne ? 0 : 1,
            ["VERTICAL SPEED"] = airborne ? 2 : 0,
            ["SIMULATION TIME"] = second, ["SIMULATION RATE"] = 1
        };
        foreach (var change in changes) data[change.Key] = change.Value;
        var profile = new ExperimentalPlatformContactProfile(); var now = Epoch.AddSeconds(second);
        return new(now, now, identity, profile.Id, profile.Fields(identity).Select(f => f.Observe(data.GetValueOrDefault(f.Key), now)).ToArray())
        { Runtime = new(true, false, 0) };
    }
    private static PlatformVisitStatus Observe(ExperimentalPlatformVisit visit, ConditionFrame frame) => visit.Observe(frame, frame.CompletedUtc);
    private static ExperimentalPlatformVisit Armed()
    {
        var visit = new ExperimentalPlatformVisit();
        for (int i = 0; i <= 20; i++) Observe(visit, Frame(i));
        Assert.Equal(PlatformVisitStage.ReadyForDeparture, visit.Current.Stage);
        return visit;
    }
    private static void Depart(ExperimentalPlatformVisit visit)
    {
        Observe(visit, Frame(21, 1.25, true));
        for (int i = 22; i <= 25; i++) Observe(visit, Frame(i, 1.75 + (i - 22) * .5, true));
        Assert.Equal(PlatformVisitStage.AwaitingReturn, visit.Current.Stage);
    }

    [Fact]
    public void DeckContactLossReturnCompletesOnceAndNoLivePermissionSurvivesClose()
    {
        var visit = Armed(); Depart(visit);
        for (int i = 26; i <= 47; i++) Observe(visit, Frame(i));
        Assert.True(visit.Current.Completed);
        Assert.True(visit.Current.CurrentDeckPermission);
        Assert.Equal(Enum.GetValues<PlatformVisitMilestone>(), visit.Current.Events.Select(x => x.Milestone));
        for (int i = 48; i <= 80; i++) Observe(visit, Frame(i));
        Assert.Equal(3, visit.Current.Events.Count);
        var closed = visit.Observe(null, Epoch.AddSeconds(81));
        Assert.True(closed.Completed);
        Assert.False(closed.CurrentDeckPermission);
    }

    [Fact]
    public void InitialAirborneAndWaterNeverArmOrComplete()
    {
        var visit = new ExperimentalPlatformVisit();
        for (int i = 0; i <= 25; i++) Observe(visit, Frame(i, 2, true));
        Assert.Empty(visit.Current.Events);
        for (int i = 26; i <= 60; i++) Observe(visit, Frame(i, -ExperimentalPlatformContactProfile.DeckMeters, false, ("GROUND ALTITUDE", 0)));
        Assert.Empty(visit.Current.Events);
    }

    [Theory]
    [InlineData("any-contact")]
    [InlineData("on-ground")]
    [InlineData("too-low")]
    [InlineData("horizontal-jump")]
    [InlineData("vertical-jump")]
    [InlineData("slew")]
    [InlineData("rate")]
    public void FlagsProximityAndImplausibleOrStagedMotionCannotProveDeparture(string problem)
    {
        var visit = Armed();
        for (int i = 21; i <= 30; i++)
        {
            var changes = problem switch
            {
                "any-contact" => new[] { ("CONTACT POINT IS ON GROUND:19", 1d) },
                "on-ground" => [("SIM ON GROUND", 1d)],
                "horizontal-jump" => [("PLANE LONGITUDE", ExperimentalPlatformContactProfile.Longitude + .0002)],
                "slew" => [("IS SLEW ACTIVE", 1d)],
                "rate" => [("SIMULATION RATE", 2d)],
                _ => Array.Empty<(string, double)>()
            };
            Observe(visit, Frame(i, problem == "vertical-jump" ? 10 : problem == "too-low" ? 1 : 1.75, true, changes));
        }
        Assert.DoesNotContain(visit.Current.Events, x => x.Milestone == PlatformVisitMilestone.DepartureObserved);
        Assert.False(visit.Current.Completed);
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("gap")]
    [InlineData("clock")]
    [InlineData("discontinuity")]
    [InlineData("disconnect")]
    [InlineData("missing")]
    public void InterruptionsCannotBridgeDepartureDwell(string problem)
    {
        var visit = Armed(); Observe(visit, Frame(21, 1.25, true)); Observe(visit, Frame(22, 1.75, true));
        var frame = Frame(23, 2.25, true);
        frame = problem switch
        {
            "pause" => frame with { Runtime = new(true, true, 0) },
            "gap" => Frame(30, 2.25, true),
            "clock" => Frame(23, 2.25, true, ("SIMULATION TIME", 0)),
            "discontinuity" => frame with { Runtime = new(true, false, 1) },
            "missing" => frame with { Readings = frame.Readings.Skip(1).ToArray() },
            _ => frame
        };
        if (problem == "disconnect") visit.Observe(null, frame.CompletedUtc); else Observe(visit, frame);
        for (int i = 31; i <= 40; i++) Observe(visit, Frame(i, 3, true));
        Assert.DoesNotContain(visit.Current.Events, x => x.Milestone == PlatformVisitMilestone.DepartureObserved);
    }

    [Fact]
    public void IdentityChangeBlocksWithoutErasingRecordedFacts()
    {
        var visit = Armed(); Depart(visit);
        var changed = Frame(26) with { Aircraft = Frame(26).Aircraft with { ObjectId = 99 } };
        Observe(visit, changed);
        for (int i = 27; i <= 60; i++) Observe(visit, Frame(i));
        Assert.Equal(PlatformVisitStage.Blocked, visit.Current.Stage);
        Assert.Equal(2, visit.Current.Events.Count);
        Assert.False(visit.Current.CurrentDeckPermission);
    }

    [Fact]
    public void DisconnectAfterRecordedDeparturePreservesHistoryButRequiresNewReturnDwell()
    {
        var visit = Armed(); Depart(visit);
        visit.Observe(null, Epoch.AddSeconds(26));
        for (int i = 40; i < 60; i++) Observe(visit, Frame(i));
        Assert.False(visit.Current.Completed);
        Observe(visit, Frame(60));
        Assert.True(visit.Current.Completed);
    }

    private sealed class Source(IEnumerable<ConditionFrame> frames) : IAircraftConditionSource
    {
        public ConditionConnectionInfo ConnectionInfo => new("synthetic", "synthetic", "synthetic");
        public async IAsyncEnumerable<ConditionFrame> ReadAsync([EnumeratorCancellation] CancellationToken token)
        {
            foreach (var frame in frames) { token.ThrowIfCancellationRequested(); yield return frame; await Task.Yield(); }
        }
    }
    private static ConditionFrame[] CompleteFrames() => Enumerable.Range(0, 48).Select(i =>
        i is >= 21 and <= 25 ? Frame(i, 1.25 + (i - 21) * .5, true) : Frame(i)).ToArray();

    [Fact]
    public async Task RecorderAndOfflineReviewRetainOneCompletionWithoutCurrentAuthorityOrDuplicateWrites()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        try
        {
            var frames = CompleteFrames(); var clock = Epoch;
            // Source sets the deterministic clock before callback; no need to weaken freshness in production.
            var source = new Source(frames.Select(f => { clock = f.CompletedUtc; return f; }));
            var status = await ExperimentalPlatformVisitRecording.RecordAsync(source, path, CancellationToken.None, clock: () => clock);
            Assert.True(status.Completed); Assert.False(status.CurrentDeckPermission);
            var before = File.ReadAllBytes(path);
            var review = ExperimentalPlatformVisitRecording.Review(path);
            Assert.True(review.RecordingClosed); Assert.True(review.HistoricalStatus.Completed);
            Assert.False(review.HistoricalStatus.CurrentDeckPermission);
            Assert.Equal(3, review.HistoricalStatus.Events.Count);
            Assert.Equal(48, review.Frames);
            Assert.Equal(before, File.ReadAllBytes(path));
            await Assert.ThrowsAsync<IOException>(() => ExperimentalPlatformVisitRecording.RecordAsync(source, path, CancellationToken.None));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("future-schema")]
    [InlineData("sequence")]
    [InlineData("trailing")]
    public async Task InvalidRecordingIsPreservedAndNeverReviewedAsCompleted(string problem)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        try
        {
            var clock = Epoch; var frames = CompleteFrames();
            await ExperimentalPlatformVisitRecording.RecordAsync(new Source(frames.Select(f => { clock = f.CompletedUtc; return f; })), path,
                CancellationToken.None, clock: () => clock);
            var text = File.ReadAllText(path);
            text = problem switch
            {
                "future-schema" => text.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":99"),
                "sequence" => text.Replace("\"Sequence\":2,", "\"Sequence\":1,"),
                _ => text + "{}\n"
            };
            File.WriteAllText(path, text);
            Assert.ThrowsAny<Exception>(() => ExperimentalPlatformVisitRecording.Review(path));
            Assert.Equal(text, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task InterruptedRecordingReviewsHistoryWithoutStartingANewFlightOrReusingDwell()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        try
        {
            var clock = Epoch;
            await ExperimentalPlatformVisitRecording.RecordAsync(new Source(CompleteFrames().Take(24).Select(f => { clock = f.CompletedUtc; return f; })),
                path, CancellationToken.None, clock: () => clock);
            var lines = File.ReadAllLines(path); File.WriteAllLines(path, lines.SkipLast(1)); // interrupted before final marker
            var review = ExperimentalPlatformVisitRecording.Review(path);
            Assert.False(review.RecordingClosed); Assert.False(review.HistoricalStatus.Completed);
            Assert.False(review.HistoricalStatus.CurrentDeckPermission);
            Assert.Single(review.HistoricalStatus.Events);
            var restarted = new ExperimentalPlatformVisit();
            for (int i = 24; i <= 40; i++) Observe(restarted, Frame(i, 3, true));
            Assert.Empty(restarted.Current.Events); // no stitching old observations into a fresh session
        }
        finally { File.Delete(path); }
    }
}

