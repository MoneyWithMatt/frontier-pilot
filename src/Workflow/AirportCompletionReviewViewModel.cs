using System.ComponentModel;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>Optional, read-only projection of already committed completion/settlement.
/// No economy mutation or delivery-evidence authority is available here.</summary>
public sealed class AirportCompletionReviewViewModel : INotifyPropertyChanged
{
    private readonly EmployerDestinationCatalogue catalogue;
    private readonly IAirportVisitStore store;
    private readonly string playerId;
    private IReadOnlyList<Arrival> arrivals=[];
    private HashSet<string> dismissed=[];
    private bool busy;
    private Arrival? current;
    private EconomyWorld? observed;
    private sealed record Arrival(string AirportKey,string LocationId,string Name,string? AircraftTypeId,DateTimeOffset CompletedUtc);
    public AirportReviewViewModel Review {get;}
    public bool HasPrompt=>current is not null;
    public bool Reporting {get;private set;}
    public string Airport=>current?.Name??"";
    public string Question=>"First visit to this airport! Was the airport correctly represented in MSFS 2024?";
    public string Explanation=>"Mission completed and payment settled. Your answer is optional and cannot affect them. Aircraft suitability is your responsibility.";
    public string Error {get;private set;}="";
    public ActionCommand VerifyCommand {get;}
    public ActionCommand ReportCommand {get;}
    public ActionCommand SkipCommand {get;}
    public ActionCommand SaveProblemCommand {get;}
    public event PropertyChangedEventHandler? PropertyChanged;
    public AirportCompletionReviewViewModel(EmployerDestinationCatalogue catalogue,IAirportVisitStore store,AircraftFleetViewModel fleet,Func<Task> refresh,string playerId)
    {
        this.catalogue=catalogue;this.store=store;this.playerId=playerId;
        Review=new(catalogue,store,fleet,refresh){CanVerifyAirport=CanVerify};
        VerifyCommand=new(()=>_=AnswerAsync(true),()=>HasPrompt&&!busy);
        SkipCommand=new(()=>_=SkipAsync(),()=>HasPrompt&&!busy);
        ReportCommand=new(()=>{Reporting=true;Notify();},()=>HasPrompt&&!busy);
        SaveProblemCommand=new(()=>_=AnswerAsync(false),()=>HasPrompt&&!busy&&Review.ProblemCommand.CanExecute(null));
        Review.PropertyChanged+=(_,_)=>SaveProblemCommand.Refresh();
    }
    public bool CanVerify(string locationId)=>arrivals.Any(a=>a.LocationId==locationId);
    public void Observe(EconomyWorld world)
    {
        if(ReferenceEquals(observed,world)){Choose();return;}
        // Core validation includes once-only ledger, cargo-leg and passenger wage receipts.
        // An arrival, unloaded mass or delivery proof alone cannot create this prompt.
        world.EnsureValid();
        observed=world;
        var cargo=world.Jobs.Where(j=>j.PlayerId==playerId&&j.State==EconomyJobState.Completed)
            .Select(j=>new {Job=j, Shipment=world.Shipments.Single(s=>s.Id==j.ShipmentId)});
        arrivals=cargo.Select(x=>(Location:x.Shipment.DestinationLocationId,Type:x.Job.CompanyAircraftTypeId,Utc:x.Job.CompletedUtc!.Value))
            .Concat(world.PassengerTransfers.Where(t=>t.PlayerId==playerId&&t.State==PassengerTransferState.Delivered&&t.Destination.Kind==PassengerEndpointKind.Airport)
                .Select(t=>(Location:t.Destination.Id,Type:t.AircraftTypeId,Utc:t.DisembarkedUtc!.Value)))
            .Select(a=>(Arrival:a,Row:catalogue.ResearchFor(a.Location))).Where(a=>a.Row is not null)
            .Select(a=>new Arrival(a.Row!.Key,a.Arrival.Location,a.Row.VerifiedName,a.Arrival.Type,a.Arrival.Utc))
            .OrderBy(a=>a.CompletedUtc).DistinctBy(a=>a.AirportKey).ToArray();
        try {dismissed=store.LoadDismissals().Select(d=>d.AirportKey).ToHashSet();Error="";Choose();}
        catch(Exception e){current=null;Error="Airport review unavailable; mission/payment remain settled. "+e.Message;Notify();}
    }
    private void Choose()
    {
        if(busy)return;
        var next=arrivals.FirstOrDefault(a=>!dismissed.Contains(a.AirportKey)&&!catalogue.IsPlayerVerified(a.AirportKey)&&
            catalogue.UnresolvedProblem(a.AirportKey) is null&&catalogue.ResearchFor(a.LocationId) is {} r&&catalogue.TechnicalProblem(r) is null&&
            catalogue.Resolve(r)?.VerificationState!=LocationVerificationState.MsfsVerified);
        if(next?.AirportKey!=current?.AirportKey)
        {
            current=next;Reporting=false;
            Review.Select(next?.LocationId);
            if(next?.AircraftTypeId is {} type&&CompanyAircraft.Types.FirstOrDefault(t=>t.Id==type) is {} aircraft)Review.SelectedAircraft=aircraft;
        }
        Notify();
    }
    public async Task AnswerAsync(bool verified)
    {
        if(!HasPrompt||busy||!verified&&!Review.ProblemCommand.CanExecute(null))return;
        busy=true;Error="";Notify();
        try
        {
            await Review.SaveAsync(verified?AirportVisitResult.Verified:AirportVisitResult.Problematic);
            if(Review.Error.Length>0)Error=Review.Error;
        }
        finally {busy=false;Choose();}
    }
    public async Task SkipAsync()
    {
        if(current is null||busy)return;
        var key=current.AirportKey;busy=true;Error="";Notify();
        try {await Task.Run(()=>store.Dismiss(new(key,DateTimeOffset.UtcNow)));dismissed.Add(key);}
        catch(Exception e){Error="Could not save dismissal. Mission/payment unchanged. "+e.Message;}
        finally {busy=false;Choose();}
    }
    private void Notify(){PropertyChanged?.Invoke(this,new(""));VerifyCommand?.Refresh();ReportCommand?.Refresh();SkipCommand?.Refresh();SaveProblemCommand?.Refresh();}
}
