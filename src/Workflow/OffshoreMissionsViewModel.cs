using System.ComponentModel;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed class OffshoreMissionsViewModel : INotifyPropertyChanged
{
    private readonly OffshoreMissionsService service;
    private readonly Func<IAircraftConditionSource> source;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private CancellationTokenSource? cancellation;
    private Task recording = Task.CompletedTask;
    private bool busy;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Enabled => service.Enabled;
    public bool Connected { get; private set; }
    public string Error { get; private set; } = "";
    public string Status => Selected?.State == OffshoreMissionState.Completed ?
        "Mission completed. Verified airborne wages settled once; this assignment is closed." :
        Selected?.State == OffshoreMissionState.Offered && !service.CanAccept ?
        "Select the employer Vision Jet in Aircraft → Fleet before accepting this mission." : service.Reason;
    public IReadOnlyList<OffshoreMission> Missions => service.Missions;
    public IReadOnlyList<OffshorePlatform> Platforms => OffshorePlatformRegistry.Platforms;
    public OffshoreMission? Selected { get => service.Selected; set { if (value is not null) { service.SelectedId = value.Id; Notify(); } } }
    public string WagePolicy => $"{DollarDisplay.Format(service.HourlyRate)} / verified airborne hour · one flight assignment at a time · no per-job reward";
    public string AreaStatus => Selected is {} m ? $"{m.Area.Name} · {m.Area.RadiusNauticalMiles:0.#} NM radius · {m.Area.ObservationSeconds / 60d:0.#} minutes continuously airborne" : "Select an observation mission.";
    public string AreaCoordinates => Selected is {} m ? FormattableString.Invariant($"Area centre: {m.Area.Latitude:0.000000}, {m.Area.Longitude:0.000000} · WGS84 decimal degrees") : "";
    public string AreaDistance => double.IsNaN(service.AreaDistance) ? "Connect tracking for operating-area distance." :
        $"{service.AreaDistance:0.0} NM from the area centre · {(double.IsNaN(service.AreaBearing) ? "bearing unavailable" : $"{service.AreaBearing:000}° true to centre")} · {(service.AreaDistance <= Selected!.Area.RadiusNauticalMiles ? "inside" : "outside")} the observation area";
    public string Progress => Selected?.ObservationDisplay ?? "No assignment selected.";
    public string FlightTime => Selected is {} m ? $"Verified airborne time: {TimeSpan.FromTicks(m.AirborneTicks).TotalMinutes:0.0} minutes" : "";
    public string Estimate => Selected is {} m ? $"Planning estimate: about {Math.Ceiling(m.Area.ObservationSeconds / 60d + 10 + 2 * WorldTracking.DistanceMeters(m.Base.Latitude, m.Base.Longitude, m.Area.Latitude, m.Area.Longitude) / 1852 / 120 * 60):0} minutes total. Pilot assesses fuel, weather and performance." : "";
    public ActionCommand RefreshCommand { get; } = null!;
    public ActionCommand AcceptCommand { get; } = null!;
    public ActionCommand StartCommand { get; } = null!;
    public ActionCommand ResumeCommand { get; } = null!;
    public ActionCommand CompleteCommand { get; } = null!;
    public ActionCommand ConnectCommand { get; } = null!;
    public ActionCommand DisconnectCommand { get; } = null!;

    public OffshoreMissionsViewModel(OffshoreMissionsService service, Func<IAircraftConditionSource> source)
    {
        this.service = service; this.source = source;
        RefreshCommand = new(() => _ = RefreshAsync(), () => !busy && Enabled);
        AcceptCommand = new(() => _ = Run(() => service.AcceptAsync()), () => !busy && service.CanAccept);
        StartCommand = new(() => _ = Run(() => service.StartOrResumeAsync()), () => !busy && service.CanStart);
        ResumeCommand = new(() => _ = Run(() => service.StartOrResumeAsync()), () => !busy && service.CanResume);
        CompleteCommand = new(() => _ = Run(() => service.CompleteAsync()), () => !busy && service.CanComplete);
        ConnectCommand = new(() => _ = ConnectAsync(), () => !Connected && !busy && Enabled);
        DisconnectCommand = new(() => cancellation?.Cancel(), () => Connected);
    }
    public Task RefreshAsync() => Run(() => service.RefreshAsync());
    public async Task ConnectAsync()
    {
        if (Connected || !Enabled) return;
        await RefreshAsync();
        Connected = true; Error = ""; cancellation = new(); Notify();
        recording = ReadAsync(cancellation.Token);
    }
    private async Task ReadAsync(CancellationToken token)
    {
        try { await foreach (var frame in source().ReadAsync(token).WithCancellation(token)) { await service.ObserveAsync(frame, token); Notify(); } }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Error = "Mission tracking stopped: " + error.Message; }
        finally
        {
            try { await service.DisconnectAsync(); } catch (Exception error) { Error = "Recovery state could not be saved; existing file preserved: " + error.Message; }
            Connected = false; Notify();
        }
    }
    public async Task ShutdownAsync() { cancellation?.Cancel(); await recording; cancellation?.Dispose(); }
    private async Task Run(Func<Task> action)
    {
        if (busy) return; busy = true; Error = ""; Notify();
        try { await action(); } catch (Exception error) { Error = error.Message; }
        finally { busy = false; Notify(); }
    }
    private void Notify()
    {
        void Apply()
        {
            PropertyChanged?.Invoke(this, new("")); RefreshCommand?.Refresh(); AcceptCommand?.Refresh(); StartCommand?.Refresh();
            ResumeCommand?.Refresh(); CompleteCommand?.Refresh(); ConnectCommand?.Refresh(); DisconnectCommand?.Refresh();
        }
        if (context is not null && SynchronizationContext.Current != context) context.Post(_ => Apply(), null); else Apply();
    }
}
