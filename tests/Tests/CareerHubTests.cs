using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class CareerHubTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FrontierCareerHub", Guid.NewGuid().ToString("N"));
    private BuilderViewModel Create(string category = "Offshore")
    {
        var store = new CareerStore(root);
        store.SaveDraft(CareerStage.Opportunities, "Synthetic Career Pilot", "namibia");
        store.AcceptEmployment(CareerCatalogue.Employers.Single(e => e.Category == category).Id);
        var now = DateTimeOffset.UtcNow;
        var airports = new EmployerDestinationCatalogue([new("FYWB", "Approved", "FYWB", -23, 15, "Synthetic hub test only", now, []),
            new("FYWE", "Approved", "FYWE", -22.6, 17.1, "Synthetic hub test only", now, [])]);
        return new(BuilderServices.CreateDryRun(), "", careerStore: store, employerDestinations: airports);
    }
    private static async Task Initialize(BuilderViewModel vm)
    { await vm.Jobs.InitializeAsync(); await vm.Passengers.RefreshAsync(); await vm.Offshore.RefreshAsync(); await vm.Operations.RefreshAsync(); await vm.Career.RefreshAsync(); }

    [Theory][InlineData("Offshore", 800)][InlineData("Missionary", 400)][InlineData("Industrial", 650)][InlineData("Safari", 400)]
    public async Task OverviewAndMarketplaceUseCatalogueWithoutInventingLocationsOrMaintenance(string category, int rate)
    {
        var vm = Create(category); await Initialize(vm); var hub = vm.Career;
        var employer = CareerCatalogue.Employers.Single(e => e.Category == category);
        Assert.Equal(employer.Name, hub.Overview.Company);
        Assert.StartsWith("$" + rate, hub.Overview.Pay);
        Assert.Contains("verified airborne hour", hub.Overview.Pay);
        Assert.Equal(4, hub.Work.Employers.Count); Assert.Single(hub.Work.Employers.Where(e => e.Current));
        Assert.Equal(employer.Fleet.Length, hub.Fleet.Aircraft.Count);
        Assert.All(hub.Fleet.Aircraft, a => { Assert.Contains("not observed location", a.Base); Assert.Contains("No simulator", a.Location); Assert.Contains("No confirmed", a.Binding); });
        Assert.Contains("inspect", hub.Fleet.MaintenancePolicy); Assert.Contains("not safely supported", hub.Work.Switching);
        await vm.Fleet.ShutdownAsync();
    }
    [Fact] public async Task NavigationAndPhysicalGuidSurviveReadOnlyRefreshWithoutSaveMigration()
    {
        var vm = Create(); await Initialize(vm); var hub = vm.Career;
        var aircraft = hub.Fleet.Aircraft.Single(a => a.Airframe.CompanyTypeId == "vision-jet");
        hub.Fleet.SelectedId = aircraft.Id; hub.Show(CareerSection.FindWork);
        hub.Navigation.AircraftSection = 1; hub.Navigation.LoadingSection = 1;
        var profile = File.ReadAllBytes(Path.Combine(root, "career.json"));
        var fleet = File.ReadAllBytes(Path.Combine(root, "fleet", "aircraft-fleet.json"));
        for (int i = 0; i < 3; i++) await hub.RefreshAsync();
        Assert.Equal((int)CareerSection.FindWork, hub.Navigation.Section); Assert.Equal(1, hub.Navigation.AircraftSection); Assert.Equal(1, hub.Navigation.LoadingSection);
        Assert.Equal(aircraft.Id, vm.Fleet.SelectedAirframeId); Assert.Equal(aircraft.Id, hub.Fleet.SelectedId);
        Assert.Equal(profile, File.ReadAllBytes(Path.Combine(root, "career.json"))); Assert.Equal(fleet, File.ReadAllBytes(Path.Combine(root, "fleet", "aircraft-fleet.json")));
        var selected = vm.Fleet.SelectedAirframeId; Assert.False(hub.SelectAircraft(Guid.NewGuid())); Assert.Equal(selected, vm.Fleet.SelectedAirframeId);
        await vm.Fleet.ShutdownAsync();
    }
    [Fact] public async Task UnifiedBoardDelegatesPassengerReservationOnceAndOpensSameAirframeWithoutBindingPayload()
    {
        var vm = Create(); await Initialize(vm); var hub = vm.Career;
        var offer = hub.Board.All.First(j => j.Family == CareerJobFamily.Personnel && j.Available);
        hub.Board.Selected = offer; hub.Board.SelectedAircraft = hub.Board.AircraftChoices.Single(a => a.Id == "vision-jet");
        hub.Board.SelectedAircraft=null;Assert.Equal("vision-jet",hub.Board.SelectedAircraft!.Id);
        Assert.True(hub.Board.AcceptCommand.CanExecute(null)); await hub.Board.AcceptAsync();
        Assert.Equal("", hub.Board.Error); Assert.True(hub.Board.Selected!.Accepted, $"Selected {hub.Board.Selected.Key} {hub.Board.Selected.Status}; source {vm.Passengers.SelectedTransfer?.Id} {vm.Passengers.SelectedTransfer?.State}; reserved {string.Join(",", vm.Passengers.Transfers.Where(t=>t.State==PassengerTransferState.Reserved).Select(t=>t.Id))}"); Assert.False(hub.Board.AcceptCommand.CanExecute(null));
        await hub.Board.AcceptAsync(); Assert.Single(vm.Passengers.Transfers.Where(t => t.State == PassengerTransferState.Reserved));
        Assert.True(hub.HasActive); Assert.Contains("not yet physically bound", hub.ActiveAircraft);
        await hub.OpenLoadAsync(hub.Board.Selected);
        Assert.Equal(1, hub.Navigation.Section); Assert.Equal(2, hub.Navigation.AircraftSection); Assert.Equal(1, hub.Navigation.LoadingSection);
        Assert.Equal("vision-jet", vm.Fleet.Selected!.CompanyTypeId); Assert.Equal(vm.Fleet.Selected.Id, hub.Fleet.SelectedId);
        Assert.Equal(offer.SourceId, vm.Passengers.SelectedTransfer!.Id); Assert.Null(vm.Passengers.SelectedTransfer.AirframeId);
        Assert.False(vm.Passengers.BoardCommand.CanExecute(null)); Assert.Contains("physical boarding blocked", vm.Passengers.SupportSummary);
        var unsupported = new PassengerAircraftConfigurations(() => null);
        Assert.Throws<InvalidOperationException>(() => unsupported.Resolve("vision-jet", null!));
        Assert.Same(vm.Fleet, hub.Fleet.Source); Assert.Same(vm.Operations, hub.OwnerJobs.Operations);
        await vm.Fleet.ShutdownAsync();
    }
    [Fact] public async Task BoardCannotAcceptDifferentSourceSelectionOrDisplayUnqualifiedPlatformAsOffer()
    {
        var vm = Create(); await Initialize(vm); var board = vm.Career.Board;
        var offers = board.All.Where(j => j.Family == CareerJobFamily.Personnel && j.Available).Take(2).ToArray(); Assert.Equal(2, offers.Length);
        board.Selected = offers[0]; board.SelectedAircraft = board.AircraftChoices.First();
        vm.Passengers.SelectedTransfer = vm.Passengers.Transfers.Single(t => t.Id == offers[1].SourceId);
        Assert.False(board.AcceptCommand.CanExecute(null)); await board.AcceptAsync();
        Assert.DoesNotContain(vm.Passengers.Transfers, t => t.State == PassengerTransferState.Reserved);
        var platform = Assert.Single(board.All.Where(j => j.Family == CareerJobFamily.Platform)); board.Selected = platform;
        Assert.False(platform.Available); Assert.False(board.AcceptCommand.CanExecute(null)); Assert.False(board.LoadCommand.CanExecute(null));
        Assert.NotEmpty(platform.Requirements); Assert.NotEmpty(platform.Route);
        await vm.Fleet.ShutdownAsync();
    }
    [Fact] public async Task BoardProjectsObservationRouteAreaAndAuthoritativePayWithoutChangingSelectionOnRefresh()
    {
        var vm = Create(); await Initialize(vm); var hub = vm.Career;
        var row = hub.Board.All.First(j => j.Family == CareerJobFamily.Observation); hub.Board.Selected = row;
        Assert.Equal(3, row.Route.Count); Assert.Equal(row.Route[0], row.Route[2]); Assert.Equal(5, row.AreaRadiusNauticalMiles);
        Assert.True(row.DistanceNauticalMiles > 0); Assert.StartsWith("$800", row.Compensation);
        hub.Show(CareerSection.JobBoard); await hub.RefreshAsync();
        Assert.Equal(row.Key, hub.Board.Selected!.Key); Assert.Equal(3, hub.Navigation.Section);
        Assert.Equal(row.SourceId, vm.Offshore.Selected!.Id);
        hub.Board.Selected = null; Assert.Equal(row.Key, hub.Board.Selected!.Key);
        hub.Board.Filter = 3; Assert.Empty(hub.Board.Items); Assert.Null(hub.Board.Selected);
        Assert.False(hub.Board.AcceptCommand.CanExecute(null)); Assert.NotEmpty(hub.Board.EmptyState);
        await vm.Fleet.ShutdownAsync();
    }
    [Fact] public async Task EmployeesCannotPublishNpcCompanyContractsOrEnableHelicopterCapability()
    {
        var vm = Create(); await Initialize(vm); var hub = vm.Career;
        Assert.False(hub.OwnerJobs.HasOwnedOperations); Assert.False(hub.OwnerJobs.PublishCommand.CanExecute(null));
        await hub.OwnerJobs.PublishAsync(); Assert.Contains("No employer contract", hub.OwnerJobs.Error);
        var helicopter = hub.Fleet.Aircraft.Single(a => a.Airframe.CompanyTypeId == "h125");
        Assert.Contains("disabled", helicopter.Restrictions); hub.Fleet.SelectedId = helicopter.Id;
        Assert.False(vm.Offshore.AcceptCommand.CanExecute(null)); Assert.False(vm.Passengers.BoardCommand.CanExecute(null));
        Assert.Equal(CareerCatalogue.Employers.Single(e=>e.Category=="Offshore").Role, new CareerStore(root).Load().Employment!.Role);
        await vm.Fleet.ShutdownAsync();
    }
    [Fact] public async Task CargoDraftPreventsAircraftSwitchThroughBothCareerAndOriginalFleetSelector()
    {
        var vm = Create(); await Initialize(vm);
        var before = vm.Fleet.Selected!.Id; var other = vm.Fleet.Aircraft.First(a => a.Id != before).Id;
        var draft = new CargoConsignmentRow { JobId = "synthetic-draft", Description = "Synthetic cargo", Origin = "Synthetic origin", Destination = "Synthetic destination", Status = "Reserved", AvailableHere = 10, DesiredAboard = 10 };
        vm.CargoJourney.Consignments.Add(draft);
        Assert.False(vm.CargoJourney.CanChooseAircraft);
        Assert.False(vm.Career.SelectAircraft(other)); Assert.Equal(before, vm.Fleet.SelectedAirframeId);
        vm.Fleet.SelectedAirframeId = other; Assert.Equal(before, vm.Fleet.SelectedAirframeId);
        Assert.Contains("No assignment was transferred", vm.Fleet.Status);
        Assert.Contains(draft, vm.CargoJourney.Consignments); Assert.Equal(10, draft.DesiredAboard);
        vm.CargoJourney.Consignments.Clear(); Assert.True(vm.Career.SelectAircraft(other)); Assert.Equal(other, vm.Fleet.SelectedAirframeId);
        await vm.Fleet.ShutdownAsync();
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
