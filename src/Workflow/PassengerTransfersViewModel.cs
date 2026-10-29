using System.ComponentModel;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed class PassengerTransfersViewModel : INotifyPropertyChanged
{
    private readonly PassengerTransfersService service;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private readonly Action? connect;
    private bool busy;
    private string error = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public ActionCommand RefreshCommand { get; }
    public ActionCommand AcceptCommand { get; }
    public ActionCommand BoardCommand { get; }
    public ActionCommand DisembarkCommand { get; }
    public bool Enabled => service.Enabled;
    public bool IsBusy => busy;
    public string Error => error;
    public string Description => Enabled ? "Offshore specialist transfers · $800 per verified airborne hour, paid on disembarkation. One prototype passenger per transfer. Boarding is simulated; MSFS payload and passenger animations are unchanged. Platform destinations remain unavailable until live qualification." : "Passenger transfers are available in the Offshore career.";
    public string Status => service.Reason;
    public string Balance => DollarDisplay.Format(service.Balance);
    public IReadOnlyList<PassengerTransfer> Transfers => service.VisibleTransfers;
    public PassengerTransfer? SelectedTransfer
    {
        get => service.Selected;
        set { if (value is null) return; service.SelectedId = value.Id; Changed(); }
    }
    public IReadOnlyList<CompanyAircraftType> AircraftChoices => service.AircraftChoices;
    private string? selectedType;
    public CompanyAircraftType? SelectedAircraft { get => AircraftChoices.FirstOrDefault(x => x.Id == selectedType); set { selectedType = value?.Id; Changed(); } }
    public bool HasBoardedPassengers => service.HasBoardedPassengers;
    public PassengerTransfersViewModel(PassengerTransfersService service, Action? connect = null)
    {
        this.service = service; this.connect = connect;
        RefreshCommand = new(() => _ = RefreshAsync(), () => !busy && Enabled);
        AcceptCommand = new(() => _ = AcceptAsync(), () => !busy && SelectedTransfer?.State == PassengerTransferState.Offered && SelectedAircraft is not null);
        BoardCommand = new(() => _ = BoardAsync(), () => !busy && service.CanBoard);
        DisembarkCommand = new(() => _ = DisembarkAsync(), () => !busy && service.CanDisembark);
    }
    public Task RefreshAsync() => Run(() => service.RefreshAsync());
    public Task AcceptAsync() => Run(() => service.AcceptAsync(selectedType ?? ""));
    public Task BoardAsync() => Run(() => service.BoardAsync());
    public Task DisembarkAsync() => Run(() => service.DisembarkAsync());
    public async Task ObserveAsync(AircraftTelemetry? sample)
    {
        try { await service.ObserveAsync(sample).ConfigureAwait(false); }
        catch (Exception ex) { error = ex.Message; }
        Changed();
    }
    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true; error = ""; Changed();
        try { connect?.Invoke(); await action(); }
        catch (Exception ex) { error = ex.Message; }
        finally { busy = false; Changed(); }
    }
    private void Changed()
    {
        void Notify() { PropertyChanged?.Invoke(this, new("")); RefreshCommand?.Refresh(); AcceptCommand?.Refresh(); BoardCommand?.Refresh(); DisembarkCommand?.Refresh(); }
        if (context is not null && SynchronizationContext.Current != context) context.Post(_ => Notify(), null); else Notify();
    }
}
