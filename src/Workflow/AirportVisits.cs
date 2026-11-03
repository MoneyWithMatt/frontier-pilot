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
            AircraftTypeId is null || AircraftTypeId.Length>0&&!CompanyAircraft.Types.Any(t => t.Id == AircraftTypeId) || AircraftDescription is null || AircraftDescription.Length > 160 ||
            SimulatorVersion is null || SimulatorVersion.Length > 100 || RecordedUtc == default || !Enum.IsDefined(Result) || !Enum.IsDefined(Issue) ||
            !double.IsFinite(Latitude) || !double.IsFinite(Longitude) || Latitude is <= -90 or >= 90 || Longitude is < -180 or > 180 ||
            Note is null || Note.Length > 500 || Result == AirportVisitResult.Problematic && (Issue == AirportIssue.None || string.IsNullOrWhiteSpace(Note)) ||
            Result == AirportVisitResult.Verified && Issue != AirportIssue.None || PublishedReference is null || PublishedReference.Length > 80)
            throw new InvalidDataException("Invalid airport visit: select the aircraft tested and supply a short problem note/category. Airport identity and route coordinates cannot be edited here.");
    }
}
public sealed record AirportPromptDismissal(string AirportKey, DateTimeOffset RecordedUtc)
{
    public void Validate()
    {
        if (!EmployerDestinationCatalogue.Research.Any(r=>r.Key==AirportKey)||RecordedUtc==default)
            throw new InvalidDataException("Invalid airport prompt acknowledgement; history preserved.");
    }
}
public interface IAirportVisitStore
{
    IReadOnlyList<AirportVisit> Load();
    void Append(AirportVisit visit);
    IReadOnlyList<AirportPromptDismissal> LoadDismissals();
    void Dismiss(AirportPromptDismissal dismissal);
}
public sealed class MemoryAirportVisitStore : IAirportVisitStore
{
    private readonly List<AirportVisit> visits = [];
    private readonly List<AirportPromptDismissal> dismissals = [];
    public IReadOnlyList<AirportVisit> Load() => visits.ToArray();
    public void Append(AirportVisit visit) { visit.Validate(); if (visits.Any(x => x.Id == visit.Id && x != visit)) throw new IOException("Visit identity conflict."); if (!visits.Any(x => x.Id == visit.Id)) visits.Add(visit); }
    public IReadOnlyList<AirportPromptDismissal> LoadDismissals()=>dismissals.ToArray();
    public void Dismiss(AirportPromptDismissal dismissal){dismissal.Validate();if(!dismissals.Any(d=>d.AirportKey==dismissal.AirportKey))dismissals.Add(dismissal);}
}

/// <summary>Player observations only. Never modifies airport coordinates, economy, flight proof or aircraft qualification.</summary>
public sealed class AirportVisitStore(string root) : IAirportVisitStore
{
    public string StoragePath { get; } = Path.Combine(Path.GetFullPath(root), "airport-visits.json");
    private sealed record Document(int SchemaVersion, AirportVisit[] Visits, AirportPromptDismissal[]? Dismissals = null);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter() } };
    private void Safe()
    {
        for (var d = new DirectoryInfo(Path.GetDirectoryName(StoragePath)!); d is not null; d = d.Parent)
            if (d.Exists && d.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Airport visits cannot use filesystem links.");
        if (File.Exists(StoragePath) && (new FileInfo(StoragePath).Length > 4_000_000 || File.GetAttributes(StoragePath).HasFlag(FileAttributes.ReparsePoint))) throw new IOException("Unsafe airport visit document preserved.");
    }
    private Document Read()
    {
        Safe(); if (!File.Exists(StoragePath)) return new(2,[],[]);
        Document data;
        try { data = JsonSerializer.Deserialize<Document>(File.ReadAllText(StoragePath), Json) ?? throw new IOException("Empty airport visit document preserved."); }
        catch (JsonException e) { throw new IOException("Unreadable airport visits preserved.", e); }
        if (data.SchemaVersion is not (1 or 2) || data.Visits is null || data.Visits.Length > 2000 || data.Visits.Any(x => x is null) || data.Visits.Select(x => x.Id).Distinct().Count() != data.Visits.Length ||
            data.SchemaVersion==2&&data.Dismissals is null || data.SchemaVersion==1&&data.Dismissals is {Length:>0}) throw new IOException("Unsupported airport visits preserved.");
        foreach (var visit in data.Visits) visit.Validate();
        var dismissed=data.Dismissals??[];
        if(dismissed.Length>2000||dismissed.Any(d=>d is null)||dismissed.Select(d=>d.AirportKey).Distinct().Count()!=dismissed.Length)throw new IOException("Invalid airport acknowledgements preserved.");
        foreach(var d in dismissed)d.Validate();
        return data with {SchemaVersion=2,Dismissals=dismissed};
    }
    public IReadOnlyList<AirportVisit> Load()=>Read().Visits;
    public IReadOnlyList<AirportPromptDismissal> LoadDismissals()=>Read().Dismissals!;
    public void Append(AirportVisit visit)
    {
        visit.Validate();
        Update(data=>{
            var existing=data.Visits.FirstOrDefault(x=>x.Id==visit.Id);
            if(existing is not null){if(existing!=visit)throw new IOException("Airport visit identity conflict; history preserved.");return data;}
            if(data.Visits.Length>=2000)throw new IOException("Airport visit history is full; preserved. Archive support is required before adding more.");
            return data with {Visits=data.Visits.Append(visit).ToArray()};
        });
    }
    public void Dismiss(AirportPromptDismissal dismissal)
    {
        dismissal.Validate();Update(data=>{
            var rows=data.Dismissals!;
            if(rows.Any(d=>d.AirportKey==dismissal.AirportKey))return data;
            if(rows.Length>=2000)throw new IOException("Airport acknowledgement history full; preserved.");
            return data with {Dismissals=rows.Append(dismissal).ToArray()};
        });
    }
    private void Update(Func<Document,Document> change)
    {
        Safe();
        using var gate = new Mutex(false, "Local\\FrontierAirportVisits-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StoragePath.ToUpperInvariant())))[..24]);
        if (!gate.WaitOne(TimeSpan.FromSeconds(10))) throw new IOException("Another airport visit is being saved.");
        try
        {
            var before=Read();var after=change(before);if(ReferenceEquals(before,after))return;
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            var temp = StoragePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, after, Json); stream.Flush(true); }
                if (File.Exists(StoragePath)) File.Replace(temp, StoragePath, StoragePath + ".previous"); else File.Move(temp, StoragePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { gate.ReleaseMutex(); }
    }
}
