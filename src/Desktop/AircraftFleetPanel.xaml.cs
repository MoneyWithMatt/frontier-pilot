using System.Windows;
using System.Windows.Controls;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Desktop;
public partial class AircraftFleetPanel : UserControl
{
    private AircraftFleetViewModel? viewModel;
    private string positions = "";
    public AircraftFleetPanel()
    {
        InitializeComponent();
        DataContextChanged += (_,_) => { if(viewModel is not null)viewModel.FleetChanged-=Refresh;viewModel=DataContext as AircraftFleetViewModel;if(viewModel is not null)viewModel.FleetChanged+=Refresh;Refresh(); };
        FleetMap.LocationSelected += key => { if(viewModel is not null)viewModel.Selected=viewModel.Aircraft.SingleOrDefault(a=>a.Id.ToString()==key); };
        Unloaded += (_,_) => { if(viewModel is not null)viewModel.FleetChanged-=Refresh; };
        Loaded += (_,_) => { if(viewModel is not null){viewModel.FleetChanged-=Refresh;viewModel.FleetChanged+=Refresh;}Refresh(); };
    }
    private void Refresh()
    {
        if(!Dispatcher.CheckAccess()){_ = Dispatcher.BeginInvoke(new Action(Refresh));return;}
        if(viewModel is null)return;
        var aircraft=viewModel.Aircraft.Where(a=>a.Latest is not null).ToArray();
        var stamp=string.Join(";",aircraft.Select(a=>$"{a.Id}:{a.Latest!.Telemetry.Latitude:F4}:{a.Latest.Telemetry.Longitude:F4}"));
        if(stamp==positions)return;positions=stamp;
        FleetMap.Show(aircraft.Select(a=>new OperationsMapLocation(a.Id.ToString(),a.Registration,a.Registration,
            OperationsMapLocationKind.PlayerAirport,"Airframe",a.Operator,a.Latest!.Telemetry.Latitude,a.Latest.Telemetry.Longitude,a.LocationLabel,[],[],null)).ToArray(), recenter: false);
    }
    private void EstablishBaseline(object sender,RoutedEventArgs e)
    {
        if(viewModel?.Selected is null)return;
        if(MessageBox.Show("Declare the selected aircraft's currently unknown components healthy for Frontier modelling? Historical hours stay unknown. This is your explicit baseline, not an inspection result or a simulator repair.","Establish baseline",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes)
            viewModel.EstablishBaseline();
    }
    private void ConfirmLegacyAssignment(object sender,RoutedEventArgs e)
    {
        if(viewModel?.SelectedLegacyCandidate is not {} a)return;
        if(MessageBox.Show($"Confirm existing aircraft {a.Registration} ({a.Id}) as company-owned and assigned to this player, using the selected company type? Existing identity, condition, location and history are retained. No simulator state changes.",
            "Reconcile legacy company assignment",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes)
            viewModel.ConfirmLegacyAssignment();
    }
    private async void BookMaintenance(object sender,RoutedEventArgs e)
    {
        if(viewModel is null)return;
        if(MessageBox.Show(viewModel.QuoteDescription+"\n\nBook this work for the selected component?", "Confirm maintenance",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes)
            await viewModel.BookMaintenanceAsync();
    }
    private void AcknowledgeRelocation(object sender,RoutedEventArgs e)
    {
        if(viewModel?.Selected?.LocationReconciliationRequired!=true)return;
        if(MessageBox.Show("Acknowledge this external simulator relocation? This records the change only; cargo, flight evidence and payments are unchanged.","Location reconciliation",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes)
            viewModel.AcknowledgeRelocation();
    }
}
