using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

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
    private string sdkRoot, airportName = "", identifier = "", length = "200", width = "20", status = "Start with a parked aircraft in a loaded MSFS flight.", error = "", activity = "";
    private RunwaySurface surface = RunwaySurface.Dirt;
    public SurveyPoint? Survey { get; private set; }
    public BuiltAirport? Built { get; private set; }
    public string InstalledPath { get; private set; } = "";
    public string Stage { get; private set; } = "1 · Connect";
    public event PropertyChangedEventHandler? PropertyChanged;
    public BuilderViewModel(IBuilderServices services, string sdkRoot)
    {
        this.services = services; this.sdkRoot = sdkRoot;
        CheckCommand = new(() => _ = CheckAsync(), () => !IsBusy);
        CaptureCommand = new(() => _ = CaptureAsync(), () => !IsBusy && checkedReady);
        BuildCommand = new(() => _ = BuildAsync(), () => !IsBusy && ValidationMessage == "");
        InstallCommand = new(() => _ = InstallAsync(), () => !IsBusy && Built != null && InstalledPath == "");
        CancelCommand = new(Cancel, () => IsBusy);
        OpenProjectCommand = new(() => Open(services.ProjectDirectory), () => !IsBusy && services.ProjectDirectory != "");
        OpenPackageCommand = new(() => Open(Built?.PackageDirectory ?? ""), () => !IsBusy && Built != null);
        OpenLogCommand = new(() => Open(services.LogPath), () => !IsBusy && services.LogPath != "");
        OpenEvidenceCommand = new(() => Open(services.EvidenceDirectory), () => !IsBusy && services.EvidenceDirectory != "");
    }
    public ActionCommand CheckCommand { get; }
    public ActionCommand CaptureCommand { get; }
    public ActionCommand BuildCommand { get; }
    public ActionCommand InstallCommand { get; }
    public ActionCommand CancelCommand { get; }
    public ActionCommand OpenProjectCommand { get; }
    public ActionCommand OpenPackageCommand { get; }
    public ActionCommand OpenLogCommand { get; }
    public ActionCommand OpenEvidenceCommand { get; }
    public IReadOnlyList<RunwaySurface> Surfaces { get; } = [RunwaySurface.Dirt, RunwaySurface.Asphalt];
    public bool IsBusy => busy;
    public bool CanEdit => !busy;
    public string Status => status;
    public string Error => error;
    public string ActivityLog => activity;
    public string SdkRoot { get => sdkRoot; set { if (busy || value == sdkRoot) return; sdkRoot = value; checkedReady = false; Survey = null; Invalidate(); } }
    public string AirportName { get => airportName; set { if (!busy) { airportName = value; Invalidate(); } } }
    public string Identifier { get => identifier; set { if (!busy) { identifier = value.ToUpperInvariant(); Invalidate(); } } }
    public string Length { get => length; set { if (!busy) { length = value; Invalidate(); } } }
    public string Width { get => width; set { if (!busy) { width = value; Invalidate(); } } }
    public RunwaySurface Surface { get => surface; set { if (!busy) { surface = value; Invalidate(); } } }
    public bool SiteConfirmed { get => siteConfirmed; set { if (!busy) { siteConfirmed = value; Invalidate(); } } }
    public string SurveyText => Survey is null ? "No survey captured yet" : string.Create(CultureInfo.InvariantCulture,
        $"{Survey.Latitude:F7}° latitude    {Survey.Longitude:F7}° longitude\n{Survey.AltitudeMeters:F3} m ground elevation    {Survey.TrueHeadingDegrees:F2}° true facing");
    public string ValidationMessage
    {
        get
        {
            var errors = new List<string>();
            if (Survey is null) errors.Add("Capture your runway centre first.");
            try { var d = Definition(); errors.AddRange(d.Validate().Select(e => e.StartsWith("Name must") ? "Enter an airport name of 1–48 characters without leading or trailing spaces; emoji are not supported." : e)); if (d.Identifier is "FPT1" or "FPSC") errors.Add("FPT1 and FPSC are preserved. Choose a new identifier."); }
            catch (ArgumentException e) { errors.Add(e.Message); }
            if (!siteConfirmed) errors.Add("Confirm that the site and identifier have been checked on the world map.");
            return string.Join("\n", errors);
        }
    }
    public string Designators { get { try { var g = RunwayGeometry.Calculate(Definition()); return $"{g.Primary.Number:00} / {g.Secondary.Number:00}"; } catch { return "— / —"; } } }
    public double PreviewHeading => Survey?.TrueHeadingDegrees ?? 0;
    public double PreviewWidth { get { if (double.TryParse(width, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && double.TryParse(length, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) && double.IsFinite(w) && double.IsFinite(l) && l > 0) return Math.Clamp(180 * w / l, 4, 100); return 18; } }
    public string SuccessInstructions => InstalledPath == "" ? "" : $"Installed {Built!.Definition.Identifier}. Launch or restart MSFS, search {Built.Definition.Identifier} in Free Flight, and test both runway starts ({Designators}). Inspect the surface, rocks and terrain edges before using the strip.\n{InstalledPath}";
    private AirstripDefinition Definition()
    {
        if (!double.TryParse(length, NumberStyles.Float, CultureInfo.InvariantCulture, out var l) || !double.TryParse(width, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
            throw new ArgumentException("Enter length and width in metres using numbers (for example 200 and 20).");
        return new() { Name = airportName, Identifier = identifier, Latitude = Survey?.Latitude ?? 0, Longitude = Survey?.Longitude ?? 0,
            AltitudeMeters = Survey?.AltitudeMeters ?? 0, TrueHeadingDegrees = Survey?.TrueHeadingDegrees ?? 0,
            MagneticVariationDegreesEast = Survey?.MagneticVariationDegrees ?? 0, LengthMeters = l, WidthMeters = w, Surface = surface };
    }
    private void Invalidate() { Built = null; InstalledPath = ""; error = ""; Refresh(); }
    public Task CheckAsync() => Run(async token => { checkedReady = false; status = await services.CheckAsync(sdkRoot, token); checkedReady = true; Stage = "2 · Survey"; });
    public Task CaptureAsync()
    {
        if (!checkedReady || busy) return Task.CompletedTask;
        Survey = null; Built = null; InstalledPath = "";
        return Run(async token => { Stage = "2 · Survey"; status = "Capturing — stay upright and still for at least five seconds"; Refresh();
            Survey = await services.CaptureAsync(sdkRoot, token); status = "Survey captured. Review the details, then close MSFS before building."; Stage = "3 · Define your airstrip"; });
    }
    public Task BuildAsync()
    {
        if (busy) return Task.CompletedTask;
        if (ValidationMessage != "") { error = ValidationMessage; Refresh(); return Task.CompletedTask; }
        var definition = Definition(); Built = null; InstalledPath = "";
        return Run(async token => {
            Stage = "4 · Build"; status = "Checking identifier and inputs"; Refresh();
            var availability = await services.ValidateIdentifierAsync(definition.Identifier, token); Append(availability);
            Built = await services.BuildAsync(sdkRoot, definition, Progress(), token);
            Stage = "5 · Install"; status = "Build verified. Install this airport when MSFS is closed.";
        });
    }
    public Task InstallAsync()
    {
        if (busy || Built is null || InstalledPath != "") return Task.CompletedTask;
        var built = Built;
        return Run(async token => { InstalledPath = await services.InstallAsync(built, Progress(), token); Stage = "6 · Ready to fly"; status = "Airport installed and verified. Restart MSFS to load it."; });
    }
    public void Cancel() { cancellation?.Cancel(); status = "Cancelling — waiting for the current operation to finish safely"; Refresh(); }
    private async Task Run(Func<CancellationToken, Task> work)
    {
        if (busy) return;
        busy = true; error = ""; cancellation = new(); Refresh();
        try { await work(cancellation.Token); Append(status); }
        catch (OperationCanceledException) { status = "Cancelled. No unverified package will be installed. If Package Tool launched MSFS, close it before retrying."; Append(status); }
        catch (Exception e) { error = e.Message; status = "Could not complete this step. See the message below and detailed evidence."; Append(e.Message); }
        finally { busy = false; cancellation.Dispose(); cancellation = null; Refresh(); }
    }
    private IProgress<string> Progress() { var operation = cancellation; return new Progress<string>(message => { if (!busy || cancellation != operation) return; status = message; Append(message); Refresh(); }); }
    private void Append(string message) { activity += (activity == "" ? "" : "\n") + message; Changed(nameof(ActivityLog)); }
    private void Open(string path) { try { services.OpenPath(path); } catch (Exception e) { error = e.Message; Refresh(); } }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void Refresh()
    {
        Changed("");
        foreach (var c in new[] { CheckCommand, CaptureCommand, BuildCommand, InstallCommand, CancelCommand, OpenProjectCommand, OpenPackageCommand, OpenLogCommand, OpenEvidenceCommand }) c.Refresh();
    }
}


