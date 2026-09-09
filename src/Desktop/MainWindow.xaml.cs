using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using FrontierAirportBuilder.Workflow;
namespace FrontierAirportBuilder.Desktop;
public partial class MainWindow : Window
{
    private readonly BuilderViewModel model;
    public MainWindow()
    {
        InitializeComponent();
        bool dryRun = Environment.GetCommandLineArgs().Contains("--dry-run", StringComparer.Ordinal);
        if (dryRun) Title += " · DRY RUN";
        model = new(dryRun ? BuilderServices.CreateDesktopDryRun() : new BuilderServices(), Environment.GetEnvironmentVariable("MSFS2024_SDK", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("MSFS2024_SDK") ?? "");
        DataContext = model;
        model.Tracking.DisplayChanged += TrackingDisplayChanged;
        Closing += OnClosing;
        Loaded += async (_,_)=>await Task.WhenAll(model.RefreshAirportsAsync(),model.Tracking.InitializeAsync(),model.Jobs.InitializeAsync());
    }
    private void MainTabsChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!IsInitialized || !ReferenceEquals(e.Source,MainTabs))return;
        BuilderFooter.Visibility=MainTabs.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;
        if(MainTabs.SelectedIndex==2 && model.Tracking.State is (TrackingState.Disconnected or TrackingState.Error))
            _=model.Tracking.ConnectAsync();
    }
    private void TrackingDisplayChanged(FrontierAirportBuilder.Core.AircraftTelemetry? aircraft,FrontierAirportBuilder.Core.PlayerTelemetry? player,bool isLive) =>
        Dispatcher.InvokeAsync(()=>TrackingMap.Show(aircraft,player,isLive));
    private void CenterAircraft(object sender,RoutedEventArgs e)=>TrackingMap.CenterOnAircraft();
    private void CenterPlayer(object sender,RoutedEventArgs e)=>TrackingMap.CenterOnPlayer();
    private void CopyTrackingLog(object sender,RoutedEventArgs e)
    {
        try { Clipboard.SetText(model.Tracking.DiagnosticLog); }
        catch (Exception error) { MessageBox.Show(this,error.Message,"Could not copy tracking diagnostics",MessageBoxButton.OK,MessageBoxImage.Error); }
    }
    private async void ClearTrackingState(object sender,RoutedEventArgs e)
    {
        if(MessageBox.Show(this,"Clear Frontier Pilot's saved aircraft position history? This does not affect MSFS or any aircraft.","Clear saved aircraft position",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        try { await model.Tracking.ClearSavedStateAsync(); }
        catch(Exception error) { MessageBox.Show(this,error.Message,"Could not clear saved aircraft position",MessageBoxButton.OK,MessageBoxImage.Error); }
    }
    private void OpenMapAttribution(object sender,RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;
    }
    private void CopyLog(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(model.TechnicalLog); }
        catch (Exception error) { model.ReportLogFailure(error); }
    }
    private async void SaveLog(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "Frontier-log-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt", Filter = "Text log|*.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try { await DiagnosticLog.SaveNewAsync(dialog.FileName, model.TechnicalLog); }
        catch (Exception error) { model.ReportLogFailure(error); }
    }
    private void NewTaxiway(object sender,RoutedEventArgs e)=>model.Layout.BeginTaxiway();
    private void FinishTaxiway(object sender,RoutedEventArgs e)=>model.Layout.FinishTaxiway();
    private void CancelTaxiway(object sender,RoutedEventArgs e)=>model.Layout.CancelTaxiway();
    private void ApplyTaxiway(object sender,RoutedEventArgs e)=>model.Layout.ApplyTaxiway();
    private void FitLayout(object sender,RoutedEventArgs e)=>LayoutCanvas.Fit();
    private async void SaveLayout(object sender,RoutedEventArgs e)
    {
        var dialog=new Microsoft.Win32.SaveFileDialog {FileName="airport-layout.json",Filter="Layout JSON|*.json"};
        if(dialog.ShowDialog(this)==true)await model.SaveLayoutAsync(dialog.FileName);
    }
    private async void LoadLayout(object sender,RoutedEventArgs e)
    {
        var dialog=new Microsoft.Win32.OpenFileDialog {Filter="Layout JSON|*.json"};
        if(dialog.ShowDialog(this)==true)await model.LoadLayoutAsync(dialog.FileName);
    }
    private bool MayDiscardEdits()=>!model.HasUnsavedAirport || MessageBox.Show(this,"This airport has unsaved edits. Continue without saving them? Use Save airport to keep them.","Unsaved airport",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
    private void NewAirport(object sender,RoutedEventArgs e){if(MayDiscardEdits())model.NewAirport();}
    private async void OpenAirport(object sender,RoutedEventArgs e)
    {
        if(model.SelectedAirport is not {} airport||!MayDiscardEdits())return;
        await model.LoadAirportAsync(airport.Id);if(model.State==WorkflowState.SurveyCaptured)MainTabs.SelectedIndex=0;
    }
    private async void DeleteAirport(object sender,RoutedEventArgs e)
    {
        if(model.SelectedAirport is not {} airport)return;
        var plan=await model.PrepareDeleteAirportAsync(airport.Id);if(plan==null)return;
        bool confirmed=MessageBox.Show(this,plan.Summary+"\n\nIf this airport is open, its unsaved edits will also be discarded.","Confirm Delete Airport",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
        await model.DeleteAirportAsync(plan,confirmed);
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!model.IsBusy) {e.Cancel=!MayDiscardEdits();if(!e.Cancel){model.Tracking.Dispose();TrackingMap.Dispose();}return;}
        model.Cancel();
        e.Cancel = true;
    }
}
