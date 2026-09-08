using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public enum TrackingState { Disconnected, Connecting, Connected, Error }

public sealed class TrackingViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ITrackingTelemetrySource source;
    private readonly IAircraftStateStore store;
    private readonly Func<string> sdkRoot;
    private readonly bool enabled;
    private readonly TimeSpan persistenceInterval;
    private readonly SemaphoreSlim persistenceGate = new(1, 1);
    private readonly SynchronizationContext? notificationContext;
    private CancellationTokenSource? cancellation;
    private Task? run;
    private TrackingState state;
    private TrackingWorldState? world;
    private AircraftTelemetry? lastKnown, latestValid;
    private AircraftLocationContext? lastKnownLocation,latestLocation;
    private AircraftLocationContext? lastPersistedLocation;
    private AircraftLocationRecognition? recognition;
    private AirstripDefinition[] airports=[];
    private readonly AirportLocationRecognizer locationRecognizer=new();
    private DateTimeOffset lastPersistedObservation;
    private string error = "", persistenceWarning = "";
    private readonly List<string> diagnostic = [];
    private PlayerControlMode lastLoggedMode = (PlayerControlMode)(-1);
    private DateTimeOffset lastSampleLog;
    private bool initialized, disposed,airportCatalogueLoaded;

    public TrackingViewModel(ITrackingTelemetrySource source, IAircraftStateStore store, Func<string> sdkRoot,
        bool enabled = true, TimeSpan? persistenceInterval = null)
    {
        this.source = source; this.store = store; this.sdkRoot = sdkRoot; this.enabled = enabled; notificationContext = SynchronizationContext.Current;
        this.persistenceInterval = persistenceInterval ?? TimeSpan.FromSeconds(10);
        ConnectCommand = new(() => _ = ConnectAsync(), () => enabled && state is (TrackingState.Disconnected or TrackingState.Error));
        DisconnectCommand = new(Disconnect, () => state is TrackingState.Connecting or TrackingState.Connected);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<TrackingWorldState?>? TelemetryChanged;
    public event Action<AircraftTelemetry?, PlayerTelemetry?, bool>? DisplayChanged;
    public ActionCommand ConnectCommand { get; }
    public ActionCommand DisconnectCommand { get; }
    public TrackingState State => state;
    public TrackingWorldState? World => world;
    public AircraftTelemetry? Current => world?.Aircraft ?? lastKnown;
    public AircraftLocationContext? CurrentLocation => world is not null ? latestLocation : lastKnownLocation;
    public bool IsLive => world is not null;
    public bool HasTelemetry => Current is not null;
    public bool HasPlayer => world?.Player is not null;
    public bool CanClearSavedState => state is not (TrackingState.Connecting or TrackingState.Connected) && (lastKnown is not null || persistenceWarning != "");
    public string StoragePath => store.StoragePath;
    public string Status => !enabled ? "Disabled in Airport Builder dry-run mode" : state switch
    {
        TrackingState.Connected => "LIVE · connected to MSFS",
        TrackingState.Connecting when lastKnown is not null => "Connecting · displaying LAST KNOWN",
        TrackingState.Connecting => "Connecting to MSFS…",
        TrackingState.Error when lastKnown is not null => "LAST KNOWN · MSFS connection unavailable",
        TrackingState.Error => "Disconnected · connection error · no saved position",
        _ when lastKnown is not null => "LAST KNOWN · MSFS disconnected",
        _ => "Disconnected · no saved aircraft position"
    };
    public string Freshness => world is not null ? "LIVE" : lastKnown is not null
        ? $"LAST KNOWN — {lastKnown.ObservedUtc.ToLocalTime():d MMM yyyy, HH:mm}"
        : "NO SAVED POSITION";
    public string Error => error;
    public string PersistenceWarning => persistenceWarning;
    public string Aircraft => Current?.AircraftTitle ?? "—";
    public string AircraftType => string.IsNullOrWhiteSpace(Current?.AircraftType) ? "—" : Current.AircraftType;
    public string Position => Current is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{Current.Latitude:F5}, {Current.Longitude:F5}");
    public string Altitude => Current is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{Current.AltitudeFeet:N0} ft");
    public string Heading => Current is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{Current.TrueHeadingDegrees:000}° true");
    public string GroundSpeed => Current is null ? "—" : string.Create(CultureInfo.InvariantCulture, $"{Current.GroundSpeedKnots:N1} kt");
    public string FlightState => Current is null ? "—" : Current.IsOnGround ? "On Ground" : "Airborne";
    public string FrontierAirport => CurrentLocation is { } location ? $"{location.AirportName} ({location.AirportIdentifier})" :
        Current is null ? "—" : IsLive ? "Outside Frontier Pilot Airport" : "No saved Frontier airport association";
    public string FrontierStand => CurrentLocation?.ParkingStandNumber is { } number ? $"Stand {number:00} · {CurrentLocation.ParkingStandName}" : "None";
    public string FrontierStandDistance => CurrentLocation?.ParkingStandDistanceMeters is { } distance ? string.Create(CultureInfo.InvariantCulture,$"{distance:N1} m") : "—";
    public string FrontierParkingState => CurrentLocation?.ParkingState switch
    {
        AircraftParkingState.NearStand => "Near Stand",
        AircraftParkingState.Parked => "Parked",
        _ => "Not at Stand"
    };
    public string FrontierLocationDebug => IsLive ? recognition?.DebugReason ?? "No saved Frontier airports are available." :
        CurrentLocation is { } location ? "Last-known recognition: "+location.Reason : "No persisted airport/stand recognition.";
    public string PlayerState => world?.PlayerMode switch { PlayerControlMode.InAircraft => "In Aircraft", PlayerControlMode.OnFoot => "On Foot", PlayerControlMode.Unknown => "Unknown · position refused", _ => "—" };
    public string PlayerPosition => world?.Player is { } p ? string.Create(CultureInfo.InvariantCulture, $"{p.Latitude:F5}, {p.Longitude:F5}") : "—";
    public string PlayerAltitude => world?.Player is { } p ? string.Create(CultureInfo.InvariantCulture, $"{p.AltitudeFeet:N0} ft") : "—";
    public string PlayerHeading => world?.Player is { } p ? string.Create(CultureInfo.InvariantCulture, $"{p.TrueHeadingDegrees:000}° true") : "—";
    public string PlayerDistance => world?.DistanceFromAircraftMeters is { } d ? d < 1000 ? string.Create(CultureInfo.InvariantCulture, $"{d:N1} m") : string.Create(CultureInfo.InvariantCulture, $"{d / 1000:N2} km") : "—";
    public string PlayerTrackingStatus => world?.PlayerTrackingStatus ?? "—";
    public string IntegrationDetails => world?.IntegrationDetails ?? "—";
    public string DiagnosticLog { get { lock (diagnostic) return string.Join(Environment.NewLine, diagnostic); } }
    public string LastUpdate => Current is null ? "—" : Current.ObservedUtc.ToLocalTime().ToString("G", CultureInfo.CurrentCulture);

    public async Task InitializeAsync()
    {
        if (initialized || disposed) return;
        initialized = true;
        try
        {
            var saved = await store.LoadActiveAsync();
            if (saved is not null && world is null && latestValid is null)
            {
                var valid=saved.EnsureValid();
                lastKnown = valid.Telemetry;lastKnownLocation=lastPersistedLocation=valid.Location;
                lastPersistedObservation = lastKnown.ObservedUtc;
                ReconcileSavedAssociation();
                AppendDiagnostic($"Loaded last-known aircraft from {store.StoragePath}.");
                NotifyTelemetry(); NotifyDisplay();
            }
        }
        catch (Exception ex)
        {
            persistenceWarning = ex.Message;
            AppendDiagnostic("Could not load saved aircraft state: " + ex.Message);
            NotifyTelemetry();
        }
    }

    public async Task ConnectAsync()
    {
        if (!ConnectCommand.CanExecute(null)) return;
        await InitializeAsync();
        cancellation = new CancellationTokenSource();
        var operation = cancellation;
        error = ""; world = null; lastLoggedMode = (PlayerControlMode)(-1); lastSampleLog = default;
        AppendDiagnostic("Connecting."); SetState(TrackingState.Connecting); TelemetryChanged?.Invoke(null); NotifyDisplay();
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
                        TelemetryChanged?.Invoke(null); NotifyDisplay(); operation.Cancel(); return;
                    }
                    latestValid = lastKnown = world.Aircraft;
                    recognition=locationRecognizer.Evaluate(world.Aircraft,airports);
                    latestLocation=lastKnownLocation=recognition.Context;
                    _ = PersistLatestAsync(false);
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
                    TelemetryChanged?.Invoke(world); NotifyDisplay();
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
                error = ex.Message; world = null; AppendDiagnostic("Connection ended: " + ex.Message); SetState(TrackingState.Error);
                TelemetryChanged?.Invoke(null); NotifyDisplay();
            }
        }
        finally
        {
            await PersistLatestAsync(true);
            if (cancellation == operation)
            {
                operation.Dispose(); cancellation = null; run = null;
                if (state != TrackingState.Error) { world = null; SetState(TrackingState.Disconnected); TelemetryChanged?.Invoke(null); NotifyDisplay(); }
            }
        }
    }

    public void Disconnect()
    {
        if (cancellation is null) return;
        cancellation.Cancel(); world = null; error = ""; AppendDiagnostic("Disconnected; retaining latest valid aircraft position.");
        SetState(TrackingState.Disconnected); TelemetryChanged?.Invoke(null); NotifyDisplay(); _ = PersistLatestAsync(true);
    }

    public async Task ClearSavedStateAsync()
    {
        if (!CanClearSavedState) throw new InvalidOperationException("Disconnect Tracking before clearing saved aircraft state.");
        await store.ClearAsync();
        lastKnown = latestValid = null;lastKnownLocation=latestLocation=lastPersistedLocation=null;recognition=null; lastPersistedObservation = default; persistenceWarning = "";
        AppendDiagnostic("Cleared saved aircraft state."); NotifyTelemetry(); NotifyDisplay();
    }

    private async Task PersistLatestAsync(bool force)
    {
        var candidate = latestValid;
        var candidateLocation=latestLocation?.ObservedUtc==candidate?.ObservedUtc?latestLocation:null;
        if (candidate is null || candidate.ObservedUtc < lastPersistedObservation ||
            candidate.ObservedUtc==lastPersistedObservation&&Equals(candidateLocation,lastPersistedLocation) ||
            !force&&candidate.ObservedUtc>lastPersistedObservation&&candidate.ObservedUtc-lastPersistedObservation<persistenceInterval) return;
        await persistenceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            candidate = latestValid;
            candidateLocation=latestLocation?.ObservedUtc==candidate?.ObservedUtc?latestLocation:null;
            if (candidate is null || candidate.ObservedUtc < lastPersistedObservation ||
                candidate.ObservedUtc==lastPersistedObservation&&Equals(candidateLocation,lastPersistedLocation) ||
                !force&&candidate.ObservedUtc>lastPersistedObservation&&candidate.ObservedUtc-lastPersistedObservation<persistenceInterval) return;
            await store.SaveActiveAsync(candidate,candidateLocation).ConfigureAwait(false);
            lastPersistedObservation = candidate.ObservedUtc;lastPersistedLocation=candidateLocation; persistenceWarning = "";
        }
        catch (Exception ex)
        {
            persistenceWarning = "Could not save last-known aircraft state: " + ex.Message;
            AppendDiagnosticThreadSafe(persistenceWarning);
        }
        finally { persistenceGate.Release(); NotifyOnContext(); }
    }

    private void NotifyDisplay() => DisplayChanged?.Invoke(Current, world?.Player, IsLive);
    public void UpdateAirports(IEnumerable<AirstripDefinition> definitions)
    {
        airports=definitions?.Where(definition=>definition!=null).ToArray()??[];airportCatalogueLoaded=true;
        if(world is not null)
        {
            recognition=locationRecognizer.Evaluate(world.Aircraft,airports);
            latestLocation=lastKnownLocation=recognition.Context;
            _=PersistLatestAsync(true);
        }
        else ReconcileSavedAssociation();
        AppendDiagnostic($"Frontier airport recognition catalogue: {airports.Length} airport(s).");
        NotifyTelemetry();
    }
    private void ReconcileSavedAssociation()
    {
        if(!airportCatalogueLoaded||lastKnownLocation is null)return;
        var airport=airports.FirstOrDefault(candidate=>candidate.Identifier==lastKnownLocation.AirportIdentifier);
        var standExists=lastKnownLocation.ParkingStandNumber is not {} stand || airport?.ParkingStands.Any(candidate=>candidate.Number==stand)==true;
        if(airport is not null&&standExists)return;
        lastKnownLocation=latestLocation=null;
        if(lastKnown is not null){latestValid=lastKnown;_=PersistLatestAsync(true);}
        AppendDiagnostic("Cleared a last-known Frontier location association because its saved airport or stand no longer exists.");
    }
    private void SetState(TrackingState value) { state = value; NotifyTelemetry(); ConnectCommand.Refresh(); DisconnectCommand.Refresh(); }
    private void NotifyTelemetry()
    {
        foreach (var property in new[] { nameof(State), nameof(World), nameof(Current), nameof(CurrentLocation), nameof(IsLive), nameof(HasTelemetry), nameof(HasPlayer), nameof(CanClearSavedState), nameof(StoragePath), nameof(Status), nameof(Freshness), nameof(Error), nameof(PersistenceWarning), nameof(Aircraft), nameof(AircraftType), nameof(Position), nameof(Altitude), nameof(Heading), nameof(GroundSpeed), nameof(FlightState), nameof(FrontierAirport), nameof(FrontierStand), nameof(FrontierStandDistance), nameof(FrontierParkingState), nameof(FrontierLocationDebug), nameof(PlayerState), nameof(PlayerPosition), nameof(PlayerAltitude), nameof(PlayerHeading), nameof(PlayerDistance), nameof(PlayerTrackingStatus), nameof(IntegrationDetails), nameof(DiagnosticLog), nameof(LastUpdate) }) Changed(property);
    }
    private void AppendDiagnostic(string message) { lock (diagnostic) diagnostic.Add($"{DateTimeOffset.Now:O} {message}"); Changed(nameof(DiagnosticLog)); }
    private void AppendDiagnosticThreadSafe(string message)
    {
        lock (diagnostic) diagnostic.Add($"{DateTimeOffset.Now:O} {message}");
        if (notificationContext is not null && !ReferenceEquals(notificationContext,SynchronizationContext.Current)) notificationContext.Post(_=>Changed(nameof(DiagnosticLog)),null);
        else Changed(nameof(DiagnosticLog));
    }
    private void NotifyOnContext()
    {
        if (notificationContext is not null && !ReferenceEquals(notificationContext,SynchronizationContext.Current)) notificationContext.Post(_=>NotifyTelemetry(),null);
        else NotifyTelemetry();
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose()
    {
        if (disposed) return;
        Disconnect();
        try { PersistLatestAsync(true).GetAwaiter().GetResult(); } catch { }
        disposed = true;
    }
    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
