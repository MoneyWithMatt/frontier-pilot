using System.ComponentModel;
using System.IO;
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
    private bool careerReady,resettingCareer;
    private CareerStore? careerStore;
    private EmployerDestinationCatalogue? employerCatalogue;
    private bool airportLookupAttempted;
    private string employmentDescription="";
    public MainWindow() : this(null) { }
    public MainWindow(BuilderViewModel? suppliedModel,CareerStore? suppliedCareerStore=null)
    {
        InitializeComponent();
        bool dryRun = Environment.GetCommandLineArgs().Contains("--dry-run", StringComparer.Ordinal);
        if (dryRun) Title += " · DRY RUN";
        CareerViewModel? career=null;
        string? careerError=null;
        // Supplied-model and dry-run UI tests remain isolated from Matt's live career.
        careerReady=(suppliedModel is not null||dryRun)&&suppliedCareerStore is null;
        if(!careerReady)
        {
            careerStore=suppliedCareerStore??new(CareerStore.DefaultRoot);
            try {career=new(careerStore);careerReady=career.IsActive;}
            catch(Exception ex){careerError="Career state could not be read. Existing files were preserved. "+ex.Message;}
        }
        EmployerDestinationCatalogue? destinations=null;
        if(career?.Profile.Employment is not null)
        {
            try{destinations=new DestinationReviewStore(careerStore!.StorageRoot).Load();}
            catch(Exception ex){careerReady=false;careerError="Destination reviews could not be loaded; files preserved. "+ex.Message;career=null;}
        }
        model = suppliedModel ?? new(dryRun ? BuilderServices.CreateDesktopDryRun() : new BuilderServices(), Environment.GetEnvironmentVariable("MSFS2024_SDK", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("MSFS2024_SDK") ?? "",careerStore:careerReady?careerStore:null,employerDestinations:destinations);
        employerCatalogue=destinations;
        DataContext = model;
        model.Jobs.PropertyChanged+=(_,args)=>
        {
            if(args.PropertyName is nameof(EmployedCargoJobsViewModel.ShowOfferCompensation) or nameof(EmployedCargoJobsViewModel.ShowReservedCompensation))
                Dispatcher.InvokeAsync(()=>
                {
                    OfferCompensationColumn.Visibility=model.Jobs.ShowOfferCompensation?Visibility.Visible:Visibility.Collapsed;
                    ReservedCompensationColumn.Visibility=model.Jobs.ShowReservedCompensation?Visibility.Visible:Visibility.Collapsed;
                });
        };
        CareerPilotLabel.Text=career?.PilotName??"Prototype pilot";
        var employer=CareerCatalogue.Employers.SingleOrDefault(x=>x.Id==career?.Profile.Employment?.EmployerId);
        EmploymentLabel.Text=employer is null?"Legacy prototype career: existing gameplay remains available.":$"{employer.Name} · {employer.Role}\n{employer.WageDisplay}\nBase: {CareerCatalogue.Bases.Single(x=>x.Id==employer.BaseId).Name}\nCompany-owned fleet assigned to you: {employer.Aircraft}";
        employmentDescription=EmploymentLabel.Text;
        if(employerCatalogue is not null)ShowAirportSummary();
        CompanyType.ItemsSource=CompanyAircraft.Types.Where(x=>x.Enabled&&employer?.Fleet.Contains(x.Id)==true).ToArray();
        if(!careerReady)
        {
            GameplayShell.Visibility=Visibility.Collapsed;Onboarding.Visibility=Visibility.Visible;
            if(career is not null)Onboarding.Show(career);else Onboarding.ShowError(careerError!);
        }
        Onboarding.DeleteRequested+=()=>DeleteCareer(this,new RoutedEventArgs());
        Onboarding.DestinationsRequested+=()=>OpenDestinations(this,new RoutedEventArgs());
        Onboarding.EmploymentAccepted+=()=>
        {
            // Reconstruct services from the durable employment assignment before any economy initialization.
            var isolated=suppliedModel is null?null:new BuilderViewModel(BuilderServices.CreateDryRun(),"",careerStore:careerStore,employerDestinations:destinations??new());
            var next=new MainWindow(isolated,careerStore);Application.Current.MainWindow=next;next.Show();Close();
        };
        model.Tracking.DisplayChanged += TrackingDisplayChanged;
        model.Operations.MapLocationsChanged += locations => Dispatcher.InvokeAsync(() => OperationsMap.Show(locations));
        OperationsMap.LocationSelected += key => Dispatcher.InvokeAsync(() => model.Operations.SelectMapLocation(key));
        Closing += OnClosing;
        worldTimer.Tick += async (_,_) => await RefreshWorldTimeAsync();
        Closed += (_,_) => worldTimer.Stop();
        Loaded += async (_,_)=>
        {
            if(!careerReady)return;
            await Task.WhenAll(model.RefreshAirportsAsync(),model.Tracking.InitializeAsync());
            await model.Jobs.InitializeAsync();
            await model.Passengers.RefreshAsync();
            await model.AircraftLoading.InitializeAsync();
            await model.Operations.RefreshAsync();
            if (!model.IsDryRun) worldTimer.Start();
            WorldClock.Text = DateTimeOffset.Now.ToString("dd MMM yyyy · HH:mm · 'Local time'");
        };
    }
    private async Task RefreshWorldTimeAsync()
    {
        WorldClock.Text = DateTimeOffset.Now.ToString("dd MMM yyyy · HH:mm · 'Local time'");
        if (refreshingWorld || model.IsBusy || model.Jobs.IsBusy || model.Passengers.IsBusy || model.Operations.IsBusy) return;
        refreshingWorld = true;
        try { await model.Jobs.RefreshLiveAsync(); await model.Passengers.RefreshAsync(); await model.Operations.RefreshAsync(); await model.Fleet.AdvanceMaintenanceAsync(DateTimeOffset.UtcNow); }
        finally { refreshingWorld = false; }
    }
    private void OpenDependencySetup(object sender,RoutedEventArgs e)
    {
        if(model.IsBusy||model.Tracking.State is TrackingState.Connecting or TrackingState.Connected||model.AircraftLoading.IsBusy||model.Jobs.IsBusy||model.Passengers.IsBusy||model.Operations.IsBusy||refreshingWorld)
        {MessageBox.Show(this,"Disconnect from MSFS and wait for current operations to finish before changing dependency setup.");return;}
        new DependencySetupWindow(model.SdkRoot,root=>
        {
            if(root!=model.SdkRoot&&!MayDiscardEdits())return false;
            model.SdkRoot=root;return true;
        }){Owner=this}.ShowDialog();
    }
    private void OpenDestinations(object sender,RoutedEventArgs e)=>new DestinationDiagnosticsWindow(careerStore?.StorageRoot??CareerStore.DefaultRoot,model.SdkRoot,cache=>
    {
        employerCatalogue?.ApplySimulatorCache(cache);ShowAirportSummary();_=model.Jobs.RefreshLiveAsync();
    },allowLive:!model.IsDryRun){Owner=this}.ShowDialog();
    private void ShowAirportSummary()
    {
        if(employerCatalogue is null)return;
        var cache=employerCatalogue.SimulatorCache;
        var problems=employerCatalogue.Diagnose().Where(x=>x.Resolved is null||cache?.Problem(x.Research.Identifier) is not null).Select(x=>x.Research.Key).Distinct().Count();
        EmploymentLabel.Text=employmentDescription+"\n"+(cache is null?"Airports: connect to MSFS for automatic lookup; no airport visits required.":$"Airport check {cache.Scan.ObservedUtc:u}: {problems} destinations need attention. Open Curated destinations for details. Runway suitability unassessed.");
    }
    private async Task CheckEmployerAirportsAsync()
    {
        if(airportLookupAttempted||model.IsDryRun||employerCatalogue is null||careerStore is null)return;
        airportLookupAttempted=true;
        EmploymentLabel.Text=employmentDescription+"\nChecking researched airports against MSFS…";
        try
        {
            var scan=await FrontierAirportBuilder.MsfsSdk.LiveAirportCatalogue.ReadAsync(model.SdkRoot);
            var cache=new SimulatorAirportStore(careerStore.StorageRoot).Save(scan);
            employerCatalogue.ApplySimulatorCache(cache);ShowAirportSummary();await model.Jobs.RefreshLiveAsync();
        }
        catch(Exception ex){EmploymentLabel.Text=employmentDescription+"\nAirport lookup incomplete: "+ex.Message+" Retry in Curated destinations.";}
    }
    private void BindCompanyAircraft(object sender,RoutedEventArgs e)
    {
        try
        {
            if(careerStore is null||model.Tracking.World?.Aircraft is not {} live||CompanyType.SelectedItem is not CompanyAircraftType type)
                throw new IOException("Connect to live MSFS tracking and choose a company aircraft type.");
            var identity=FrontierAirportBuilder.Core.FlightAircraftIdentity.From(live);
            if(MessageBox.Show(this,$"Confirm this is {type.Name}:\nTITLE: {identity.Title}\nATC MODEL: {identity.Type}\n\nThis maps this exact simulator implementation, not an owned aircraft.","Confirm aircraft compatibility",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
            careerStore.BindAircraft(identity,type.Id);MessageBox.Show(this,"Aircraft type mapping saved. Existing flight identity checks remain in force.");
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message);}
    }
    private void MainTabsChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!IsInitialized || !ReferenceEquals(e.Source,MainTabs)||!careerReady)return;
        BuilderFooter.Visibility=ReferenceEquals(MainTabs.SelectedItem,BuilderTab)?Visibility.Visible:Visibility.Collapsed;
        if(!model.IsDryRun && (MainTabs.SelectedIndex is 1 or 2 or 3 or 4 || ReferenceEquals(MainTabs.SelectedItem,PassengerTab) || ReferenceEquals(MainTabs.SelectedItem,CargoTab)) && model.Tracking.State is (TrackingState.Disconnected or TrackingState.Error))
            _=model.Tracking.ConnectAsync();
    }
    private void OpenAircraftLoading(object sender,RoutedEventArgs e)=>MainTabs.SelectedIndex=2;
    private void OpenCargoDispatch(object sender,RoutedEventArgs e)=>MainTabs.SelectedItem=CargoTab;
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
        Dispatcher.InvokeAsync(()=>{TrackingMap.Show(aircraft,player,isLive);if(isLive)_=CheckEmployerAirportsAsync();});
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
    private bool closingRecording, closeReady;
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if(closeReady)return;
        e.Cancel=true;
        if(closingRecording)return;
        if(!resettingCareer && model.IsBusy){model.Cancel();return;}
        if(!resettingCareer && !MayDiscardEdits())return;
        closingRecording=true;
        await model.ConditionRecording.ShutdownAsync();
            await model.Fleet.ShutdownAsync();
        model.Tracking.Dispose();TrackingMap.Dispose();
        closeReady=true;
        // Defer even when shutdown completed synchronously: Close cannot re-enter Closing.
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
    private async void DeleteCareer(object sender,RoutedEventArgs e)
    {
        if(careerStore is null){MessageBox.Show(this,"Career deletion is unavailable in isolated dry-run mode.");return;}
        if(model.IsBusy||model.Jobs.IsBusy||model.Passengers.IsBusy||model.Operations.IsBusy||model.AircraftLoading.IsBusy||refreshingWorld)
        {MessageBox.Show(this,"Wait for the current operation to finish before deleting the career.");return;}
        const string warning="Delete this career? This resets pilot/onboarding data, money and ledger, jobs and reservations, operations and inventories, flight progress, cargo manifests, saved fuel and last-known aircraft state.\n\nSDK settings, saved site designs, installed scenery and ownership records are retained. Old career files are archived locally for recovery and diagnostics. No MSFS payload or scenery is changed.\n\nFrontier Pilot will restart at New Career. This cannot be undone from the app. Unsaved site edits will be discarded.";
        if(MessageBox.Show(this,warning,"Delete Career — destructive reset",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        if(model.IsBusy||model.Jobs.IsBusy||model.Passengers.IsBusy||model.Operations.IsBusy||model.AircraftLoading.IsBusy||refreshingWorld)
        {MessageBox.Show(this,"An operation started while confirmation was open. Let it finish and try again.");return;}
        var resetRequested=false;
        try
        {
            var executable=Environment.ProcessPath??throw new IOException("Application executable not found.");
            if(Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Any(p=>p.Id!=Environment.ProcessId))
                throw new IOException("Close other Frontier Pilot instances before deleting a career.");
            await model.ConditionRecording.ShutdownAsync();
            await model.Fleet.ShutdownAsync();
            careerStore.RequestReset(true);resetRequested=true;worldTimer.Stop();resettingCareer=true;
            Process.Start(new ProcessStartInfo(executable,$"--career-reset-wait {Environment.ProcessId}"){UseShellExecute=false});
            Application.Current.Shutdown();
        }
        catch(Exception ex)
        {
            MessageBox.Show(this,"Career reset could not restart: "+ex.Message+
                (resetRequested?"\nThe app will close. Open Frontier Pilot again to finish the requested reset.":"\nNo reset was requested."));
            if(resetRequested)Application.Current.Shutdown();
            else resettingCareer=false;
        }
    }
}
