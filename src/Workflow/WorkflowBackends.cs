using System.Reflection;
using System.Text;
using System.Text.Json;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
namespace FrontierAirportBuilder.Workflow;

// Deliberately separate native/process entry points from replay. The replay backend cannot invoke Package Tool.
internal interface IWorkflowBackend
{
    bool IsDryRun { get; }
    void Check(string sdkRoot, CancellationToken token);
    SurveyPoint Capture(string sdkRoot, string evidence, CancellationToken token);
    Task<BuildResult> Compile(GeneratedProject project, string sdkRoot, IProgress<string> progress, CancellationToken token);
}
internal sealed class LiveWorkflowBackend : IWorkflowBackend
{
    public bool IsDryRun => false;
    public void Check(string root, CancellationToken token) => LiveSurveyCapture.CheckConnection(root, token);
    public SurveyPoint Capture(string root, string evidence, CancellationToken token) => LiveSurveyCapture.Capture(root, evidence, token);
    public Task<BuildResult> Compile(GeneratedProject project, string root, IProgress<string> progress, CancellationToken token) =>
        PackageBuilder.BuildAsync(project, SdkInspector.Inspect(root), TimeSpan.FromMinutes(3), token, progress);
}
internal sealed class ReplayWorkflowBackend(string? framesJson, bool failCompilation) : IWorkflowBackend
{
    public bool IsDryRun => true;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private string ReadFrames()
    {
        if (framesJson != null) return framesJson;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FrontierAirportBuilder.Workflow.Fixtures.namibia-centre-frames.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    public void Check(string root, CancellationToken token) { token.ThrowIfCancellationRequested(); }
    public SurveyPoint Capture(string root, string evidence, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var raw = ReadFrames();
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(evidence, "recorded-frames.json"), raw);
        File.WriteAllText(Path.Combine(evidence, "metadata.json"), JsonSerializer.Serialize(new {
            mode = "DRY RUN", source = "evidence/live-survey/namibia-centre-01/observations.jsonl; frame rows only",
            recordedUtc = "2026-09-07T06:17Z", qualification = "Historical SDK 1.2.4 and MSFS 1.8.16.0; no live qualification or simulator connection",
            limitation = "Microsoft documents aircraft-local GROUND ALTITUDE; empirical avatar behaviour and 66 cm return-to-A caveat retained."
        }, Json));
        var frames = JsonSerializer.Deserialize<SurveyFrame[]>(raw, Json) ?? throw new IOException("Recorded frames are missing.");
        var point = SurveyCapture.Create(frames); token.ThrowIfCancellationRequested();
        File.WriteAllText(Path.Combine(evidence, "result.json"), JsonSerializer.Serialize(point, Json));
        return point;
    }
    public Task<BuildResult> Compile(GeneratedProject project, string root, IProgress<string> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        progress.Report("DRY RUN: simulating compiler output; no Package Tool or MSFS process");
        var evidence = Path.Combine(project.Directory, ".build");
        var package = Path.Combine(project.Directory, "Packages", project.PackageName);
        Directory.CreateDirectory(evidence); Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "manifest.json"), """{"creator":"Frontier Pilot","package_order_hint":"CUSTOM_AIRPORT","frontier_test_only":true}""");
        if (failCompilation)
        {
            File.WriteAllText(Path.Combine(evidence, "stderr.log"), "DRY RUN: injected compiler failure.");
            return Task.FromResult(new BuildResult(1,false,new PackageValidation(false,["Simulated compiler failure."],0),package,evidence));
        }
        var content = Encoding.UTF8.GetBytes("FRONTIER DRY RUN — NOT A FLIGHT SIMULATOR BGL\n" + File.ReadAllText(Path.Combine(project.Directory,"PackageSources","data","airport.xml")));
        File.WriteAllBytes(Path.Combine(package, "airport.bgl"), content);
        File.WriteAllText(Path.Combine(package, "layout.json"), JsonSerializer.Serialize(new { content = new[] { new { path = "airport.bgl", size = content.Length } } }, Json));
        File.WriteAllText(Path.Combine(evidence, "stdout.log"), "SIMULATED COMPILATION. XML generation is real; this package is not flyable scenery.");
        var result = new BuildResult(0, false, PackageValidator.Validate(package,0,false,""),package,evidence);
        File.WriteAllText(Path.Combine(evidence, "result.json"),JsonSerializer.Serialize(result,Json));
        return Task.FromResult(result);
    }
}

