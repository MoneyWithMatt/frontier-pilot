using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public enum WorkflowState { Disconnected, ReadyToSurvey, SurveyCaptured, Validated, Built, Installed, Error, Cancelled }
public sealed class ActionCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class BuilderViewModel : INotifyPropertyChanged
{
    private readonly IBuilderServices services;
    private CancellationTokenSource? cancellation;
    private bool busy, checkedReady, siteConfirmed;
    private string sdkRoot, airportName = "", identifier = "", length = "200", width = "20", status = "Check prerequisites to begin.", error = "", activity = "";
    private RunwaySurface surface = RunwaySurface.Dirt;
    private readonly ObservableCollection<ParkingStandInput> parking = [];
    public ReadOnlyObservableCollection<ParkingStandInput> ParkingStands { get; }
    public ParkingStandInput? SelectedParking { get; set; }
    public ActionCommand AddParkingCommand { get; }
    public ActionCommand RemoveParkingCommand { get; }
    public double ParkingScale
    {
        get
        {
            try {
                var d=Definition();
                var stands=d.ParkingStands;
                double extentX=Math.Max(d.WidthMeters/2,stands.Select(s=>Math.Abs(s.RightMeters)+s.RadiusMeters).DefaultIfEmpty(0).Max());
                double extentY=Math.Max(d.LengthMeters/2,stands.Select(s=>Math.Abs(s.ForwardMeters)+s.RadiusMeters).DefaultIfEmpty(0).Max());
                return double.IsFinite(extentX+extentY) && extentX>0 && extentY>0 ? Math.Min(100/extentX,100/extentY) : 1;
            } catch { return 1; }
        }
    }
    public double ParkingRunwayWidth { get { try { var w=Definition().WidthMeters;return double.IsFinite(w)?Math.Clamp(w*ParkingScale,1,200):20; } catch { return 20; } } }
    public double ParkingRunwayLength { get { try { var l=Definition().LengthMeters;return double.IsFinite(l)?Math.Clamp(l*ParkingScale,1,200):200; } catch { return 200; } } }
    public IReadOnlyList<ParkingPreviewItem> ParkingPreview
    {
        get {
            try { var d=Definition(); if(d.Validate().Count!=0)return []; return d.ParkingStands.Where(s=>new[]{s.RightMeters,s.ForwardMeters,s.RadiusMeters,s.FacingOffsetDegrees}.All(double.IsFinite) && s.RadiusMeters>0)
                .Select(s=>new ParkingPreviewItem(s.Number,120+(s.RightMeters-s.RadiusMeters)*ParkingScale,120-(s.ForwardMeters+s.RadiusMeters)*ParkingScale,2*s.RadiusMeters*ParkingScale,Headings.Normalize(s.FacingOffsetDegrees))).ToArray(); }
            catch { return []; }
        }
    }
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
    public BuilderViewModel(IBuilderServices services, string sdkRoot)
    {
        this.services = services; this.sdkRoot = sdkRoot;
        ParkingStands = new(parking);
        AddParkingCommand = new(AddParking,()=>!busy && Survey!=null && parking.Count<8);
        RemoveParkingCommand = new(RemoveParking,()=>!busy && parking.Count>0);
        CheckCommand = new(() => _ = CheckAsync(), () => !IsBusy);
        CaptureCommand = new(() => _ = CaptureAsync(), () => !IsBusy && checkedReady);
        ValidateCommand = new(() => _ = ValidateAsync(), () => !IsBusy && Survey != null && ValidationMessage == "");
        BuildCommand = new(() => _ = BuildAsync(), () => !IsBusy && EffectiveState == WorkflowState.Validated);
        InstallCommand = new(() => _ = InstallAsync(), () => !IsBusy && EffectiveState == WorkflowState.Built && Built != null);
        CancelCommand = new(Cancel, () => IsBusy);
        OpenProjectCommand = new(() => Open(services.ProjectDirectory), () => !IsBusy && services.ProjectDirectory != "");
        OpenPackageCommand = new(() => Open(Built?.PackageDirectory ?? ""), () => !IsBusy && Built != null);
        OpenLogCommand = new(() => Open(services.LogPath), () => !IsBusy && services.LogPath != "");
        OpenEvidenceCommand = new(() => Open(services.EvidenceDirectory), () => !IsBusy && services.EvidenceDirectory != "");
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
    public IReadOnlyList<RunwaySurface> Surfaces { get; } = [RunwaySurface.Dirt, RunwaySurface.Asphalt];
    public bool IsBusy => busy;
    public bool CanEdit => !busy;
    public string Status => status;
    public string Error => error;
    public string ActivityLog => activity;
    public string TechnicalLog => $"{ModeDescription}\nState: {State}; recovery: {RecoveryState}\nSDK: {sdkRoot}\nProject: {services.ProjectDirectory}\nEvidence: {services.EvidenceDirectory}\nBuild log: {services.LogPath}\n\n{activity}";
    public string SdkRoot { get => sdkRoot; set { if (busy || value == sdkRoot) return; sdkRoot = value; checkedReady = false; Survey = null; ClearParking(); Invalidate(); } }
    public string AirportName { get => airportName; set { if (!busy && value != airportName) { airportName = value; Invalidate(); } } }
    public string Identifier { get => identifier; set { if (!busy && value.ToUpperInvariant() != identifier) { identifier = value.ToUpperInvariant(); Invalidate(); } } }
    public string Length { get => length; set { if (!busy && value != length) { length = value; Invalidate(); } } }
    public string Width { get => width; set { if (!busy && value != width) { width = value; Invalidate(); } } }
    public RunwaySurface Surface { get => surface; set { if (!busy && value != surface) { surface = value; Invalidate(); } } }
    public bool SiteConfirmed { get => siteConfirmed; set { if (!busy && value != siteConfirmed) { siteConfirmed = value; Invalidate(); } } }
    public string SurveyText => Survey is null ? "No survey captured yet" : string.Create(CultureInfo.InvariantCulture,
        $"{Survey.Latitude:F7}° latitude    {Survey.Longitude:F7}° longitude\n{Survey.AltitudeMeters:F3} m ground elevation    {Survey.TrueHeadingDegrees:F2}° true facing");
    public string ValidationMessage
    {
        get
        {
            var errors = new List<string>();
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
            MagneticVariationDegreesEast = Survey?.MagneticVariationDegrees ?? 0, LengthMeters = l, WidthMeters = w, Surface = surface, ParkingStands = parking.Select(p=>p.ToDefinition()).ToArray() };
    }
    private void AddParking()
    {
        if(busy||Survey==null||parking.Count>=8)return;
        int number=Enumerable.Range(1,99).First(n=>parking.All(p=>p.Number!=n));
        double runwayWidth=double.TryParse(width,NumberStyles.Float,CultureInfo.InvariantCulture,out var w)&&double.IsFinite(w)?w:20;
        var item=new ParkingStandInput(number,()=>!busy,runwayWidth/2+25) { Forward = (parking.Count*30).ToString(CultureInfo.InvariantCulture) };
        item.PropertyChanged += ParkingChanged; parking.Add(item);SelectedParking=item;Invalidate();
    }
    private void RemoveParking()
    {
        if(busy||parking.Count==0)return;
        var item=SelectedParking ?? parking[^1];
        item.PropertyChanged -= ParkingChanged;parking.Remove(item);SelectedParking=null;Invalidate();
    }
    private void ParkingChanged(object? sender,PropertyChangedEventArgs e) => Invalidate();
    private void ClearParking() { foreach(var item in parking)item.PropertyChanged-=ParkingChanged;parking.Clear();SelectedParking=null; }
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
            Built = built; SetState(WorkflowState.Built); status = IsDryRun ? "Test artifact verified. Install into the sandbox." : "Build verified. Install this airport when MSFS is closed.";
        });
    }
    public Task InstallAsync()
    {
        if (busy || EffectiveState != WorkflowState.Built || Built is null) return Task.CompletedTask;
        var built = Built;
        return Run("Install airport", async token => {
            InstalledPath = await services.InstallAsync(built, Progress(), token);
            // Installation may have committed after its last cancellable boundary.
            SetState(WorkflowState.Installed); status = IsDryRun ? "Dry run installed and verified in the sandbox." : "Airport installed and verified. Restart MSFS to load it.";
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
        Changed("");
        foreach (var c in new[] { AddParkingCommand, RemoveParkingCommand, CheckCommand, CaptureCommand, ValidateCommand, BuildCommand, InstallCommand, CancelCommand, OpenProjectCommand, OpenPackageCommand, OpenLogCommand, OpenEvidenceCommand }) c.Refresh();
    }
}


