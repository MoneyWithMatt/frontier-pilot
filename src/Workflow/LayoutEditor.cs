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
    private readonly Stack<(ParkingStand[] Stands,Taxiway[] Taxiways)> undo=new(),redo=new();
    private readonly Func<AirportSite> site;
    private Taxiway[] taxiways=[];
    private string saved="[][]";
    private int? selected;
    private string east="",north="",heading="",radius="",message="";
    private ParkingStandType type;
    private ParkingStandName name;
    public LayoutEditor(Func<bool> canEdit,Func<AirportSite>? site=null) { this.canEdit=canEdit;this.site=site??(()=>new(200,20,0)); }
    private int? selectedTaxiway;
    private TaxiwayVertex[] draft=[];
    private string taxiwayName="",taxiwayWidth="12";
    public IReadOnlyList<Taxiway> Taxiways=>Array.AsReadOnly(taxiways);
    public IReadOnlyList<TaxiwayVertex> DraftPoints=>Array.AsReadOnly(draft);
    public bool IsDrawingTaxiway {get;private set;}
    public bool CanFinishTaxiway=>canEdit()&&IsDrawingTaxiway&&draft.Length>=2;
    public Taxiway? SelectedTaxiway=>taxiways.FirstOrDefault(t=>t.Number==selectedTaxiway);
    public bool HasTaxiwaySelection=>canEdit()&&SelectedTaxiway!=null;
    public string TaxiwayName {get=>taxiwayName;set{taxiwayName=value;Notify();}}
    public string TaxiwayWidth {get=>taxiwayWidth;set{taxiwayWidth=value;Notify();}}
    public bool HasPendingTaxiwayProperties=>SelectedTaxiway is {} t && (taxiwayName!=t.Name||taxiwayWidth!=t.WidthMeters.ToString("R",CultureInfo.InvariantCulture));
    public string TaxiwayInstructions=>IsDrawingTaxiway?$"Drawing: {draft.Length} points. Click each bend, then Finish. Click a parking circle to connect. Escape cancels.":"New taxiway: click runway centreline, each bend, then the parking circle. Surface follows the runway. Select a line to change width/name or delete it.";
    public void SelectTaxiway(int? number){selected=null;selectedTaxiway=number;ReadTaxiwayProperties();Notify();}
    private void ReadTaxiwayProperties(){if(SelectedTaxiway is {} t){taxiwayName=t.Name;taxiwayWidth=t.WidthMeters.ToString("R",CultureInfo.InvariantCulture);}}
    public void BeginTaxiway(){if(!canEdit())return;Select(null);draft=[];IsDrawingTaxiway=true;Edited?.Invoke(this,EventArgs.Empty);Notify();}
    public void CancelTaxiway(){draft=[];IsDrawingTaxiway=false;Notify();}
    public void AddTaxiwayPoint(LocalPoint p)
    {
        if(!canEdit()||!IsDrawingTaxiway)return;
        if(draft.Length>=128){message="Use at most 128 points.";Notify();return;}
        if(draft.Length>1&&draft[^1].ParkingNumber!=null){message="Parking must be the last point. Finish this path.";Notify();return;}
        var stand=stands.FirstOrDefault(s=>TaxiwayGeometry.Distance(p,new(s.EastMeters,s.NorthMeters))<=s.RadiusMeters);
        TaxiwayVertex v;
        if(stand!=null)v=new(0,0,stand.Number);
        else
        {
            var runway=LocalCoordinates.ToRunway(p,site().TrueHeading);
            if(Math.Abs(runway.East)<=site().RunwayWidth/2&&Math.Abs(runway.North)<=site().RunwayLength/2)
                p=LocalCoordinates.FromRunway(0,runway.North,site().TrueHeading);
            v=new(p.East,p.North);
        }
        if(!site().Contains(v.Resolve(stands),6)){message=AirportSite.BoundaryMessage;Notify();return;}
        if(draft.Length>0&&TaxiwayGeometry.Distance(draft[^1].Resolve(stands),v.Resolve(stands))<1){message="Place the next point at least 1 m away.";Notify();return;}
        draft=[..draft,v];message="";Notify();
    }
    public void FinishTaxiway()
    {
        if(!CanFinishTaxiway)return;
        int number=Enumerable.Range(1,255).FirstOrDefault(n=>taxiways.All(t=>t.Number!=n&&t.Name!="T"+n));
        var next=new Taxiway(number,"T"+number,12,Array.AsReadOnly(draft.ToArray()));
        if(!CommitTaxiways([..taxiways,next]))return;
        CancelTaxiway();SelectTaxiway(number);
    }
    public void ApplyTaxiway()
    {
        if(!HasTaxiwaySelection)return;
        if(!double.TryParse(taxiwayWidth,NumberStyles.Float,CultureInfo.InvariantCulture,out var width)){message="Enter a taxiway width in metres.";Notify();return;}
        CommitTaxiways(taxiways.Select(t=>t.Number==selectedTaxiway?t with{Name=taxiwayName.ToUpperInvariant(),WidthMeters=width}:t).ToArray());
    }
    private static Taxiway[] Freeze(IReadOnlyList<Taxiway> paths)=>paths.Select(t=>t with {Points=Array.AsReadOnly(t.Points.ToArray())}).ToArray();
    private bool CommitTaxiways(Taxiway[] next,bool validate=true)
    {
        if(!canEdit())return false;
        var errors=TaxiwayGeometry.Validate(next,stands,site());
        if(validate&&errors.Count>0){message=string.Join("\n",errors);Notify();return false;}
        if(taxiways.SequenceEqual(next))return true;
        undo.Push((stands,taxiways));redo.Clear();taxiways=Freeze(next);Changed();return true;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Edited;
    public IReadOnlyList<ParkingStand> Stands=>Array.AsReadOnly(stands);
    public int? SelectedNumber=>selected;
    public ParkingStand? Selected=>stands.FirstOrDefault(s=>s.Number==selected);
    public bool CanUndo=>canEdit()&&undo.Count>0;
    public bool CanRedo=>canEdit()&&redo.Count>0;
    public bool CanChange=>canEdit();
    public bool HasSelection=>canEdit()&&Selected!=null;
    public bool IsDirty=>Signature(stands)+JsonSerializer.Serialize(taxiways)!=saved;
    public string Message=>message;
    public bool HasPendingProperties => IsDrawingTaxiway || HasPendingTaxiwayProperties || Selected is {} s && (east!=s.EastMeters.ToString("R",CultureInfo.InvariantCulture) ||
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
    public void Select(int? number) { selectedTaxiway=null;selected=number;message="";ReadProperties();Notify(); }
    private void ReadProperties()
    {
        if(Selected is not {} s)return;
        east=s.EastMeters.ToString("R",CultureInfo.InvariantCulture);north=s.NorthMeters.ToString("R",CultureInfo.InvariantCulture);
        heading=s.TrueHeadingDegrees.ToString("R",CultureInfo.InvariantCulture);radius=s.RadiusMeters.ToString(CultureInfo.InvariantCulture);type=s.Type;name=s.Name;
    }
    public void Add(LocalPoint point,double facing)
    {
        if(!canEdit()||IsDrawingTaxiway||stands.Length>=8)return;
        int number=Enumerable.Range(1,99).First(n=>stands.All(s=>s.Number!=n));
        selectedTaxiway=null;selected=number;
        Commit([..stands,new(number,point.East,point.North,Headings.Normalize(facing),10)]);
    }
    public void Move(int number,LocalPoint point) => Update(number,s=>s with { EastMeters=point.East,NorthMeters=point.North });
    public void Rotate(double degrees) {if(selected is {} n)Update(n,s=>s with {TrueHeadingDegrees=Headings.Normalize(s.TrueHeadingDegrees+degrees)});}
    public void Delete()
    {
        if(!canEdit())return;
        if(SelectedTaxiway!=null){CommitTaxiways(taxiways.Where(t=>t.Number!=selectedTaxiway).ToArray(),validate:false);return;}
        if(Selected is not {} stand)return;
        undo.Push((stands,taxiways));redo.Clear();
        taxiways=Freeze(taxiways.Select(t=>t with {Points=t.Points.Select(v=>v.ParkingNumber==stand.Number?new TaxiwayVertex(stand.EastMeters,stand.NorthMeters):v).ToArray()}).ToArray());
        stands=stands.Where(s=>s.Number!=selected).ToArray();Changed();
    }
    public void Apply()
    {
        if(!HasSelection)return;
        bool Parse(string text,out double n)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&double.IsFinite(n);
        if(!Parse(east,out var e)||!Parse(north,out var n)||!Parse(heading,out var h)||!Parse(radius,out var r)||!site().Contains(new(e,n),r)||r is <5 or >30)
        {message="Enter finite offsets inside the airport site, a heading, and a radius of 5–30 m.";Notify();return;}
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
        if(next.Any(s=>!double.IsFinite(s.EastMeters+s.NorthMeters+s.TrueHeadingDegrees+s.RadiusMeters)||!site().Contains(new(s.EastMeters,s.NorthMeters),s.RadiusMeters)))
        {message=AirportSite.BoundaryMessage;Notify();return;}
        undo.Push((stands,taxiways));redo.Clear();stands=next;Changed();
    }
    public void Undo() {if(!CanUndo)return;redo.Push((stands,taxiways));(stands,taxiways)=undo.Pop();CancelTaxiway();Changed();}
    public void Redo() {if(!CanRedo)return;undo.Push((stands,taxiways));(stands,taxiways)=redo.Pop();CancelTaxiway();Changed();}
    private void Changed() {message="";if(Selected==null)selected=null;if(SelectedTaxiway==null)selectedTaxiway=null;ReadTaxiwayProperties();ReadProperties();Edited?.Invoke(this,EventArgs.Empty);Notify();}
    public void Reset() {stands=[];taxiways=[];undo.Clear();redo.Clear();selected=null;selectedTaxiway=null;CancelTaxiway();saved="[][]";Changed();}
    public void Replace(IReadOnlyList<ParkingStand> items,IReadOnlyList<Taxiway>? paths=null)
    {
        if(!canEdit())return;
        undo.Push((stands,taxiways));redo.Clear();stands=items.ToArray();taxiways=Freeze(paths??[]);CancelTaxiway();Changed();
    }
    public static string Signature(IReadOnlyList<ParkingStand> items)=>JsonSerializer.Serialize(items.OrderBy(s=>s.Number));
    public void MarkSaved(IReadOnlyList<ParkingStand> snapshot) {saved=Signature(snapshot)+JsonSerializer.Serialize(taxiways);Notify();}
    public void Notify()=>PropertyChanged?.Invoke(this,new(""));
}
public sealed record LayoutSnapshot(int Version, SurveyPoint Anchor,double RunwayLengthMeters,double RunwayWidthMeters,IReadOnlyList<ParkingStand> ParkingStands)
{ public IReadOnlyList<Taxiway> Taxiways {get;init;}=[]; }
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
        var taxiErrors=TaxiwayGeometry.Validate(file.Taxiways,file.ParkingStands,new(length,width,anchor.TrueHeadingDegrees));
        if(taxiErrors.Count>0)throw new IOException(string.Join("\n",taxiErrors));
    }
}
