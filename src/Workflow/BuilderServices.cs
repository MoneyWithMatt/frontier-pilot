using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;

namespace FrontierAirportBuilder.Workflow;

public sealed record BuiltAirport(AirstripDefinition Definition, string ProjectDirectory, string PackageDirectory,
    string LogPath, IReadOnlyList<PackageFile> Files);
public interface IBuilderServices
{
    bool IsDryRun => false;
    string EvidenceDirectory { get; }
    string ProjectDirectory { get; }
    string LogPath { get; }
    Task<string> CheckAsync(string sdkRoot, CancellationToken token);
    Task<SurveyPoint> CaptureAsync(string sdkRoot, CancellationToken token);
    Task<string> ValidateIdentifierAsync(string identifier, CancellationToken token);
    Task<BuiltAirport> BuildAsync(string sdkRoot, AirstripDefinition definition, IProgress<string> progress, CancellationToken token);
    Task<string> InstallAsync(BuiltAirport airport, IProgress<string> progress, CancellationToken token);
    void OpenPath(string path);
}

public sealed class BuilderServices : IBuilderServices
{
    private readonly string root;
    private readonly IWorkflowBackend backend;
    private readonly string? sandbox;
    private BuiltAirport? issuedBuild;
    private SurveyPoint? capturedSurvey;
    private string captureEvidence = "";
    public bool IsDryRun => backend.IsDryRun;
    public string? SandboxDirectory => sandbox;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string EvidenceDirectory { get; private set; } = "";
    public string ProjectDirectory { get; private set; } = "";
    public string LogPath { get; private set; } = "";
    public BuilderServices(string? stateRoot = null)
    {
        root = stateRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrontierAirportBuilder", "desktop");
        backend = new LiveWorkflowBackend();
    }
    private BuilderServices(string directory, string? recordedFramesJson, bool failCompilation)
    {
        sandbox = directory; root = Path.Combine(directory, "state"); backend = new ReplayWorkflowBackend(recordedFramesJson, failCompilation);
    }
    // No caller-selected destination: every replay session has a fresh, app-owned temporary sandbox.
    public static BuilderServices CreateDryRun(string? recordedFramesJson = null, bool failCompilation = false) =>
        new(Path.Combine(Path.GetTempPath(), "FrontierAirportBuilder-DryRuns", Guid.NewGuid().ToString("N")), recordedFramesJson, failCompilation);
    public Task<string> CheckAsync(string sdkRoot, CancellationToken token) => Task.Run(() =>
    {
        capturedSurvey = null; issuedBuild = null; CommunityInstaller.SafePath(root);
        if (sandbox != null)
        {
            CommunityInstaller.SafePath(sandbox);
            Directory.CreateDirectory(Path.Combine(sandbox,"Packages","Community2024"));
            File.WriteAllText(Path.Combine(sandbox,"DRY-RUN.txt"), "Frontier disposable offline test. Not simulator scenery.");
        }
        var installer = Installer();
        if (!Directory.Exists(installer.Community)) throw new IOException("Configured Community2024 folder is missing.");
        installer.ValidateDestination(); backend.Check(sdkRoot, token);
        return IsDryRun ? "Dry-run sandbox ready. No simulator connection." : "SDK 1.2.4 verified · MSFS connected · Community2024 found";
    }, token);
    public Task<SurveyPoint> CaptureAsync(string sdkRoot, CancellationToken token) => Task.Run(() =>
    {
        capturedSurvey = null; issuedBuild = null;
        EvidenceDirectory = Fresh("surveys"); ProjectDirectory = ""; LogPath = Path.Combine(EvidenceDirectory, "result.json");
        var point = backend.Capture(sdkRoot, EvidenceDirectory, token);
        token.ThrowIfCancellationRequested();
        if (SurveyCapture.ValidatePoint(point) is { } problem) throw new IOException(problem);
        captureEvidence = EvidenceDirectory;
        return capturedSurvey = point;
    }, token);
    public Task<string> ValidateIdentifierAsync(string identifier, CancellationToken token) => Task.Run(() => Installer().CheckIdentifier(identifier), token);
    public Task<BuiltAirport> BuildAsync(string sdkRoot, AirstripDefinition definition, IProgress<string> progress, CancellationToken token) => Task.Run(async () =>
    {
        // Validate and check read-only prerequisites before allocating any project workspace.
        definition.EnsureValid();
        if (capturedSurvey == null || definition.Latitude != capturedSurvey.Latitude || definition.Longitude != capturedSurvey.Longitude ||
            definition.AltitudeMeters != capturedSurvey.AltitudeMeters || definition.TrueHeadingDegrees != capturedSurvey.TrueHeadingDegrees ||
            definition.MagneticVariationDegreesEast != capturedSurvey.MagneticVariationDegrees)
            throw new IOException("Definition must use this session's validated survey point without substituted coordinates or elevation.");
        issuedBuild = null; Installer().CheckIdentifier(definition.Identifier); if (!IsDryRun) AssertSimulatorClosed();
        if (!IsDryRun) SdkInspector.Inspect(sdkRoot); token.ThrowIfCancellationRequested();
        progress.Report("Generating isolated airport project");
        string dir = Fresh("projects");
        var generated = ProjectGenerator.Generate(definition, dir);
        ProjectDirectory = dir; EvidenceDirectory = Path.Combine(dir, ".build"); LogPath = Path.Combine(dir, "build-report.txt");
        File.WriteAllText(Path.Combine(dir, "survey-reference.json"), JsonSerializer.Serialize(new { point = capturedSurvey, evidenceDirectory = captureEvidence, dryRun = IsDryRun }, Json));
        File.WriteAllText(Path.Combine(dir, "airstrip.json"), JsonSerializer.Serialize(definition, Json));
        try
        {
            var result = await backend.Compile(generated, sdkRoot, progress, token);
            File.WriteAllText(LogPath, "Frontier Airport Builder\n" + JsonSerializer.Serialize(result, Json) + "\n\n" +
                string.Join("\n", new[] { "command.ps1", "stdout.log", "stderr.log", "builder-errors-this-run.log" }.Select(f =>
                    f + "\n" + (File.Exists(Path.Combine(result.EvidenceDirectory, f)) ? File.ReadAllText(Path.Combine(result.EvidenceDirectory, f)) : "Unavailable"))));
            token.ThrowIfCancellationRequested();
            if (!result.Validation.Success) throw new IOException(string.Join("\n", result.Validation.Errors));
            return issuedBuild = new BuiltAirport(definition, dir, result.PackageDirectory, LogPath, CommunityInstaller.Inventory(result.PackageDirectory));
        }
        catch (Exception e)
        {
            File.AppendAllText(LogPath, "\n" + e); throw;
        }
    }, token);
    public Task<string> InstallAsync(BuiltAirport airport, IProgress<string> progress, CancellationToken token) => Task.Run(() =>
    {
        if (!ReferenceEquals(airport, issuedBuild)) throw new IOException("Only the verified build produced by this service session may be installed.");
        CommunityInstaller.Within(airport.ProjectDirectory, root);
        CommunityInstaller.Within(airport.PackageDirectory, airport.ProjectDirectory);
        var receipt = Installer().Install(airport.Definition.Identifier, airport.PackageDirectory, airport.Files, token, progress);
        return receipt.InstallPath;
    }, token);
    public void OpenPath(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) throw new IOException("That output is not available yet.");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    private string Fresh(string kind) { CommunityInstaller.SafePath(root); return Path.Combine(root, kind, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]); }
    private CommunityInstaller Installer()
    {
        if (sandbox != null) return new CommunityInstaller(Path.Combine(sandbox,"Packages"), root, () => { }, allowTestPackages: true);
        string config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator 2024", "UserCfg.opt");
        var matches = Regex.Matches(File.ReadAllText(config), "(?m)^\\s*InstalledPackagesPath\\s+\"([^\"]+)\"\\s*$");
        if (matches.Count != 1) throw new IOException("UserCfg.opt must contain exactly one InstalledPackagesPath.");
        return new CommunityInstaller(matches[0].Groups[1].Value, root, AssertSimulatorClosed);
    }
    public static void AssertSimulatorClosed()
    {
        foreach (string name in new[] { "FlightSimulator2024", "fspackagetool" })
        {
            var processes = Process.GetProcessesByName(name); bool running = processes.Length != 0;
            foreach (var p in processes) p.Dispose();
            if (running) throw new IOException("Close MSFS and Package Tool before building or installing. Your captured survey is retained.");
        }
    }
}
