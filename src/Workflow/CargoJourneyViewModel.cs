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
    public bool CanPickup=>AvailableHere>Math.Max(0,DesiredAboard-Aboard);
    public bool CanDeliver=>Status=="DELIVER HERE"&&Aboard>0;
    private decimal desiredAboard;
    public Action? Edited { get; init; }
    public decimal DesiredAboard { get=>desiredAboard; set {desiredAboard=value;Edited?.Invoke();} }
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
    public decimal? Maximum { get; init; }
    public string StationLabel=>$"{Index}: {Name}";
    public string Detail=>$"Actual {Actual:0.###} kg · planned cargo {DesiredCargo:0.###} kg · limit {Limit}";
    public override string ToString()=>$"{Index}: {Name}";
}
public sealed record CargoStop(PassengerEndpoint Endpoint)
{ public override string ToString() => Endpoint.Name; }
public sealed record CargoPickupPlan(string JobId, string Description, string Destination, int StationIndex, string StationName, decimal Kilograms)
{
    public string Summary=>$"{Description} · {Kilograms:0.###} kg → {StationIndex}: {StationName}";
}

public static class CargoDispatchDocument
{
    public static string Render(EconomyWorld? w, CargoAirframeManifest? m, string registration, AircraftLoadingSnapshot? live)
    {
        if (w is null || m is null) return "FRONTIER PILOT / DISPATCH\n\nNo committed aircraft cargo manifest.\nAccept jobs, read the aircraft, select consignments\nand prepare an explicit station loading preview.";
        var b = new StringBuilder();
        void Rule() => b.AppendLine("--------------------------------------------------------");
        b.AppendLine("FRONTIER PILOT        CARGO / PASSENGER MANIFEST"); Rule();
        b.AppendLine("DISPATCH: " + m.Id); b.AppendLine($"REVISION: {m.Revision} / ECONOMY {w.Revision}");
        var stamped = new DateTimeOffset?[] { m.History.LastOrDefault()?.Utc, m.Legs.LastOrDefault()?.CreatedUtc,
            m.Legs.LastOrDefault()?.Flight?.WageObservedUtc, m.Legs.LastOrDefault()?.SettledUtc, m.Pending?.CreatedUtc }.Max();
        b.AppendLine($"RECORDED UTC: {stamped:u}");
        b.AppendLine($"AIRCRAFT: {m.Aircraft.Title} / {m.Aircraft.Type}");
        b.AppendLine($"REG: {registration} / AIRFRAME: {m.AirframeId}");
        var operators = m.ManagedJobs.Select(id => w.Jobs.Single(j => j.Id == id).EmployerBusinessId).Distinct().Select(id => w.Businesses.Single(x => x.Id == id).Name);
        b.AppendLine("OPERATOR: " + string.Join("; ", operators));
        b.AppendLine("LAST COMMITTED STOP: " + w.Locations.Single(x => x.Id == m.LocationId).Name);
        if (m.Legs.LastOrDefault() is {} leg)
        {
            b.AppendLine($"LEG: {leg.Origin.Name} -> {leg.Destination.Name}\n{leg.Id} / {(leg.SettledUtc is null ? leg.Flight is null ? "PLANNED / NOT ARMED" : "IN PROGRESS" : "ARRIVED / QUALIFIED")}");
            if (leg.EmployerId is not null) b.AppendLine(w.FinancialLedger.SingleOrDefault(t=>t.Kind==FinancialTransactionKind.CargoLegWage&&t.EvidenceId==leg.Id) is {} paid ?
                $"LEG WAGES: {DollarDisplay.Format(paid.Amount)} paid once / {paid.OccurredUtc:u}" : "LEG WAGES: pending successful consignment delivery.");
        }
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
    private string technicalDetails="No failed operation recorded.";
    private string status = "Accept jobs in Jobs, select your physical airframe in Fleet, then read this aircraft.";
    public ObservableCollection<CargoConsignmentRow> Consignments { get; } = [];
    public ObservableCollection<CargoStationRow> Stations { get; } = [];
    public ObservableCollection<CargoStop> Stops { get; } = [];
    public ObservableCollection<CargoPickupPlan> Pickups { get; } = [];
    public CargoConsignmentRow? PickupCargo { get; private set; }
    public string PickupQuantityText { get; set; } = "";
    public bool IsChoosingStation=>CanEditCargo&&PickupCargo is not null;
    public string PickupHeading=>PickupCargo is {} c?$"Load {c.Description} → {c.Destination}":"";
    public string PickupHelp=>PickupCargo is {} c?$"{c.AvailableHere-Math.Max(0,c.DesiredAboard-c.Aboard):0.###} kg still available. Choose a station below to add this amount to the plan.":"";
    public CargoStationRow? SelectedStation { get; set; }
    public CargoStop? SelectedStop { get; set; }
    public string Status => status;
    public string TechnicalDetails=>technicalDetails;
    public bool CanEditCargo=>!busy&&service.Manifest?.Pending is null&&service.Manifest?.Legs.Any(l=>l.SettledUtc is null)!=true;
    public decimal RequestedKilograms=>Consignments.Sum(c=>c.DesiredAboard);
    public string CargoSummary=>$"Confirmed aboard: {Consignments.Sum(c=>c.Aboard):0.###} kg  |  After this operation: {RequestedKilograms:0.###} kg";
    public string PrimaryActionText=>service.Manifest?.Pending is not null?"Apply cargo in cockpit":"Confirm loading / unloading plan";
    public string NextStep
    {
        get
        {
            if(service.Manifest?.Pending is {} pending)return pending.Problem is not null?
                "Loading needs attention. Read the message below before retrying; the saved plan is retained.":
                "2. Review the proposed dispatch sheet, board this aircraft, then apply cargo. Nothing is aboard until verified.";
            if(service.Manifest?.Legs.SingleOrDefault(l=>l.SettledUtc is null) is {} leg)return leg.Flight is null?
                "3. Remain stopped for ten tracking seconds, then Start flight recording before takeoff.":
                "Flight recording active. Fly to the planned stop; remaining cargo stays aboard.";
            return service.Manifest?.Cargo.Length>0?"Cargo confirmed aboard. Choose your next destination below, or select cargo for a pickup or delivery here.":
                "1. Click Load beside cargo, then choose its station. Confirm the plan while walking near this aircraft.";
        }
    }
    public string DepartureActionText=>service.Manifest?.Legs.Any(l=>l.SettledUtc is null)==true?"Start flight recording":"Prepare departure";
    public ActionCommand PrimaryCommand { get; }
    public ActionCommand LoadAvailableCommand { get; }
    public ActionCommand DeliverHereCommand { get; }
    public ActionCommand DepartureCommand { get; }
    public string ManifestText => CargoDispatchDocument.Render(service.World, service.Manifest, registration(), service.Live);
    public string AircraftStatus => service.Live is {} l ? $"{l.Aircraft.Title} / {registration()} / {service.Here?.Name ?? "location unrecognised"} / actual payload {l.PayloadStations.Sum(x=>x.CurrentWeightKilograms):0.###} kg" : "No current simulator readback.";
    public string Warnings => service.Live is {} l ? string.Join("\n", AircraftLoadPlan.EvaluateWarnings(l.Weights, l.Weights.CurrentGrossWeightKilograms, l.Weights.CurrentCgPercent)) +
        (l.Weights.CurrentGrossWeightKilograms is null ? "\nTotal gross is not reported; physical verification uses retained station readback without an independent gross delta." : "") +
        "\nGross weight, CG and aircraft performance are pilot decisions. Actual station limits and verified ownership remain enforced." : "";
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
        PrimaryCommand=new(()=>_=HandleCargoAsync(),()=>!busy&&service.Live is not null&&
            (service.Manifest?.Pending is not null||CanEditCargo&&Consignments.Any(c=>c.DesiredAboard!=c.Aboard)));
        LoadAvailableCommand=new(()=>SetQuantities(false),()=>CanEditCargo&&Consignments.Any(c=>c.AvailableHere>0));
        DeliverHereCommand=new(()=>SetQuantities(true),()=>CanEditCargo&&Consignments.Any(c=>c.Status=="DELIVER HERE"&&c.Aboard>0));
        DepartureCommand=new(()=>_=PrepareDepartureAsync(),()=>!busy&&service.Manifest is {Pending:null} m&&m.Cargo.Length>0&&m.Legs.All(l=>l.SettledUtc is not null||l.Flight is null));
        ReadCommand = new(() => _ = RefreshAsync(), () => !busy);
        PrepareCommand = new(() => _ = Run(async () => { await service.PrepareAsync(Consignments.ToDictionary(x => x.JobId, x => x.DesiredAboard), Stations.Select(x => new CargoStationAllocation(x.Index, x.DesiredCargo)).ToArray()); }, false), () => !busy);
        ApplyCommand = new(() => _ = Run(() => service.ApplyAsync(), true), () => !busy);
        PlanLegCommand = new(() => _ = Run(() => service.PlanLegAsync(SelectedStop?.Endpoint ?? throw new InvalidOperationException("Select the next stop.")), false), () => !busy);
        ArmLegCommand = new(() => _ = Run(() => service.ArmLegAsync(), false), () => !busy);
        RenewCommand = new(() => _ = Run(() => service.RenewAuthorizationAsync(), false), () => !busy);
        CancelPlanCommand = new(() => _ = Run(() => service.CancelUnappliedPlanAsync(), true), () => !busy);
        MatchMassCommand = new(MatchMass, () => !busy);
        service.Changed += () => { if (ui is not null) ui.Post(_ => Update(false), null); else Update(false); };
    }
    public async Task ObserveAsync(AircraftTelemetry? sample)
    {await service.ObserveAsync(sample);if(!busy&&service.Manifest?.Legs.Any(l=>l.SettledUtc is null)==true){status=service.Reason;Update(false);}}
    public Task RefreshAsync()=>Run(async()=>{await refreshJobs();await service.RefreshAsync();},true);
    public void BeginPickup(CargoConsignmentRow cargo)
    {
        if(!CanEditCargo||!Consignments.Contains(cargo))return;
        var remaining=cargo.AvailableHere-Math.Max(0,cargo.DesiredAboard-cargo.Aboard);
        if(remaining<=0){status="All available cargo for this job is already selected or aboard.";Update(false);return;}
        PickupCargo=cargo;PickupQuantityText=remaining.ToString();Update(false);
    }
    public void ClosePickup(){PickupCargo=null;Update(false);}
    public void AddPickup(CargoStationRow station)
    {
        if(!CanEditCargo||PickupCargo is not {} cargo)return;
        try
        {
            if(!Stations.Contains(station)||!station.Writable)throw new InvalidOperationException("This simulator station is not writable. Choose another station.");
            if(!decimal.TryParse(PickupQuantityText,out var pickupQuantity))throw new InvalidOperationException("Enter a valid cargo amount in kilograms.");
            var remaining=cargo.AvailableHere-Math.Max(0,cargo.DesiredAboard-cargo.Aboard);
            if(pickupQuantity<=0||pickupQuantity>remaining)throw new InvalidOperationException($"Choose an amount greater than zero and no more than {remaining:0.###} kg.");
            if(station.Maximum is {} max&&station.Unrelated+station.DesiredCargo+pickupQuantity>max)
                throw new InvalidOperationException("This amount exceeds the simulator-reported station limit. Choose a smaller amount or another station.");
            // A station choice is an explicit pickup target; MSFS still receives aggregate mass, not job identity.
            station.DesiredCargo+=pickupQuantity;cargo.DesiredAboard+=pickupQuantity;
            var existing=Pickups.SingleOrDefault(p=>p.JobId==cargo.JobId&&p.StationIndex==station.Index);
            if(existing is not null)Pickups.Remove(existing);
            Pickups.Add(new(cargo.JobId,cargo.Description,cargo.Destination,station.Index,station.Name,pickupQuantity+(existing?.Kilograms??0)));
            status=$"Added {pickupQuantity:0.###} kg of {cargo.Description} to {station.Name} in the plan. Nothing has been loaded yet.";
            PickupCargo=null;RefreshEditableRows();
        }
        catch(Exception ex){status=ex.Message;}
        Update(false);
    }
    public void RemovePickup(CargoPickupPlan pickup)
    {
        if(!CanEditCargo||!Pickups.Contains(pickup))return;
        var cargo=Consignments.Single(c=>c.JobId==pickup.JobId);var station=Stations.Single(s=>s.Index==pickup.StationIndex);
        if(cargo.DesiredAboard-cargo.Aboard<pickup.Kilograms||station.DesiredCargo<pickup.Kilograms)
        {status="Station totals were edited in advanced details. Refresh aircraft & jobs to start a new plan.";Update(false);return;}
        cargo.DesiredAboard-=pickup.Kilograms;station.DesiredCargo-=pickup.Kilograms;Pickups.Remove(pickup);PickupCargo=null;
        RefreshEditableRows();Update(false);
    }
    public void DeliverConsignment(CargoConsignmentRow cargo)
    {
        if(!CanEditCargo||!Consignments.Contains(cargo)||cargo.Status!="DELIVER HERE"||cargo.Aboard<=0)return;
        var before=cargo.DesiredAboard;
        try{cargo.DesiredAboard=0;DistributeMass();status=$"Selected {cargo.Aboard:0.###} kg of {cargo.Description} for delivery. Confirm and apply to unload.";}
        catch(Exception ex){cargo.DesiredAboard=before;status=ex.Message;}
        PickupCargo=null;RefreshEditableRows();Update(false);
    }
    private void RefreshEditableRows()
    {
        var cargo=Consignments.ToArray();Consignments.Clear();foreach(var c in cargo)Consignments.Add(c);
        var index=SelectedStation?.Index;var stations=Stations.ToArray();Stations.Clear();foreach(var s in stations)Stations.Add(s);
        SelectedStation=Stations.SingleOrDefault(s=>s.Index==index);
    }
    private void SetQuantities(bool deliver)
    {
        foreach(var c in Consignments)
        {
            if(deliver&&c.Status=="DELIVER HERE")c.DesiredAboard=0;
            else if(!deliver&&c.AvailableHere>0)c.DesiredAboard=c.Aboard+c.AvailableHere;
        }
        var rows=Consignments.ToArray();Consignments.Clear();foreach(var c in rows)Consignments.Add(c);
        Update(false);
    }
    public Task HandleCargoAsync()=>Run(async()=>
    {
        if(service.Manifest?.Pending is not null){await service.ApplyAsync();ReloadRows();return;}
        DistributeMass();
        await service.PrepareAsync(Consignments.ToDictionary(c=>c.JobId,c=>c.DesiredAboard),Stations.Select(s=>new CargoStationAllocation(s.Index,s.DesiredCargo)).ToArray());
    },false);
    public Task PrepareDepartureAsync()=>Run(async()=>
    {
        if(service.Manifest?.Legs.Any(l=>l.SettledUtc is null)==true)await service.ArmLegAsync();
        else await service.PlanLegAsync(SelectedStop?.Endpoint??throw new InvalidOperationException("Choose your next destination first."));
    },false);
    private void MatchMass()
    {
        try
        {
            DistributeMass();
            status=$"Suggested distribution: {RequestedKilograms:0.###} kg. Confirm the plan outside; no simulator write has occurred.";
        }
        catch(Exception ex){status=ex.Message;}
        Update(false);
    }
    private void DistributeMass()
    {
            if (Consignments.Any(x=>x.DesiredAboard<0)) throw new InvalidOperationException("Consignment quantities must be nonnegative.");
            var total=Consignments.Sum(x=>x.DesiredAboard); var delta=total-Stations.Sum(x=>x.DesiredCargo);
            if (delta>0)
            {
                var target=SelectedStation ?? (Stations.Count(x=>x.CargoAboard>0)==1 ? Stations.Single(x=>x.CargoAboard>0) : null);
                if(target is null || !target.Writable) throw new InvalidOperationException("Choose a cargo compartment below. The app will place the selected cargo there; it cannot safely guess which stations are cargo areas.");
                target.DesiredCargo+=delta;
            }
            else
            {
                var remove=-delta;
                if(Stations.Where(x=>x.Writable).Sum(x=>Math.Max(0,x.DesiredCargo-Pickups.Where(p=>p.StationIndex==x.Index).Sum(p=>p.Kilograms)))<remove)
                    throw new InvalidOperationException("The current writable station allocations cannot supply this reduction.");
                foreach(var station in Stations.Where(x=>x.Writable).OrderByDescending(x=>x.DesiredCargo))
                { var retainedPickup=Pickups.Where(p=>p.StationIndex==station.Index).Sum(p=>p.Kilograms);
                  var take=Math.Min(remove,Math.Max(0,station.DesiredCargo-retainedPickup)); station.DesiredCargo-=take;remove-=take; }
                if(remove>0)throw new InvalidOperationException("The current writable station allocations cannot supply this reduction.");
            }
            var selectedIndex=SelectedStation?.Index;
            var rows=Stations.ToArray();Stations.Clear();foreach(var row in rows)Stations.Add(row);
            SelectedStation=Stations.SingleOrDefault(s=>s.Index==selectedIndex);
    }
    private async Task Run(Func<Task> action, bool rows)
    {
        busy = true; Update();
        try { await action(); status = service.Reason; if (rows) ReloadRows(); }
        catch (Exception ex) { technicalDetails=ex.ToString();status="Could not complete this step: "+ex.Message; }
        finally { busy = false; Update(false); }
    }
    private void ReloadRows()
    {
        Consignments.Clear(); Stations.Clear(); Stops.Clear();Pickups.Clear();PickupCargo=null;
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
                Delivered = s.DeliveredQuantity, DesiredAboard = m?.Pending?.Changes.SingleOrDefault(c=>c.JobId==job.Id)?.After??aboard, Status = s.DestinationLocationId == here ? "DELIVER HERE" : s.State.ToString(), Edited=()=>Update(false) });
        }
        foreach (var s in service.Live?.PayloadStations ?? [])
        {
            var claim = m?.Stations.SingleOrDefault(x=>x.StationIndex==s.Index)?.FrontierCargoKilograms ?? 0;
            var pending=m?.Pending?.Targets.SingleOrDefault(x=>x.StationIndex==s.Index);
            Stations.Add(new() { Index=s.Index, Name=s.Name, Actual=s.CurrentWeightKilograms, Unrelated=pending?.BaselineKilograms??s.CurrentWeightKilograms-claim,
                CargoAboard=claim, DesiredCargo=pending?.FrontierCargoKilograms??claim, Writable=s.IsWritable, Maximum=s.AuthoritativeMaximumKilograms, Limit=s.AuthoritativeMaximumKilograms?.ToString("0.###") ?? "Not reported" });
        }
        var selectedStationIndex=SelectedStation?.Index;
        SelectedStation=Stations.SingleOrDefault(s=>s.Index==selectedStationIndex)??(Stations.Count(s=>s.CargoAboard>0)==1?Stations.Single(s=>s.CargoAboard>0):null);
        var stopId=SelectedStop?.Endpoint.Id;
        foreach (var p in destinations(w).Where(p=>p.Id!=here)) Stops.Add(new(p));
        SelectedStop=Stops.SingleOrDefault(s=>s.Endpoint.Id==stopId);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Update(bool reason = true)
    {
        if (reason && !busy) status = service.Reason;
        foreach (var name in new[] { nameof(Status), nameof(ManifestText), nameof(AircraftStatus), nameof(Warnings), nameof(SelectedStop),nameof(SelectedStation),nameof(TechnicalDetails),nameof(CanEditCargo),nameof(CargoSummary),nameof(PrimaryActionText),nameof(NextStep),nameof(DepartureActionText),nameof(IsChoosingStation),nameof(PickupQuantityText),nameof(PickupHeading),nameof(PickupHelp) }) PropertyChanged?.Invoke(this, new(name));
        PrimaryCommand?.Refresh();LoadAvailableCommand?.Refresh();DeliverHereCommand?.Refresh();DepartureCommand?.Refresh();
        ReadCommand.Refresh(); PrepareCommand.Refresh(); ApplyCommand.Refresh(); PlanLegCommand.Refresh(); ArmLegCommand.Refresh(); RenewCommand.Refresh(); CancelPlanCommand.Refresh(); MatchMassCommand.Refresh();
    }
}
