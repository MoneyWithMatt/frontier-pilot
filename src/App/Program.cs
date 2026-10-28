using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
using FrontierAirportBuilder.Workflow;

var json = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true, WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    // Airstrip JSON now contains runway, parking and taxiway enum values.
    Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
};
try
{
    if (args.Length == 7 && args[0] == "condition-airframe")
    {
        var record = AircraftConditionRecord.Create(args[2], args[3], args[4], args[5]);
        if (args[6] != "--confirm-model-binding") throw new ArgumentException("Explicit model binding confirmation is required.");
        AircraftConditionFiles.Create(args[1], record);
        Console.WriteLine($"Created individual aircraft {record.AirframeId}. Hours/condition unknown; no simulator writes.");
        return 0;
    }
    if (args.Length is 4 or 5 && args[0] == "record-condition")
    {
        if (!int.TryParse(args[2], out var seconds) || seconds is < 1 or > 3600)
            throw new ArgumentException("Recording duration must be 1–3600 seconds.");
        var selected = args.Length == 5 ? AircraftConditionFiles.Load(args[4]) : null;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.WriteLine("Recording read-only condition diagnostics. Ctrl+C stops. No capability is automatically qualified.");
        var count = await new AircraftConditionRecorder().RecordAsync(new LiveAircraftCondition(args[3]), args[1], selected, stop.Token);
        Console.WriteLine($"Exported {count} frames. Native state was not modified. Results remain unverified.");
        return count > 0 ? 0 : 1;
    }
    if (args.Length is 2 or 3 && args[0] == "capture-survey")
    {
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        var root = args.Length == 3 ? args[2] : Environment.GetEnvironmentVariable("MSFS2024_SDK", EnvironmentVariableTarget.User)
            ?? throw new ArgumentException("Supply the SDK root or set MSFS2024_SDK.");
        Console.WriteLine(JsonSerializer.Serialize(LiveSurveyCapture.Capture(root, args[1], stop.Token), json));
        return 0;
    }
    if (args.Length == 2 && args[0] == "inspect")
    {
        Console.WriteLine(JsonSerializer.Serialize(SdkInspector.Inspect(args[1]), json));
        return 0;
    }
    if (args.Length is < 3 or > 4 || args[0] is not ("generate" or "build") || (args[0] == "generate" && args.Length != 3))
    {
        Console.Error.WriteLine("Frontier Airport Builder 0.1 (CLI spike)\nUsage:\n  inspect <SDK root>\n  generate <airstrip.json> <new output directory>\n  build <airstrip.json> <new output directory> [SDK root]\n  record-condition <new.jsonl> <seconds 1-3600> <SDK root> [airframe.json]\n  condition-airframe <new.json> <label> <exact TITLE> <exact ATC MODEL> <package/version note> --confirm-model-binding\nAll geometry is in metres / true degrees. Output must not exist.");
        return 2;
    }
    var input = JsonSerializer.Deserialize<AirstripDefinition>(await File.ReadAllTextAsync(args[1]), json)
        ?? throw new JsonException("Expected one airstrip definition.");
    input.EnsureValid();
    var sdk = args[0] == "build" ? SdkInspector.Inspect(args.Length == 4 ? args[3] :
        Environment.GetEnvironmentVariable("MSFS2024_SDK", EnvironmentVariableTarget.User) ??
        Environment.GetEnvironmentVariable("MSFS2024_SDK") ?? throw new ArgumentException("Supply the SDK root or set MSFS2024_SDK.")) : null;
    var generated = ProjectGenerator.Generate(input, args[2]);
    if (sdk is null)
    {
        Console.WriteLine(JsonSerializer.Serialize(generated, json));
        return 0;
    }
    using var cancel = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
    var result = await PackageBuilder.BuildAsync(generated, sdk, TimeSpan.FromMinutes(3), cancel.Token);
    await File.WriteAllTextAsync(Path.Combine(result.EvidenceDirectory, "airstrip.json"), JsonSerializer.Serialize(input, json));
    await File.WriteAllTextAsync(Path.Combine(result.EvidenceDirectory, "geometry.json"), JsonSerializer.Serialize(generated.Geometry, json));
    Console.WriteLine(JsonSerializer.Serialize(result, json));
    return result.Validation.Success ? 0 : 1;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or ArithmeticException or PlatformNotSupportedException or System.ComponentModel.Win32Exception or OperationCanceledException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
