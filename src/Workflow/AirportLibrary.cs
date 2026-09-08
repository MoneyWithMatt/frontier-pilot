using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
namespace FrontierAirportBuilder.Workflow;

public sealed record SurveyArchive(SurveyPoint Point,IReadOnlyList<SurveyFrame> Frames,string MetadataJson,bool DryRun)
{
    public void Validate(bool dryRun)
    {
        if(DryRun!=dryRun)throw new IOException("Live and dry-run airports cannot be mixed.");
        if(Frames==null||Frames.Count is <16 or >1000||Point==null||SurveyCapture.ValidatePoint(Point)!=null)
            throw new IOException("Saved survey evidence is incomplete.");
        if(SurveyCapture.Create(Frames)!=Point)throw new IOException("Saved survey does not agree with its guarded avatar frames.");
        using var metadata=JsonDocument.Parse(MetadataJson);
        if(!dryRun && (metadata.RootElement.GetProperty("sdkVersion").GetString()!="1.2.4" ||
            metadata.RootElement.GetProperty("simulatorHash").GetString()!=LiveSurveyCapture.TestedSimulatorHash ||
            metadata.RootElement.GetProperty("sdkHash").GetString()!=LiveSurveyCapture.TestedSimConnectHash ||
            metadata.RootElement.GetProperty("headerHash").GetString()!=LiveSurveyCapture.TestedHeaderHash))
            throw new IOException("Saved survey is not from the qualified simulator/SDK build.");
    }
    public void VerifyInstalledQualification(string sdkRoot)
    {
        if(DryRun)return;
        Validate(false);var sdk=SdkInspector.Inspect(sdkRoot);
        using var metadata=JsonDocument.Parse(MetadataJson);
        string Hash(string path){CommunityInstaller.SafePath(path);using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
        if(sdk.Version!="1.2.4" || Hash(Path.Combine(sdk.Root,"SimConnect SDK","lib","SimConnect.dll"))!=LiveSurveyCapture.TestedSimConnectHash ||
            Hash(Path.Combine(sdk.Root,"SimConnect SDK","include","SimConnect.h"))!=LiveSurveyCapture.TestedHeaderHash ||
            Hash(metadata.RootElement.GetProperty("simulatorPath").GetString()!)!=LiveSurveyCapture.TestedSimulatorHash)
            throw new IOException("Installed SDK/simulator changed since the saved survey. Requalification is required before rebuilding.");
    }
    internal static SurveyArchive ReadCapture(string directory,SurveyPoint point,bool dryRun)
    {
        SurveyFrame[] frames;
        if(dryRun)frames=JsonSerializer.Deserialize<SurveyFrame[]>(File.ReadAllText(Path.Combine(directory,"recorded-frames.json")))!;
        else
        {
            var window=new List<SurveyFrame>();
            foreach(var line in File.ReadLines(Path.Combine(directory,"observations.jsonl")))
            {
                using var row=JsonDocument.Parse(line);
                if(row.RootElement.GetProperty("kind").GetString()!="frame")continue;
                var frame=row.RootElement.GetProperty("data").Deserialize<SurveyFrame>(new JsonSerializerOptions{NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals})!;window.Add(frame);
                while(window.Count>2&&frame.Seconds-window[1].Seconds>=5)window.RemoveAt(0);
            }
            frames=window.ToArray();
        }
        var archive=new SurveyArchive(point,frames,File.ReadAllText(Path.Combine(directory,"metadata.json")),dryRun);archive.Validate(dryRun);return archive;
    }
}
public sealed record AirportArtifact(string Key,string State,IReadOnlyList<PackageFile> Files);
public sealed record SavedAirport(int SchemaVersion,Guid Id,int Revision,DateTimeOffset SavedUtc,AirstripDefinition Definition,
    SurveyArchive Survey,IReadOnlyList<AirportArtifact> Artifacts,string LastBuild,string LastInstall)
{
    [JsonIgnore] public string Name=>Definition.Name;
    [JsonIgnore] public string Identifier=>Definition.Identifier;
    [JsonIgnore] public string Location=>string.Create(CultureInfo.InvariantCulture,$"{Definition.Latitude:F5}, {Definition.Longitude:F5}");
    [JsonIgnore] public string Status=>$"Build: {LastBuild}; install: {LastInstall} (history only)";
    public override string ToString()=>$"{Name} ({Identifier})";
}
public sealed record AirportList(IReadOnlyList<SavedAirport> Airports,IReadOnlyList<string> Warnings);
public sealed record AirportDeletionPlan(Guid AirportId,int Revision,string Name,string Identifier,IReadOnlyList<string> Projects,
    string? InstalledPackage,string Fingerprint,string Summary);
public interface IAirportManagement
{
    Guid? ActiveAirportId {get;}
    string LibraryDirectory {get;}
    Task<AirportList> ListAirportsAsync(CancellationToken token);
    Task<SavedAirport> SaveAirportAsync(AirstripDefinition definition,CancellationToken token);
    Task<SavedAirport> LoadAirportAsync(Guid id,CancellationToken token);
    Task<AirportDeletionPlan> PrepareDeleteAsync(Guid id,CancellationToken token);
    Task<string> DeleteAirportAsync(AirportDeletionPlan plan,bool confirmed,CancellationToken token);
    void NewAirport();
}

// All mutations are serialized across application instances by the service's Library.Lock lease.
// Paths in the document are opaque keys, never arbitrary deletion destinations.
internal sealed class AirportLibrary(string stateRoot,bool dryRun)
{
    internal static readonly JsonSerializerOptions Json=new(){WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,Converters={new JsonStringEnumConverter()}};
    public string DirectoryPath=>Path.Combine(stateRoot,"airports");
    public IDisposable Lock()
    {
        CommunityInstaller.SafePath(stateRoot);
        var name=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(stateRoot).ToUpperInvariant())))[..24];
        var gate=new Semaphore(1,1,"Local\\Frontier-Airports-"+name);
        if(!gate.WaitOne(0)){gate.Dispose();throw new IOException("Another Frontier airport operation is running. Retry when it finishes.");}
        return new Lease(gate);
    }
    private sealed class Lease(Semaphore gate):IDisposable{public void Dispose(){gate.Release();gate.Dispose();}}
    public string DocumentPath(Guid id)
    {
        if(id==Guid.Empty)throw new IOException("Airport key is missing.");
        var path=Path.Combine(DirectoryPath,id.ToString("N")+".json");CommunityInstaller.Within(path,DirectoryPath);return path;
    }
    public string ProjectPath(string key)
    {
        if(!Regex.IsMatch(key,"\\A[0-9a-f]{16}\\z"))throw new IOException("Invalid managed workspace key.");
        var path=Path.Combine(stateRoot,"projects",key);CommunityInstaller.Within(path,Path.Combine(stateRoot,"projects"));return path;
    }
    public SavedAirport Read(Guid id)
    {
        var path=DocumentPath(id);
        if(new FileInfo(path).Length>16_000_000)throw new IOException("Airport document is too large.");
        var saved=JsonSerializer.Deserialize<SavedAirport>(File.ReadAllText(path),Json)??throw new IOException("Empty airport document.");
        if(saved.Id!=id||saved.SchemaVersion!=1||saved.Revision<1)throw new IOException("Unsupported or mismatched airport document. File preserved.");
        saved.Definition.EnsureValid();CommunityInstaller.PackageName(saved.Identifier);saved.Survey.Validate(dryRun);
        MatchSurvey(saved.Definition,saved.Survey.Point);
        if(saved.Artifacts==null||saved.Artifacts.Count>10000)throw new IOException("Invalid workspace registry.");
        if(saved.Artifacts.Select(a=>a.Key).Distinct().Count()!=saved.Artifacts.Count)throw new IOException("Duplicate workspace registry entry.");
        foreach(var a in saved.Artifacts)ProjectPath(a.Key);
        return saved;
    }
    public AirportList List()
    {
        CommunityInstaller.SafePath(DirectoryPath);
        if(!Directory.Exists(DirectoryPath))return new([],[]);
        var rows=new List<SavedAirport>();var warnings=new List<string>();
        foreach(var path in Directory.EnumerateFiles(DirectoryPath,"*.json"))
        {
            if(!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path),"N",out var id)){warnings.Add($"Unrecognised file preserved: {Path.GetFileName(path)}");continue;}
            try{rows.Add(Read(id));}catch(Exception e){warnings.Add($"Cannot load {Path.GetFileName(path)}: {e.Message}");}
        }
        return new(rows.OrderBy(a=>a.Name,StringComparer.OrdinalIgnoreCase).ToArray(),warnings);
    }
    public SavedAirport Save(Guid? id,int? expectedRevision,AirstripDefinition definition,SurveyArchive survey)
    {
        definition.EnsureValid();CommunityInstaller.PackageName(definition.Identifier);survey.Validate(dryRun);MatchSurvey(definition,survey.Point);
        var list=List();if(list.Warnings.Count>0)throw new IOException("Resolve unreadable airport documents before saving; files were preserved. "+string.Join(" ",list.Warnings));
        SavedAirport? previous=id is {} key?Read(key):null;
        if(previous!=null&&(previous.Revision!=expectedRevision||previous.Identifier!=definition.Identifier))
            throw new IOException("This airport changed in another window, or its identifier changed. Reload it before saving.");
        if(list.Airports.Any(a=>a.Identifier==definition.Identifier&&a.Id!=id))throw new IOException("An airport with that identifier is already saved. Open it from My Airports or choose another identifier.");
        bool changed=previous!=null&&JsonSerializer.Serialize(previous.Definition,Json)!=JsonSerializer.Serialize(definition,Json);
        var saved=new SavedAirport(1,id??Guid.NewGuid(),(previous?.Revision??0)+1,DateTimeOffset.UtcNow,definition,survey,previous?.Artifacts??[],
            changed?"definition changed; rebuild required":previous?.LastBuild??"not built",previous?.LastInstall??"not installed");
        Write(saved);return saved;
    }
    public void Write(SavedAirport saved)
    {
        var path=DocumentPath(saved.Id);Directory.CreateDirectory(DirectoryPath);
        var temp=path+"."+Guid.NewGuid().ToString("N")[..8]+".tmp";CommunityInstaller.Within(temp,DirectoryPath);
        using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {JsonSerializer.Serialize(stream,saved,Json);stream.Flush(true);}
        // Atomic replacement on this volume. Readers see the previous complete document or the next one.
        File.Move(temp,path,true);
    }
    public static void MatchSurvey(AirstripDefinition d,SurveyPoint p)
    {
        if(d.Latitude!=p.Latitude||d.Longitude!=p.Longitude||d.AltitudeMeters!=p.AltitudeMeters||d.TrueHeadingDegrees!=p.TrueHeadingDegrees||d.MagneticVariationDegreesEast!=p.MagneticVariationDegrees)
            throw new IOException("Airport location must match its validated avatar survey evidence.");
    }
    public void MarkProject(string path,Guid airportId,string key)
    {
        CommunityInstaller.Within(path,Path.Combine(stateRoot,"projects"));
        File.WriteAllText(Path.Combine(path,"frontier-workspace.json"),JsonSerializer.Serialize(new {airportId,key},Json));
    }
    public void VerifyProject(SavedAirport saved,AirportArtifact artifact)
    {
        var path=ProjectPath(artifact.Key);
        if(!Directory.Exists(path)){if(File.Exists(path))throw new IOException("Workspace is a file.");return;}
        using var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(path,"frontier-workspace.json")));
        if(marker.RootElement.GetProperty("airportId").GetGuid()!=saved.Id||marker.RootElement.GetProperty("key").GetString()!=artifact.Key)
            throw new IOException("Workspace ownership does not match this airport.");
        CommunityInstaller.VerifyInventory(artifact.Files,CommunityInstaller.Inventory(path));
    }
}
