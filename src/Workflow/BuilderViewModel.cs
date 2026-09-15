using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public enum WorkflowState { Disconnected, ReadyToSurvey, SurveyCaptured, Validated, Built, Installed, Error, Cancelled }
public sealed record RunwaySurfaceChoice(string? PresetId, RunwaySurface PhysicalSurface, string DisplayName, string Detail)
{
    public override string ToString() => DisplayName;
}
public sealed class ActionCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed partial class BuilderViewModel : INotifyPropertyChanged
{
    private readonly IBuilderServices services;
    private CancellationTokenSource? cancellation;
    private bool busy, checkedReady, siteConfirmed;
    private string sdkRoot, airportName = "", identifier = "", length = "200", width = "20", status = "Check prerequisites to begin.", error = "", activity = "";
    private RunwaySurface surface = RunwaySurface.Dirt;
    private string? surfacePresetId = RunwaySurfacePresets.CompactedDirtId;
    public LayoutEditor Layout { get; }
    public ActionCommand AddParkingCommand { get; }
    public ActionCommand RemoveParkingCommand { get; }
    public ActionCommand UndoLayoutCommand { get; }
    public ActionCommand RedoLayoutCommand { get; }
    public ActionCommand RotateLeftCommand { get; }
    public ActionCommand RotateRightCommand { get; }
    public ActionCommand ApplyParkingCommand { get; }
    public ActionCommand AddObjectCommand { get; }
    public ActionCommand ApplyObjectCommand { get; }
    public TrackingViewModel Tracking { get; }
    public EmployedCargoJobsViewModel Jobs { get; }
    public AircraftLoadingViewModel AircraftLoading { get; }
    public ProspectingOperationsViewModel Operations { get; }
    public double LayoutRunwayHeading => Survey?.TrueHeadingDegrees ?? 0;
    public double LayoutRunwayLength => double.TryParse(length,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)&&n>0 ? n : 200;
    public double LayoutRunwayWidth => double.TryParse(width,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)&&n>0 ? n : 20;
    public bool CanLayout => !busy && Survey!=null;
    public SurveyPoint? Survey { get; private set; }
    public BuiltAirport? Built { get; private set; }
    public string InstalledPath { get; private set; } = "";
    public WorkflowState State { get; private set; } = WorkflowState.Disconnected;
    public WorkflowState RecoveryState { get; private set; } = WorkflowState.Disconnected;
    private WorkflowState EffectiveState => State is WorkflowState.Error or WorkflowState.Cancelled ? RecoveryState : State;
    public string Stage => State switch { WorkflowState.ReadyToSurvey => "Ready to Survey", WorkflowState.SurveyCaptured => "Survey Captured", _ => State.ToString() };
    public string RecoveryAction => State is not (WorkflowState.Error or WorkflowState.Cancelled) ? "" : RecoveryState switch {
        WorkflowState.Disconnected => "Check the prerequisites, then retry Check and connect.",
        WorkflowState.ReadyToSurvey => "Stand upright and still, then retry Capture survey.",
        WorkflowState.SurveyCaptured => "Correct the inputs or choose another identifier, then Validate.",
        WorkflowState.Validated => "Review the log, resolve the build problem, then retry Generate and compile.",
        WorkflowState.Built => "Resolve the destination or running-simulator problem, then retry Install airport.",
        _ => "Review the log before continuing."
    };
    public event PropertyChangedEventHandler? PropertyChanged;
    public BuilderViewModel(IBuilderServices services, string sdkRoot, ITrackingTelemetrySource? telemetrySource = null,
        IAircraftStateStore? aircraftStateStore = null, IEmployedCargoJobs? employedCargoJobs = null,
        IFlightAttemptTracker? flightAttemptTracker = null, IProspectingOperations? prospectingOperations = null)
    {
        this.services = services; this.sdkRoot = sdkRoot;
        aircraftStateStore ??= services.IsDryRun ? new MemoryAircraftStateStore() : new LocalAircraftStateStore();
        Tracking = new(telemetrySource ?? new FrontierAirportBuilder.MsfsSdk.LiveAircraftTelemetry(), aircraftStateStore, () => this.sdkRoot, !services.IsDryRun);
        IEconomyStateStore economyStore = services.IsDryRun ? new MemoryEconomyStateStore() : new LocalEconomyStateStore();
        employedCargoJobs ??= new EmployedCargoJobsService(economyStore);
        IFlightAttemptStore attemptStore = services.IsDryRun ? new MemoryFlightAttemptStore() : new LocalFlightAttemptStore();
        flightAttemptTracker ??= new FlightAttemptTracker(economyStore, attemptStore);
        Jobs = new(employedCargoJobs, flightAttemptTracker);
        IAircraftLoadingStore loadingStore = services.IsDryRun ? new MemoryAircraftLoadingStore() : new LocalAircraftLoadingStore();
        var loadingCoordinator = new AircraftLoadingCoordinator(loadingStore, new FrontierAirportBuilder.MsfsSdk.SimConnectAircraftLoading(),
            flightAttemptTracker, () => this.sdkRoot, () => Tracking.World, () => Jobs.ActiveJob);
        AircraftLoading = new(loadingCoordinator, () => { if (Tracking.ConnectCommand.CanExecute(null)) _ = Tracking.ConnectAsync(); });
        if (flightAttemptTracker is FlightAttemptTracker concreteFlightTracker) concreteFlightTracker.AircraftLoadingDiagnosticProvider = loadingCoordinator.CreateDiagnosticSnapshot;
        if (services is BuilderServices concrete) concrete.SetEconomyDependencyStore(economyStore);
        prospectingOperations ??= services is IAirportManagement management
            ? new ProspectingOperationsService(economyStore, management, new GuardedAvatarSitePresence(() => Tracking.World))
            : new UnavailableProspectingOperations();
        Operations = new(prospectingOperations);
        Tracking.TelemetryChanged += sample => { _ = Jobs.ObserveTelemetryAsync(sample?.Aircraft); _ = AircraftLoading.ObserveAsync(sample); };
        Layout = new(()=>CanLayout,()=>new(LayoutRunwayLength,LayoutRunwayWidth,LayoutRunwayHeading));
        Layout.Edited += (_,_)=>Invalidate();
        AddParkingCommand = new(()=>AddParkingAt(LocalCoordinates.FromRunway(LayoutRunwayWidth/2+25,Layout.Stands.Count*30,LayoutRunwayHeading)),()=>CanLayout&&!Layout.IsDrawingTaxiway&&Layout.Stands.Count<8);
        AddObjectCommand = new(()=>Layout.AddObject(LocalCoordinates.FromRunway(LayoutRunwayWidth/2+45,Layout.Objects.Count*40,LayoutRunwayHeading),LayoutRunwayHeading,SceneryAssets.PrototypeBaseLodgeId),()=>CanLayout&&!Layout.IsDrawingTaxiway&&Layout.Objects.Count<64);
        RemoveParkingCommand = new(Layout.Delete,()=>CanLayout&&(Layout.Selected!=null||Layout.SelectedTaxiway!=null||Layout.SelectedObject!=null));
        UndoLayoutCommand = new(Layout.Undo,()=>Layout.CanUndo);
        RedoLayoutCommand = new(Layout.Redo,()=>Layout.CanRedo);
        RotateLeftCommand = new(()=>Layout.Rotate(-15),()=>Layout.HasRotatableSelection);
        RotateRightCommand = new(()=>Layout.Rotate(15),()=>Layout.HasRotatableSelection);
        ApplyParkingCommand = new(Layout.Apply,()=>Layout.HasSelection);
        ApplyObjectCommand = new(Layout.ApplyObject,()=>Layout.HasObjectSelection);
        CheckCommand = new(() => _ = CheckAsync(), () => !IsBusy);
        CaptureCommand = new(() => _ = CaptureAsync(), () => !IsBusy && checkedReady);
        ValidateCommand = new(() => _ = ValidateAsync(), () => !IsBusy && Survey != null && ValidationMessage == "");
        BuildCommand = new(() => _ = BuildAsync(), () => !IsBusy && EffectiveState == WorkflowState.Validated && !Layout.HasPendingProperties);
        InstallCommand = new(() => _ = InstallAsync(), () => !IsBusy && EffectiveState == WorkflowState.Built && Built != null && !Layout.HasPendingProperties);
        CancelCommand = new(Cancel, () => IsBusy);
        OpenProjectCommand = new(() => Open(services.ProjectDirectory), () => !IsBusy && services.ProjectDirectory != "");
        OpenPackageCommand = new(() => Open(Built?.PackageDirectory ?? ""), () => !IsBusy && Built != null);
        OpenLogCommand = new(() => Open(services.LogPath), () => !IsBusy && services.LogPath != "");
        OpenEvidenceCommand = new(() => Open(services.EvidenceDirectory), () => !IsBusy && services.EvidenceDirectory != "");
        Layout.PropertyChanged += (_,_)=> { Changed(nameof(Layout)); Changed(nameof(ValidationMessage)); BuildCommand.Refresh(); InstallCommand.Refresh(); ValidateCommand.Refresh(); RefreshManagement(); foreach(var c in new[]{AddParkingCommand,AddObjectCommand,RemoveParkingCommand,UndoLayoutCommand,RedoLayoutCommand,RotateLeftCommand,RotateRightCommand,ApplyParkingCommand,ApplyObjectCommand})c.Refresh(); };
        InitializeManagement();
        Append(ModeDescription);
    }
    public ActionCommand CheckCommand { get; }
    public ActionCommand CaptureCommand { get; }
    public ActionCommand ValidateCommand { get; }
    public ActionCommand BuildCommand { get; }
    public ActionCommand InstallCommand { get; }
    public ActionCommand CancelCommand { get; }
    public ActionCommand OpenProjectCommand { get; }
    public ActionCommand OpenPackageCommand { get; }
    public ActionCommand OpenLogCommand { get; }
    public ActionCommand OpenEvidenceCommand { get; }
    public bool IsDryRun => services.IsDryRun;
    public string ModeDescription => IsDryRun ? "DRY RUN · recorded survey · simulated compiler · isolated test Community · no MSFS connection" : "LIVE MODE · guarded avatar survey · MSFS must be closed before build and install";
    public bool CanEditSdk => CanEdit && !IsDryRun;
    public string CheckInstructions => IsDryRun ? "Check and connect prepares a disposable offline sandbox. No SDK or simulator is needed." : "Load a flight in MSFS 2024 with your aircraft parked, then check the supported simulator and SDK.";
    public string SiteCheckText => IsDryRun ? "I confirm this is a simulated site check using recorded data." : "I checked the world map: this identifier is unused and the site is flat, empty and away from existing airports.";
    public string CaptureInstructions => IsDryRun ? "Capture survey replays the recorded Namibia frames through the same Core safeguards. No simulator is needed." : "Exit the aircraft. Stand upright at the runway centre, face along the runway and remain still for five seconds. Leave the aircraft parked.";
    public string AfterCaptureInstructions => IsDryRun ? "Continue with Validate, Generate and compile, then Install airport. All output stays in the test sandbox." : "After capture, close MSFS before compiling or installing. Your survey stays in this window.";
    public IReadOnlyList<RunwaySurfaceChoice> SurfacePresets { get; } =
    [
        new(null, RunwaySurface.Dirt, "Legacy Dirt · existing airports", "Preserves the original Frontier procedural dirt appearance."),
        new(null, RunwaySurface.Asphalt, "Legacy Asphalt · existing airports", "Preserves the original SDK-sample-derived asphalt appearance."),
        .. RunwaySurfacePresets.All.Select(x => new RunwaySurfaceChoice(x.Id, x.PhysicalSurface, x.ToString(),
            x.FallbackNote ?? $"SDK reference: {x.SdkSurfaceReference}. Visual MSFS verification pending."))
    ];
    public bool IsBusy => busy;
    public bool CanEdit => !busy;
    public string Status => status;
    public string Error => error;
    public string ActivityLog => activity;
    public string TechnicalLog => $"{ModeDescription}\nState: {State}; recovery: {RecoveryState}\nSDK: {sdkRoot}\nProject: {services.ProjectDirectory}\nEvidence: {services.EvidenceDirectory}\nBuild log: {services.LogPath}\n\n{activity}";
    public string SdkRoot { get => sdkRoot; set { if (busy || value == sdkRoot) return; sdkRoot = value; checkedReady = false; Survey = null; ClearParking(); Invalidate(); } }
    public string AirportName { get => airportName; set { if (!busy && value != airportName) { airportName = value; Invalidate(); } } }
    public string Identifier { get => identifier; set { if (CanEditIdentifier && value.ToUpperInvariant() != identifier) { identifier = value.ToUpperInvariant(); Invalidate(); } } }
    public string Length { get => length; set { if (!busy && value != length) { length = value; Invalidate(); } } }
    public string Width { get => width; set { if (!busy && value != width) { width = value; Invalidate(); } } }
    public RunwaySurface Surface { get => surface; set { if (!busy && (value != surface || surfacePresetId is not null)) { surface = value; surfacePresetId = null; Changed(nameof(SelectedSurfacePreset)); Changed(nameof(SurfacePresetDetail)); Invalidate(); } } }
    public RunwaySurfaceChoice SelectedSurfacePreset
    {
        get => SurfacePresets.First(x => x.PresetId == surfacePresetId && (x.PresetId is not null || x.PhysicalSurface == surface));
        set
        {
            if (busy || value is null || value.PresetId == surfacePresetId && value.PhysicalSurface == surface) return;
            surface = value.PhysicalSurface; surfacePresetId = value.PresetId;
            Changed(); Changed(nameof(Surface)); Changed(nameof(SurfacePresetDetail)); Invalidate();
        }
    }
    public string SurfacePresetDetail => SelectedSurfacePreset.Detail;
    public bool SiteConfirmed { get => siteConfirmed; set { if (!busy && value != siteConfirmed) { siteConfirmed = value; Invalidate(); } } }
    public string SurveyText => Survey is null ? "No survey captured yet" : string.Create(CultureInfo.InvariantCulture,
        $"{Survey.Latitude:F7}° latitude    {Survey.Longitude:F7}° longitude\n{Survey.AltitudeMeters:F3} m ground elevation    {Survey.TrueHeadingDegrees:F2}° true facing");
    public string ValidationMessage
    {
        get
        {
            var errors = new List<string>();
            if(Layout.HasPendingProperties)errors.Add("Finish or cancel the taxiway and apply property edits before validating or building.");
            if (Survey is null) errors.Add("Capture your runway centre first.");
            else if (SurveyCapture.ValidatePoint(Survey) is { } problem) errors.Add(problem);
            try { var d = Definition(); errors.AddRange(d.Validate().Select(e => e.StartsWith("Name must") ? "Enter an airport name of 1–48 characters without leading or trailing spaces; emoji are not supported." : e)); if (d.Identifier is "FPT1" or "FPSC") errors.Add("FPT1 and FPSC are preserved. Choose a new identifier."); }
            catch (ArgumentException e) { errors.Add(e.Message); }
            if (!siteConfirmed) errors.Add(IsDryRun ? "Confirm the simulated site check for this dry run." : "Confirm that the site and identifier have been checked on the world map.");
            return string.Join("\n", errors);
        }
    }
    public string Designators { get { try { var g = RunwayGeometry.Calculate(Definition()); return $"{g.Primary.Number:00} / {g.Secondary.Number:00}"; } catch { return "— / —"; } } }
    public double PreviewHeading => Survey?.TrueHeadingDegrees ?? 0;
    public double PreviewWidth { get { if (double.TryParse(width, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && double.TryParse(length, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) && double.IsFinite(w) && double.IsFinite(l) && l > 0) return Math.Clamp(180 * w / l, 4, 100); return 18; } }
    public string SuccessInstructions => InstalledPath == "" ? "" : IsDryRun ? $"Dry run completed. Synthetic test package installed only in:\n{InstalledPath}\nIt is not flyable scenery. Do not copy it to MSFS." : $"Installed {Built!.Definition.Identifier}. Launch or restart MSFS, search {Built.Definition.Identifier} in Free Flight, and test both runway starts ({Designators}). Inspect surface, rocks and terrain edges.\n{InstalledPath}";
    private AirstripDefinition Definition()
    {
        if (!double.TryParse(length, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) || !double.TryParse(width, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
            throw new ArgumentException("Enter length and width in metres using numbers (for example 200 and 20).");
        return new() { Name = airportName, Identifier = identifier, Latitude = Survey?.Latitude ?? 0, Longitude = Survey?.Longitude ?? 0,
            AltitudeMeters = Survey?.AltitudeMeters ?? 0, TrueHeadingDegrees = Survey?.TrueHeadingDegrees ?? 0,
            MagneticVariationDegreesEast = Survey?.MagneticVariationDegrees ?? 0, LengthMeters = l, WidthMeters = w, Surface = surface, SurfacePresetId = surfacePresetId, ParkingStands = Layout.Stands.ToArray(), Taxiways = Layout.Taxiways.ToArray(), PlacedObjects = Layout.Objects.ToArray() };
    }
    public void AddParkingAt(LocalPoint point) { if(CanLayout)Layout.Add(point,LayoutRunwayHeading); }
    private void ClearParking() => Layout.Reset();
    public async Task SaveLayoutAsync(string path)
    {
        if(!CanLayout)return;
        await Run("Save layout",async token=>{
            if(Layout.HasPendingProperties)throw new IOException("Apply property edits before saving the layout.");
            var d=Definition();d.EnsureValid();
            var snapshot=new LayoutSnapshot(1,Survey!,d.LengthMeters,d.WidthMeters,d.ParkingStands) {Taxiways=d.Taxiways,PlacedObjects=d.PlacedObjects};
            await LayoutFiles.SaveNewAsync(path,snapshot);
            Layout.MarkSaved(snapshot.ParkingStands,snapshot.PlacedObjects);status="Layout saved. Future edits require validation and a new build.";
        });
    }
    public async Task LoadLayoutAsync(string path)
    {
        if(!CanLayout)return;
        LayoutSnapshot? loaded=null;
        await Run("Load layout",async token=>{
            var file=await LayoutFiles.ReadAsync(path);
            LayoutFiles.ValidateForAnchor(file,Survey!,LayoutRunwayLength,LayoutRunwayWidth);
            loaded=file;
        });
        if(loaded!=null) {Layout.Replace(loaded.ParkingStands,loaded.Taxiways,loaded.PlacedObjects);Invalidate();Layout.MarkSaved(loaded.ParkingStands,loaded.PlacedObjects);status="Layout loaded for this survey. Validate before building.";Refresh();}
    }
    private void SetState(WorkflowState value) { State = value; RecoveryState = value; }
    private void Invalidate() { Built = null; InstalledPath = ""; error = ""; SetState(Survey != null ? WorkflowState.SurveyCaptured : checkedReady ? WorkflowState.ReadyToSurvey : WorkflowState.Disconnected); Refresh(); }
    public Task CheckAsync()
    {
        if (busy) return Task.CompletedTask;
        checkedReady = false; Survey = null; siteConfirmed = false; ClearParking(); Invalidate();
        return Run("Check prerequisites", async token => { status = await services.CheckAsync(sdkRoot, token); token.ThrowIfCancellationRequested(); checkedReady = true; SetState(WorkflowState.ReadyToSurvey); });
    }
    public Task CaptureAsync()
    {
        if (!checkedReady || busy) return Task.CompletedTask;
        Survey = null; siteConfirmed = false; ClearParking(); Invalidate();
        return Run("Capture survey", async token => {
            status = IsDryRun ? "Validating recorded avatar frames" : "Capturing — stay upright and still for at least five seconds"; Refresh();
            var captured = await services.CaptureAsync(sdkRoot, token); token.ThrowIfCancellationRequested();
            if (SurveyCapture.ValidatePoint(captured) is { } problem) throw new IOException(problem);
            Survey = captured; status = "Survey captured. Enter details and Validate."; SetState(WorkflowState.SurveyCaptured);
        });
    }
    public Task ValidateAsync()
    {
        if (busy || Survey is null) return Task.CompletedTask;
        Invalidate();
        return Run("Validate definition", async token => {
            if (ValidationMessage != "") throw new ArgumentException(ValidationMessage);
            status = await services.ValidateIdentifierAsync(identifier, token); token.ThrowIfCancellationRequested();
            SetState(WorkflowState.Validated); status = "Validated. " + status + (IsDryRun ? " Ready for simulated compilation." : " Close MSFS, then generate and compile.");
        });
    }
    public Task BuildAsync()
    {
        if (busy) return Task.CompletedTask;
        if (EffectiveState != WorkflowState.Validated || ValidationMessage != "") { Fail(new InvalidOperationException("Validate the captured survey and current inputs before building.")); return Task.CompletedTask; }
        var definition = Definition();
        return Run("Generate and compile", async token => {
            Append(await services.ValidateIdentifierAsync(definition.Identifier, token));
            var built = await services.BuildAsync(sdkRoot, definition, Progress(), token); token.ThrowIfCancellationRequested();
            Built = built; await SynchronizeSavedAsync(token); SetState(WorkflowState.Built); status = IsDryRun ? "Test artifact verified. Install into the sandbox." : "Build verified. Install this airport when MSFS is closed.";
        });
    }
    public Task InstallAsync()
    {
        if (busy || EffectiveState != WorkflowState.Built || Built is null || Layout.HasPendingProperties) return Task.CompletedTask;
        var built = Built;
        return Run("Install airport", async token => {
            InstalledPath = await services.InstallAsync(built, Progress(), token);
            // Installation may have committed after its last cancellable boundary.
            await SynchronizeSavedAsync(CancellationToken.None); SetState(WorkflowState.Installed); status = IsDryRun ? "Dry run installed and verified in the sandbox." : "Airport installed and verified. Restart MSFS to load it.";
        });
    }
    public void Cancel() { if (!busy) return; cancellation?.Cancel(); status = "Cancelling — waiting for a safe stopping point"; Refresh(); }
    private async Task Run(string operation, Func<CancellationToken, Task> work)
    {
        if (busy) return;
        busy = true; error = ""; State = RecoveryState; cancellation = new(); Append(operation); Refresh();
        try { await work(cancellation.Token); Append(status); }
        catch (OperationCanceledException) { State = WorkflowState.Cancelled; status = "Cancelled. Review the recovery action before retrying."; Append(status); }
        catch (Exception e) { Fail(e); }
        finally { busy = false; cancellation.Dispose(); cancellation = null; Refresh(); }
    }
    private void Fail(Exception e) { State = WorkflowState.Error; error = e.Message; status = "This step failed; your last completed stage is retained."; Append(e.ToString()); Refresh(); }
    private IProgress<string> Progress() { var operation = cancellation; return new Progress<string>(message => { if (!busy || cancellation != operation) return; status = message; Append(message); Refresh(); }); }
    private void Append(string message) { activity += (activity == "" ? "" : "\n") + $"{DateTimeOffset.UtcNow:O} {message}"; Changed(nameof(ActivityLog)); Changed(nameof(TechnicalLog)); }
    public void ReportLogFailure(Exception e) => Fail(e);
    private void Open(string path) { try { services.OpenPath(path); } catch (Exception e) { Fail(e); } }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void Refresh()
    {
        Changed(""); Layout.Notify(); RefreshManagement();
        foreach (var c in new[] { AddParkingCommand, RemoveParkingCommand, UndoLayoutCommand, RedoLayoutCommand, RotateLeftCommand, RotateRightCommand, ApplyParkingCommand, CheckCommand, CaptureCommand, ValidateCommand, BuildCommand, InstallCommand, CancelCommand, OpenProjectCommand, OpenPackageCommand, OpenLogCommand, OpenEvidenceCommand }) c.Refresh();
    }
}
