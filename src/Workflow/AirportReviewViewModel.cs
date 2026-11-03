using System.ComponentModel;

namespace FrontierAirportBuilder.Workflow;

/// <summary>Explicit player reports, separate from official data and flight/aircraft qualification.</summary>
public sealed class AirportReviewViewModel : INotifyPropertyChanged
{
    public sealed record IssueOption(AirportIssue Value,string Label) { public override string ToString()=>Label; }
    private readonly EmployerDestinationCatalogue catalogue;
    private readonly IAirportVisitStore store;
    private readonly AircraftFleetViewModel fleet;
    private readonly Func<Task> refresh;
    public Func<string,bool> CanVerifyAirport {get;set;} = _=>false;
    private string? endpoint;
    private bool busy;
    private bool aircraftEdited,descriptionEdited;
    private string note="", simulatorVersion="", aircraftDescription="";
    private CompanyAircraftType? selectedAircraft;
    private AirportIssue issue=AirportIssue.AirportMissing;
    public string Note {get=>note;set{note=value;Notify();}}
    public string SimulatorVersion {get=>simulatorVersion;set{simulatorVersion=value;Notify();}}
    public string AircraftDescription {get=>aircraftDescription;set{if(value!=aircraftDescription)descriptionEdited=true;aircraftDescription=value;Notify();}}
    public CompanyAircraftType? SelectedAircraft {get=>selectedAircraft;set{if(value is null||value.Id==selectedAircraft?.Id)return;aircraftEdited=true;if(!descriptionEdited)aircraftDescription=value.Name;selectedAircraft=value;Notify();}}
    public AirportIssue SelectedIssue {get=>issue;set{issue=value;Notify();}}
    public IReadOnlyList<CompanyAircraftType> AircraftTypes=>CompanyAircraft.Types;
    public IReadOnlyList<AirportIssue> Issues {get;}=Enum.GetValues<AirportIssue>().Where(x=>x is not (AirportIssue.None or AirportIssue.AircraftUnsuitable)).ToArray();
    public IReadOnlyList<IssueOption> IssueOptions {get;}=[new(AirportIssue.AirportMissing,"Airport missing"),new(AirportIssue.IncorrectLocation,"Incorrect location"),new(AirportIssue.RunwayUnusable,"Runway / landing area missing or broken"),new(AirportIssue.SceneryOrTerrain,"Scenery or terrain malfunction"),new(AirportIssue.Other,"Other simulator / airport problem")];
    public bool IsBusy=>busy;
    public bool HasAirport=>endpoint is not null&&catalogue.ResearchFor(endpoint) is not null;
    public string Airport=>HasAirport?catalogue.ResearchFor(endpoint!)!.VerifiedName:"Select an airport transport job to review its destination.";
    public string Knowledge=>catalogue.Describe(endpoint,SelectedAircraft?.Id);
    public string Cautions=>HasAirport&&catalogue.ResearchFor(endpoint!) is {} r? r.RunwayDetail+" · "+(CuratedNamibiaDestinations.Find(r.Key)?.Notes??r.VerificationNote):"Platform/observation areas cannot be verified as airports here.";
    public string Policy=>"Report missing, incorrect or broken airport representation at any time. Verification becomes available after a completed and settled mission. Aircraft is observation context only. Runway suitability and flight performance are the pilot's responsibility. Reports do not change payment, accepted work or aircraft/loading qualification.";
    public string Status {get;private set;}="";
    public string Error {get;private set;}="";
    public ActionCommand VerifyCommand {get;}
    public ActionCommand ProblemCommand {get;}
    private bool CanSave=>!busy&&HasAirport&&AircraftDescription.Length<=160&&Note.Length<=500&&SimulatorVersion.Length<=100;
    public AirportReviewViewModel(EmployerDestinationCatalogue catalogue, IAirportVisitStore store, AircraftFleetViewModel fleet, Func<Task> refresh)
    {
        this.catalogue=catalogue;this.store=store;this.fleet=fleet;this.refresh=refresh;
        VerifyCommand=new(()=>_=SaveAsync(AirportVisitResult.Verified),()=>CanSave&&CanVerifyAirport(endpoint!));
        ProblemCommand=new(()=>_=SaveAsync(AirportVisitResult.Problematic),()=>CanSave&&!string.IsNullOrWhiteSpace(Note)&&SelectedIssue!=AirportIssue.AircraftUnsuitable);
    }
    public void Select(string? locationId)
    {
        if(endpoint==locationId){FollowSelectedAirframe();return;}endpoint=locationId;note="";Status="";Error="";aircraftEdited=false;descriptionEdited=false;
        selectedAircraft=CompanyAircraft.Types.FirstOrDefault(t=>t.Id==fleet.Selected?.CompanyTypeId);
        aircraftDescription=fleet.Selected?.AllocatedTypeName??fleet.Selected?.Identity.SimulatorModel??"";Notify();
    }
    private void FollowSelectedAirframe()
    {
        if(busy||aircraftEdited||descriptionEdited||!string.IsNullOrWhiteSpace(note))return;
        var current=CompanyAircraft.Types.FirstOrDefault(t=>t.Id==fleet.Selected?.CompanyTypeId);
        if(current?.Id==selectedAircraft?.Id)return;selectedAircraft=current;
        aircraftDescription=fleet.Selected?.AllocatedTypeName??fleet.Selected?.Identity.SimulatorModel??"";Notify();
    }
    public async Task SaveAsync(AirportVisitResult result)
    {
        if(!CanSave || result==AirportVisitResult.Verified&&!CanVerifyAirport(endpoint!) || result==AirportVisitResult.Problematic&&(string.IsNullOrWhiteSpace(Note)||SelectedIssue==AirportIssue.AircraftUnsuitable))return;
        var row=catalogue.ResearchFor(endpoint!)!;var location=catalogue.Resolve(row)!;var point=location.ApproximateCoordinates!;
        if(result==AirportVisitResult.Verified&&catalogue.IsPlayerVerified(row.Key)){Status="Airport already verified; no duplicate observation added.";Notify();return;}
        var visit=new AirportVisit(Guid.NewGuid(),row.Key,location.StableId,row.VerifiedName,row.PublishedReference??row.Identifier,
            point.Latitude,point.Longitude,SelectedAircraft?.Id??"",AircraftDescription.Trim(),SelectedAircraft is not null&&fleet.Selected?.CompanyTypeId==SelectedAircraft.Id?fleet.Selected.Id:null,
            SimulatorVersion.Trim(),DateTimeOffset.UtcNow,result,result==AirportVisitResult.Verified?AirportIssue.None:SelectedIssue,Note.Trim());
        busy=true;Error="";Status="";Notify();
        try
        {
            await Task.Run(()=>store.Append(visit));catalogue.ApplyVisits(store.Load());
            await refresh();
            Status=visit.AirportName+": "+(result==AirportVisitResult.Verified?"visit saved. Airport representation verified; aircraft is context only. Payment and mission qualification are unchanged.":"problem saved. New jobs withheld; accepted work and its cargo/passengers/proven wages are preserved. No incomplete flight was paid.");
        }
        catch(Exception e){Error="Airport report could not be fully applied; existing records preserved. "+e.Message;}
        finally{busy=false;Notify();}
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Notify(){PropertyChanged?.Invoke(this,new(""));VerifyCommand?.Refresh();ProblemCommand?.Refresh();}
}
