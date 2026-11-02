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
    public ActionCommand ManifestCommand { get; }
    public ActionCommand CheckPayloadCommand { get; }
    private string manifestWeights = "", aircraftPackageDirectory = "";
    public string ManifestWeights { get => manifestWeights; set { manifestWeights = value; Changed(); } }
    public string AircraftPackageDirectory { get => aircraftPackageDirectory; set { aircraftPackageDirectory = value; Changed(); } }
    public string PayloadStatus => service.PayloadStatus;
    public string SupportSummary => service.SupportSummary;
    public bool Enabled => service.Enabled;
    public bool IsBusy => busy;
    public string Error => error;
    public string Description => Enabled ? "Offshore specialist transfers / Missionary community visits. Existing career hourly wages are paid once after verified flight and disembarkation. Enter one declared kilogram weight per passenger. Boarding requires an evidenced aircraft configuration and actual payload readback. Platform destinations remain unavailable until live qualification." : "Passenger transfers are available in Offshore and Missionary careers.";
    public string Status => SelectedTransfer?.State switch
    {
        PassengerTransferState.Delivered => "Transfer completed. Passenger disembarked; hourly wages were paid once.",
        PassengerTransferState.Offered => "Choose the company aircraft and reserve this transfer.",
        _ => service.Reason
    };
    public string Balance => DollarDisplay.Format(service.Balance);
    public IReadOnlyList<PassengerTransfer> Transfers => service.VisibleTransfers;
    public PassengerTransfer? SelectedTransfer
    {
        get => service.Selected;
        set { if (value is null || value.Id == service.SelectedId) return; service.SelectedId = value.Id; Changed(); }
    }
    public IReadOnlyList<CompanyAircraftType> AircraftChoices => service.AircraftChoices;
    private string? selectedType;
    public CompanyAircraftType? SelectedAircraft
    {
        get => AircraftChoices.FirstOrDefault(x => x.Id == selectedType);
        set { if (value is null || value.Id == selectedType) return; selectedType = value.Id; Changed(); }
    }
    public bool HasBoardedPassengers => service.HasBoardedPassengers;
    public PassengerTransfersViewModel(PassengerTransfersService service, Action? connect = null)
    {
        this.service = service; this.connect = connect;
        RefreshCommand = new(() => _ = RefreshAsync(), () => !busy && Enabled);
        AcceptCommand = new(() => _ = AcceptAsync(), () => !busy && SelectedTransfer?.State == PassengerTransferState.Offered && SelectedAircraft is not null);
        BoardCommand = new(() => _ = BoardAsync(), () => !busy && service.CanBoard);
        DisembarkCommand = new(() => _ = DisembarkAsync(), () => !busy && service.CanDisembark);
        ManifestCommand = new(() => _ = SetManifestAsync(), () => !busy && SelectedTransfer?.State == PassengerTransferState.Reserved);
        CheckPayloadCommand = new(() => _ = CheckPayloadAsync(), () => !busy && SelectedTransfer?.State == PassengerTransferState.Boarded);
    }
    public Task RefreshAsync() => Run(() => service.RefreshAsync());
    public Task AcceptAsync() => Run(() => service.AcceptAsync(selectedType ?? ""));
    public Task BoardAsync() => Run(() => service.BoardAsync());
    public Task DisembarkAsync() => Run(() => service.DisembarkAsync());
    public Task CheckPayloadAsync() => Run(() => service.CheckPayloadAsync());
    public Task SetManifestAsync() => Run(() => service.SetManifestAsync(manifestWeights.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(x => decimal.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray()));
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
        void Notify() { PropertyChanged?.Invoke(this, new("")); RefreshCommand?.Refresh(); AcceptCommand?.Refresh(); BoardCommand?.Refresh(); DisembarkCommand?.Refresh(); ManifestCommand?.Refresh(); CheckPayloadCommand?.Refresh(); }
        if (context is not null && SynchronizationContext.Current != context) context.Post(_ => Notify(), null); else Notify();
    }
}
