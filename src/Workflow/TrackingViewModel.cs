using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public enum TrackingState { Disconnected, Connecting, Connected, Error }

public sealed class TrackingViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ITrackingTelemetrySource source;
    private readonly Func<string> sdkRoot;
    private readonly bool enabled;
    private CancellationTokenSource? cancellation;
    private Task? run;
    private TrackingState state;
    private TrackingWorldState? world;
    private string error = "";
    private readonly List<string> diagnostic = [];
    private PlayerControlMode lastLoggedMode = (PlayerControlMode)(-1);
    private DateTimeOffset lastSampleLog;

    public TrackingViewModel(ITrackingTelemetrySource source, Func<string> sdkRoot, bool enabled = true)
    {
        this.source = source;
        this.sdkRoot = sdkRoot;
        this.enabled = enabled;
        ConnectCommand = new(() => _ = ConnectAsync(), () => enabled && state is (TrackingState.Disconnected or TrackingState.Error));
        DisconnectCommand = new(Disconnect, () => state is TrackingState.Connecting or TrackingState.Connected);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<TrackingWorldState?>? TelemetryChanged;
    public ActionCommand ConnectCommand { get; }
    public ActionCommand DisconnectCommand { get; }
    public TrackingState State => state;
    public TrackingWorldState? World => world;
    public AircraftTelemetry? Current => world?.Aircraft;
    public bool HasTelemetry => world is not null;
    public bool HasPlayer => world?.Player is not null;
    public string Status => !enabled ? "Disabled in Airport Builder dry-run mode" : state switch
    {
        TrackingState.Disconnected => "Disconnected",
        TrackingState.Connecting => "Connecting to MSFS…",
        TrackingState.Connected => "Connected · live aircraft and player-state telemetry",
        _ => "Disconnected · connection error"
    };
    public string Error => error;
    public string Aircraft => Current?.AircraftTitle ?? "—";
    public string AircraftType => string.IsNullOrWhiteSpace(Current?.AircraftType) ? "—" : Current.AircraftType;
    public string Position => Current is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{Current.Latitude:F5}, {Current.Longitude:F5}");
    public string Altitude => Current is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{Current.AltitudeFeet:N0} ft");
    public string Heading => Current is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{Current.TrueHeadingDegrees:000}° true");
    public string GroundSpeed => Current is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{Current.GroundSpeedKnots:N1} kt");
    public string FlightState => Current is null ? "—" : Current.IsOnGround ? "On Ground" : "Airborne";
    public string PlayerState => world?.PlayerMode switch { PlayerControlMode.InAircraft => "In Aircraft", PlayerControlMode.OnFoot => "On Foot", PlayerControlMode.Unknown => "Unknown · position refused", _ => "—" };
    public string PlayerPosition => world?.Player is { } p ? string.Create(CultureInfo.InvariantCulture, $"{p.Latitude:F5}, {p.Longitude:F5}") : "—";
    public string PlayerAltitude => world?.Player is { } p ? string.Create(CultureInfo.InvariantCulture, $"{p.AltitudeFeet:N0} ft") : "—";
    public string PlayerHeading => world?.Player is { } p ? string.Create(CultureInfo.InvariantCulture, $"{p.TrueHeadingDegrees:000}° true") : "—";
    public string PlayerDistance => world?.DistanceFromAircraftMeters is { } d ? d < 1000 ? string.Create(CultureInfo.InvariantCulture, $"{d:N1} m") : string.Create(CultureInfo.InvariantCulture, $"{d / 1000:N2} km") : "—";
    public string PlayerTrackingStatus => world?.PlayerTrackingStatus ?? "—";
    public string IntegrationDetails => world?.IntegrationDetails ?? "—";
    public string DiagnosticLog => string.Join(Environment.NewLine, diagnostic);
    public string LastUpdate => Current is null ? "—" : Current.ObservedUtc.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);

    public async Task ConnectAsync()
    {
        if (!ConnectCommand.CanExecute(null)) return;
        cancellation = new CancellationTokenSource();
        var operation = cancellation;
        error = ""; world = null; diagnostic.Clear(); lastLoggedMode = (PlayerControlMode)(-1); lastSampleLog = default; AppendDiagnostic("Connecting."); SetState(TrackingState.Connecting); TelemetryChanged?.Invoke(null);
        try
        {
            var context = SynchronizationContext.Current;
            var progress = new CallbackProgress<TrackingWorldState>(sample =>
            {
                void Apply()
                {
                    if (cancellation != operation || operation.IsCancellationRequested) return;
                    try { world = sample.EnsureValid(); }
                    catch (Exception ex)
                    {
                        error = ex.Message; world = null; AppendDiagnostic("Rejected telemetry: " + ex.Message); SetState(TrackingState.Error);
                        TelemetryChanged?.Invoke(null); operation.Cancel(); return;
                    }
                    if (state != TrackingState.Connected) SetState(TrackingState.Connected); else NotifyTelemetry();
                    if (world.PlayerMode != lastLoggedMode)
                    {
                        lastLoggedMode = world.PlayerMode;
                        AppendDiagnostic($"Mode={world.PlayerMode}; aircraftObject={world.AircraftObjectId}; avatarObject={world.AvatarObjectId?.ToString() ?? "none"}; currentObject={world.CurrentObjectId}; {world.PlayerTrackingStatus}; {world.IntegrationDetails}");
                    }
                    if (lastSampleLog == default || world.Aircraft.ObservedUtc - lastSampleLog >= TimeSpan.FromSeconds(2))
                    {
                        lastSampleLog = world.Aircraft.ObservedUtc;
                        var player = world.Player is { } p ? string.Create(CultureInfo.InvariantCulture, $"{p.Latitude:F7},{p.Longitude:F7},{p.AltitudeFeet:F1}ft,{p.TrueHeadingDegrees:F1}deg") : "refused/none";
                        AppendDiagnostic(string.Create(CultureInfo.InvariantCulture, $"Sample mode={world.PlayerMode}; aircraft={world.Aircraft.Latitude:F7},{world.Aircraft.Longitude:F7},{world.Aircraft.AltitudeFeet:F1}ft,{world.Aircraft.TrueHeadingDegrees:F1}deg; player={player}"));
                    }
                    TelemetryChanged?.Invoke(world);
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
                error = ex.Message; world = null; AppendDiagnostic("Connection ended: " + ex.Message); SetState(TrackingState.Error); TelemetryChanged?.Invoke(null);
            }
        }
        finally
        {
            if (cancellation == operation)
            {
                operation.Dispose(); cancellation = null; run = null;
                if (state != TrackingState.Error) { world = null; SetState(TrackingState.Disconnected); TelemetryChanged?.Invoke(null); }
            }
        }
    }

    public void Disconnect()
    {
        if (cancellation is null) return;
        cancellation.Cancel();
        world = null; error = ""; AppendDiagnostic("Disconnected."); SetState(TrackingState.Disconnected); TelemetryChanged?.Invoke(null);
    }

    private void SetState(TrackingState value) { state = value; NotifyTelemetry(); ConnectCommand.Refresh(); DisconnectCommand.Refresh(); }
    private void NotifyTelemetry()
    {
        foreach (var property in new[] { nameof(State), nameof(World), nameof(Current), nameof(HasTelemetry), nameof(HasPlayer), nameof(Status), nameof(Error), nameof(Aircraft), nameof(AircraftType), nameof(Position), nameof(Altitude), nameof(Heading), nameof(GroundSpeed), nameof(FlightState), nameof(PlayerState), nameof(PlayerPosition), nameof(PlayerAltitude), nameof(PlayerHeading), nameof(PlayerDistance), nameof(PlayerTrackingStatus), nameof(IntegrationDetails), nameof(DiagnosticLog), nameof(LastUpdate) }) Changed(property);
    }
    private void AppendDiagnostic(string message) { diagnostic.Add($"{DateTimeOffset.Now:O} {message}"); Changed(nameof(DiagnosticLog)); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose() { Disconnect(); }
    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
