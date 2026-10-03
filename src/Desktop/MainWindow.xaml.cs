using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using FrontierAirportBuilder.Workflow;
namespace FrontierAirportBuilder.Desktop;
public partial class MainWindow : Window
{
    private readonly BuilderViewModel model;
    private readonly System.Windows.Threading.DispatcherTimer worldTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool refreshingWorld;
    public MainWindow() : this(null) { }
    public MainWindow(BuilderViewModel? suppliedModel)
    {
        InitializeComponent();
        bool dryRun = Environment.GetCommandLineArgs().Contains("--dry-run", StringComparer.Ordinal);
        if (dryRun) Title += " · DRY RUN";
        model = suppliedModel ?? new(dryRun ? BuilderServices.CreateDesktopDryRun() : new BuilderServices(), Environment.GetEnvironmentVariable("MSFS2024_SDK", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("MSFS2024_SDK") ?? "");
        DataContext = model;
        model.Tracking.DisplayChanged += TrackingDisplayChanged;
        model.Operations.MapLocationsChanged += locations => Dispatcher.InvokeAsync(() => OperationsMap.Show(locations));
        OperationsMap.LocationSelected += key => Dispatcher.InvokeAsync(() => model.Operations.SelectMapLocation(key));
        Closing += OnClosing;
        worldTimer.Tick += async (_,_) => await RefreshWorldTimeAsync();
        Closed += (_,_) => worldTimer.Stop();
        Loaded += async (_,_)=>
        {
            await Task.WhenAll(model.RefreshAirportsAsync(),model.Tracking.InitializeAsync());
            await model.Jobs.InitializeAsync();
            await model.AircraftLoading.InitializeAsync();
            await model.Operations.RefreshAsync();
            if (!model.IsDryRun) worldTimer.Start();
            WorldClock.Text = DateTimeOffset.Now.ToString("dd MMM yyyy · HH:mm · 'Local time'");
        };
    }
    private async Task RefreshWorldTimeAsync()
    {
        WorldClock.Text = DateTimeOffset.Now.ToString("dd MMM yyyy · HH:mm · 'Local time'");
        if (refreshingWorld || model.IsBusy || model.Jobs.IsBusy || model.Operations.IsBusy) return;
        refreshingWorld = true;
        try { await model.Jobs.RefreshLiveAsync(); await model.Operations.RefreshAsync(); }
        finally { refreshingWorld = false; }
    }
    private void MainTabsChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!IsInitialized || !ReferenceEquals(e.Source,MainTabs))return;
        BuilderFooter.Visibility=ReferenceEquals(MainTabs.SelectedItem,BuilderTab)?Visibility.Visible:Visibility.Collapsed;
        if(!model.IsDryRun && MainTabs.SelectedIndex is 1 or 2 or 3 or 4 && model.Tracking.State is (TrackingState.Disconnected or TrackingState.Error))
            _=model.Tracking.ConnectAsync();
    }
    private void OpenAircraftLoading(object sender,RoutedEventArgs e)=>MainTabs.SelectedIndex=2;
    private async void PrepareReservedJobs(object sender,RoutedEventArgs e)
    {
        var selected=ReservedJobsList.SelectedItems.Cast<CargoJobDisplay>().ToArray();
        await model.Jobs.PrepareReservedJobsAsync(selected);
        if(string.IsNullOrEmpty(model.Jobs.Error))MainTabs.SelectedIndex=2;
    }
    private void SelectAllNumericText(object sender,KeyboardFocusChangedEventArgs e)
    { if(sender is TextBox textBox)textBox.SelectAll(); }
    private void FocusNumericText(object sender,MouseButtonEventArgs e)
    { if(sender is TextBox textBox&&!textBox.IsKeyboardFocusWithin){e.Handled=true;textBox.Focus();} }
    private async void ConfirmSimulatedUnload(object sender,RoutedEventArgs e)
    {
        const string warning="Use simulated compatibility unload only when verified MSFS payload removal is unsupported or has failed. This leaves MSFS payload unchanged and commits the existing verified economic delivery. Continue?";
        if(MessageBox.Show(this,warning,"Confirm simulated compatibility unload",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes)
            await model.AircraftLoading.CompleteSimulatedUnloadAsync();
    }
    private async void SkipPrototypeMission(object sender,RoutedEventArgs e)
    {
        const string warning="Unload and decline this incomplete job? While grounded and stopped, Frontier Pilot will remove and verify its manifest cargo (or verify it is already absent), return the reserved goods to the origin through an inventory audit entry, archive the job, and create a fresh replacement offer. No delivery or payment will be created. Completed jobs and ledger transactions cannot be changed.";
        if(MessageBox.Show(this,warning,"Unload and decline job",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        await model.AircraftLoading.AbandonPrototypeMissionAsync();
        await model.Jobs.InitializeAsync();
    }
    private async void ApplyPrimaryAircraftLoad(object sender,RoutedEventArgs e)
    {
        try { await model.AircraftLoading.ApplyPrimaryCargoAsync(false); await model.Jobs.InitializeAsync(); }
        catch (CargoForfeitConfirmationRequiredException error)
        {
            if (MessageBox.Show(this,error.Message+"\n\nContinue and forfeit this job?","Confirm cargo unload away from destination",
                MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes)
            { await model.AircraftLoading.ApplyPrimaryCargoAsync(true); await model.Jobs.InitializeAsync(); }
        }
    }
    private void TrackingDisplayChanged(FrontierAirportBuilder.Core.AircraftTelemetry? aircraft,FrontierAirportBuilder.Core.PlayerTelemetry? player,bool isLive) =>
        Dispatcher.InvokeAsync(()=>TrackingMap.Show(aircraft,player,isLive));
    private void CenterAircraft(object sender,RoutedEventArgs e)=>TrackingMap.CenterOnAircraft();
    private void CenterPlayer(object sender,RoutedEventArgs e)=>TrackingMap.CenterOnPlayer();
    private void OpenSelectedOperation(object sender,RoutedEventArgs e)
    { if(model.Operations.SelectOperationFromMap())OperationsTabs.SelectedIndex=1; }
    private void OpenSelectedOperationFromList(object sender,RoutedEventArgs e)
    { if(model.Operations.SelectedOperation is not null)OperationsTabs.SelectedIndex=1; }
    private void BackToOperationsMap(object sender,RoutedEventArgs e)
    { OperationsTabs.SelectedIndex=0; OperationsMap.CenterOn(model.Operations.SelectedMapLocation); }
    private void OpenCreateLogisticsJob(object sender,RoutedEventArgs e)
    { if(model.Operations.SelectedOperation is not null)OperationsTabs.SelectedIndex=2; }
    private async void PublishOperationLogistics(object sender,RoutedEventArgs e)
    { await model.Operations.PublishLogisticsAsync(); }
    private void BackToOperationDetail(object sender,RoutedEventArgs e)
    { OperationsTabs.SelectedIndex=1; }
    private async void TransferPrototypeRations(object sender,RoutedEventArgs e)
    {
        const string message="Developer test: move 1 kg of real persistent rations from Eros to this operation? The transfer is atomic, audited, and persists; it does not create a job, flight, or payment.";
        if(MessageBox.Show(this,message,"Test persistent inventory transfer",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes)
            await model.Operations.TransferPrototypeRationsAsync();
    }
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
    private async void ExportFlightDiagnostic(object sender,RoutedEventArgs e)
    {
        var dialog=new Microsoft.Win32.SaveFileDialog {FileName="Frontier-flight-diagnostic-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json",Filter="JSON|*.json"};
        if(dialog.ShowDialog(this)!=true)return;
        try { await DiagnosticLog.SaveNewAsync(dialog.FileName,await model.Jobs.ExportFlightDiagnosticAsync()); }
        catch(Exception error) { MessageBox.Show(this,error.Message,"Could not export flight diagnostic",MessageBoxButton.OK,MessageBoxImage.Error); }
    }
    private async void CopyFlightDiagnosticSummary(object sender,RoutedEventArgs e)
    {
        try { Clipboard.SetText(await model.Jobs.CopyFlightDiagnosticSummaryAsync()); }
        catch(Exception error) { MessageBox.Show(this,error.Message,"Could not copy flight diagnostic",MessageBoxButton.OK,MessageBoxImage.Error); }
    }
    private async void ResetIncompleteFlight(object sender,RoutedEventArgs e)
    {
        const string warning="Reset only the persisted airborne, movement and arrival observations for this incomplete prototype flight? Cargo will remain loaded and InTransit. Completed deliveries, inventory, ledger payments and balance cannot be changed.";
        if(MessageBox.Show(this,warning,"Reset incomplete prototype observations",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        await model.Jobs.ResetIncompleteFlightAsync();
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
    private void PlaceRunway(object sender,RoutedEventArgs e)=>model.BeginRunwayPlacement();
    private void RemoveRunway(object sender,RoutedEventArgs e)
    {
        model.CancelRunwayPlacement();model.HasRunway=false;
    }
    private void CancelRunway(object sender,RoutedEventArgs e)=>model.CancelRunwayPlacement();
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
    private bool MayDiscardEdits()=>!model.HasUnsavedAirport || MessageBox.Show(this,"This site has unsaved edits. Continue without saving them? Use Save site to keep them.","Unsaved site",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
    private void NewAirport(object sender,RoutedEventArgs e){if(MayDiscardEdits())model.NewAirport();}
    private void CreateSiteFromLibrary(object sender,RoutedEventArgs e)
    { if(MayDiscardEdits()){model.NewAirport();MainTabs.SelectedItem=BuilderTab;} }
    private void BackToMySites(object sender,RoutedEventArgs e)=>MainTabs.SelectedIndex=5;
    private async void OpenAirport(object sender,RoutedEventArgs e)
    {
        if(model.SelectedAirport is not {} airport||!MayDiscardEdits())return;
        await model.LoadAirportAsync(airport.Id);
        if(model.State==WorkflowState.SurveyCaptured)
        {
            MainTabs.SelectedItem=BuilderTab;
            await Dispatcher.InvokeAsync(()=>{BuilderScroll.ScrollToTop();LayoutCanvas.Fit();},System.Windows.Threading.DispatcherPriority.Loaded);
        }
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
