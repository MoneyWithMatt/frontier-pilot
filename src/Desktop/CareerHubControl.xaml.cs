using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Desktop;

public partial class CareerHubControl : UserControl
{
    private CareerHubViewModel? viewModel;
    private object? observationActions, platformActions;
    public CareerHubControl()
    {
        InitializeComponent();
        JobList.SelectionChanged += (_, _) => { if (JobList.SelectedItem is {} selected) JobList.ScrollIntoView(selected); };
        DataContextChanged += (_, _) => { if (viewModel is not null) viewModel.Board.SelectionChanged -= RefreshMap; viewModel = DataContext as CareerHubViewModel; if (viewModel is not null) viewModel.Board.SelectionChanged += RefreshMap; RefreshMap(); };
        Loaded += (_, _) => { if (viewModel is not null) { viewModel.Board.SelectionChanged -= RefreshMap; viewModel.Board.SelectionChanged += RefreshMap; } RefreshMap(); };
        Unloaded += (_, _) => { if (viewModel is not null) viewModel.Board.SelectionChanged -= RefreshMap; };
    }
    public void AttachLoadingWorkspaces(object cargo, object passengers, object aircraft)
    { CargoWorkspace.Content = cargo; PassengerWorkspace.Content = passengers; AircraftWorkspace.Content = aircraft; }
    public void SetMissionWorkspace(object? content) => MissionWorkspace.Content = content;
    public void AttachMissionWorkspaces(object observation, object platform)
    { observationActions = observation; platformActions = platform; RefreshMap(); }
    private void RefreshMap()
    {
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.BeginInvoke(new Action(RefreshMap)); return; }
        JobMap.Show(viewModel?.Board.Selected);
        MissionWorkspace.Content = viewModel?.Board.Selected?.Family switch
        { CareerJobFamily.Observation => observationActions, CareerJobFamily.Platform => platformActions, _ => null };
    }
    private void FitRoute(object sender, RoutedEventArgs e) => JobMap.Fit();
}
