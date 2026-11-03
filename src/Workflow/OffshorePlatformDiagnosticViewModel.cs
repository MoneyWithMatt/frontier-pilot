using System.ComponentModel;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>Explicit read-only PR9 diagnostic UI, isolated from career mission service and ledger.</summary>
public sealed class OffshorePlatformDiagnosticViewModel : INotifyPropertyChanged
{
    private readonly Func<IAircraftConditionSource> source;
    private readonly string directory;
    private readonly Func<IAircraftConditionSource>? roundTripSource;
    private readonly Func<PassengerEndpoint?>? baseAirport;
    private readonly Func<DateTimeOffset>? clock;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private CancellationTokenSource? cancellation;
    private Task task = Task.CompletedTask;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Running { get; private set; }
    public string Status { get; private set; } = "Experimental H125 contact cycle only. No career reward or normal flown evidence.";
    public string RecordingPath { get; private set; } = "";
    public ActionCommand StartCommand { get; }
    public ActionCommand StopCommand { get; }
    public ActionCommand RoundTripCommand { get; }
    public string RoundTripRequirements => baseAirport?.Invoke() is {} b
        ? $"Experimental route: {b.Name} → OL400 H01 → {b.Name}. 20 s deck contact, then 60 s visit, genuine departure and return. No pay or payload writes."
        : "Resolve the Offshore employer base airport before the experimental round-trip recorder is available.";
    public OffshorePlatformDiagnosticViewModel(Func<IAircraftConditionSource> source, string directory,
        Func<IAircraftConditionSource>? roundTripSource = null, Func<PassengerEndpoint?>? baseAirport = null, Func<DateTimeOffset>? clock = null)
    {
        this.source = source; this.directory = directory;
        this.roundTripSource = roundTripSource; this.baseAirport = baseAirport;
        this.clock = clock;
        StartCommand = new(() => _ = StartAsync(), () => !Running);
        StopCommand = new(() => cancellation?.Cancel(), () => Running);
        RoundTripCommand = new(() => _ = StartRoundTripAsync(), () => !Running && roundTripSource is not null && baseAirport?.Invoke() is not null);
    }
    public Task StartRoundTripAsync()
    {
        if (!RoundTripCommand.CanExecute(null)) return Task.CompletedTask;
        Running = true; cancellation = new(TimeSpan.FromMinutes(45));
        task = RecordRoundTripAsync(cancellation.Token); Notify(); return Task.CompletedTask;
    }
    private async Task RecordRoundTripAsync(CancellationToken token)
    {
        try
        {
            var airport = baseAirport?.Invoke() ?? throw new InvalidOperationException("Resolved base is unavailable.");
            Directory.CreateDirectory(directory);
            RecordingPath = Path.Combine(directory, "ol400-roundtrip-" + Guid.NewGuid().ToString("N") + ".jsonl");
            await ExperimentalOffshoreRoundTripRecording.RecordAsync(roundTripSource!(), airport,
                OffshorePlatformRegistry.Find("ol400-h01"), RecordingPath, token, status =>
                {
                    Status = $"NO-PAY ROUND TRIP · {status.Stage}: {status.Reason} Visit: {status.InspectionSeconds:0} / 60 s. Milestones: {status.Events.Count} / 6. {status.Authority}";
                    Notify();
                }, clock: clock);
        }
        catch (Exception error) { Status = "Round-trip diagnostic stopped: " + error.Message + " Existing recording preserved; no career or payload state changed."; }
        finally { Running = false; Notify(); }
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
        void Apply() { PropertyChanged?.Invoke(this, new("")); StartCommand.Refresh(); StopCommand.Refresh(); RoundTripCommand.Refresh(); }
        if (context is not null && SynchronizationContext.Current != context) context.Post(_ => Apply(), null); else Apply();
    }
}
