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
    public IReadOnlyList<AirportIssue> Issues {get;}=Enum.GetValues<AirportIssue>().Where(x=>x!=AirportIssue.None).ToArray();
    public IReadOnlyList<IssueOption> IssueOptions {get;}=[new(AirportIssue.AirportMissing,"Airport missing"),new(AirportIssue.IncorrectLocation,"Incorrect location"),new(AirportIssue.RunwayUnusable,"Runway unusable"),new(AirportIssue.SceneryOrTerrain,"Scenery or terrain issue"),new(AirportIssue.AircraftUnsuitable,"Aircraft unsuitable"),new(AirportIssue.Other,"Other")];
    public bool IsBusy=>busy;
    public bool HasAirport=>endpoint is not null&&catalogue.ResearchFor(endpoint) is not null;
    public string Airport=>HasAirport?catalogue.ResearchFor(endpoint!)!.VerifiedName:"Select an airport transport job to review its destination.";
    public string Knowledge=>catalogue.Describe(endpoint,SelectedAircraft?.Id);
    public string Cautions=>HasAirport&&catalogue.ResearchFor(endpoint!) is {} r? r.RunwayDetail+" · "+(CuratedNamibiaDestinations.Find(r.Key)?.Notes??r.VerificationNote):"Platform/observation areas cannot be verified as airports here.";
    public string Policy=>"Record your own visit after testing this airport. This does not prove flight completion, qualify an aircraft configuration or certify runway performance. Verified applies to the aircraft you name. Problem reports withhold new jobs; accepted work, payload and wages are retained. Database references such as NA-0114 are not simulator ICAO codes.";
    public string Status {get;private set;}="";
    public string Error {get;private set;}="";
    public ActionCommand VerifyCommand {get;}
    public ActionCommand ProblemCommand {get;}
    private bool CanSave=>!busy&&HasAirport&&SelectedAircraft is not null&&!string.IsNullOrWhiteSpace(AircraftDescription)&&AircraftDescription.Length<=160&&Note.Length<=500&&SimulatorVersion.Length<=100;
    public AirportReviewViewModel(EmployerDestinationCatalogue catalogue, IAirportVisitStore store, AircraftFleetViewModel fleet, Func<Task> refresh)
    {
        this.catalogue=catalogue;this.store=store;this.fleet=fleet;this.refresh=refresh;
        VerifyCommand=new(()=>_=SaveAsync(AirportVisitResult.Verified),()=>CanSave);
        ProblemCommand=new(()=>_=SaveAsync(AirportVisitResult.Problematic),()=>CanSave&&!string.IsNullOrWhiteSpace(Note));
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
        if(!CanSave || result==AirportVisitResult.Problematic&&string.IsNullOrWhiteSpace(Note))return;
        var row=catalogue.ResearchFor(endpoint!)!;var location=catalogue.Resolve(row)!;var point=location.ApproximateCoordinates!;
        var visit=new AirportVisit(Guid.NewGuid(),row.Key,location.StableId,row.VerifiedName,row.PublishedReference??row.Identifier,
            point.Latitude,point.Longitude,SelectedAircraft!.Id,AircraftDescription.Trim(),fleet.Selected?.CompanyTypeId==SelectedAircraft.Id?fleet.Selected.Id:null,
            SimulatorVersion.Trim(),DateTimeOffset.UtcNow,result,result==AirportVisitResult.Verified?AirportIssue.None:SelectedIssue,Note.Trim());
        busy=true;Error="";Status="";Notify();
        try
        {
            await Task.Run(()=>store.Append(visit));catalogue.ApplyVisits(store.Load());
            await refresh();
            Status=visit.AirportName+": "+(result==AirportVisitResult.Verified?"visit saved for the aircraft named. Physical loading and mission qualification are unchanged.":"problem saved. New jobs withheld; accepted work and its cargo/passengers/proven wages are preserved. No incomplete flight was paid.");
        }
        catch(Exception e){Error="Airport report could not be fully applied; existing records preserved. "+e.Message;}
        finally{busy=false;Notify();}
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Notify(){PropertyChanged?.Invoke(this,new(""));VerifyCommand?.Refresh();ProblemCommand?.Refresh();}
}
