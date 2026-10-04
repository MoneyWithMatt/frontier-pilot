using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public enum CareerStage { New, PilotName, Region, Opportunities, LegacyActive }
public sealed record CareerProfile(int SchemaVersion, CareerStage Stage, string PilotName, string? RegionId)
{
    [JsonIgnore] public bool IsActive => Stage==CareerStage.LegacyActive;
    public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name)&&name.Trim().Length is >=2 and <=48&&
        name.Trim().Any(char.IsLetter)&&!name.Any(c=>char.IsControl(c)||char.IsSurrogate(c)||c is '<' or '>');
    public CareerProfile Validate()
    {
        if(SchemaVersion!=1||!Enum.IsDefined(Stage))throw new IOException("Unsupported career profile. File preserved.");
        if(PilotName is null||PilotName.Length>48||PilotName.Any(char.IsControl))throw new IOException("Invalid saved pilot name. File preserved.");
        if(Stage is CareerStage.Region or CareerStage.Opportunities && !ValidName(PilotName))throw new IOException("Invalid saved pilot name. File preserved.");
        if(Stage==CareerStage.Opportunities && !CareerCatalogue.Regions.Any(r=>r.Id==RegionId))throw new IOException("Unknown saved region. File preserved.");
        return this;
    }
}
public sealed record CareerRegion(string Id,string Name,double Latitude,double Longitude,double MapResolution);
public sealed record CareerBase(string Id,string Name,string Identifier,double Latitude,double Longitude);
public sealed record CareerEmployer(string Id,string RegionId,string BaseId,string Name,string Role,string Aircraft,string Description,string Work)
{ public string Availability=>"COMING SOON"; public override string ToString()=>Name; }
public sealed record CareerDefinitions(CareerRegion[] Regions,CareerBase[] Bases,CareerEmployer[] Employers);
public static class CareerCatalogue
{
    private static readonly CareerDefinitions Data=Load();
    public static IReadOnlyList<CareerRegion> Regions=>Data.Regions;
    public static IReadOnlyList<CareerBase> Bases=>Data.Bases;
    public static IReadOnlyList<CareerEmployer> Employers=>Data.Employers;
    private static CareerDefinitions Load()
    {
        using var stream=typeof(CareerCatalogue).Assembly.GetManifestResourceStream("FrontierAirportBuilder.Workflow.CareerCatalogue.json")!;
        var data=JsonSerializer.Deserialize<CareerDefinitions>(stream)??throw new IOException("Career catalogue missing.");
        if(data.Regions.Select(r=>r.Id).Distinct().Count()!=data.Regions.Length||data.Bases.Select(b=>b.Id).Distinct().Count()!=data.Bases.Length||data.Employers.Select(e=>e.Id).Distinct().Count()!=data.Employers.Length||
            data.Employers.Any(e=>!data.Regions.Any(r=>r.Id==e.RegionId)||!data.Bases.Any(b=>b.Id==e.BaseId)))throw new IOException("Career catalogue identities and references must be valid.");
        return data;
    }
}

/// <summary>Career shell, separate from authoritative gameplay ledgers. Reset executes before gameplay services start.</summary>
public sealed class CareerStore
{
    public static string DefaultRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FrontierAirportBuilder","desktop");
    private readonly string root;
    private readonly Action<string>? resetCheckpoint;
    private string ProfilePath=>Path.Combine(root,"career.json");
    private string PendingPath=>Path.Combine(root,"career-reset.json");
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,Converters={new JsonStringEnumConverter()}};
    public static IReadOnlyList<string> ResetFiles {get;}=["career.json","economy/economy-state.json","economy/flight-attempts.json","economy/aircraft-loading.json","tracking/aircraft-state.json"];
    public CareerStore(string rootPath) : this(rootPath,null) { }
    internal CareerStore(string rootPath,Action<string>? checkpoint) {root=Path.GetFullPath(rootPath);resetCheckpoint=checkpoint;}
    private void Safe(string path)
    {
        for(var directory=new DirectoryInfo(root);directory is not null;directory=directory.Parent)
            if(directory.Exists&&directory.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("Career path cannot use filesystem links.");
        var relative=Path.GetRelativePath(root,path);
        if(relative.StartsWith("..")||Path.IsPathRooted(relative))throw new IOException("Career path escaped its storage root.");
        var current=root;
        foreach(var segment in relative.Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar))
        {current=Path.Combine(current,segment);if((File.Exists(current)||Directory.Exists(current))&&File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))throw new IOException("Career path cannot use filesystem links.");}
    }
    public CareerProfile Load()
    {
        Safe(ProfilePath);
        CompletePendingReset();
        if(File.Exists(ProfilePath))return (JsonSerializer.Deserialize<CareerProfile>(File.ReadAllText(ProfilePath),Json)??throw new IOException("Career profile is empty. File preserved.")).Validate();
        // Presence of legacy gameplay files protects existing users, including unreadable saves.
        if(ResetFiles.Skip(1).Any(f=>File.Exists(Path.Combine(root,f))))
        {var legacy=new CareerProfile(1,CareerStage.LegacyActive,"Pilot",null);Write(ProfilePath,legacy);return legacy;}
        return new(1,CareerStage.New,"",null);
    }
    public CareerProfile SaveDraft(CareerStage stage,string name,string? regionId)
    {
        if(stage==CareerStage.LegacyActive)throw new IOException("Onboarding cannot activate a career.");
        if(Load().IsActive)throw new IOException("Existing careers cannot be replaced by onboarding.");
        var profile=new CareerProfile(1,stage,name.Trim(),regionId).Validate();Write(ProfilePath,profile);return profile;
    }
    public bool RequestReset(bool confirmed)
    {
        if(!confirmed)return false;
        // A persisted intent permits restart-safe replay. No gameplay state changes in a running session.
        Safe(PendingPath);if(!File.Exists(PendingPath))Write(PendingPath,Guid.NewGuid().ToString("N"));return true;
    }
    public void CompletePendingReset()
    {
        Safe(PendingPath);if(!File.Exists(PendingPath))return;
        var id=JsonSerializer.Deserialize<string>(File.ReadAllText(PendingPath),Json);
        if(id is null||id.Length!=32||!Guid.TryParseExact(id,"N",out _))throw new IOException("Invalid career reset intent. Files preserved.");
        var archive=Path.Combine(root,"career-archives",id);Safe(archive);
        // Preflight all source/destination paths before touching any files.
        foreach(var file in ResetFiles){Safe(Path.Combine(root,file));Safe(Path.Combine(archive,file));}
        foreach(var file in ResetFiles)
        {
            var source=Path.Combine(root,file);var destination=Path.Combine(archive,file);
            if(!File.Exists(source))continue;
            // Crash after writing the clean shell but before clearing the reset intent.
            if(file=="career.json"&&File.ReadAllText(source)==JsonSerializer.Serialize(new CareerProfile(1,CareerStage.New,"",null),Json))continue;
            if(File.Exists(destination))throw new IOException("Reset archive conflict. Files preserved.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Move(source,destination);
            resetCheckpoint?.Invoke(file);
        }
        Write(ProfilePath,new CareerProfile(1,CareerStage.New,"",null));
        resetCheckpoint?.Invoke("clean-profile");
        File.Delete(PendingPath);
    }
    private void Write<T>(string path,T value)
    {
        Safe(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,value,Json);stream.Flush(true);}File.Move(temporary,path,true);}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
}
public sealed class CareerViewModel : INotifyPropertyChanged
{
    private readonly CareerStore store;
    public CareerProfile Profile {get;private set;}
    private string name;
    private CareerRegion? region;
    private CareerEmployer? employer;
    private IReadOnlyList<CareerEmployer> employers=[];
    public event PropertyChangedEventHandler? PropertyChanged;
    public CareerViewModel(CareerStore store){this.store=store;Profile=store.Load();name=Profile.PilotName;region=Regions.FirstOrDefault(r=>r.Id==Profile.RegionId);employers=CareerCatalogue.Employers.Where(e=>e.RegionId==region?.Id).ToArray();}
    public bool IsActive=>Profile.IsActive;
    public CareerStage Stage=>Profile.Stage;
    public string PilotName {get=>name;set{if(name==value)return;name=value;Notify(nameof(PilotName));Notify(nameof(NameError));}}
    public string NameError=>CareerProfile.ValidName(name)?"":"Enter 2–48 characters, including a letter. Control characters and < > are not allowed.";
    public IReadOnlyList<CareerRegion> Regions=>CareerCatalogue.Regions;
    public CareerRegion? SelectedRegion {get=>region;set{if(region==value)return;region=value;employer=null;employers=CareerCatalogue.Employers.Where(e=>e.RegionId==region?.Id).ToArray();Notify();}}
    public IReadOnlyList<CareerEmployer> Employers=>employers;
    public CareerEmployer? SelectedEmployer {get=>employer;set{if(employer==value)return;employer=value;Notify(nameof(SelectedEmployer));Notify(nameof(EmployerBase));}}
    public string EmployerBase=>CareerCatalogue.Bases.FirstOrDefault(b=>b.Id==employer?.BaseId)?.Name??"";
    public bool Continue()
    {
        var next=Stage switch {CareerStage.New=>CareerStage.PilotName,CareerStage.PilotName=>CareerStage.Region,CareerStage.Region=>CareerStage.Opportunities,_=>Stage};
        if(Stage==CareerStage.PilotName&&!CareerProfile.ValidName(name)||Stage==CareerStage.Region&&region is null||IsActive||Stage==CareerStage.Opportunities)return false;
        Profile=store.SaveDraft(next,name,region?.Id);Notify();return true;
    }
    public void Back()
    {
        if(IsActive)return;
        var previous=Stage switch {CareerStage.Opportunities=>CareerStage.Region,CareerStage.Region=>CareerStage.PilotName,_=>CareerStage.New};
        Profile=store.SaveDraft(previous,name,region?.Id);Notify();
    }
    private void Notify(string property="")=>PropertyChanged?.Invoke(this,new(property));
}
