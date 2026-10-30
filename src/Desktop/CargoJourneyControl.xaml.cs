using System.Windows.Controls;
using System.Windows;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Desktop;

public partial class CargoJourneyControl : UserControl
{
    public CargoJourneyControl() => InitializeComponent();
    private void LoadCargo_Click(object sender,RoutedEventArgs e)
    {
        if(DataContext is CargoJourneyViewModel vm && ((FrameworkElement)sender).DataContext is CargoConsignmentRow cargo)
        {vm.BeginPickup(cargo);CargoStationPicker.UpdateLayout();if(CargoStationPicker.IsVisible)CargoStationPicker.BringIntoView();}
    }
    private void ChooseStation_Click(object sender,RoutedEventArgs e)
    {if(DataContext is CargoJourneyViewModel vm && ((FrameworkElement)sender).DataContext is CargoStationRow station)vm.AddPickup(station);}
    private void DeliverCargo_Click(object sender,RoutedEventArgs e)
    {if(DataContext is CargoJourneyViewModel vm && ((FrameworkElement)sender).DataContext is CargoConsignmentRow cargo)vm.DeliverConsignment(cargo);}
    private void RemovePickup_Click(object sender,RoutedEventArgs e)
    {if(DataContext is CargoJourneyViewModel vm && ((FrameworkElement)sender).DataContext is CargoPickupPlan pickup)vm.RemovePickup(pickup);}
    private void ClosePicker_Click(object sender,RoutedEventArgs e)
    {if(DataContext is CargoJourneyViewModel vm)vm.ClosePickup();}
}
