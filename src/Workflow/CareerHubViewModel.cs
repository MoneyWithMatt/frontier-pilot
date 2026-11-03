using System.ComponentModel;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>Shared navigation/composition only. Existing services remain authoritative; no simulator source or save migration.</summary>
public sealed class CareerHubViewModel : INotifyPropertyChanged
{
    private readonly AircraftFleetViewModel fleet;
    private readonly CargoJourneyViewModel loading;
    private readonly PassengerTransfersViewModel passengers;
    private readonly Func<Task<EconomyWorld?>> readWorld;
    private readonly Func<PassengerEndpoint?> baseAirport;
    private EconomyWorld? world;
    private bool projecting;
    private readonly object projectionGate = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    public CareerNavigation Navigation { get; } = new();
    public CareerOverviewViewModel Overview { get; }
    public CareerWorkViewModel Work { get; }
    public CareerFleetViewModel Fleet { get; }
    public CareerJobBoardViewModel Board { get; }
    public CareerOwnerJobsViewModel OwnerJobs { get; }
    public string Error { get; private set; } = "";
    public string Connection => fleet.ConnectionStatus;
    public CareerJobRow? Active => Board.Selected is { Accepted: true, Started: true } selected ? selected : Board.All.FirstOrDefault(j => j.Accepted && j.Started) ?? (Board.Selected is { Accepted: true } reserved ? reserved : Board.All.FirstOrDefault(j => j.Accepted));
    public bool HasActive => Active is not null;
    public string ActiveHeading => Active is {} a ? $"{a.Title} · {Board.All.Count(j => j.Accepted)} accepted assignment(s)" : "No active assignment";
    public string ActiveAircraft => Active?.AirframeId is {} id ? fleet.Aircraft.FirstOrDefault(a => a.Id == id)?.Registration ?? "Recorded airframe unavailable: " + id : "Aircraft reservation not yet physically bound; choose an eligible airframe before loading.";
    public string ActiveProgress => Active?.Status ?? "Accept work from the Job Board to begin.";
    public ActionCommand ActiveBoardCommand { get; }
    public ActionCommand ActiveLoadCommand { get; }
    public ActionCommand FleetLoadCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? NavigationRequested;
    public CareerHubViewModel(CareerProfile? profile, CareerEmployer? employer, AircraftFleetViewModel fleet,
        EmployedCargoJobsViewModel cargo, PassengerTransfersViewModel passengers, OffshoreMissionsViewModel offshore,
        CargoJourneyViewModel loading, ProspectingOperationsViewModel operations, Func<Task<EconomyWorld?>> readWorld,
        Func<PassengerEndpoint?>? baseAirport = null)
    {
        this.fleet = fleet; this.loading = loading; this.passengers = passengers; this.readWorld = readWorld; this.baseAirport = baseAirport ?? (() => null);
        fleet.SelectionGuard = _ => SelectionBlocked ? "Keep the current airframe while cargo plans or passenger payload ownership/recovery are active. No assignment was transferred." : null;
        Overview = new(profile, employer, fleet); Work = new(employer); Fleet = new(fleet, employer, SelectAircraft);
        Board = new(Navigation, cargo, passengers, offshore, fleet) { Reload = RefreshAsync, OpenLoad = () => OpenLoadAsync(Board!.Selected) };
        OwnerJobs = new(operations) { Reload = RefreshAsync };
        ActiveBoardCommand = new(() => { if (Active is {} a) Board.Selected = a; Show(CareerSection.JobBoard); });
        ActiveLoadCommand = new(() => _ = OpenLoadAsync(Active), () => HasActive);
        FleetLoadCommand = new(() => { Navigation.AircraftSection = (int)CareerAircraftSection.LoadManager; Show(CareerSection.Fleet); }, () => fleet.Selected is not null);
        fleet.FleetChanged += () => { Fleet.Notify(); Notify(); };
        foreach (var source in new INotifyPropertyChanged[] { cargo, passengers, offshore, operations }) source.PropertyChanged += (_, _) => Project();
        Board.SelectionChanged += Notify;
    }
    private void Project()
    {
        lock (projectionGate)
        {
            if (projecting) return; projecting = true;
            try { Board.Rebuild(world, baseAirport()); OwnerJobs.Update(world); Notify(); }
            finally { projecting = false; }
        }
    }
    public async Task RefreshAsync()
    {
        await refreshGate.WaitAsync();
        try { world = await readWorld(); Project(); }
        catch (Exception e) { Error = "Career snapshot unavailable; existing records preserved. " + e.Message; Notify(); }
        finally { refreshGate.Release(); }
    }
    public void Show(CareerSection section) { Navigation.Section = (int)section; NavigationRequested?.Invoke(); }
    private bool SelectionBlocked => !loading.CanChooseAircraft || passengers.IsBusy || passengers.HasBoardedPassengers || passengers.HasPayloadOwnership || world?.PassengerTransports.Any(t => t.Payload is not null && t.State is not (PassengerCarriageState.Disembarked or PassengerCarriageState.Planned or PassengerCarriageState.Reserved)) == true;
    public bool SelectAircraft(Guid id)
    {
        if (fleet.Selected?.Id == id) return true;
        if (!fleet.Aircraft.Any(a => a.Id == id)) { Error = "This physical aircraft is not authorised in the current fleet."; Notify(); return false; }
        if (SelectionBlocked)
        { Error = "Keep the current airframe while cargo plans or passenger payload ownership/recovery are active. No assignment was transferred."; Notify(); return false; }
        fleet.SelectedAirframeId = id;
        if (fleet.Selected?.Id != id) { Error = fleet.Status; Notify(); return false; }
        Error = ""; Fleet.Notify(); Board.Notify(); Notify(); return true;
    }
    public async Task OpenLoadAsync(CareerJobRow? job)
    {
        if (job is not { Accepted: true }) return;
        await RefreshAsync();
        job = Board.All.FirstOrDefault(j => j.Key == job.Key) ?? job;
        var candidates = fleet.Aircraft.Where(a => job.AircraftTypes.Length == 0 || job.AircraftTypes.Contains(a.CompanyTypeId!)).ToArray();
        var id = job.AirframeId ?? (candidates.Any(a => a.Id == fleet.Selected?.Id) ? fleet.Selected!.Id : candidates.Length == 1 ? candidates[0].Id : (Guid?)null);
        if (id is null) { Error = "Choose an eligible physical aircraft in Fleet; this reservation has no unique airframe assignment. Nothing was transferred."; Show(CareerSection.Fleet); Notify(); return; }
        if (!SelectAircraft(id.Value)) return;
        Board.Selected = job;
        if (job.Family == CareerJobFamily.Personnel) passengers.SelectedTransfer = passengers.Transfers.FirstOrDefault(t => t.Id == job.SourceId);
        Navigation.AircraftSection = (int)CareerAircraftSection.LoadManager;
        Navigation.LoadingSection = job.Family == CareerJobFamily.Personnel ? (int)CareerLoadingSection.Passengers : (int)CareerLoadingSection.Cargo;
        Error = ""; Show(CareerSection.Fleet); Notify();
    }
    private void Notify()
    {
        void Apply() { PropertyChanged?.Invoke(this, new("")); ActiveLoadCommand?.Refresh(); FleetLoadCommand?.Refresh(); }
        if (context is not null && SynchronizationContext.Current != context) context.Post(_ => Apply(), null); else Apply();
    }
}
