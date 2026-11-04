using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.Desktop;

internal static partial class Program
{
    private static async Task CheckCareerHub(string folder)
    {
        var root = Path.Combine(Path.GetTempPath(), "FrontierCareerHubUi", Guid.NewGuid().ToString("N"));
        var store = new CareerStore(root); store.SaveDraft(CareerStage.Opportunities, "Synthetic Hub Pilot", "namibia");
        var employer = CareerCatalogue.Employers.Single(e => e.Category == "Offshore"); store.AcceptEmployment(employer.Id);
        var airports = new EmployerDestinationCatalogue([new("FYWB", "Approved", "FYWB", -23, 15, "Synthetic Career Hub fixture", DateTimeOffset.UtcNow, [])]);
        var model = new BuilderViewModel(BuilderServices.CreateDryRun(), "", careerStore: store, employerDestinations: airports);
        var host = new MainWindow(model) { Width = 1280, Height = 900, ShowInTaskbar = false }; host.Show();
        await Task.Delay(400); await model.Career.Board.RefreshAsync(); Pump();
        var hub = (CareerHubControl)host.FindName("CareerHub");
        Check(((TabControl)host.FindName("MainTabs")).SelectedItem == host.FindName("CareerTab"), "Career is the default main workspace");
        Check(Descendants<TextBlock>(hub).Any(t => t.Text == employer.Name), "Overview renders actual employer");
        Save(host, Path.Combine(folder, "career-hub-overview.png"));
        var selected = model.Fleet.Aircraft.Single(a => a.CompanyTypeId == "vision-jet");
        model.Career.Show(CareerSection.Fleet); ((ComboBox)hub.FindName("FleetSelector")).SelectedValue = selected.Id; Pump();
        Check(model.Fleet.SelectedAirframeId == selected.Id, "Career fleet selector propagates exact physical GUID");
        model.Career.Navigation.AircraftSection = 1; Pump();
        Check(!Descendants<Button>(hub).Any(b => b.Content?.ToString()?.Contains("healthy baseline") == true), "Career condition workspace exposes no healthy reset");
        Save(host, Path.Combine(folder, "career-hub-condition.png"));
        model.Career.Show(CareerSection.FindWork); Pump(); Save(host, Path.Combine(folder, "career-hub-find-work.png"));
        Check(Descendants<ItemsControl>(hub).Any(i => ReferenceEquals(i.ItemsSource, model.Career.Work.Employers)), "Find Work uses catalogue listings");
        model.Career.Show(CareerSection.JobBoard);
        model.Career.Board.Selected = model.Career.Board.All.First(j => j.Family == CareerJobFamily.Observation); Pump();
        var map = (CareerJobMap)hub.FindName("JobMap");
        Check(map.RoutePointCount == 3 && map.HasObservationArea && map.DisplayedJobKey == model.Career.Board.Selected.Key, "Job selection draws ordered return route and observation area");
        var selectedJobKey = model.Career.Board.Selected.Key;
        for (int i = 0; i < 2; i++) { await model.Career.RefreshAsync(); Pump(); }
        Check(model.Career.Board.Selected?.Key == selectedJobKey && ((ListBox)hub.FindName("JobList")).SelectedItem is CareerJobRow visible && visible.Key == selectedJobKey, "Visible job selection survives immutable refresh without transient null writeback");
        Save(host, Path.Combine(folder, "career-hub-job-board.png"));
        model.Career.Board.Selected = model.Career.Board.All.First(j => j.Family == CareerJobFamily.Personnel && j.Available); Pump();
        model.Career.Board.SelectedAircraft = model.Career.Board.AircraftChoices.Single(a => a.Id == "vision-jet");
        await model.Career.Board.AcceptAsync(); Pump();
        Check(model.Career.HasActive && model.Career.Board.Selected!.Accepted, "Career passenger acceptance persists active assignment through existing service");
        await model.Career.OpenLoadAsync(model.Career.Board.Selected); Pump();
        var passengers = ((ContentControl)hub.FindName("PassengerWorkspace")).Content;
        var cargo = ((ContentControl)hub.FindName("CargoWorkspace")).Content;
        Check(ReferenceEquals(((FrameworkElement)passengers).DataContext, model.Passengers), "Accepted-job entry uses the original shared passenger workspace");
        Check(ReferenceEquals(((FrameworkElement)cargo).DataContext, model.CargoJourney), "Fleet entry retains original click-to-assign cargo workspace");
        Check(model.Fleet.SelectedAirframeId == selected.Id && model.Passengers.SelectedTransfer!.AirframeId is null, "Opening loading preserves physical GUID without silently binding assignment");
        Check(!model.Passengers.BoardCommand.CanExecute(null), "Unqualified SF50 physical boarding remains blocked");
        Save(host, Path.Combine(folder, "career-hub-passenger-loading.png"));
        var passengerScroller = Descendants<ScrollViewer>((DependencyObject)passengers).First();
        Check(passengerScroller.ScrollableHeight > 0, "Embedded passenger manifest and recovery actions remain reachable by scrolling at ordinary desktop height");
        var manifestInput = Descendants<TextBox>((DependencyObject)passengers).Single(t => t.Name == "PassengerManifestWeights");
        manifestInput.BringIntoView(); Pump();
        var inputPosition = manifestInput.TransformToAncestor(passengerScroller).Transform(new Point(0, 0));
        Check(inputPosition.Y >= 0 && inputPosition.Y + manifestInput.ActualHeight <= passengerScroller.ActualHeight + 1, "Passenger manifest input can be brought fully into the embedded viewport");
        Save(host, Path.Combine(folder, "career-hub-passenger-actions.png"));
        model.Career.Navigation.LoadingSection = 0; Pump(); Save(host, Path.Combine(folder, "career-hub-cargo-loading.png"));
        model.Career.Show(CareerSection.Overview); model.Career.FleetLoadCommand.Execute(null); Pump();
        Check(ReferenceEquals(cargo, ((ContentControl)hub.FindName("CargoWorkspace")).Content), "Fleet re-entry reuses exact cargo control instance");
        await model.Career.RefreshAsync(); Check(model.Career.Navigation.Section == 1 && model.Career.Navigation.AircraftSection == 2, "Refresh retains Career and aircraft subsection");
        model.Career.Show(CareerSection.CreateJobs); Pump();
        Check(!model.Career.OwnerJobs.PublishCommand.CanExecute(null), "Employee cannot publish company contracts");
        Save(host, Path.Combine(folder, "career-hub-owner-empty.png"));
        host.Width = 1024; host.Height = 768; model.Career.Show(CareerSection.JobBoard); Pump();
        Check(hub.ActualWidth > 650 && map.ActualWidth > 250, "Career two-panel board fits ordinary desktop width");
        Save(host, Path.Combine(folder, "career-hub-1024.png"));
        model.Career.Show(CareerSection.Fleet); model.Career.Navigation.AircraftSection = 0; Pump();
        var recovery = (Button)hub.FindName("AcknowledgeAircraftRelocation");
        Check(!recovery.IsVisible, "Career aircraft move recovery is hidden when no relocation needs review");
        model.Fleet.ResolveCompanyType = _ => "vision-jet";
        var basis = CareerCatalogue.Bases.Single(b => b.Id == employer.BaseId);
        var sample = new AircraftTelemetry(DateTimeOffset.UtcNow.AddSeconds(-12), "Synthetic location recovery", "Synthetic model", basis.Latitude, basis.Longitude, 500, 0, 0, true);
        for (var i = 0; i <= 11; i++) model.Fleet.ObserveTracking(new(sample with { ObservedUtc = sample.ObservedUtc.AddSeconds(i) }, 42, 42, null, PlayerControlMode.InAircraft, null, "Synthetic UI recovery"), null);
        model.Fleet.ObserveTracking(null, null);
        var moved = sample with { ObservedUtc = DateTimeOffset.UtcNow, Latitude = sample.Latitude + 3 };
        model.Fleet.ObserveTracking(new(moved, 42, 42, null, PlayerControlMode.InAircraft, null, "Synthetic UI recovery"), null); Pump();
        recovery.BringIntoView(); Pump();
        Check(model.Fleet.Selected!.LocationReconciliationRequired && recovery.IsVisible && recovery.IsEnabled, "Career Hub exposes the existing recovery action after an unobserved move");
        Save(host, Path.Combine(folder, "career-aircraft-relocation-review.png"));
        var transportBeforeRecovery = System.Text.Json.JsonSerializer.Serialize(new { model.Passengers.Transfers, model.Jobs.ReservedJobs, model.Passengers.Balance });
        model.Fleet.AcknowledgeRelocation(); Pump();
        Check(!recovery.IsVisible && !model.Fleet.Selected!.LocationReconciliationRequired && model.Fleet.Selected.Latest!.State == AirframePositionState.Uncertain, "Acknowledgement hides recovery without certifying a flown arrival");
        await model.Passengers.RefreshAsync(); await model.Jobs.RefreshLiveAsync(); Pump();
        Check(transportBeforeRecovery == System.Text.Json.JsonSerializer.Serialize(new { model.Passengers.Transfers, model.Jobs.ReservedJobs, model.Passengers.Balance }), "Aircraft move acknowledgement preserves accepted transport and balance after authoritative reload");
        host.Close();
    }
}
