using System.Runtime.CompilerServices;
using System.Text.Json;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class AircraftConditionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FrontierConditionTests", Guid.NewGuid().ToString("N"));
    private static readonly ConditionAircraftIdentity Aircraft = new(524288, "Test C172", "C172", 1);
    private static readonly ConditionField Failure = new("engine.failed", "GENERAL ENG FAILED:1", "bool", ConditionKind.ReportedFailure, 0, 1, true);
    public AircraftConditionTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(-1)] [InlineData(2)] [InlineData(0.5)]
    public void InvalidFailureReadingsCannotBecomeFailureOrHealthy(double value)
    {
        var result = Failure.Observe(value, DateTimeOffset.UtcNow);
        Assert.Equal(ConditionAvailability.Invalid, result.Availability); Assert.Null(result.Value);
    }

    [Fact]
    public void ZeroReceiptDoesNotQualifySupport()
    {
        var result = Failure.Observe(0, DateTimeOffset.UtcNow);
        Assert.Equal(ConditionAvailability.ReceivedUnverified, result.Availability);
        Assert.Equal(ConditionIntegrationLevel.Unverified, new StandardConditionProfile().Level);
    }

    [Fact]
    public void EngineCountDrivesProfileWithoutModelAliases()
    {
        var fields = new StandardConditionProfile().Fields(Aircraft with { Title = "Custom Twin", EngineCount = 2 });
        Assert.Contains(fields, x => x.SimVar == "GENERAL ENG FAILED:2");
        Assert.DoesNotContain(fields, x => x.SimVar == "GENERAL ENG FAILED:3");
        Assert.DoesNotContain(fields, x => x.SimVar.StartsWith("WEAR AND TEAR LEVEL:"));
        Assert.DoesNotContain(fields, x => x.Kind == ConditionKind.EstimatedCondition);
        Assert.Equal(fields.Count, fields.Select(x => x.Key).Distinct().Count());
    }

    [Fact]
    public void SameModelGetsDifferentDurableAirframeIdentitiesAndUnknownCondition()
    {
        var first = AircraftConditionRecord.Create("First", Aircraft.Title, Aircraft.Model, "test/version 1");
        var second = AircraftConditionRecord.Create("Second", Aircraft.Title, Aircraft.Model, "test/version 1");
        Assert.NotEqual(first.AirframeId, second.AirframeId);
        Assert.Null(first.AirframeHours); Assert.Null(first.LandingCycles); Assert.Empty(first.Components);
        var path = Path.Combine(root, "airframe.json"); AircraftConditionFiles.Create(path, first);
        var restored = AircraftConditionFiles.Load(path);
        Assert.Equal(first.AirframeId, restored.AirframeId);
        Assert.True(restored.Matches(Aircraft));
        Assert.False(restored.Matches(Aircraft with { Title = "Different aircraft" }));
        var bytes = File.ReadAllBytes(path);
        Assert.Throws<IOException>(() => AircraftConditionFiles.Create(path, second));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"SchemaVersion\":99}")]
    [InlineData("{\"SchemaVersion\":1,\"Unexpected\":42}")]
    public void CorruptFutureAndUnknownDocumentsArePreserved(string content)
    {
        var path = Path.Combine(root, "airframe.json"); File.WriteAllText(path, content);
        Assert.ThrowsAny<Exception>(() => AircraftConditionFiles.Load(path));
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void ComponentFaultsAndEstimatesRoundTripWithoutNativeAuthority()
    {
        var record = AircraftConditionRecord.Create("Second aircraft", Aircraft.Title, Aircraft.Model, "test") with
        { Components = [new("engine/1", "piston-engine", 123, 0.75, ["fault-1"])] };
        var path = Path.Combine(root, "airframe.json"); AircraftConditionFiles.Create(path, record);
        var component = Assert.Single(AircraftConditionFiles.Load(path).Components);
        Assert.Equal("fault-1", Assert.Single(component.FaultIds)); Assert.Equal(0.75, component.EstimatedHealth);
    }

    [Fact]
    public void RejectsInvalidOrDuplicateComponents()
    {
        var record = AircraftConditionRecord.Create("Test", Aircraft.Title, Aircraft.Model, "test");
        var component = new AircraftComponentCondition("engine/1", "piston", 1, double.NaN, []);
        Assert.Throws<InvalidDataException>(() => (record with { Components = [component] }).Validate());
        component = component with { EstimatedHealth = null };
        Assert.Throws<InvalidDataException>(() => (record with { Components = [component, component] }).Validate());
    }

    [Fact]
    public void UnsupportedVariableDoesNotDiscardValidSiblingOrCreateZero()
    {
        var batch = new ConditionReplies(3);
        batch.Add(101, 0, 20, false); batch.Add(102, 1, 21, false); batch.Add(103, 2, 22, false);
        batch.Sent(700, 101); batch.Sent(701, 102);
        batch.Receive(ExceptionPacket(700));
        batch.Receive(DataPacket(102, 21, 1234));
        var results = batch.Finish();
        Assert.Null(results[0].Number); Assert.Contains("exception", results[0].Error);
        Assert.Equal(1234, results[1].Number); Assert.Null(results[1].Error);
        Assert.Null(results[2].Number); Assert.Contains("No reply", results[2].Error);
    }

    [Fact]
    public void LateAndDuplicatePacketsCannotBeAttributedToAnotherField()
    {
        var batch = new ConditionReplies(1); batch.Add(102, 0, 21, false); batch.Sent(701, 102);
        batch.Receive(DataPacket(101, 20, 99)); batch.Receive(ExceptionPacket(700));
        Assert.Equal(1, batch.Pending);
        batch.Receive(DataPacket(102, 21, 10)); batch.Receive(DataPacket(102, 21, 999));
        Assert.Equal(10, Assert.Single(batch.Finish()).Number);
    }

    [Theory]
    [InlineData(20, 1, 524288, 47)]
    [InlineData(21, 1, 524288, 48)]
    [InlineData(20, 2, 524288, 48)]
    [InlineData(20, 1, 0, 48)]
    public void RejectsMalformedOrMisidentifiedPackets(uint definition, uint count, uint obj, int length)
    {
        var packet = DataPacket(101, definition, 1); Put(packet, 36, count); Put(packet, 16, obj);
        Assert.Throws<IOException>(() => ConditionPacket.Decode(packet[..length], 20, false));
    }

    [Fact]
    public async Task ExportsTimestampedUnavailableAndFailureObservationsWithoutUpdatingCondition()
    {
        var record = AircraftConditionRecord.Create("Test", Aircraft.Title, Aircraft.Model, "package/version");
        var state = Path.Combine(root, "airframe.json"); AircraftConditionFiles.Create(state, record);
        var before = File.ReadAllBytes(state);
        var frame = Frame();
        frame = frame with { Readings = [Failure.Observe(1, frame.StartedUtc),
            new ConditionField("oil", "GENERAL ENG OIL PRESSURE:1", "psf", ConditionKind.OperatingTelemetry, 0, 10000).Missing(frame.StartedUtc, "Unsupported query")] };
        var output = Path.Combine(root, "recording.jsonl");
        var count = await new AircraftConditionRecorder().RecordAsync(new FakeSource([frame]), output, record, default);
        Assert.Equal(1, count); Assert.Equal(before, File.ReadAllBytes(state));
        var lines = File.ReadAllLines(output).Select(x => JsonDocument.Parse(x)).ToArray();
        try
        {
            Assert.Equal("session", lines[0].RootElement.GetProperty("Kind").GetString());
            Assert.True(lines[0].RootElement.GetProperty("ReadOnly").GetBoolean());
            var readings = lines[1].RootElement.GetProperty("Frame").GetProperty("Readings");
            Assert.Equal(1, readings[0].GetProperty("Value").GetDouble());
            Assert.Equal(JsonValueKind.Null, readings[1].GetProperty("Value").ValueKind);
            Assert.Equal("completed", lines[2].RootElement.GetProperty("Outcome").GetString());
        }
        finally { foreach (var line in lines) line.Dispose(); }
        Assert.DoesNotContain(root, File.ReadAllText(output));
        await Assert.ThrowsAsync<IOException>(() => new AircraftConditionRecorder().RecordAsync(new FakeSource([]), output, null, default));
    }

    [Fact]
    public async Task AircraftChangeEndsSessionWithoutExportingMixedFrame()
    {
        var first = Frame(); var second = Frame(first.CompletedUtc.AddSeconds(1)) with { Aircraft = Aircraft with { ObjectId = 100 } };
        var output = Path.Combine(root, "changed.jsonl");
        await Assert.ThrowsAsync<InvalidDataException>(() => new AircraftConditionRecorder().RecordAsync(new FakeSource([first, second]), output, null, default));
        Assert.Equal(3, File.ReadAllLines(output).Length);
        Assert.Contains("\"Outcome\":\"error\"", File.ReadAllText(output));
    }

    [Fact]
    public async Task ExplicitAirframeMismatchRefusesAllObservations()
    {
        var record = AircraftConditionRecord.Create("Wrong", "Wrong title", Aircraft.Model, "test");
        var output = Path.Combine(root, "mismatch.jsonl");
        await Assert.ThrowsAsync<InvalidDataException>(() => new AircraftConditionRecorder().RecordAsync(new FakeSource([Frame()]), output, record, default));
        Assert.Equal(2, File.ReadAllLines(output).Length);
    }

    [Fact]
    public async Task CancellationClosesReadablePartialExport()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        var output = Path.Combine(root, "cancelled.jsonl");
        Assert.Equal(0, await new AircraftConditionRecorder().RecordAsync(new FakeSource([]), output, null, stop.Token));
        Assert.Contains("\"Outcome\":\"cancelled\"", File.ReadAllText(output));
    }

    [Fact]
    public void StaleValuesAndInferencesCannotEnterObservationExport()
    {
        var frame = Frame();
        Assert.Throws<InvalidDataException>(() => AircraftConditionRecorder.ValidateFrame(frame, frame.CompletedUtc));
        Assert.Throws<InvalidDataException>(() => AircraftConditionRecorder.ValidateFrame(frame with
        { Readings = [Failure.Observe(1, frame.StartedUtc.AddSeconds(-1))] }, null));
        Assert.Throws<InvalidDataException>(() => AircraftConditionRecorder.ValidateFrame(frame with
        { Readings = [(Failure with { Kind = ConditionKind.EstimatedCondition }).Observe(1, frame.StartedUtc)] }, null));
    }

    private static ConditionFrame Frame(DateTimeOffset? start = null)
    {
        var utc = start ?? DateTimeOffset.UtcNow;
        return new(utc, utc.AddMilliseconds(10), Aircraft, "synthetic-test-only", [Failure.Observe(0, utc)]);
    }
    private sealed class FakeSource(IEnumerable<ConditionFrame> frames) : IAircraftConditionSource
    {
        public ConditionConnectionInfo ConnectionInfo => new("synthetic", "synthetic", "synthetic");
        public async IAsyncEnumerable<ConditionFrame> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield(); cancellationToken.ThrowIfCancellationRequested();
            foreach (var frame in frames) { cancellationToken.ThrowIfCancellationRequested(); yield return frame; }
        }
    }
    private static byte[] DataPacket(uint request, uint definition, double value)
    {
        var data = new byte[48]; Put(data, 0, 48); Put(data, 8, 8); Put(data, 12, request);
        Put(data, 16, 524288); Put(data, 20, definition); Put(data, 36, 1);
        BitConverter.GetBytes(value).CopyTo(data, 40); return data;
    }
    private static byte[] ExceptionPacket(uint packet)
    {
        var data = new byte[24]; Put(data, 8, 1); Put(data, 12, 7); Put(data, 16, packet); return data;
    }
    private static void Put(byte[] data, int offset, uint value) => BitConverter.GetBytes(value).CopyTo(data, offset);
}
