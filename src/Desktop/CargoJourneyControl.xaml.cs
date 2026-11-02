using System.Windows.Controls;
using System.Windows;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Desktop;

public partial class CargoJourneyControl : UserControl
{
    private Guid? displayedAirframe;
    public CargoJourneyControl() => InitializeComponent();
    private void LoadCargo_Click(object sender,RoutedEventArgs e)
    {
        if(DataContext is CargoJourneyViewModel vm && ((FrameworkElement)sender).DataContext is CargoConsignmentRow cargo)
        {vm.SelectCargo(cargo);CargoStationPicker.UpdateLayout();if(vm.IsChoosingStation)CargoStationPicker.BringIntoView();}
    }
    private void ChooseStation_Click(object sender,RoutedEventArgs e)
    {
        if(DataContext is CargoJourneyViewModel vm && ((FrameworkElement)sender).DataContext is CargoStationRow station)
        {vm.ChooseStation(station);}
    }
    private void ChangePickup_Click(object sender,RoutedEventArgs e)
    {if(DataContext is CargoJourneyViewModel vm && ((FrameworkElement)sender).DataContext is CargoPickupPlan pickup){vm.ChangePickup(pickup);CargoStationPicker.BringIntoView();}}
    private void AircraftSelection_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(DataContext is not CargoJourneyViewModel vm || vm.Fleet?.Selected is not {} aircraft)return;
        if(displayedAirframe==aircraft.Id)return;
        displayedAirframe=aircraft.Id;
        if(IsLoaded && vm.CanChooseAircraft)_=vm.RefreshAsync();
    }
    private void DeliverCargo_Click(object sender,RoutedEventArgs e)
    {if(DataContext is CargoJourneyViewModel vm && ((FrameworkElement)sender).DataContext is CargoConsignmentRow cargo)vm.DeliverConsignment(cargo);}
    private void RemovePickup_Click(object sender,RoutedEventArgs e)
    {if(DataContext is CargoJourneyViewModel vm && ((FrameworkElement)sender).DataContext is CargoPickupPlan pickup)vm.RemovePickup(pickup);}
    private void ClosePicker_Click(object sender,RoutedEventArgs e)
    {if(DataContext is CargoJourneyViewModel vm)vm.ClosePickup();}
}
