using System.ComponentModel;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>Own-site logistics authorisation, not a new aviation-company role or NPC contract permission.</summary>
public sealed class CareerOwnerJobsViewModel : INotifyPropertyChanged
{
    public ProspectingOperationsViewModel Operations { get; }
    private EconomyWorld? world;
    private bool busy;
    public CareerOwnerJobsViewModel(ProspectingOperationsViewModel operations)
    {
        Operations = operations;
        PublishCommand = new(() => _ = PublishAsync(), () => !busy && CanPublish);
    }
    public IReadOnlyList<OperationDisplay> OwnedOperations => Operations.Operations.Where(o => world?.Operations.Any(w => w.Id == o.OperationId && w.OwnerId == EmployedCargoPrototype.PlayerId) == true).ToArray();
    public OperationDisplay? SelectedOperation { get => OwnedOperations.FirstOrDefault(o => o.OperationId == Operations.SelectedOperation?.OperationId); set { if (value is not null && OwnedOperations.Any(o => o.OperationId == value.OperationId)) Operations.SelectedOperation = value; Notify(); } }
    public bool HasOwnedOperations => OwnedOperations.Count > 0;
    public bool CanPublish => SelectedOperation is {} o && world?.Operations.Any(w => w.Id == o.OperationId && w.OwnerId == EmployedCargoPrototype.PlayerId) == true && Operations.PublishLogisticsCommand.CanExecute(null);
    public string Explanation => "Existing capability: cargo logistics orders for your own founded site operations. This does not authorise contracts for your NPC employer. General aviation-company jobs, passenger orders, hiring and multiplayer publication are not implemented.";
    public string EmptyState => "No authorised owned operation is available. Employees cannot create employer contracts. Founded player-owned sites can use the existing logistics-order workflow; aviation-company ownership and manager permissions remain future work.";
    public string Error { get; private set; } = "";
    public ActionCommand PublishCommand { get; }
    public Func<Task>? Reload { get; set; }
    public async Task PublishAsync()
    {
        if (busy || !CanPublish) { Error = "Select an authorised operation that you own. No employer contract was created."; Notify(); return; }
        busy = true; Error = ""; Notify();
        try { await Operations.PublishLogisticsAsync(); Error = Operations.Error; if (Reload is not null) await Reload(); }
        catch (Exception e) { Error = e.Message; } finally { busy = false; Notify(); }
    }
    public void Update(EconomyWorld? snapshot) { world = snapshot; Notify(); }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Notify() { PropertyChanged?.Invoke(this, new("")); PublishCommand.Refresh(); }
}
