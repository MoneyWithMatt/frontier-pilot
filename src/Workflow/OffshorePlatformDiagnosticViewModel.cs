using System.ComponentModel;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>Explicit read-only PR9 diagnostic UI, isolated from career mission service and ledger.</summary>
public sealed class OffshorePlatformDiagnosticViewModel : INotifyPropertyChanged
{
    private readonly Func<IAircraftConditionSource> source;
    private readonly string directory;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private CancellationTokenSource? cancellation;
    private Task task = Task.CompletedTask;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Running { get; private set; }
    public string Status { get; private set; } = "Experimental H125 contact cycle only. No career reward or normal flown evidence.";
    public string RecordingPath { get; private set; } = "";
    public ActionCommand StartCommand { get; }
    public ActionCommand StopCommand { get; }
    public OffshorePlatformDiagnosticViewModel(Func<IAircraftConditionSource> source, string directory)
    {
        this.source = source; this.directory = directory;
        StartCommand = new(() => _ = StartAsync(), () => !Running);
        StopCommand = new(() => cancellation?.Cancel(), () => Running);
    }
    public Task StartAsync()
    {
        if (Running) return Task.CompletedTask;
        Running = true; cancellation = new(TimeSpan.FromMinutes(15));
        task = RecordAsync(cancellation.Token); Notify(); return Task.CompletedTask;
    }
    private async Task RecordAsync(CancellationToken token)
    {
        try
        {
            Directory.CreateDirectory(directory);
            RecordingPath = Path.Combine(directory, "ol400-contact-" + Guid.NewGuid().ToString("N") + ".jsonl");
            await ExperimentalPlatformVisitRecording.RecordAsync(source(), RecordingPath, token, status =>
            {
                Status = $"NO-PAY DIAGNOSTIC · {status.Stage}: {status.Reason} Recorded milestones: {status.Events.Count} / 3. Current deck presence: {(status.CurrentDeckPermission ? "confirmed" : "not confirmed")}.";
                Notify();
            });
        }
        catch (Exception error) { Status = "Diagnostic stopped: " + error.Message + " Existing recording preserved; no career or payload state changed."; }
        finally { Running = false; Notify(); }
    }
    public async Task ShutdownAsync() { cancellation?.Cancel(); await task; cancellation?.Dispose(); }
    private void Notify()
    {
        void Apply() { PropertyChanged?.Invoke(this, new("")); StartCommand.Refresh(); StopCommand.Refresh(); }
        if (context is not null && SynchronizationContext.Current != context) context.Post(_ => Apply(), null); else Apply();
    }
}
