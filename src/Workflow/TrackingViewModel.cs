using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public enum TrackingState { Disconnected, Connecting, Connected, Error }

public sealed class TrackingViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAircraftTelemetrySource source;
    private readonly Func<string> sdkRoot;
    private readonly bool enabled;
    private CancellationTokenSource? cancellation;
    private Task? run;
    private TrackingState state;
    private AircraftTelemetry? current;
    private string error = "";

    public TrackingViewModel(IAircraftTelemetrySource source, Func<string> sdkRoot, bool enabled = true)
    {
        this.source = source;
        this.sdkRoot = sdkRoot;
        this.enabled = enabled;
        ConnectCommand = new(() => _ = ConnectAsync(), () => enabled && state is (TrackingState.Disconnected or TrackingState.Error));
        DisconnectCommand = new(Disconnect, () => state is TrackingState.Connecting or TrackingState.Connected);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<AircraftTelemetry?>? TelemetryChanged;
    public ActionCommand ConnectCommand { get; }
    public ActionCommand DisconnectCommand { get; }
    public TrackingState State => state;
    public AircraftTelemetry? Current => current;
    public bool HasTelemetry => current is not null;
    public string Status => !enabled ? "Disabled in Airport Builder dry-run mode" : state switch
    {
        TrackingState.Disconnected => "Disconnected",
        TrackingState.Connecting => "Connecting to MSFS…",
        TrackingState.Connected => "Connected · live aircraft telemetry",
        _ => "Disconnected · connection error"
    };
    public string Error => error;
    public string Aircraft => current?.AircraftTitle ?? "—";
    public string AircraftType => string.IsNullOrWhiteSpace(current?.AircraftType) ? "—" : current.AircraftType;
    public string Position => current is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{current.Latitude:F5}, {current.Longitude:F5}");
    public string Altitude => current is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{current.AltitudeFeet:N0} ft");
    public string Heading => current is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{current.TrueHeadingDegrees:000}° true");
    public string GroundSpeed => current is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{current.GroundSpeedKnots:N1} kt");
    public string FlightState => current is null ? "—" : current.IsOnGround ? "On Ground" : "Airborne";
    public string LastUpdate => current is null ? "—" : current.ObservedUtc.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);

    public async Task ConnectAsync()
    {
        if (!ConnectCommand.CanExecute(null)) return;
        cancellation = new CancellationTokenSource();
        var operation = cancellation;
        error = ""; current = null; SetState(TrackingState.Connecting); TelemetryChanged?.Invoke(null);
        try
        {
            var context = SynchronizationContext.Current;
            var progress = new CallbackProgress<AircraftTelemetry>(sample =>
            {
                void Apply()
                {
                    if (cancellation != operation || operation.IsCancellationRequested) return;
                    try { current = sample.EnsureValid(); }
                    catch (Exception ex)
                    {
                        error = ex.Message; current = null; SetState(TrackingState.Error);
                        TelemetryChanged?.Invoke(null); operation.Cancel(); return;
                    }
                    if (state != TrackingState.Connected) SetState(TrackingState.Connected); else NotifyTelemetry();
                    TelemetryChanged?.Invoke(current);
                }
                if (context is null || ReferenceEquals(context, SynchronizationContext.Current)) Apply();
                else context.Post(_ => Apply(), null);
            });
            run = source.RunAsync(sdkRoot(), progress, operation.Token);
            await run;
            if (!operation.IsCancellationRequested) throw new IOException("The aircraft telemetry service stopped unexpectedly.");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (cancellation == operation)
            {
                error = ex.Message; current = null; SetState(TrackingState.Error); TelemetryChanged?.Invoke(null);
            }
        }
        finally
        {
            if (cancellation == operation)
            {
                operation.Dispose(); cancellation = null; run = null;
                if (state != TrackingState.Error) { current = null; SetState(TrackingState.Disconnected); TelemetryChanged?.Invoke(null); }
            }
        }
    }

    public void Disconnect()
    {
        if (cancellation is null) return;
        cancellation.Cancel();
        current = null; error = ""; SetState(TrackingState.Disconnected); TelemetryChanged?.Invoke(null);
    }

    private void SetState(TrackingState value) { state = value; NotifyTelemetry(); ConnectCommand.Refresh(); DisconnectCommand.Refresh(); }
    private void NotifyTelemetry()
    {
        foreach (var property in new[] { nameof(State), nameof(Current), nameof(HasTelemetry), nameof(Status), nameof(Error), nameof(Aircraft), nameof(AircraftType), nameof(Position), nameof(Altitude), nameof(Heading), nameof(GroundSpeed), nameof(FlightState), nameof(LastUpdate) }) Changed(property);
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose() { Disconnect(); }
    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
