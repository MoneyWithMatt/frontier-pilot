using System.ComponentModel;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public enum CareerJobFamily { Cargo, Personnel, Observation, Platform }
public sealed record CareerRoutePoint(string Name, double Latitude, double Longitude);
public sealed record CareerJobRow(string Key, string SourceId, CareerJobFamily Family, string Title, string Purpose,
    string Origin, string Destination, string Payload, string Status, bool Accepted, bool Completed,
    Guid? AirframeId, string[] AircraftTypes, IReadOnlyList<CareerRoutePoint> Route, string Compensation, string Requirements,
    double? AreaRadiusNauticalMiles = null, bool Started = false, bool OfferAvailable = true)
{
    public bool Available => OfferAvailable && !Accepted && !Completed && Family != CareerJobFamily.Platform;
    public string Category => Family.ToString();
    public string RouteText => Origin + " → " + Destination;
    public string Aircraft => AircraftTypes.Length == 0 ? "Existing aircraft eligibility applies" : string.Join(" · ", AircraftTypes.Select(id => CompanyAircraft.Types.SingleOrDefault(t => t.Id == id)?.Name ?? id));
    public double? DistanceNauticalMiles => Route.Count < 2 ? null : Enumerable.Range(1, Route.Count - 1).Sum(i => WorldTracking.DistanceMeters(Route[i - 1].Latitude, Route[i - 1].Longitude, Route[i].Latitude, Route[i].Longitude)) / 1852;
    public string Distance => DistanceNauticalMiles is {} d ? $"Route reference: {d:0.0} NM" + (AreaRadiusNauticalMiles is null ? "" : " + observation within operating area") : "Distance unavailable · route coordinates unresolved";
}

/// <summary>Read-only presentation projection plus delegation to existing acceptance commands. No mission or payment rules.</summary>
public sealed class CareerJobBoardViewModel : INotifyPropertyChanged
{
    private readonly CareerNavigation navigation;
    private readonly EmployedCargoJobsViewModel cargo;
    private readonly PassengerTransfersViewModel passengers;
    private readonly OffshoreMissionsViewModel offshore;
    private readonly AircraftFleetViewModel fleet;
    private IReadOnlyList<CareerJobRow> jobs = [];
    private bool busy;
    private int filter;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? SelectionChanged;
    public Func<Task>? Reload { get; set; }
    public Func<Task>? OpenLoad { get; set; }
    public string Error { get; private set; } = "";
    public IReadOnlyList<string> Filters { get; } = ["All work", "Available", "Accepted", "Completed"];
    public int Filter { get => filter; set { if (value is < 0 or > 3 || value == filter) return; filter = value; if (!Items.Any(j => j.Key == navigation.SelectedJobKey)) Selected = Items.FirstOrDefault(); Notify(); } }
    public IReadOnlyList<CareerJobRow> All => jobs;
    public IReadOnlyList<CareerJobRow> Items => jobs.Where(j => filter switch { 1 => j.Available, 2 => j.Accepted, 3 => j.Completed, _ => true }).ToArray();
    public CareerJobRow? Selected
    {
        get => jobs.FirstOrDefault(j => j.Key == navigation.SelectedJobKey);
        set { if (value is null || value.Key == navigation.SelectedJobKey) return; navigation.SelectedJobKey = value.Key; SelectSource(); Notify(); SelectionChanged?.Invoke(); }
    }
    public IReadOnlyList<CompanyAircraftType> AircraftChoices => Selected?.Family switch
    { CareerJobFamily.Cargo => cargo.CompanyAircraftChoices, CareerJobFamily.Personnel => passengers.AircraftChoices, _ => [] };
    public CompanyAircraftType? SelectedAircraft
    {
        get => Selected?.Family == CareerJobFamily.Cargo ? cargo.SelectedCompanyAircraft : passengers.SelectedAircraft;
        set { if (Selected?.Family == CareerJobFamily.Cargo) cargo.SelectedCompanyAircraft = value; else if (Selected?.Family == CareerJobFamily.Personnel) passengers.SelectedAircraft = value; Notify(); }
    }
    public string Estimate => Selected?.Family == CareerJobFamily.Observation ? offshore.Estimate : "Flight-time estimate unavailable; plan fuel, weather and aircraft performance.";
    public string Progress => Selected?.Family switch
    { CareerJobFamily.Observation => offshore.Status + "\n" + offshore.Progress, CareerJobFamily.Personnel => passengers.Status + "\n" + passengers.PayloadStatus,
      CareerJobFamily.Cargo => Selected.Status + "\n" + cargo.FlightReason, _ => Selected?.Requirements ?? "Select work to inspect its route and requirements." };
    public ActionCommand AcceptCommand { get; }
    public ActionCommand RefreshCommand { get; }
    public ActionCommand LoadCommand { get; }
    public CareerJobBoardViewModel(CareerNavigation navigation, EmployedCargoJobsViewModel cargo, PassengerTransfersViewModel passengers,
        OffshoreMissionsViewModel offshore, AircraftFleetViewModel fleet)
    {
        this.navigation = navigation; this.cargo = cargo; this.passengers = passengers; this.offshore = offshore; this.fleet = fleet;
        AcceptCommand = new(() => _ = AcceptAsync(), () => !busy && Selected is { Available: true } && (Selected.Family switch {
            CareerJobFamily.Cargo => cargo.SelectedOffer?.JobId == Selected.SourceId && cargo.AcceptCommand.CanExecute(null) && (AircraftChoices.Count == 0 || SelectedAircraft is not null),
            CareerJobFamily.Personnel => passengers.SelectedTransfer?.Id == Selected.SourceId && passengers.AcceptCommand.CanExecute(null), CareerJobFamily.Observation => offshore.Selected?.Id == Selected.SourceId && offshore.AcceptCommand.CanExecute(null), _ => false }));
        RefreshCommand = new(() => _ = RefreshAsync(), () => !busy);
        LoadCommand = new(() => _ = OpenLoad?.Invoke(), () => !busy && Selected is { Accepted: true });
    }
    public async Task RefreshAsync()
    {
        if (busy) return; busy = true; Notify();
        try { await Task.WhenAll(cargo.RefreshLiveAsync(), passengers.RefreshAsync(), offshore.RefreshAsync()); if (Reload is not null) await Reload(); }
        catch (Exception e) { Error = e.Message; } finally { busy = false; Notify(); }
    }
    public async Task AcceptAsync()
    {
        if (!AcceptCommand.CanExecute(null)) return; busy = true; Error = ""; Notify();
        try
        {
            if (Selected!.Family == CareerJobFamily.Cargo) { await cargo.AcceptSelectedAsync(); Error = cargo.Error; }
            else if (Selected.Family == CareerJobFamily.Personnel) { await passengers.AcceptAsync(); Error = passengers.Error; }
            else { await offshore.AcceptAsync(); Error = offshore.Error; }
            if (Reload is not null) await Reload();
        }
        catch (Exception e) { Error = e.Message; } finally { busy = false; Notify(); }
    }
    private void SelectSource()
    {
        var j = Selected;
        if (j?.Family == CareerJobFamily.Cargo)
        {
            var offer = cargo.Offers.FirstOrDefault(o => o.JobId == j.SourceId);
            if (cargo.SelectedOffer?.JobId != offer?.JobId) cargo.SelectedOffer = offer;
            if (cargo.SelectedCompanyAircraft is null && fleet.Selected?.CompanyTypeId is {} type)
                cargo.SelectedCompanyAircraft = cargo.CompanyAircraftChoices.FirstOrDefault(a => a.Id == type);
        }
        else if (j?.Family == CareerJobFamily.Personnel)
        { passengers.SelectedTransfer = passengers.Transfers.FirstOrDefault(t => t.Id == j.SourceId); if (passengers.SelectedAircraft is null) passengers.SelectedAircraft = passengers.AircraftChoices.FirstOrDefault(a => a.Id == fleet.Selected?.CompanyTypeId); }
        else if (j?.Family == CareerJobFamily.Observation) offshore.Selected = offshore.Missions.FirstOrDefault(m => m.Id == j.SourceId);
    }
    public void Rebuild(EconomyWorld? world, PassengerEndpoint? origin)
    {
        var rows = new List<CareerJobRow>();
        foreach (var c in cargo.Offers.Concat(cargo.ReservedJobs).Concat(cargo.CompletedJobs).DistinctBy(c => c.JobId))
        {
            var manifest = world?.CargoAirframes.FirstOrDefault(m => m.ManagedJobs.Contains(c.JobId) && m.Cargo.Any(x => x.JobId == c.JobId));
            CareerRoutePoint[] route = c.OriginLatitude is {} lat && c.OriginLongitude is {} lon && c.DestinationLatitude is {} dlat && c.DestinationLongitude is {} dlon ? [new(c.Origin, lat, lon), new(c.Destination, dlat, dlon)] : [];
            var legs = manifest?.Legs.Where(l => l.Carried.Any(x => x.JobId == c.JobId)).ToArray() ?? [];
            if (legs.Length > 0)
                route = new[] { new CareerRoutePoint(legs[0].Origin.Name, legs[0].Origin.Latitude, legs[0].Origin.Longitude) }
                    .Concat(legs.Select(l => new CareerRoutePoint(l.Destination.Name, l.Destination.Latitude, l.Destination.Longitude))).ToArray();
            rows.Add(new("cargo:" + c.JobId, c.JobId, CareerJobFamily.Cargo, c.CargoCategory, c.CustomerPurpose, c.Origin, c.Destination,
                $"{c.CargoMassKilograms:0.###} kg · {c.LoadedCargoKilograms:0.###} loaded · {c.DeliveredCargoKilograms:0.###} delivered", c.ShipmentState?.ToString() ?? "Available",
                c.ShipmentState is ShipmentState.Reserved or ShipmentState.Loaded or ShipmentState.InTransit, c.ShipmentState is ShipmentState.Delivered or ShipmentState.Consumed or ShipmentState.Sold,
                manifest?.AirframeId, c.CompanyAircraftTypeId is {} type ? [type] : cargo.AircraftChoicesFor(c).Select(a => a.Id).ToArray(), route, c.HourlyWage is {} rate ? DollarDisplay.Format(rate) + " / verified airborne hour" : "Recorded contract: " + DollarDisplay.Format(c.PaymentAmount), c.KnownDestinationInformation, Started: c.LoadedCargoKilograms > 0));
        }
        foreach (var p in passengers.Transfers)
            rows.Add(new("personnel:" + p.Id, p.Id, CareerJobFamily.Personnel, p.Purpose ?? "Personnel transfer", p.Purpose ?? "Existing passenger assignment", p.Origin.Name, p.Destination.Name,
                p.People + " · " + (world?.PassengerTransports.FirstOrDefault(t => t.Id == p.TransportId)?.Group.WeightKilograms is {} kg ? $"{kg:0.##} kg declared" : "individual weights required"), p.Status,
                p.State is PassengerTransferState.Reserved or PassengerTransferState.Boarded, p.State == PassengerTransferState.Delivered, p.AirframeId, p.EligibleAircraftTypes,
                [new(p.Origin.Name, p.Origin.Latitude, p.Origin.Longitude), new(p.Destination.Name, p.Destination.Latitude, p.Destination.Longitude)], DollarDisplay.Format(p.HourlyRate) + " / verified airborne hour", passengers.SupportSummary, Started: p.State == PassengerTransferState.Boarded, OfferAvailable: p.State == PassengerTransferState.Offered));
        foreach (var m in offshore.Missions)
            rows.Add(new("observation:" + m.Id, m.Id, CareerJobFamily.Observation, m.Title, m.Purpose, m.Base.Name, m.Area.Name + " → " + m.Base.Name, "No transport load · observation task", m.State.ToString(), m.Active,
                m.State == OffshoreMissionState.Completed, m.AirframeId, ["vision-jet"], [new(m.Base.Name, m.Base.Latitude, m.Base.Longitude), new(m.Area.Name, m.Area.Latitude, m.Area.Longitude), new(m.Base.Name, m.Base.Latitude, m.Base.Longitude)],
                m.WageDisplay, "Airborne observation and return required. Paused, accelerated, frozen or repositioned data cannot qualify.", m.Area.RadiusNauticalMiles, m.Active && m.State != OffshoreMissionState.Accepted, m.State == OffshoreMissionState.Offered));
        if (offshore.Enabled) foreach (var p in OffshorePlatformRegistry.Platforms)
            rows.Add(new("platform:" + p.Id, p.Id, CareerJobFamily.Platform, p.Name, "Platform work · qualification required", origin?.Name ?? "Base unresolved", p.Name,
                "Supply / personnel / visit · unavailable", p.Status, false, false, null, ["h125"], origin is null ? [new(p.Name, p.Latitude, p.Longitude)] : [new(origin.Name, origin.Latitude, origin.Longitude), new(p.Name, p.Latitude, p.Longitude)],
                "No available mission", p.BlockedReason));
        jobs = rows.OrderBy(j => j.Accepted ? 0 : j.Available ? 1 : j.Completed ? 3 : 2).ToArray();
        if (!Items.Any(j => j.Key == navigation.SelectedJobKey)) { navigation.SelectedJobKey = Items.FirstOrDefault()?.Key; SelectSource(); }
        Notify(); SelectionChanged?.Invoke();
    }
    public void Notify() { PropertyChanged?.Invoke(this, new("")); AcceptCommand.Refresh(); RefreshCommand.Refresh(); LoadCommand.Refresh(); }
}
