using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;

var json = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true, WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Converters = { new JsonStringEnumConverter<RunwaySurface>(allowIntegerValues: false) }
};
try
{
    if (args.Length == 2 && args[0] == "inspect")
    {
        Console.WriteLine(JsonSerializer.Serialize(SdkInspector.Inspect(args[1]), json));
        return 0;
    }
    if (args.Length is < 3 or > 4 || args[0] is not ("generate" or "build") || (args[0] == "generate" && args.Length != 3))
    {
        Console.Error.WriteLine("Frontier Airport Builder 0.1 (CLI spike)\nUsage:\n  inspect <SDK root>\n  generate <airstrip.json> <new output directory>\n  build <airstrip.json> <new output directory> [SDK root]\nAll geometry is in metres / true degrees. Output directory must not exist.");
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
catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or ArithmeticException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
