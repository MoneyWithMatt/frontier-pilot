using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrontierAirportBuilder.Workflow;

public enum AirportVisitResult { Verified, Problematic }
public enum AirportIssue { None, AirportMissing, IncorrectLocation, RunwayUnusable, SceneryOrTerrain, AircraftUnsuitable, Other }
public sealed record AirportVisit(Guid Id, string AirportKey, string LocationId, string AirportName, string PublishedReference,
    double Latitude, double Longitude, string AircraftTypeId, string AircraftDescription, Guid? SelectedAirframeId,
    string SimulatorVersion, DateTimeOffset RecordedUtc, AirportVisitResult Result, AirportIssue Issue, string Note)
{
    public void Validate()
    {
        if (Id == Guid.Empty || !EmployerDestinationCatalogue.Research.Any(r => r.Key == AirportKey) ||
            string.IsNullOrWhiteSpace(LocationId) || string.IsNullOrWhiteSpace(AirportName) || AirportName.Length > 160 ||
            !CompanyAircraft.Types.Any(t => t.Id == AircraftTypeId) || string.IsNullOrWhiteSpace(AircraftDescription) || AircraftDescription.Length > 160 ||
            SimulatorVersion is null || SimulatorVersion.Length > 100 || RecordedUtc == default || !Enum.IsDefined(Result) || !Enum.IsDefined(Issue) ||
            !double.IsFinite(Latitude) || !double.IsFinite(Longitude) || Latitude is <= -90 or >= 90 || Longitude is < -180 or > 180 ||
            Note is null || Note.Length > 500 || Result == AirportVisitResult.Problematic && (Issue == AirportIssue.None || string.IsNullOrWhiteSpace(Note)) ||
            Result == AirportVisitResult.Verified && Issue != AirportIssue.None || PublishedReference is null || PublishedReference.Length > 80)
            throw new InvalidDataException("Invalid airport visit: select the aircraft tested and supply a short problem note/category. Airport identity and route coordinates cannot be edited here.");
    }
}
public interface IAirportVisitStore { IReadOnlyList<AirportVisit> Load(); void Append(AirportVisit visit); }
public sealed class MemoryAirportVisitStore : IAirportVisitStore
{
    private readonly List<AirportVisit> visits = [];
    public IReadOnlyList<AirportVisit> Load() => visits.ToArray();
    public void Append(AirportVisit visit) { visit.Validate(); if (visits.Any(x => x.Id == visit.Id && x != visit)) throw new IOException("Visit identity conflict."); if (!visits.Any(x => x.Id == visit.Id)) visits.Add(visit); }
}

/// <summary>Player observations only. Never modifies airport coordinates, economy, flight proof or aircraft qualification.</summary>
public sealed class AirportVisitStore(string root) : IAirportVisitStore
{
    public string StoragePath { get; } = Path.Combine(Path.GetFullPath(root), "airport-visits.json");
    private sealed record Document(int SchemaVersion, AirportVisit[] Visits);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter() } };
    private void Safe()
    {
        for (var d = new DirectoryInfo(Path.GetDirectoryName(StoragePath)!); d is not null; d = d.Parent)
            if (d.Exists && d.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Airport visits cannot use filesystem links.");
        if (File.Exists(StoragePath) && (new FileInfo(StoragePath).Length > 4_000_000 || File.GetAttributes(StoragePath).HasFlag(FileAttributes.ReparsePoint))) throw new IOException("Unsafe airport visit document preserved.");
    }
    public IReadOnlyList<AirportVisit> Load()
    {
        Safe(); if (!File.Exists(StoragePath)) return [];
        Document data;
        try { data = JsonSerializer.Deserialize<Document>(File.ReadAllText(StoragePath), Json) ?? throw new IOException("Empty airport visit document preserved."); }
        catch (JsonException e) { throw new IOException("Unreadable airport visits preserved.", e); }
        if (data.SchemaVersion != 1 || data.Visits is null || data.Visits.Length > 2000 || data.Visits.Any(x => x is null) || data.Visits.Select(x => x.Id).Distinct().Count() != data.Visits.Length) throw new IOException("Unsupported airport visits preserved.");
        foreach (var visit in data.Visits) visit.Validate();
        return data.Visits;
    }
    public void Append(AirportVisit visit)
    {
        Safe(); visit.Validate();
        using var gate = new Mutex(false, "Local\\FrontierAirportVisits-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StoragePath.ToUpperInvariant())))[..24]);
        if (!gate.WaitOne(TimeSpan.FromSeconds(10))) throw new IOException("Another airport visit is being saved.");
        try
        {
            var visits = Load(); var existing = visits.FirstOrDefault(x => x.Id == visit.Id);
            if (existing is not null) { if (existing != visit) throw new IOException("Airport visit identity conflict; history preserved."); return; }
            if (visits.Count >= 2000) throw new IOException("Airport visit history is full; preserved. Archive support is required before adding more.");
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            var temp = StoragePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, new Document(1, visits.Append(visit).ToArray()), Json); stream.Flush(true); }
                if (File.Exists(StoragePath)) File.Replace(temp, StoragePath, StoragePath + ".previous"); else File.Move(temp, StoragePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { gate.ReleaseMutex(); }
    }
}
