using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed class PayloadStationViewModel : INotifyPropertyChanged
{
    private decimal assigned;
    public int Index { get; init; } public string Name { get; init; }=""; public decimal CurrentKilograms { get; init; }
    public decimal AssignmentMaximumKilograms { get; init; }
    public decimal? LimitKilograms { get; init; } public string Limit => LimitKilograms is { } x?$"{x:0.0} kg":"Unavailable from aircraft";
    public decimal AssignedKilograms {get=>assigned;set{if(value==assigned)return;assigned=value;PropertyChanged?.Invoke(this,new(nameof(AssignedKilograms)));PropertyChanged?.Invoke(this,new(nameof(PlannedKilograms)));PropertyChanged?.Invoke(this,new(nameof(PlannedPounds)));}}
    public decimal PlannedKilograms=>CurrentKilograms+AssignedKilograms;
    public decimal CurrentPounds=>LoadingUnits.KilogramsToPounds(CurrentKilograms);public decimal PlannedPounds=>LoadingUnits.KilogramsToPounds(PlannedKilograms);
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed class FuelTankViewModel : INotifyPropertyChanged
{
    private decimal planned;
    public string Id {get;init;}="";public string Name {get;init;}="";public decimal CurrentGallons{get;init;}public decimal CapacityGallons{get;init;}
    public decimal PlannedGallons{get=>planned;set{if(value==planned)return;planned=value;PropertyChanged?.Invoke(this,new(nameof(PlannedGallons)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class AircraftLoadingViewModel : INotifyPropertyChanged
{
    private readonly AircraftLoadingCoordinator service;private bool busy;private string error="",status="Connect to MSFS, then read aircraft loading data.";
    public event PropertyChangedEventHandler? PropertyChanged;public event Action? OpenRequested;
    public ObservableCollection<PayloadStationViewModel> PayloadStations{get;}=[];public ObservableCollection<FuelTankViewModel> FuelTanks{get;}=[];
    public ActionCommand RefreshCommand{get;}public ActionCommand ClearAllocationCommand{get;}public ActionCommand ApplyCargoCommand{get;}public ActionCommand SimulatedCargoCommand{get;}public ActionCommand ApplyFuelCommand{get;}public ActionCommand KeepFuelCommand{get;}public ActionCommand RestoreFuelCommand{get;}public ActionCommand RestoreCargoCommand{get;}public ActionCommand UnloadCommand{get;}
    public AircraftLoadingViewModel(AircraftLoadingCoordinator service)
    {this.service=service;service.Changed+=Apply;RefreshCommand=new(()=>_=Run(service.RefreshAsync,"Live aircraft loading data refreshed."),()=>!busy);
     ClearAllocationCommand=new(()=>{foreach(var x in PayloadStations)x.AssignedKilograms=0;},()=>!busy&&PayloadStations.Count>0);
     ApplyCargoCommand=new(()=>_=ApplyPlannedCargoAsync(),()=>!busy&&service.Current.CanApplyCargo&&(AllocationValid||service.Current.Manifest?.State is LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied));
     SimulatedCargoCommand=new(()=>_=Run(service.ApplySimulatedCargoAsync,"Simulated Frontier loading committed; MSFS payload unchanged."),()=>!busy&&service.Current.CanApplyCargo);
     ApplyFuelCommand=new(()=>_=Run(t=>service.ApplyFuelAsync(FuelTanks.ToDictionary(x=>x.Id,x=>x.PlannedGallons),t),"Fuel applied and read back from MSFS."),()=>!busy&&service.Current.CanApplyFuel);
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
    public string CargoReason=>service.Current.CargoActionReason;public string FuelReason=>service.Current.FuelActionReason;public string Reconciliation=>service.Current.ReconciliationReason;
    public string EngineState=>service.Current.Live is null?"—":service.Current.Live.Engines.Count==0?"Unavailable":string.Join(", ",service.Current.Live.Engines.Select(x=>$"Engine {x.Index}: {(x.IsRunning is null?"unknown":x.IsRunning.Value?"running":"off")}"));
    public string WeightAndBalance
    {get{var s=service.Current.Live;if(s is null)return "No live weight and balance data.";var cargo=service.Current.Plan?.CargoKilograms??AssignedCargo;var w=s.Weights;var existing=s.PayloadStations.Sum(x=>x.CurrentWeightKilograms);var currentFuel=s.FuelWeightKilogramsPerGallon is { } d?s.FuelTanks.Sum(x=>x.QuantityGallons)*d:(decimal?)null;var plannedFuel=s.FuelWeightKilogramsPerGallon is { } d2?FuelTanks.Sum(x=>x.PlannedGallons)*d2:(decimal?)null;var plannedGross=w.CurrentGrossWeightKilograms+cargo+(plannedFuel-currentFuel);return $"Empty: {Mass(w.EmptyWeightKilograms)}\nExisting payload: {Mass(existing)}\nFrontier cargo: {Mass(cargo)}\nTotal payload: {Mass(existing+cargo)}\nCurrent fuel: {Mass(currentFuel)}\nPlanned fuel: {Mass(plannedFuel)}\nZero-fuel weight: {Mass(w.EmptyWeightKilograms+existing+cargo)}\nRamp/current gross: {Mass(w.CurrentGrossWeightKilograms)}\nPlanned gross: {Mass(plannedGross)}\nMaximum gross: {Mass(w.MaximumGrossWeightKilograms)}\nCurrent CG: {Pct(w.CurrentCgPercent)}\nPlanned CG: {(service.Current.Plan?.PlannedCgPercent is { } c?Pct(c):"Unavailable from aircraft data")}\nReported CG limits: {Pct(w.ForwardCgLimitPercent)} to {Pct(w.AftCgLimitPercent)}";}}
    public string Warnings{get{var warnings=service.Current.Warnings.ToList();var s=service.Current.Live;if(s?.FuelWeightKilogramsPerGallon is { } d&&s.Weights.CurrentGrossWeightKilograms is { } gross&&s.Weights.MaximumGrossWeightKilograms is { } maximum){var planned=gross+AssignedCargo+(FuelTanks.Sum(x=>x.PlannedGallons)-s.FuelTanks.Sum(x=>x.QuantityGallons))*d;if(planned>maximum&&!warnings.Any(x=>x.Contains("maximum gross")))warnings.Add("Planned weight is above the reported maximum gross weight.");}return string.Join("\n",warnings);}}public decimal AssignedCargo=>PayloadStations.Sum(x=>x.AssignedKilograms);public decimal RemainingCargo=>ActiveCargo-AssignedCargo;
    public double AllocationProgressMaximum=>Math.Max(1,(double)ActiveCargo);public double AllocationProgressValue=>Math.Clamp((double)AssignedCargo,0,AllocationProgressMaximum);
    public string AllocationState=>AllocationValid?"READY TO APPLY":RemainingCargo>0?$"ASSIGN {RemainingCargo:0.##} KG MORE":$"REMOVE {-RemainingCargo:0.##} KG";
    public bool CanUnload=>service.Current.CanUnload;
    private decimal ActiveCargo=>service.ActiveCargoKilograms;
    public bool AllocationValid=>ActiveCargo>0&&Math.Abs(AssignedCargo-ActiveCargo)<=.001m;
    private void Apply(AircraftLoadingStatus value)
    {if(value.Live is { } live){var allocations=PayloadStations.ToDictionary(x=>x.Index,x=>x.AssignedKilograms);PayloadStations.Clear();foreach(var p in live.PayloadStations){var maximum=Math.Max(0,Math.Min(ActiveCargo,p.AuthoritativeMaximumKilograms is { } limit?limit-p.CurrentWeightKilograms:ActiveCargo));var row=new PayloadStationViewModel{Index=p.Index,Name=p.Name,CurrentKilograms=p.CurrentWeightKilograms,LimitKilograms=p.AuthoritativeMaximumKilograms,AssignmentMaximumKilograms=maximum,AssignedKilograms=Math.Min(maximum,allocations.GetValueOrDefault(p.Index))};row.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(PayloadStationViewModel.AssignedKilograms)){Changed(nameof(AssignedCargo));Changed(nameof(RemainingCargo));Changed(nameof(AllocationValid));Changed(nameof(AllocationProgressValue));Changed(nameof(AllocationState));Changed(nameof(WeightAndBalance));Changed(nameof(Warnings));ApplyCargoCommand.Refresh();}};PayloadStations.Add(row);}FuelTanks.Clear();foreach(var f in live.FuelTanks){var row=new FuelTankViewModel{Id=f.Id,Name=f.Name,CurrentGallons=f.QuantityGallons,CapacityGallons=f.CapacityGallons,PlannedGallons=f.QuantityGallons};row.PropertyChanged+=(_,_)=>{Changed(nameof(WeightAndBalance));Changed(nameof(Warnings));};FuelTanks.Add(row);}}
     NotifyAll();}
    private void UpdatePlan(){try{service.CreatePlan(PayloadStations.Select(x=>new CargoStationAllocation(x.Index,x.AssignedKilograms)).ToArray());Error="";}catch(Exception e){Error=e.Message;}NotifyAll();}
    private async Task ApplyPlannedCargoAsync(){if(service.Current.Manifest?.State is not (LoadManifestState.PendingApplication or LoadManifestState.SimulatorApplied))UpdatePlan();if(Error=="")await Run(service.ApplyCargoAsync,"MSFS payload verified and shipment committed InTransit.");}
    private async Task Run(Func<CancellationToken,Task> action,string success){if(busy)return;busy=true;Error="";Refresh();try{await action(default);Status=success;}catch(Exception e){Error=e.Message;Status="Loading action was refused; persistent economy and simulator state were not guessed.";}finally{busy=false;Refresh();}}
    private static string Mass(decimal? x)=>x is null?"Unavailable":$"{x:0.0} kg / {LoadingUnits.KilogramsToPounds(x.Value):0.0} lb";private static string Pct(double? x)=>x is null?"Unavailable":$"{x:0.00}%";
    private static string FuelMass(AircraftLoadingSnapshot s)=>s.FuelWeightKilogramsPerGallon is { } d?$"{s.FuelTanks.Sum(x=>x.QuantityGallons)*d:0.0} kg":"Unavailable (fuel density not reported)";
    private void NotifyAll(){foreach(var n in new[]{nameof(Shipment),nameof(Aircraft),nameof(CargoReason),nameof(FuelReason),nameof(Reconciliation),nameof(EngineState),nameof(WeightAndBalance),nameof(Warnings),nameof(AssignedCargo),nameof(RemainingCargo),nameof(AllocationValid),nameof(AllocationProgressMaximum),nameof(AllocationProgressValue),nameof(AllocationState),nameof(CargoMassKilograms),nameof(CanUnload)})Changed(n);Refresh();}
    private void Refresh(){foreach(var c in new[]{RefreshCommand,ClearAllocationCommand,ApplyCargoCommand,SimulatedCargoCommand,ApplyFuelCommand,KeepFuelCommand,RestoreFuelCommand,RestoreCargoCommand,UnloadCommand})c.Refresh();Changed(nameof(Status));Changed(nameof(Error));}
    private void Changed([CallerMemberName]string? n=null)=>PropertyChanged?.Invoke(this,new(n));
}

