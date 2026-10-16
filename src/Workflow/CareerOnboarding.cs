using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public enum CareerStage { New, PilotName, Region, Opportunities, LegacyActive, Employed }
public sealed record CareerEmployment(string EmployerId, string Role, string BaseId, string[] FleetAccess);
public sealed record CareerProfile(int SchemaVersion, CareerStage Stage, string PilotName, string? RegionId)
{
    public CareerEmployment? Employment { get; init; }
    public AircraftTypeBinding[] AircraftBindings {get;init;}=[];
    [JsonIgnore] public bool IsActive => Stage is CareerStage.LegacyActive or CareerStage.Employed;
    public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name)&&name.Trim().Length is >=2 and <=48&&
        name.Trim().Any(char.IsLetter)&&!name.Any(c=>char.IsControl(c)||char.IsSurrogate(c)||c is '<' or '>');
    public CareerProfile Validate()
    {
        if(SchemaVersion is <1 or >2||!Enum.IsDefined(Stage))throw new IOException("Unsupported career profile. File preserved.");
        if (Stage==CareerStage.Employed)
        {
            var employer=CareerCatalogue.Employers.SingleOrDefault(x=>x.Id==Employment?.EmployerId);
            if(SchemaVersion!=2||!ValidName(PilotName)||employer is null||RegionId!=employer.RegionId||Employment!.Role!=employer.Role||Employment.BaseId!=employer.BaseId||Employment.FleetAccess is null||!Employment.FleetAccess.SequenceEqual(employer.Fleet))
                throw new IOException("Invalid employment assignment. File preserved.");
        }
        else if(Employment is not null)throw new IOException("Unexpected employment assignment. File preserved.");
        if(AircraftBindings is null||AircraftBindings.Select(x=>x.Title+"\n"+x.Model).Distinct().Count()!=AircraftBindings.Length||
            AircraftBindings.Any(x=>string.IsNullOrWhiteSpace(x.Title)||string.IsNullOrWhiteSpace(x.Model)||x.Title.Length>128||x.Model.Length>128||!CompanyAircraft.Types.Any(t=>t.Id==x.AircraftTypeId)))
            throw new IOException("Invalid aircraft compatibility mappings. File preserved.");
        if(PilotName is null||PilotName.Length>48||PilotName.Any(char.IsControl))throw new IOException("Invalid saved pilot name. File preserved.");
        if(Stage is CareerStage.Region or CareerStage.Opportunities && !ValidName(PilotName))throw new IOException("Invalid saved pilot name. File preserved.");
        if(Stage==CareerStage.Opportunities && !CareerCatalogue.Regions.Any(r=>r.Id==RegionId))throw new IOException("Unknown saved region. File preserved.");
        return this;
    }
}
public sealed record CareerRegion(string Id,string Name,double Latitude,double Longitude,double MapResolution);
public sealed record CareerBase(string Id,string Name,string Identifier,double Latitude,double Longitude);
public sealed record CareerEmployer(string Id,string RegionId,string BaseId,string Name,string Role,string Aircraft,string Description,string Work)
{
    public string Category {get;init;}="";
    public string[] Fleet {get;init;}=[];
    public string[] CargoMissions {get;init;}=[];
    public decimal[] PrototypeCargoKilograms {get;init;}=[];
    public decimal PrototypePaymentPerKilogram {get;init;}=2;
    public int PreferredMinimumMinutes {get;init;}
    public int PreferredMaximumMinutes {get;init;}
    public string Availability=>"ACCEPT EMPLOYMENT · destinations subject to verification";
    public override string ToString()=>Name;
}
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
        if(data.Employers.Any(e=>e.Fleet.Length==0||e.Fleet.Distinct().Count()!=e.Fleet.Length||e.Fleet.Any(id=>!CompanyAircraft.Types.Any(t=>t.Id==id))||
            e.CargoMissions.Length==0||e.PrototypeCargoKilograms.Length!=e.CargoMissions.Length||e.PrototypeCargoKilograms.Any(x=>x<=0)||e.PrototypePaymentPerKilogram<=0||e.PreferredMinimumMinutes<=0||e.PreferredMaximumMinutes<e.PreferredMinimumMinutes))throw new IOException("Invalid employer fleet or generation profile.");
        return data;
    }
}

/// <summary>Career shell, separate from authoritative gameplay ledgers. Reset executes before gameplay services start.</summary>
public sealed class CareerStore
{
    public static string DefaultRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FrontierAirportBuilder","desktop");
    private readonly string root;
    public string StorageRoot=>root;
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
        if(stage is CareerStage.LegacyActive or CareerStage.Employed)throw new IOException("Use explicit employment acceptance to activate a career.");
        if(Load().IsActive)throw new IOException("Existing careers cannot be replaced by onboarding.");
        var profile=new CareerProfile(1,stage,name.Trim(),regionId).Validate();Write(ProfilePath,profile);return profile;
    }
    public CareerProfile AcceptEmployment(string employerId)
    {
        var draft=Load();
        if(draft.Stage!=CareerStage.Opportunities)throw new IOException("Select a region before accepting employment; active careers cannot change employer.");
        var employer=CareerCatalogue.Employers.SingleOrDefault(x=>x.Id==employerId&&x.RegionId==draft.RegionId)??throw new IOException("Unknown employer for this region.");
        var profile=(draft with {SchemaVersion=2,Stage=CareerStage.Employed,Employment=new(employer.Id,employer.Role,employer.BaseId,employer.Fleet.ToArray())}).Validate();
        Write(ProfilePath,profile);return profile;
    }
    public void BindAircraft(FlightAircraftIdentity identity,string typeId)
    {
        identity.EnsureValid();var profile=Load();
        if(profile.Employment is null||!profile.Employment.FleetAccess.Contains(typeId)||!CompanyAircraft.Types.Any(x=>x.Id==typeId&&x.Enabled))throw new IOException("Choose an available company aircraft type.");
        var binding=new AircraftTypeBinding(identity.Title,identity.Type,typeId);
        var existing=profile.AircraftBindings.SingleOrDefault(x=>x.Title==identity.Title&&x.Model==identity.Type);
        if(existing is not null&&existing!=binding)throw new IOException("This exact simulator identity is already assigned to another aircraft type. Reset the test career to change it.");
        Write(ProfilePath,(profile with {AircraftBindings=profile.AircraftBindings.Where(x=>x!=binding).Append(binding).ToArray()}).Validate());
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
    public void AcceptEmployment()
    {if(employer is null)throw new IOException("Choose an employer.");Profile=store.AcceptEmployment(employer.Id);Notify();}
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
