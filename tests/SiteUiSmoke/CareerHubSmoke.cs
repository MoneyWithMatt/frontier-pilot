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
        model.Career.Navigation.LoadingSection = 0; Pump(); Save(host, Path.Combine(folder, "career-hub-cargo-loading.png"));
        model.Career.Show(CareerSection.Overview); model.Career.FleetLoadCommand.Execute(null); Pump();
        Check(ReferenceEquals(cargo, ((ContentControl)hub.FindName("CargoWorkspace")).Content), "Fleet re-entry reuses exact cargo control instance");
        await model.Career.RefreshAsync(); Check(model.Career.Navigation.Section == 1 && model.Career.Navigation.AircraftSection == 2, "Refresh retains Career and aircraft subsection");
        model.Career.Show(CareerSection.CreateJobs); Pump();
        Check(!model.Career.OwnerJobs.PublishCommand.CanExecute(null), "Employee cannot publish company contracts");
        Save(host, Path.Combine(folder, "career-hub-owner-empty.png"));
        host.Width = 1024; host.Height = 768; model.Career.Show(CareerSection.JobBoard); Pump();
        Check(hub.ActualWidth > 650 && map.ActualWidth > 250, "Career two-panel board fits ordinary desktop width");
        Save(host, Path.Combine(folder, "career-hub-1024.png")); host.Close();
    }
}
