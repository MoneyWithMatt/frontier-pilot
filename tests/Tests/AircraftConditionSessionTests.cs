using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class AircraftConditionSessionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "condition-session-" + Guid.NewGuid());
    private static TrackingWorldState World(string title = "Test aircraft", uint id = 42) => new(
        new(DateTimeOffset.UtcNow, title, "Test model", 0, 0, 100, 0, 0, true), id, id, null, PlayerControlMode.InAircraft, null, "test");
    private static ConditionFrame Frame(string title = "Test aircraft")
    {
        var now = DateTimeOffset.UtcNow;
        var field = new ConditionField("unsupported", "TEST", "Number", ConditionKind.OperatingTelemetry, 0, 1);
        return new(now, now, new(42, title, "Test model", 1), "synthetic", [field.Missing(now, "Unavailable")]);
    }
    [Fact]
    public async Task ConnectedStartStopAndRepeatedCommandsPreserveClosedExport()
    {
        var source = new Source(); var calls = 0; string? opened = null;
        var session = new AircraftConditionSession(() => { calls++; return source; }, root, p => opened = p);
        Assert.False(session.Start()); session.ObserveConnection(World()); Assert.True(session.Start()); Assert.False(session.Start());
        source.Frames.Writer.TryWrite(Frame()); await Until(() => session.Observations == 1);
        Assert.True(session.IsActive); Assert.Contains("RECORDING", session.Status); Assert.Equal(1, calls);
        await session.StopAsync(); await session.StopAsync();
        Assert.False(session.IsActive); Assert.True(session.CanStart);
        using (var exclusive = new FileStream(session.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Assert.True(exclusive.Length > 0);
        var json = File.ReadAllText(session.FilePath);
        Assert.Contains("Unavailable", json); Assert.Contains("\"Value\":null", json); Assert.Contains("cancelled", json);
        session.OpenFolderCommand.Execute(null); Assert.Equal(root, opened);
        var first = session.FilePath; Assert.True(session.Start()); await session.StopAsync(); Assert.NotEqual(first, session.FilePath); Assert.True(File.Exists(first));
        await session.ShutdownAsync(); Assert.False(session.Start());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DisconnectOrAircraftChangeStopsWithoutRestart(bool change)
    {
        var source = new Source(); var session = new AircraftConditionSession(() => source, root, _ => {});
        session.ObserveConnection(World()); session.Start(); source.Frames.Writer.TryWrite(Frame()); await Until(() => session.Observations == 1);
        session.ObserveConnection(change ? World("Other aircraft") : null); await session.StopAsync();
        Assert.False(session.IsActive); Assert.Contains(change ? "identity changed" : "disconnected", session.Status);
        Assert.Equal(1, session.Observations); Assert.Equal(change, session.CanStart);
    }
    [Fact]
    public async Task MismatchedFirstFrameAndSourceErrorsAreVisibleAndRecoverable()
    {
        var source = new Source(); var session = new AircraftConditionSession(() => source, root, _ => {});
        session.ObserveConnection(World()); session.Start(); source.Frames.Writer.TryWrite(Frame("Wrong aircraft"));
        await Until(() => !session.IsActive); Assert.Contains("Recording error", session.Status); Assert.Equal(0, session.Observations);
        Assert.DoesNotContain("\"Kind\":\"frame\"", File.ReadAllText(session.FilePath));
        source = new Source(); session.Start(); source.Frames.Writer.TryComplete(new IOException("synthetic read error"));
        await Until(() => !session.IsActive); Assert.Contains("synthetic read error", session.Status); Assert.True(session.CanStart);
    }
    [Fact]
    public async Task ShutdownFlushesAndDisabledModeNeverCreatesSource()
    {
        var source = new Source(); var session = new AircraftConditionSession(() => source, root, _ => {});
        session.ObserveConnection(World()); session.Start(); source.Frames.Writer.TryWrite(Frame()); await Until(() => session.Observations == 1);
        await session.ShutdownAsync(); Assert.False(session.IsActive); Assert.False(session.Start()); Assert.Contains("application closed", session.Status);
        using var closed = File.Open(session.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var disabled = new AircraftConditionSession(() => throw new Exception("must not run"), root, _ => {}, ConditionRecordingMode.Disabled);
        disabled.ObserveConnection(World()); Assert.False(disabled.Start()); await disabled.ShutdownAsync();
    }
    [Fact]
    public async Task FileCreationErrorDoesNotEscapeOrLeaveActiveSession()
    {
        Directory.CreateDirectory(root); var blocked = Path.Combine(root, "file"); File.WriteAllText(blocked, "preserve");
        var session = new AircraftConditionSession(() => new Source(), blocked, _ => {});
        session.ObserveConnection(World()); Assert.True(session.Start()); await Until(() => !session.IsActive);
        Assert.Contains("Recording error", session.Status); Assert.Equal("preserve", File.ReadAllText(blocked)); await session.ShutdownAsync();
    }
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    private sealed class Source : IAircraftConditionSource
    {
        public Channel<ConditionFrame> Frames { get; } = Channel.CreateUnbounded<ConditionFrame>();
        public ConditionConnectionInfo ConnectionInfo => new("synthetic", "synthetic", "synthetic");
        public async IAsyncEnumerable<ConditionFrame> ReadAsync([EnumeratorCancellation] CancellationToken token)
        { await foreach (var frame in Frames.Reader.ReadAllAsync(token)) yield return frame; }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
