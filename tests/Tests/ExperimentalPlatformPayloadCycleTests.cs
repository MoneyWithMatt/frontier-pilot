using System.Text.Json;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class ExperimentalPlatformPayloadCycleTests
{
    [Fact]
    public async Task AddsAndRemovesOnlyFiveKilogramsWithDurableIntentsAndRetainedChecks()
    {
        using var fixture = new Fixture();
        fixture.Simulator.BeforeWrite = () => Assert.Contains("Intent", fixture.LastStage());
        await fixture.Run();
        Assert.Equal(2, fixture.Simulator.Writes.Count);
        Assert.Equal(ExperimentalPlatformPayloadCycle.ReviewedBaselineKilograms + 5, fixture.Simulator.Writes[0][6]);
        Assert.Equal(ExperimentalPlatformPayloadCycle.ReviewedBaselineKilograms, fixture.Simulator.Writes[1][6]);
        Assert.All(fixture.Simulator.Writes, target => Assert.Single(target));
        Assert.Equal("Completed", fixture.LastStage());
        Assert.Equal(2, fixture.RetentionChecks);
        Assert.Equal(fixture.BaselineWeights, fixture.Simulator.Weights);
        await Assert.ThrowsAsync<IOException>(fixture.Run);
        Assert.Equal(2, fixture.Simulator.Writes.Count); // duplicate command/path never reaches simulator
    }

    [Theory]
    [InlineData("wrong-aircraft")]
    [InlineData("wrong-layout")]
    [InlineData("wrong-baseline")]
    [InlineData("station-limit")]
    [InlineData("manual-before-write")]
    public async Task UnsupportedOrChangedStateIsRefusedBeforeAnyWrite(string problem)
    {
        using var fixture = new Fixture();
        fixture.Simulator.Problem = problem;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(fixture.Run);
        Assert.Empty(fixture.Simulator.Writes);
        Assert.Equal("Unresolved", fixture.LastStage());
    }

    [Theory]
    [InlineData("clamp")]
    [InlineData("throw-after-write")]
    [InlineData("retention-reset")]
    [InlineData("manual-before-unload")]
    public async Task AppliedOrPartialWritesRemainExplicitlyUnresolvedWithoutGuessedRemoval(string problem)
    {
        using var fixture = new Fixture();
        fixture.Simulator.Problem = problem;
        await Assert.ThrowsAnyAsync<Exception>(fixture.Run);
        Assert.Single(fixture.Simulator.Writes);
        Assert.Equal("Unresolved", fixture.LastStage());
    }

    [Fact]
    public async Task OperationalGrossWarningDoesNotBlockThisTechnicallyPermittedTest()
    {
        using var fixture = new Fixture();
        fixture.Simulator.Problem = "over-gross";
        await fixture.Run();
        Assert.Equal("Completed", fixture.LastStage());
        Assert.Contains("above the reported maximum gross", File.ReadAllText(fixture.Path));
    }

    [Fact]
    public async Task ChangedUnrelatedPayloadIsPreservedAtUnloading()
    {
        using var fixture = new Fixture();
        fixture.Simulator.Problem = "unrelated-before-unload";
        await fixture.Run();
        Assert.Equal(fixture.BaselineWeights[6] + 10, fixture.Simulator.Weights[6]);
        Assert.Equal(fixture.BaselineWeights[5], fixture.Simulator.Weights[5]);
    }

    [Fact]
    public async Task DisconnectAfterLoadingCannotInventArrivalOrTriggerAutomaticUnload()
    {
        using var fixture = new Fixture();
        fixture.DisconnectAfterLoad = true;
        await Assert.ThrowsAsync<IOException>(fixture.Run);
        Assert.Single(fixture.Simulator.Writes);
        Assert.Equal("Unresolved", fixture.LastStage());
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Frontier-platform-test-{Guid.NewGuid():N}.jsonl");
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public FakeSimulator Simulator { get; }
        public decimal[] BaselineWeights { get; }
        public int RetentionChecks;
        public bool DisconnectAfterLoad;
        private int seconds;
        public Fixture() { Simulator = new(this); BaselineWeights = Simulator.Weights.ToArray(); }
        public Task Run() => new ExperimentalPlatformPayloadCycle(Simulator, Next, () => Now,
            _ => { RetentionChecks++; Now = Now.AddSeconds(3); seconds += 3; return Task.CompletedTask; })
            .RunAsync("synthetic-sdk", Path, CancellationToken.None);
        private Task<ConditionFrame> Next(CancellationToken token)
        {
            if (DisconnectAfterLoad && Simulator.Writes.Count > 0) throw new IOException("Synthetic disconnect.");
            Now = Now.AddSeconds(1); seconds++;
            var identity = new ConditionAircraftIdentity(42, "H125 Passengers", "H125", 1);
            var profile = new ExperimentalPlatformContactProfile();
            var values = new Dictionary<string, double>
            {
                ["PLANE LATITUDE"] = ExperimentalPlatformContactProfile.Latitude,
                ["PLANE LONGITUDE"] = ExperimentalPlatformContactProfile.Longitude,
                ["PLANE ALTITUDE"] = (ExperimentalPlatformContactProfile.DeckMeters + .75) / .3048,
                ["GROUND ALTITUDE"] = ExperimentalPlatformContactProfile.DeckMeters / .3048,
                ["SIM ON GROUND"] = 1, ["SIMULATION RATE"] = 1, ["SIMULATION TIME"] = seconds,
                ["CONTACT POINT IS ON GROUND:8"] = 1
            };
            return Task.FromResult(new ConditionFrame(Now, Now, identity, profile.Id,
                profile.Fields(identity).Select(f => f.Observe(values.GetValueOrDefault(f.Key), Now)).ToArray())
            { Runtime = new(true, false, 0) });
        }
        public string LastStage()
        {
            using var reader = new StreamReader(new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            using var json = JsonDocument.Parse(reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Last());
            return json.RootElement.GetProperty("Stage").GetString()!;
        }
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }

    private sealed class FakeSimulator(Fixture fixture) : IAircraftLoadingSimulator
    {
        private static readonly string[] Names = ["Pilot", "Passenger 1", "Passenger 2", "Passenger 3", "Passenger 4", "Cargo 1", "Cargo 2", "Cargo 3", "Co-Pilot"];
        public decimal[] Weights = [77, 77, 77, 77, 77, ExperimentalPlatformPayloadCycle.ReviewedBaselineKilograms, 119, 79, 0];
        public List<IReadOnlyDictionary<int, decimal>> Writes { get; } = [];
        public string Problem = "";
        public Action? BeforeWrite;
        private int reads;
        private AircraftLoadingSnapshot Snapshot()
        {
            var position = new FlightPosition(fixture.Now, ExperimentalPlatformContactProfile.Latitude,
                ExperimentalPlatformContactProfile.Longitude, (ExperimentalPlatformContactProfile.DeckMeters + .75) / .3048, 180, 0, true);
            var stations = Enumerable.Range(1, 9).Select(i => new PayloadStationReading(i,
                Problem == "wrong-layout" && i == 6 ? "Changed station" : Names[i - 1], Weights[i - 1],
                AuthoritativeMaximumKilograms: Problem == "station-limit" && i == 6 ? Weights[i - 1] + 1 : null)).ToArray();
            return new(fixture.Now, new(Problem == "wrong-aircraft" ? "Caravan" : "H125 Passengers", "H125"), position,
                stations, [new("center", "Center", 70, 160)], [new(1, true)], 2.72m,
                new(1174, Problem == "over-gross" ? 1 : 2370, 1200 + Weights.Sum(), null, 42, 0, 100), "Synthetic test data");
        }
        public Task<AircraftLoadingSnapshot> ReadAsync(string sdkRoot, CancellationToken token = default)
        {
            reads++;
            if (Problem == "wrong-baseline" && reads == 1) Weights[5] += 5;
            if (Problem == "manual-before-write" && reads == 2) Weights[5] += 1;
            if (Problem == "retention-reset" && reads == 3) Weights[5] = fixture.BaselineWeights[5];
            if (Problem == "manual-before-unload" && reads == 4) Weights[5] += 1;
            if (Problem == "unrelated-before-unload" && reads == 4) Weights[6] += 10;
            return Task.FromResult(Snapshot());
        }
        public Task<AircraftLoadingSnapshot> WritePayloadAsync(string sdkRoot, IReadOnlyDictionary<int, decimal> targets, CancellationToken token = default)
        {
            BeforeWrite?.Invoke(); Writes.Add(new Dictionary<int, decimal>(targets));
            foreach (var target in targets) Weights[target.Key - 1] = Problem == "clamp" ? target.Value - 2 : target.Value;
            if (Problem == "throw-after-write") throw new IOException("Synthetic exception after mass applied.");
            return Task.FromResult(Snapshot());
        }
        public Task<AircraftLoadingSnapshot> WriteFuelAsync(string sdkRoot, IReadOnlyDictionary<string, decimal> targets, CancellationToken token = default) =>
            throw new InvalidOperationException("Test must never write fuel.");
    }
}
