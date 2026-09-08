using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;
public sealed class LayoutEditor : INotifyPropertyChanged
{
    private readonly Func<bool> canEdit;
    private ParkingStand[] stands=[];
    private readonly Stack<ParkingStand[]> undo=new(),redo=new();
    private string saved="[]";
    private int? selected;
    private string east="",north="",heading="",radius="",message="";
    private ParkingStandType type;
    private ParkingStandName name;
    public LayoutEditor(Func<bool> canEdit) { this.canEdit=canEdit; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Edited;
    public IReadOnlyList<ParkingStand> Stands=>Array.AsReadOnly(stands);
    public int? SelectedNumber=>selected;
    public ParkingStand? Selected=>stands.FirstOrDefault(s=>s.Number==selected);
    public bool CanUndo=>canEdit()&&undo.Count>0;
    public bool CanRedo=>canEdit()&&redo.Count>0;
    public bool CanChange=>canEdit();
    public bool HasSelection=>canEdit()&&Selected!=null;
    public bool IsDirty=>Signature(stands)!=saved;
    public string Message=>message;
    public bool HasPendingProperties => Selected is {} s && (east!=s.EastMeters.ToString("R",CultureInfo.InvariantCulture) ||
        north!=s.NorthMeters.ToString("R",CultureInfo.InvariantCulture) || heading!=s.TrueHeadingDegrees.ToString("R",CultureInfo.InvariantCulture) ||
        radius!=s.RadiusMeters.ToString(CultureInfo.InvariantCulture) || type!=s.Type || name!=s.Name);
    public string East {get=>east;set {east=value;Notify();}}
    public string North {get=>north;set {north=value;Notify();}}
    public string Heading {get=>heading;set {heading=value;Notify();}}
    public string Radius {get=>radius;set {radius=value;Notify();}}
    public ParkingStandType Type {get=>type;set {type=value;Notify();}}
    public ParkingStandName Name {get=>name;set {name=value;Notify();}}
    public Array Types=>Enum.GetValues<ParkingStandType>();
    public Array Names=>Enum.GetValues<ParkingStandName>();
    public void Select(int? number) { selected=number;message="";ReadProperties();Notify(); }
    private void ReadProperties()
    {
        if(Selected is not {} s)return;
        east=s.EastMeters.ToString("R",CultureInfo.InvariantCulture);north=s.NorthMeters.ToString("R",CultureInfo.InvariantCulture);
        heading=s.TrueHeadingDegrees.ToString("R",CultureInfo.InvariantCulture);radius=s.RadiusMeters.ToString(CultureInfo.InvariantCulture);type=s.Type;name=s.Name;
    }
    public void Add(LocalPoint point,double facing)
    {
        if(!canEdit()||stands.Length>=8)return;
        int number=Enumerable.Range(1,99).First(n=>stands.All(s=>s.Number!=n));
        selected=number;
        Commit([..stands,new(number,point.East,point.North,Headings.Normalize(facing),10)]);
    }
    public void Move(int number,LocalPoint point) => Update(number,s=>s with { EastMeters=point.East,NorthMeters=point.North });
    public void Rotate(double degrees) {if(selected is {} n)Update(n,s=>s with {TrueHeadingDegrees=Headings.Normalize(s.TrueHeadingDegrees+degrees)});}
    public void Delete() {if(canEdit()&&Selected!=null)Commit(stands.Where(s=>s.Number!=selected).ToArray());}
    public void Apply()
    {
        if(!HasSelection)return;
        bool Parse(string text,out double n)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&double.IsFinite(n);
        if(!Parse(east,out var e)||!Parse(north,out var n)||!Parse(heading,out var h)||!Parse(radius,out var r)||Math.Abs(e)>500||Math.Abs(n)>500||r is <5 or >30)
        {message="Enter finite east/north offsets within ±500 m, a heading, and a radius of 5–30 m.";Notify();return;}
        message="";
        Update(selected!.Value,s=>s with {EastMeters=e,NorthMeters=n,TrueHeadingDegrees=Headings.Normalize(h),RadiusMeters=r,Type=type,Name=name});Notify();
    }
    private void Update(int number,Func<ParkingStand,ParkingStand> update)
    {
        if(!canEdit())return;
        Commit(stands.Select(s=>s.Number==number?update(s):s).ToArray());
    }
    private void Commit(ParkingStand[] next)
    {
        if(!canEdit()||stands.SequenceEqual(next))return;
        if(next.Any(s=>!double.IsFinite(s.EastMeters+s.NorthMeters+s.TrueHeadingDegrees+s.RadiusMeters)||Math.Abs(s.EastMeters)>500||Math.Abs(s.NorthMeters)>500))
        {message="Placement must remain within ±500 metres of the anchor.";Notify();return;}
        undo.Push(stands);redo.Clear();stands=next;Changed();
    }
    public void Undo() {if(!CanUndo)return;redo.Push(stands);stands=undo.Pop();Changed();}
    public void Redo() {if(!CanRedo)return;undo.Push(stands);stands=redo.Pop();Changed();}
    private void Changed() {message="";if(Selected==null)selected=null;ReadProperties();Edited?.Invoke(this,EventArgs.Empty);Notify();}
    public void Reset() {stands=[];undo.Clear();redo.Clear();selected=null;saved="[]";Changed();}
    public void Replace(IReadOnlyList<ParkingStand> items) {Commit(items.ToArray());}
    public static string Signature(IReadOnlyList<ParkingStand> items)=>JsonSerializer.Serialize(items.OrderBy(s=>s.Number));
    public void MarkSaved(IReadOnlyList<ParkingStand> snapshot) {saved=Signature(snapshot);Notify();}
    public void Notify()=>PropertyChanged?.Invoke(this,new(""));
}
public sealed record LayoutSnapshot(int Version, SurveyPoint Anchor,double RunwayLengthMeters,double RunwayWidthMeters,IReadOnlyList<ParkingStand> ParkingStands);
public static class LayoutFiles
{
    private static readonly JsonSerializerOptions Json=new() {WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,Converters={new JsonStringEnumConverter()}};
    public static Task SaveNewAsync(string path,LayoutSnapshot snapshot)=>DiagnosticLog.SaveNewAsync(path,JsonSerializer.Serialize(snapshot,Json));
    public static async Task<LayoutSnapshot> ReadAsync(string path)
    {
        MsfsSdk.CommunityInstaller.SafePath(path);
        if(new FileInfo(path).Length>1000000)throw new IOException("Layout file is too large.");
        return JsonSerializer.Deserialize<LayoutSnapshot>(await File.ReadAllTextAsync(path),Json)??throw new IOException("Layout file is empty.");
    }
    public static void ValidateForAnchor(LayoutSnapshot file,SurveyPoint anchor,double length,double width)
    {
        if(file.Version!=1||file.Anchor!=anchor||file.RunwayLengthMeters!=length||file.RunwayWidthMeters!=width)
            throw new IOException("Layout belongs to a different survey or runway. Capture provenance cannot be substituted.");
        var errors=ParkingGeometry.Validate(file.ParkingStands,length,width,anchor.TrueHeadingDegrees);
        if(errors.Count>0)throw new IOException(string.Join("\n",errors));
    }
}


