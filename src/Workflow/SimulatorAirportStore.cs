using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.MsfsSdk;

namespace FrontierAirportBuilder.Workflow;

public sealed record SavedSimulatorAirport(SimulatorAirport Airport, DateTimeOffset ObservedUtc, string Source);
public sealed record SimulatorAirportCache(int SchemaVersion, SimulatorAirportScan Scan, SavedSimulatorAirport[] Routes)
{
    public string? Problem(string identifier)
    {
        var found = Scan.Airports.Where(x => x.Identifier == identifier).Distinct().ToArray();
        if (found.Length == 0) return "Not found in MSFS airport catalogue";
        if (found.Length != 1) return "Ambiguous identifier in MSFS airport catalogue";
        var original = Routes.SingleOrDefault(x => x.Airport.Identifier == identifier);
        return original is not null && original.Airport != found[0] ? "Airport coordinates changed; investigate before new jobs" : null;
    }
}

/// <summary>Separate from personal careers. Completed scans only; first route coordinates remain stable for accepted flights.</summary>
public sealed class SimulatorAirportStore(string root)
{
    private readonly string path = Path.Combine(Path.GetFullPath(root), "simulator-airports.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private void Safe()
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(path)!); dir is not null; dir = dir.Parent)
            if (dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Airport cache cannot use filesystem links.");
        if (File.Exists(path) && (new FileInfo(path).Length > 1_000_000 || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))) throw new IOException("Unsafe airport cache preserved.");
    }
    public SimulatorAirportCache? Load()
    {
        Safe();
        if (!File.Exists(path)) return null;
        var data = JsonSerializer.Deserialize<SimulatorAirportCache>(File.ReadAllText(path), Json) ?? throw new IOException("Empty airport cache preserved.");
        Validate(data); return data;
    }
    private static void Validate(SimulatorAirportCache data)
    {
        if (data.SchemaVersion != 1 || data.Scan is null || data.Scan.ObservedUtc == default || string.IsNullOrWhiteSpace(data.Scan.Source) ||
            data.Scan.Airports is null || data.Routes is null || data.Routes.Any(x=>x.Airport is null||x.ObservedUtc==default||string.IsNullOrWhiteSpace(x.Source)) || data.Routes.DistinctBy(x => x.Airport.Identifier).Count() != data.Routes.Length)
            throw new IOException("Unsupported airport cache preserved.");
        foreach (var row in data.Scan.Airports.Concat(data.Routes.Select(x=>x.Airport)))
            if (!EmployerDestinationCatalogue.Research.Any(x => x.Identifier == row.Identifier && row.Identifier.Length > 0) ||
                !double.IsFinite(row.Latitude) || !double.IsFinite(row.Longitude) || row.Latitude is < -90 or > 90 || row.Longitude is < -180 or > 180)
                throw new IOException("Invalid airport cache preserved.");
    }
    public SimulatorAirportCache Save(SimulatorAirportScan completeScan)
    {
        Safe();
        using var gate = new Mutex(false, "Local\\FrontierAirportCache-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..24]);
        if (!gate.WaitOne(TimeSpan.FromSeconds(10))) throw new IOException("Another airport lookup is being saved.");
        try
        {
            var prior = Load();
            var codes = EmployerDestinationCatalogue.Research.Select(x => x.Identifier).Where(x => x.Length > 0).ToHashSet();
            var scan = completeScan with { Airports = completeScan.Airports.Where(x => codes.Contains(x.Identifier)).Distinct().ToArray() };
            var routes = (prior?.Routes ?? []).Concat(scan.Airports.GroupBy(x => x.Identifier).Where(x => x.Count() == 1).Select(x => new SavedSimulatorAirport(x.Single(),scan.ObservedUtc,scan.Source))).DistinctBy(x => x.Airport.Identifier).ToArray();
            var result = new SimulatorAirportCache(1, scan, routes); Validate(result);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, result, Json); stream.Flush(true); }
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return result;
        }
        finally { gate.ReleaseMutex(); }
    }
}
