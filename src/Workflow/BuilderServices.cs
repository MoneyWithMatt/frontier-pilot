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
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string EvidenceDirectory { get; private set; } = "";
    public string ProjectDirectory { get; private set; } = "";
    public string LogPath { get; private set; } = "";
    public BuilderServices(string? stateRoot = null) => root = stateRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrontierAirportBuilder", "desktop");
    public Task<string> CheckAsync(string sdkRoot, CancellationToken token) => Task.Run(() =>
    {
        CommunityInstaller.SafePath(root);
        var installer = Installer();
        if (!Directory.Exists(installer.Community)) throw new IOException("Configured Community2024 folder is missing.");
        LiveSurveyCapture.CheckConnection(sdkRoot, token);
        return "SDK 1.2.4 verified · MSFS connected · Community2024 found";
    }, token);
    public Task<SurveyPoint> CaptureAsync(string sdkRoot, CancellationToken token) => Task.Run(() =>
    {
        EvidenceDirectory = Fresh("surveys"); ProjectDirectory = ""; LogPath = Path.Combine(EvidenceDirectory, "result.json");
        return LiveSurveyCapture.Capture(sdkRoot, EvidenceDirectory, token);
    }, token);
    public Task<string> ValidateIdentifierAsync(string identifier, CancellationToken token) => Task.Run(() => Installer().CheckIdentifier(identifier), token);
    public Task<BuiltAirport> BuildAsync(string sdkRoot, AirstripDefinition definition, IProgress<string> progress, CancellationToken token) => Task.Run(async () =>
    {
        // Validate and check read-only prerequisites before allocating any project workspace.
        definition.EnsureValid(); Installer().CheckIdentifier(definition.Identifier); AssertSimulatorClosed();
        var sdk = SdkInspector.Inspect(sdkRoot); token.ThrowIfCancellationRequested();
        progress.Report("Generating isolated airport project");
        string dir = Fresh("projects");
        var generated = ProjectGenerator.Generate(definition, dir);
        ProjectDirectory = dir; EvidenceDirectory = Path.Combine(dir, ".build"); LogPath = Path.Combine(dir, "build-report.txt");
        File.WriteAllText(Path.Combine(dir, "airstrip.json"), JsonSerializer.Serialize(definition, Json));
        try
        {
            var result = await PackageBuilder.BuildAsync(generated, sdk, TimeSpan.FromMinutes(3), token, progress);
            File.WriteAllText(LogPath, "Frontier Airport Builder\n" + JsonSerializer.Serialize(result, Json) + "\n\n" +
                string.Join("\n", new[] { "command.ps1", "stdout.log", "stderr.log", "builder-errors-this-run.log" }.Select(f =>
                    f + "\n" + (File.Exists(Path.Combine(result.EvidenceDirectory, f)) ? File.ReadAllText(Path.Combine(result.EvidenceDirectory, f)) : "Unavailable"))));
            token.ThrowIfCancellationRequested();
            if (!result.Validation.Success) throw new IOException(string.Join("\n", result.Validation.Errors));
            return new BuiltAirport(definition, dir, result.PackageDirectory, LogPath, CommunityInstaller.Inventory(result.PackageDirectory));
        }
        catch (Exception e)
        {
            File.AppendAllText(LogPath, "\n" + e); throw;
        }
    }, token);
    public Task<string> InstallAsync(BuiltAirport airport, IProgress<string> progress, CancellationToken token) => Task.Run(() =>
    {
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
        string config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator 2024", "UserCfg.opt");
        var match = Regex.Match(File.ReadAllText(config), "(?m)^\\s*InstalledPackagesPath\\s+\"([^\"]+)\"\\s*$");
        if (!match.Success) throw new IOException("Cannot locate MSFS Packages from UserCfg.opt.");
        return new CommunityInstaller(Path.GetFullPath(match.Groups[1].Value), root, AssertSimulatorClosed);
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
