using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed class CargoConsignmentRow
{
    public required string JobId { get; init; }
    public required string Description { get; init; }
    public required string Origin { get; init; }
    public required string Destination { get; init; }
    public decimal AvailableHere { get; init; }
    public decimal Aboard { get; init; }
    public decimal Delivered { get; init; }
    public required string Status { get; init; }
    public decimal DesiredAboard { get; set; }
}
public sealed class CargoStationRow
{
    public int Index { get; init; }
    public required string Name { get; init; }
    public decimal Actual { get; init; }
    public decimal CargoAboard { get; init; }
    public decimal Unrelated { get; init; }
    public string Limit { get; init; } = "Not reported";
    public bool Writable { get; init; }
    public decimal DesiredCargo { get; set; }
}
public sealed record CargoStop(PassengerEndpoint Endpoint)
{ public override string ToString() => Endpoint.Name; }

public static class CargoDispatchDocument
{
    public static string Render(EconomyWorld? w, CargoAirframeManifest? m, string registration, AircraftLoadingSnapshot? live)
    {
        if (w is null || m is null) return "FRONTIER PILOT / DISPATCH\n\nNo committed aircraft cargo manifest.\nAccept jobs, read the aircraft, select consignments\nand prepare an explicit station loading preview.";
        var b = new StringBuilder();
        void Rule() => b.AppendLine("--------------------------------------------------------");
        b.AppendLine("FRONTIER PILOT        CARGO / PASSENGER MANIFEST"); Rule();
        b.AppendLine("DISPATCH: " + m.Id); b.AppendLine($"REVISION: {m.Revision} / ECONOMY {w.Revision}");
        var stamped = m.History.LastOrDefault()?.Utc ?? m.Legs.LastOrDefault()?.CreatedUtc ?? m.Pending?.CreatedUtc;
        b.AppendLine($"RECORDED UTC: {stamped:u}");
        b.AppendLine($"AIRCRAFT: {m.Aircraft.Title} / {m.Aircraft.Type}");
        b.AppendLine($"REG: {registration} / AIRFRAME: {m.AirframeId}");
        var operators = m.ManagedJobs.Select(id => w.Jobs.Single(j => j.Id == id).EmployerBusinessId).Distinct().Select(id => w.Businesses.Single(x => x.Id == id).Name);
        b.AppendLine("OPERATOR: " + string.Join("; ", operators));
        b.AppendLine("LAST COMMITTED STOP: " + w.Locations.Single(x => x.Id == m.LocationId).Name);
        if (m.Legs.LastOrDefault() is {} leg) b.AppendLine($"LEG: {leg.Origin.Name} -> {leg.Destination.Name}\n{leg.Id} / {(leg.SettledUtc is null ? leg.Flight is null ? "PLANNED / NOT ARMED" : "IN PROGRESS" : "ARRIVED / SETTLED")}");
        Rule(); b.AppendLine("CONFIRMED ABOARD / LOGICAL CONSIGNMENTS");
        foreach (var c in m.Cargo)
        {
            var s = w.Shipments.Single(x => x.Id == c.ShipmentId);
            b.AppendLine("JOB: " + c.JobId); b.AppendLine($"{w.Resources.Single(x => x.Id == s.ResourceId).Name} / {c.Kilograms:0.###} kg");
            b.AppendLine("FROM: " + w.Locations.Single(x => x.Id == s.OriginLocationId).Name);
            b.AppendLine("DELIVER TO: " + w.Locations.Single(x => x.Id == s.DestinationLocationId).Name);
            b.AppendLine(s.DestinationLocationId == m.LocationId ? "UNLOAD HERE only after qualified arrival proof." : "RETAIN ABOARD for subsequent destination."); Rule();
        }
        b.AppendLine($"TOTAL COMMITTED CARGO: {m.Cargo.Sum(x => x.Kilograms):0.###} kg");
        b.AppendLine("PASSENGERS: none under this cargo manifest.");
        b.AppendLine("Mixed transport pending shared ownership qualification."); Rule();
        b.AppendLine("STATIONS / UNRELATED BASE + FRONTIER = COMMITTED TARGET");
        foreach (var s in m.Stations)
            b.AppendLine($"{s.StationIndex,2}: {s.BaselineKilograms:0.###} + {s.FrontierCargoKilograms:0.###} = {s.TargetKilograms:0.###} kg / observed {live?.PayloadStations.SingleOrDefault(x => x.Index == s.StationIndex)?.CurrentWeightKilograms:0.###}");
        b.AppendLine("Simulator observation is live information, not ownership.");
        if (m.Pending is {} p)
        {
            Rule(); b.AppendLine("PROPOSED / PENDING - NOT CONFIRMED ABOARD"); b.AppendLine(p.Id);
            foreach (var c in p.Changes) b.AppendLine($"{c.JobId}: {c.Before:0.###} -> {c.After:0.###} kg aboard");
            foreach (var s in p.Targets) b.AppendLine($"Station {s.StationIndex}: TARGET {s.TargetKilograms:0.###} kg / Frontier {s.FrontierCargoKilograms:0.###}");
            if (p.Problem is not null) b.AppendLine("RECOVERY REQUIRED: " + p.Problem);
        }
        Rule(); b.AppendLine("RECENT COMMITTED PICKUPS / DELIVERIES");
        foreach (var h in m.History.TakeLast(6)) b.AppendLine($"{h.Utc:u} / {w.Locations.Single(x => x.Id == h.LocationId).Name}\n{h.Id} / retained {h.KilogramsAboard:0.###} kg");
        Rule(); b.AppendLine("PILOT RESPONSIBILITY: weight, balance and performance.\nNo dispatch clearance or approval is implied."); return b.ToString();
    }
}

public sealed class CargoJourneyViewModel : INotifyPropertyChanged
{
    private readonly CargoJourneyService service;
    private readonly Func<Task> refreshJobs;
    private readonly Func<EconomyWorld, IEnumerable<PassengerEndpoint>> destinations;
    private readonly Func<string> registration;
    private readonly SynchronizationContext? ui = SynchronizationContext.Current;
    private bool busy;
    private string status = "Accept jobs in Jobs, select your physical airframe in Fleet, then read this aircraft.";
    public ObservableCollection<CargoConsignmentRow> Consignments { get; } = [];
    public ObservableCollection<CargoStationRow> Stations { get; } = [];
    public ObservableCollection<CargoStop> Stops { get; } = [];
    public CargoStationRow? SelectedStation { get; set; }
    public CargoStop? SelectedStop { get; set; }
    public string Status => status;
    public string ManifestText => CargoDispatchDocument.Render(service.World, service.Manifest, registration(), service.Live);
    public string AircraftStatus => service.Live is {} l ? $"{l.Aircraft.Title} / {registration()} / {service.Here?.Name ?? "location unrecognised"} / actual payload {l.PayloadStations.Sum(x=>x.CurrentWeightKilograms):0.###} kg" : "No current simulator readback.";
    public string Warnings => service.Live is {} l ? string.Join("\n", AircraftLoadPlan.EvaluateWarnings(l.Weights, l.Weights.CurrentGrossWeightKilograms, l.Weights.CurrentCgPercent)) + "\nGross weight, CG and aircraft performance are pilot decisions. Actual station limits and verified ownership remain enforced." : "";
    public ActionCommand ReadCommand { get; }
    public ActionCommand PrepareCommand { get; }
    public ActionCommand ApplyCommand { get; }
    public ActionCommand PlanLegCommand { get; }
    public ActionCommand ArmLegCommand { get; }
    public ActionCommand RenewCommand { get; }
    public ActionCommand CancelPlanCommand { get; }
    public ActionCommand MatchMassCommand { get; }
    public CargoJourneyViewModel(CargoJourneyService service, Func<Task> refreshJobs, Func<EconomyWorld, IEnumerable<PassengerEndpoint>> destinations, Func<string> registration)
    {
        this.service = service; this.refreshJobs = refreshJobs; this.destinations = destinations; this.registration = registration;
        ReadCommand = new(() => _ = Run(async () => { await refreshJobs(); await service.RefreshAsync(); }, true), () => !busy);
        PrepareCommand = new(() => _ = Run(async () => { await service.PrepareAsync(Consignments.ToDictionary(x => x.JobId, x => x.DesiredAboard), Stations.Select(x => new CargoStationAllocation(x.Index, x.DesiredCargo)).ToArray()); }, false), () => !busy);
        ApplyCommand = new(() => _ = Run(() => service.ApplyAsync(), true), () => !busy);
        PlanLegCommand = new(() => _ = Run(() => service.PlanLegAsync(SelectedStop?.Endpoint ?? throw new InvalidOperationException("Select the next stop.")), false), () => !busy);
        ArmLegCommand = new(() => _ = Run(() => service.ArmLegAsync(), false), () => !busy);
        RenewCommand = new(() => _ = Run(() => service.RenewAuthorizationAsync(), false), () => !busy);
        CancelPlanCommand = new(() => _ = Run(() => service.CancelUnappliedPlanAsync(), true), () => !busy);
        MatchMassCommand = new(MatchMass, () => !busy);
        service.Changed += () => { if (ui is not null) ui.Post(_ => Update(), null); else Update(); };
    }
    public Task ObserveAsync(AircraftTelemetry? sample) => service.ObserveAsync(sample);
    private void MatchMass()
    {
        try
        {
            if (Consignments.Any(x=>x.DesiredAboard<0)) throw new InvalidOperationException("Consignment quantities must be nonnegative.");
            var total=Consignments.Sum(x=>x.DesiredAboard); var delta=total-Stations.Sum(x=>x.DesiredCargo);
            if (delta>0)
            {
                var target=SelectedStation ?? (Stations.Count(x=>x.CargoAboard>0)==1 ? Stations.Single(x=>x.CargoAboard>0) : null);
                if(target is null || !target.Writable) throw new InvalidOperationException("Select the writable cargo station for this pickup, or enter a distribution manually.");
                target.DesiredCargo+=delta;
            }
            else
            {
                var remove=-delta;
                foreach(var station in Stations.Where(x=>x.Writable).OrderByDescending(x=>x.DesiredCargo))
                { var take=Math.Min(remove,station.DesiredCargo); station.DesiredCargo-=take;remove-=take; }
                if(remove>0)throw new InvalidOperationException("The current writable station allocations cannot supply this reduction.");
            }
            var rows=Stations.ToArray();Stations.Clear();foreach(var row in rows)Stations.Add(row);
            status=$"Suggested station allocation totals {total:0.###} kg. Review/edit the distribution, then prepare the preview. No simulator write has occurred.";
        }
        catch(Exception ex){status=ex.Message;}
        Update(false);
    }
    private async Task Run(Func<Task> action, bool rows)
    {
        busy = true; Update();
        try { await action(); status = service.Reason; if (rows) ReloadRows(); }
        catch (Exception ex) { status = ex.Message; }
        finally { busy = false; Update(false); }
    }
    private void ReloadRows()
    {
        Consignments.Clear(); Stations.Clear(); Stops.Clear();
        if (service.World is not {} w) return;
        var m = service.Manifest; var here = service.Here?.Id;
        foreach (var job in w.Jobs.Where(j => j.State == EconomyJobState.Accepted))
        {
            var s = w.Shipments.Single(x => x.JobId == job.Id);
            var aboard = m?.Cargo.SingleOrDefault(x => x.JobId == job.Id)?.Kilograms ?? 0;
            if (s.OriginLocationId != here && aboard == 0) continue;
            Consignments.Add(new() { JobId = job.Id, Description = w.Resources.Single(x=>x.Id==s.ResourceId).Name,
                Origin = w.Locations.Single(x=>x.Id==s.OriginLocationId).Name, Destination = w.Locations.Single(x=>x.Id==s.DestinationLocationId).Name,
                Aboard = aboard, AvailableHere = s.OriginLocationId == here ? s.Quantity - s.DeliveredQuantity - s.LoadedQuantity : 0,
                Delivered = s.DeliveredQuantity, DesiredAboard = aboard, Status = s.DestinationLocationId == here ? "DELIVER HERE" : s.State.ToString() });
        }
        foreach (var s in service.Live?.PayloadStations ?? [])
        {
            var claim = m?.Stations.SingleOrDefault(x=>x.StationIndex==s.Index)?.FrontierCargoKilograms ?? 0;
            Stations.Add(new() { Index=s.Index, Name=s.Name, Actual=s.CurrentWeightKilograms, Unrelated=s.CurrentWeightKilograms-claim,
                CargoAboard=claim, DesiredCargo=claim, Writable=s.IsWritable, Limit=s.AuthoritativeMaximumKilograms?.ToString("0.###") ?? "Not reported" });
        }
        foreach (var p in destinations(w)) Stops.Add(new(p));
        SelectedStop = null;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Update(bool reason = true)
    {
        if (reason && !busy) status = service.Reason;
        foreach (var name in new[] { nameof(Status), nameof(ManifestText), nameof(AircraftStatus), nameof(Warnings), nameof(SelectedStop) }) PropertyChanged?.Invoke(this, new(name));
        ReadCommand.Refresh(); PrepareCommand.Refresh(); ApplyCommand.Refresh(); PlanLegCommand.Refresh(); ArmLegCommand.Refresh(); RenewCommand.Refresh(); CancelPlanCommand.Refresh(); MatchMassCommand.Refresh();
    }
}
