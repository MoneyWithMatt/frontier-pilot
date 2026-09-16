using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed class PayloadStationViewModel : INotifyPropertyChanged
{
    private decimal assigned; private string assignedText="0";
    public PayloadStationViewModel()=>ResetCommand=new(()=>AssignedKilograms=0);
    public int Index { get; init; } public string Name { get; init; }=""; public decimal CurrentKilograms { get; set; }
    public decimal AssignmentMaximumKilograms { get; set; }
    public decimal? LimitKilograms { get; set; } public string Limit => LimitKilograms is { } x?$"{x:0.0} kg":"Unavailable from aircraft";
    public ActionCommand ResetCommand{get;}
    public decimal AssignedKilograms {get=>assigned;set{if(value==assigned&&assignedText==Format(value))return;assigned=value;assignedText=Format(value);Changed(nameof(AssignedKilograms));Changed(nameof(AssignedKilogramsText));Changed(nameof(PlannedKilograms));Changed(nameof(PlannedPounds));}}
    public string AssignedKilogramsText {get=>assignedText;set{if(value==assignedText)return;assignedText=value;Changed();if(TryNumber(value,out var number)&&number!=assigned){assigned=number;Changed(nameof(AssignedKilograms));Changed(nameof(PlannedKilograms));Changed(nameof(PlannedPounds));}}}
    public bool IsInputValid=>TryNumber(assignedText,out var value)&&value>=0&&value<=AssignmentMaximumKilograms;
    public void RefreshReading(PayloadStationReading reading,decimal maximum)
    {CurrentKilograms=reading.CurrentWeightKilograms;LimitKilograms=reading.AuthoritativeMaximumKilograms;AssignmentMaximumKilograms=maximum;Changed(nameof(CurrentKilograms));Changed(nameof(CurrentPounds));Changed(nameof(Limit));Changed(nameof(AssignmentMaximumKilograms));Changed(nameof(PlannedKilograms));Changed(nameof(PlannedPounds));}
    public decimal PlannedKilograms=>CurrentKilograms+AssignedKilograms;
    public decimal CurrentPounds=>LoadingUnits.KilogramsToPounds(CurrentKilograms);public decimal PlannedPounds=>LoadingUnits.KilogramsToPounds(PlannedKilograms);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName]string name="")=>PropertyChanged?.Invoke(this,new(name));
    internal static bool TryNumber(string value,out decimal number)=>decimal.TryParse(value,NumberStyles.Number,CultureInfo.CurrentCulture,out number)||decimal.TryParse(value,NumberStyles.Number,CultureInfo.InvariantCulture,out number);
    internal static string Format(decimal value)=>value.ToString("0.##",CultureInfo.CurrentCulture);
}
public sealed class FuelTankViewModel : INotifyPropertyChanged
{
    private decimal planned;private string plannedText="0";
    public FuelTankViewModel()=>ResetCommand=new(()=>PlannedGallons=0);
    public string Id {get;init;}="";public string Name {get;init;}="";public decimal CurrentGallons{get;set;}public decimal CapacityGallons{get;set;}
    public ActionCommand ResetCommand{get;}
    public decimal PlannedGallons{get=>planned;set{if(value==planned&&plannedText==PayloadStationViewModel.Format(value))return;planned=value;plannedText=PayloadStationViewModel.Format(value);Changed(nameof(PlannedGallons));Changed(nameof(PlannedGallonsText));}}
    public string PlannedGallonsText{get=>plannedText;set{if(value==plannedText)return;plannedText=value;Changed();if(PayloadStationViewModel.TryNumber(value,out var number)&&number!=planned){planned=number;Changed(nameof(PlannedGallons));}}}
    public bool IsInputValid=>PayloadStationViewModel.TryNumber(plannedText,out var value)&&value>=0&&value<=CapacityGallons;
    public void RefreshReading(FuelTankReading reading)
    {CurrentGallons=reading.QuantityGallons;CapacityGallons=reading.CapacityGallons;Changed(nameof(CurrentGallons));Changed(nameof(CapacityGallons));}
    public decimal CurrentLitres=>LoadingUnits.GallonsToLitres(CurrentGallons);public decimal PlannedLitres=>LoadingUnits.GallonsToLitres(PlannedGallons);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName]string name="")=>PropertyChanged?.Invoke(this,new(name));
}

public sealed class AircraftLoadingViewModel : INotifyPropertyChanged
{
    private readonly AircraftLoadingCoordinator service;private bool busy;private string error="",status="Connect to MSFS, then read aircraft loading data.";
    public event PropertyChangedEventHandler? PropertyChanged;public event Action? OpenRequested;
    public ObservableCollection<PayloadStationViewModel> PayloadStations{get;}=[];public ObservableCollection<FuelTankViewModel> FuelTanks{get;}=[];
    public ActionCommand RefreshCommand{get;}public ActionCommand ClearAllocationCommand{get;}public ActionCommand ApplyCargoCommand{get;}public ActionCommand ApplyFuelCommand{get;}public ActionCommand KeepFuelCommand{get;}public ActionCommand RestoreFuelCommand{get;}public ActionCommand RestoreCargoCommand{get;}public ActionCommand UnloadCommand{get;}
    public AircraftLoadingViewModel(AircraftLoadingCoordinator service, Action? ensureTracking = null)
    {this.service=service;var context=SynchronizationContext.Current;service.Changed+=value=>{if(context is not null&&SynchronizationContext.Current!=context)context.Post(_=>Apply(value),null);else Apply(value);};RefreshCommand=new(()=>{ensureTracking?.Invoke();_=Run(service.RefreshAsync,"Live aircraft loading data refreshed.");},()=>!busy);
     ClearAllocationCommand=new(()=>{foreach(var x in PayloadStations)x.AssignedKilograms=0;},()=>!busy&&!HasCommittedCargo&&PayloadStations.Count>0);
     ApplyCargoCommand=new(()=>_=ApplyPlannedCargoAsync(),()=>!busy&&service.Current.CanApplyCargo&&(AllocationValid||service.Current.Manifest?.State is LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied));
     ApplyFuelCommand=new(()=>_=Run(t=>service.ApplyFuelAsync(FuelTanks.ToDictionary(x=>x.Id,x=>x.PlannedGallons),t),"Fuel applied and read back from MSFS."),()=>!busy&&service.Current.CanApplyFuel&&FuelTanks.Count>0&&FuelTanks.All(x=>x.IsInputValid));
     KeepFuelCommand=new(()=>_=Run(service.KeepSimulatorFuelAsync,"Simulator fuel explicitly kept."),()=>!busy&&service.Current.FuelDecisionRequired);
     RestoreFuelCommand=new(()=>_=Run(service.RestoreSavedFuelAsync,"Saved fuel restored and verified."),()=>!busy&&service.Current.CanRestoreFuel);
     RestoreCargoCommand=new(()=>_=Run(service.RestoreCargoAsync,"Cargo manifest restored and verified."),()=>!busy&&service.Current.CanRestoreCargo);
     UnloadCommand=new(()=>_=Run(t=>service.UnloadAsync(false,t),"MSFS cargo removed and delivery committed."),()=>!busy&&service.Current.CanUnload);}
    public async Task InitializeAsync(CancellationToken token=default)=>await service.InitializeAsync(token);
    public Task CompleteSimulatedUnloadAsync(CancellationToken token=default)=>Run(t=>service.UnloadAsync(true,t),"Compatibility unload confirmed and economic delivery committed.");
    public Task ObserveAsync(TrackingWorldState? world,CancellationToken token=default)=>service.ObserveConnectionAsync(world,token);
    public void RequestOpen()=>OpenRequested?.Invoke();
    public string Status{get=>status;private set{status=value;Changed();}}public string Error{get=>error;private set{error=value;Changed();}}
    public string StoragePath=>service.StoragePath;public decimal CargoMassKilograms=>service.ActiveCargoKilograms;
    public string Shipment=>service.Current.Manifest is { } m?$"{m.ShipmentId} · {m.State} · {m.Mode}":"No load manifest yet";
    public string Aircraft=>service.Current.Live is { } s?$"{s.Aircraft.Title} / {s.Aircraft.Type}":"No live aircraft data";
    public bool HasCommittedCargo=>service.Current.Manifest?.State==LoadManifestState.CommittedInTransit;
    public bool CanEditCargo=>!HasCommittedCargo;
    public string CargoReason=>HasCommittedCargo?service.Current.CargoActionReason:!PayloadStations.All(x=>x.IsInputValid)?"Enter valid non-negative cargo weights within reported limits.":!AllocationValid?$"Assign exactly {ActiveCargo:0.##} kg. Remaining: {RemainingCargo:0.##} kg.":service.Current.CargoActionReason;public string FuelReason=>service.Current.FuelActionReason;public string Reconciliation=>service.Current.ReconciliationReason;
    public string EngineState=>service.Current.Live is null?"—":service.Current.Live.Engines.Count==0?"Unavailable":string.Join(", ",service.Current.Live.Engines.Select(x=>$"Engine {x.Index}: {(x.IsRunning is null?"unknown":x.IsRunning.Value?"running":"off")}"));
    public string WeightAndBalance
    {get{var s=service.Current.Live;if(s is null)return "No live weight and balance data.";var cargo=service.Current.Plan?.CargoKilograms??AssignedCargo;var w=s.Weights;var existing=s.PayloadStations.Sum(x=>x.CurrentWeightKilograms);var currentFuel=s.FuelWeightKilogramsPerGallon is { } d?s.FuelTanks.Sum(x=>x.QuantityGallons)*d:(decimal?)null;var plannedFuel=s.FuelWeightKilogramsPerGallon is { } d2?FuelTanks.Sum(x=>x.PlannedGallons)*d2:(decimal?)null;var plannedGross=w.CurrentGrossWeightKilograms+cargo+(plannedFuel-currentFuel);return $"Empty: {Mass(w.EmptyWeightKilograms)}\nExisting payload: {Mass(existing)}\nFrontier cargo: {Mass(cargo)}\nTotal payload: {Mass(existing+cargo)}\nCurrent fuel: {Mass(currentFuel)}\nPlanned fuel: {Mass(plannedFuel)}\nZero-fuel weight: {Mass(w.EmptyWeightKilograms+existing+cargo)}\nRamp/current gross: {Mass(w.CurrentGrossWeightKilograms)}\nPlanned gross: {Mass(plannedGross)}\nMaximum gross: {Mass(w.MaximumGrossWeightKilograms)}\nCurrent CG: {Pct(w.CurrentCgPercent)}\nPlanned CG: {(service.Current.Plan?.PlannedCgPercent is { } c?Pct(c):"Unavailable from aircraft data")}\nReported CG limits: {Pct(w.ForwardCgLimitPercent)} to {Pct(w.AftCgLimitPercent)}";}}
    public string Warnings{get{var warnings=service.Current.Warnings.ToList();var s=service.Current.Live;if(s?.FuelWeightKilogramsPerGallon is { } d&&s.Weights.CurrentGrossWeightKilograms is { } gross&&s.Weights.MaximumGrossWeightKilograms is { } maximum){var planned=gross+AssignedCargo+(FuelTanks.Sum(x=>x.PlannedGallons)-s.FuelTanks.Sum(x=>x.QuantityGallons))*d;if(planned>maximum&&!warnings.Any(x=>x.Contains("maximum gross")))warnings.Add("Planned weight is above the reported maximum gross weight.");}return string.Join("\n",warnings);}}public decimal AssignedCargo=>PayloadStations.Sum(x=>x.AssignedKilograms);public decimal RemainingCargo=>ActiveCargo-AssignedCargo;
    public double AllocationProgressMaximum=>Math.Max(1,(double)ActiveCargo);public double AllocationProgressValue=>Math.Clamp((double)AssignedCargo,0,AllocationProgressMaximum);
    public string AllocationState=>HasCommittedCargo?"CARGO ALREADY LOADED":AllocationValid?(service.Current.CanApplyCargo?"READY TO APPLY":"ALLOCATION COMPLETE � WAITING"):RemainingCargo>0?$"ASSIGN {RemainingCargo:0.##} KG MORE":$"REMOVE {-RemainingCargo:0.##} KG";
    public string FuelPlanState=>FuelTanks.Count==0?"No fuel tanks reported.":$"Live {FuelTanks.Sum(x=>x.CurrentGallons):0.##} gal  →  Planned {FuelTanks.Sum(x=>x.PlannedGallons):0.##} gal";
    public double CurrentCgMarkerTop=>CgTop(service.Current.Live?.Weights.CurrentCgPercent);
    public double PlannedCgMarkerTop=>CgTop(service.Current.Plan?.PlannedCgPercent);
    public double ForwardCgTop=>CgTop(service.Current.Live?.Weights.ForwardCgLimitPercent);
    public double AftCgTop=>CgTop(service.Current.Live?.Weights.AftCgLimitPercent);
    public string CurrentCgLabel=>service.Current.Live?.Weights.CurrentCgPercent is { } value?$"Current {value:0.00}%":"Current CG unavailable";
    public string PlannedCgLabel=>service.Current.Plan?.PlannedCgPercent is { } value?$"Planned {value:0.00}%":"Planned CG unavailable";
    public bool CanUnload=>service.Current.CanUnload;
    private decimal ActiveCargo=>service.ActiveCargoKilograms;
    public bool AllocationValid=>PayloadStations.All(x=>x.IsInputValid)&&ActiveCargo>0&&Math.Abs(AssignedCargo-ActiveCargo)<=.001m;
    private string? editingIdentity;
    private string? displayedManifest;
    private void Apply(AircraftLoadingStatus value)
    {
        if(value.Live is { } live)
        {
            var identity=$"{live.Aircraft.Title}/{live.Aircraft.Type}/{service.ActiveShipmentId}";
            if(editingIdentity!=identity){PayloadStations.Clear();FuelTanks.Clear();editingIdentity=identity;}
            foreach(var old in PayloadStations.Where(x=>!live.PayloadStations.Any(p=>p.Index==x.Index)).ToArray())PayloadStations.Remove(old);
            foreach(var p in live.PayloadStations)
            {
                var maximum=Math.Max(0,Math.Min(ActiveCargo,p.AuthoritativeMaximumKilograms is { } limit?limit-p.CurrentWeightKilograms:ActiveCargo));
                var row=PayloadStations.SingleOrDefault(x=>x.Index==p.Index);
                if(row is null){row=new PayloadStationViewModel{Index=p.Index,Name=p.Name};row.PropertyChanged+=(_,_)=>NotifyAll();PayloadStations.Add(row);}
                row.RefreshReading(p,maximum);
            }
            if(value.Manifest is {State:LoadManifestState.CommittedInTransit} manifest&&displayedManifest!=manifest.Id)
            {
                displayedManifest=manifest.Id;
                foreach(var row in PayloadStations)row.AssignedKilograms=manifest.Stations.SingleOrDefault(x=>x.StationIndex==row.Index)?.FrontierCargoKilograms??0;
            }
            foreach(var old in FuelTanks.Where(x=>!live.FuelTanks.Any(f=>f.Id==x.Id)).ToArray())FuelTanks.Remove(old);
            foreach(var f in live.FuelTanks)
            {
                var row=FuelTanks.SingleOrDefault(x=>x.Id==f.Id);
                if(row is null){row=new FuelTankViewModel{Id=f.Id,Name=f.Name,PlannedGallons=f.QuantityGallons};row.PropertyChanged+=(_,_)=>NotifyAll();FuelTanks.Add(row);}
                row.RefreshReading(f);
            }
        }
        NotifyAll();
    }
    private void UpdatePlan(){try{service.CreatePlan(PayloadStations.Select(x=>new CargoStationAllocation(x.Index,x.AssignedKilograms)).ToArray());Error="";}catch(Exception e){Error=e.Message;}NotifyAll();}
    private async Task ApplyPlannedCargoAsync(){if(service.Current.Manifest?.State is not (LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied))UpdatePlan();if(Error=="")await Run(service.ApplyCargoAsync,"MSFS payload verified and shipment committed InTransit.");}
    private async Task Run(Func<CancellationToken,Task> action,string success){if(busy)return;busy=true;Error="";Refresh();try{await action(default);Status=success;}catch(Exception e){Error=e.Message;Status="Loading action was refused; persistent economy and simulator state were not guessed.";}finally{busy=false;Refresh();}}
    private static string Mass(decimal? x)=>x is null?"Unavailable":$"{x:0.0} kg / {LoadingUnits.KilogramsToPounds(x.Value):0.0} lb";private static string Pct(double? x)=>x is null?"Unavailable":$"{x:0.00}%";
    private static string FuelMass(AircraftLoadingSnapshot s)=>s.FuelWeightKilogramsPerGallon is { } d?$"{s.FuelTanks.Sum(x=>x.QuantityGallons)*d:0.0} kg":"Unavailable (fuel density not reported)";
    private static double CgTop(double? percent)=>percent is { } value?25+Math.Clamp(value,0,50)/50*210:-100;
    private void NotifyAll(){foreach(var n in new[]{nameof(HasCommittedCargo),nameof(CanEditCargo),nameof(Shipment),nameof(Aircraft),nameof(CargoReason),nameof(FuelReason),nameof(Reconciliation),nameof(EngineState),nameof(WeightAndBalance),nameof(Warnings),nameof(AssignedCargo),nameof(RemainingCargo),nameof(AllocationValid),nameof(AllocationProgressMaximum),nameof(AllocationProgressValue),nameof(AllocationState),nameof(FuelPlanState),nameof(CurrentCgMarkerTop),nameof(PlannedCgMarkerTop),nameof(ForwardCgTop),nameof(AftCgTop),nameof(CurrentCgLabel),nameof(PlannedCgLabel),nameof(CargoMassKilograms),nameof(CanUnload)})Changed(n);Refresh();}
    private void Refresh(){foreach(var c in new[]{RefreshCommand,ClearAllocationCommand,ApplyCargoCommand,ApplyFuelCommand,KeepFuelCommand,RestoreFuelCommand,RestoreCargoCommand,UnloadCommand})c.Refresh();Changed(nameof(Status));Changed(nameof(Error));}
    private void Changed([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new(n));
}

