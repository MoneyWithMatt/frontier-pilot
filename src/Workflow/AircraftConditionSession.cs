using System.ComponentModel;
using System.Diagnostics;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public enum ConditionRecordingMode { Disabled, Manual }

/// <summary>Single owner of the optional condition stream. Manual commands and future connection policy use this lifecycle.</summary>
public sealed class AircraftConditionSession : INotifyPropertyChanged
{
    private readonly object gate = new();
    private readonly Func<IAircraftConditionSource> sourceFactory;
    private readonly string directory;
    private readonly Action<string> openFolder;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private TrackingWorldState? world;
    private CancellationTokenSource? cancellation;
    private Task running = Task.CompletedTask;
    private bool shutdown, active, stopping;
    private string status = "Ready · manual recording", file = "", aircraft = "No identified aircraft", stopReason = "Stopped";
    private int observations;
    private Stopwatch elapsed = new();
    private Timer? timer;
    private TrackingWorldState? target;
    public AircraftConditionSession(Func<IAircraftConditionSource> sourceFactory, string directory, Action<string> openFolder,
        ConditionRecordingMode mode = ConditionRecordingMode.Manual)
    {
        this.sourceFactory = sourceFactory; this.directory = directory; this.openFolder = openFolder; Mode = mode;
        StartCommand = new(() => Start(), () => CanStart);
        StopCommand = new(() => RequestStop("Stopped by user"), () => IsActive && !IsStopping);
        OpenFolderCommand = new(OpenRecordingFolder, () => FilePath.Length != 0 && Directory.Exists(directory));
    }
    public ConditionRecordingMode Mode { get; }
    public bool IsActive { get { lock (gate) return active; } }
    public bool IsStopping { get { lock (gate) return stopping; } }
    public bool CanStart { get { lock (gate) return !shutdown && !active && Mode == ConditionRecordingMode.Manual && Usable(world); } }
    public string Status { get { lock (gate) return status; } }
    public string FilePath { get { lock (gate) return file; } }
    public string Aircraft { get { lock (gate) return aircraft; } }
    public int Observations { get { lock (gate) return observations; } }
    public string Duration { get { lock (gate) return elapsed.Elapsed.ToString(@"hh\:mm\:ss"); } }
    public ActionCommand StartCommand { get; }
    public ActionCommand StopCommand { get; }
    public ActionCommand OpenFolderCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void ObserveConnection(TrackingWorldState? sample)
    {
        lock (gate)
        {
            world = sample;
            if (active && (!Usable(sample) || !Matches(sample!, target!)))
                RequestStop(sample is null ? "Stopped · simulator disconnected" : "Stopped · aircraft identity changed or unavailable");
            if (!active)
            {
                aircraft = Usable(sample) ? $"{sample!.Aircraft.AircraftTitle} · {sample.Aircraft.AircraftType}" : "No identified aircraft";
                if (file.Length == 0) status = Mode == ConditionRecordingMode.Disabled ? "Recording disabled in dry-run mode" :
                    Usable(sample) ? "Ready · manual recording" : "Connect Tracking to an identified aircraft to record";
            }
        }
        Notify();
    }

    public bool Start()
    {
        lock (gate)
        {
            if (!CanStart) return false;
            target = world!;
            aircraft = $"{target.Aircraft.AircraftTitle} · {target.Aircraft.AircraftType}";
            file = Path.Combine(directory, $"condition-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
            active = true; stopping = false; observations = 0; status = "Starting recording…"; stopReason = "Recording completed";
            elapsed = Stopwatch.StartNew(); cancellation = new();
            var token = cancellation.Token; var expected = target; var path = file;
            timer = new Timer(_ => Notify(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            running = Task.Run(() => RecordAsync(expected, path, token));
        }
        Notify(); return true;
    }
    private async Task RecordAsync(TrackingWorldState expected, string path, CancellationToken token)
    {
        try
        {
            Directory.CreateDirectory(directory);
            await new AircraftConditionRecorder().RecordAsync(sourceFactory(), path, null, token,
                id => id.ObjectId == expected.AircraftObjectId && id.Title == expected.Aircraft.AircraftTitle && id.Model == expected.Aircraft.AircraftType,
                (count, _) => { lock (gate) { observations = count; if (!stopping) status = "RECORDING · read-only · compatibility unverified"; } Notify(); });
            lock (gate) status = stopReason + (observations == 0 ? " · no observations" : " · file preserved");
        }
        catch (Exception error) { lock (gate) status = "Recording error · " + error.Message + " · any partial file is preserved"; }
        finally
        {
            lock (gate)
            {
                elapsed.Stop(); timer?.Dispose(); timer = null;
                cancellation?.Dispose(); cancellation = null; active = false; stopping = false;
            }
            Notify();
        }
    }
    public void RequestStop(string reason)
    {
        lock (gate)
        {
            if (!active || stopping) return;
            stopping = true; stopReason = reason; status = "Stopping · flushing recording…"; cancellation?.Cancel();
        }
        Notify();
    }
    public async Task StopAsync(string reason = "Stopped by user")
    {
        Task pending;
        lock (gate) { RequestStop(reason); pending = running; }
        await pending;
    }
    public async Task ShutdownAsync()
    {
        lock (gate) shutdown = true;
        await StopAsync("Stopped · application closed");
        Notify();
    }
    private void OpenRecordingFolder()
    {
        try { openFolder(directory); }
        catch (Exception error) { lock (gate) status = "Could not open recording folder: " + error.Message; Notify(); }
    }
    private static bool Usable(TrackingWorldState? value) => value is not null && value.AircraftObjectId != 0 &&
        !string.IsNullOrWhiteSpace(value.Aircraft.AircraftTitle) && value.Aircraft.AircraftTitle != "Unknown aircraft" &&
        !string.IsNullOrWhiteSpace(value.Aircraft.AircraftType);
    private static bool Matches(TrackingWorldState a, TrackingWorldState b) => a.AircraftObjectId == b.AircraftObjectId &&
        a.Aircraft.AircraftTitle == b.Aircraft.AircraftTitle && a.Aircraft.AircraftType == b.Aircraft.AircraftType;
    private void Notify()
    {
        void Publish()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            StartCommand.Refresh(); StopCommand.Refresh(); OpenFolderCommand.Refresh();
        }
        if (context is not null && SynchronizationContext.Current != context) context.Post(_ => Publish(), null);
        else Publish();
    }
}
