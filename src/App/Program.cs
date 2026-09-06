// See https://aka.ms/new-console-template for more information
if (args.Length != 1)
{
    Console.WriteLine("Frontier Airport Builder 0.1 — SDK inspection only; UI pending sample proof.");
    Console.WriteLine("Usage: FrontierAirportBuilder.App <SDK root>");
    return 2;
}
try
{
    var sdk = FrontierAirportBuilder.MsfsSdk.SdkInspector.Inspect(args[0]);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(sdk, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
