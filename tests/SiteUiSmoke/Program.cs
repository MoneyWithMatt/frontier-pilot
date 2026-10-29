using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.Desktop;

internal static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var theme=System.Xml.Linq.XDocument.Load("src/Desktop/App.xaml");
        var resources=theme.Root!.Elements().Single();resources.Name=System.Xml.Linq.XName.Get("ResourceDictionary","http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        resources.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml");
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        int result=1;
        app.Startup+=async (_,_)=>
        {
            try
            {
                var services=BuilderServices.CreateDryRun();var vm=new BuilderViewModel(services,"");
                await vm.CheckAsync();await vm.CaptureAsync();vm.AirportName="Bush lodge";vm.Identifier="UITST";
                vm.Layout.AddObject(new(30,30),0,SceneryAssets.PrototypeBaseLodgeId);vm.AddParkingAt(new(0,0));
                await vm.SaveAirportAsync();Check(vm.Error=="","Saved isolated test site: "+vm.Error);
                var edit=new BuilderViewModel(services.ReopenDryRun(),"");
                var window=new MainWindow(edit){ShowInTaskbar=false};window.Show();
                await Task.Delay(400);await edit.RefreshAirportsAsync();edit.SelectedAirport=edit.Airports.Single();
                var button=Descendants<Button>(window).Single(b=>b.Content?.ToString()=="Edit selected site…");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(500);Pump();
                Check(edit.IsEditingSavedSite&&edit.Layout.Objects.Count==1&&edit.Layout.Stands.Count==1,"Edit restored saved lodge and parking");
                var tabs=(TabControl)window.FindName("MainTabs");Check(tabs.SelectedIndex==0,"Edit navigated to builder");
                Check(!Descendants<CheckBox>(window).Any(c=>c.Content?.ToString()=="Build runway now"),"No runway checkbox");
                Check(!Descendants<Button>(window).Any(b=>b.IsVisible&&b.Content?.ToString()=="Capture survey"),"Saved site hides recapture controls");
                var map=Descendants<AirportLayoutCanvas>(window).Single();
                Check(map.ActualWidth>200&&map.ViewState.PixelsPerMeter>.1,"Map fitted after measurement");
                var folder=args.FirstOrDefault()??Path.Combine(Path.GetTempPath(),"FrontierPilot-UiSmoke");Directory.CreateDirectory(folder);
                Save(window,Path.Combine(folder,"site-editor.png"));
                var draw=Descendants<Button>(window).Single(b=>b.IsVisible&&b.Content?.ToString()=="Draw runway");
                draw.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                edit.PlaceRunwayEnd(new(120,100));Check(edit.IsPlacingRunway,"First endpoint waits for opposite end");
                edit.PlaceRunwayEnd(new(120,400));Pump();map.Fit();Pump();
                Check(edit.HasRunway&&edit.RunwayEast==120&&edit.RunwayNorth==250,"Two endpoint placement moved runway independently of anchor");
                var heading=Descendants<TextBox>(window).Single(b=>b.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path=="RunwayHeading");
                heading.Text="90";heading.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();Pump();
                Check(edit.RunwayHeading==90,"Actual heading textbox updates runway");
                Save(window,Path.Combine(folder,"runway-editor.png"));
                foreach(var index in new[]{5,1,2,3,4}){tabs.SelectedIndex=index;Pump();Save(window,Path.Combine(folder,$"page-{index}.png"));}
                var fleetStore=new AircraftFleetStore(Path.Combine(folder,"synthetic-fleet-"+Guid.NewGuid(),"aircraft-fleet.json"));
                var aircraft=FleetOperations.DiscoverComponents(FleetOperations.Enrol("SYNTHETIC-1","test",new(DateTimeOffset.UtcNow,"Synthetic aircraft","Test model",-22,15,5000,90,0,true),"synthetic-package",null,DateTimeOffset.UtcNow),1);
                fleetStore.Commit(new(1,1,aircraft.Id,[aircraft]),0);
                var fleetModel=new AircraftFleetViewModel(fleetStore,()=>throw new Exception("UI smoke must not connect"));
                var fleetPanel=new AircraftFleetPanel{DataContext=fleetModel};var fleetWindow=new Window{Content=fleetPanel,Width=1200,Height=850,ShowInTaskbar=false};fleetWindow.Show();Pump();
                Check(Descendants<TextBlock>(fleetPanel).Any(t=>t.Text.Contains("SYNTHETIC-1")),"Physical fleet registration rendered");
                Check(Descendants<TextBlock>(fleetPanel).Any(t=>t.Text.Contains("Unknown location")),"Unknown location is explicit; no illustrative position");
                fleetModel.EstablishBaseline();Pump();
                Check(fleetModel.SelectedAirframeId==aircraft.Id,"Fleet selection survives immutable snapshot refresh");
                Save(fleetWindow,Path.Combine(folder,"physical-fleet.png"));
                var fleetTabs=Descendants<TabControl>(fleetPanel).Single();fleetTabs.SelectedIndex=2;Pump();Save(fleetWindow,Path.Combine(folder,"aircraft-maintenance.png"));
                await fleetModel.ShutdownAsync();fleetWindow.Close();
                edit.NewAirport();window.Close();
                var careerRoot=Path.Combine(Path.GetTempPath(),"FrontierCareerUi",Guid.NewGuid().ToString("N"));
                var careerStore=new CareerStore(careerRoot);
                var newPlayerModel=new BuilderViewModel(BuilderServices.CreateDryRun(),"");
                var careerWindow=new MainWindow(newPlayerModel,careerStore){ShowInTaskbar=false};careerWindow.Show();await Task.Delay(300);Pump();
                Check(((FrameworkElement)careerWindow.FindName("GameplayShell")).Visibility==Visibility.Collapsed,"No career hides normal gameplay");
                Check(!Directory.Exists(Path.Combine(careerRoot,"economy")),"Onboarding never initializes gameplay economy");
                Check(Descendants<Button>(careerWindow).Any(b=>b.IsVisible&&b.Content?.ToString()=="Dependency setup…"),"Dependency setup is available before career activation");
                var setupWindow=new DependencySetupWindow("",_=>throw new Exception("Smoke test must not change SDK configuration"),selected=>new[]{selected}){ShowInTaskbar=false};
                setupWindow.Show();await Task.Delay(100);Pump();
                Check(((TextBlock)setupWindow.FindName("SdkStatus")).Text.Length>0,"Dependency detector displays SDK status");
                var feedback=(TextBlock)setupWindow.FindName("DetectionFeedback");feedback.Text="stale feedback";
                ((Button)setupWindow.FindName("RecheckButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                Check(feedback.Text.StartsWith("Checked at "),"Actual recheck button displays completion feedback");
                var partialSdk=Path.Combine(careerRoot,"core-only-sdk");Directory.CreateDirectory(Path.Combine(partialSdk,"Tools","bin"));
                File.WriteAllText(Path.Combine(partialSdk,"version.txt"),"1.2.4");File.WriteAllText(Path.Combine(partialSdk,"Tools","bin","fspackagetool.exe"),"fixture only");
                var sdkBox=(TextBox)setupWindow.FindName("SdkFolder");sdkBox.Text=partialSdk;
                ((Button)setupWindow.FindName("RecheckButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                Check(sdkBox.Text==partialSdk&&((TextBlock)setupWindow.FindName("SdkStatus")).Text.Contains("Samples is missing"),"Recheck retains Core-only folder and identifies missing Samples");
                Check(!((Button)setupWindow.FindName("UseButton")).IsEnabled,"Incomplete SDK cannot be applied");
                Check(((Button)setupWindow.FindName("CoreButton")).IsEnabled||((Button)setupWindow.FindName("CoreInstallButton")).IsEnabled,"Core download or cached install action is available");
                Check(!((Button)setupWindow.FindName("SamplesButton")).IsEnabled&&!((Button)setupWindow.FindName("SamplesInstallButton")).IsEnabled,"Samples waits for verified Core");
                Check(((TextBlock)setupWindow.FindName("NextStep")).Text.Contains("Core"),"Next instruction directs the user to Core first");
                Check(((Button)setupWindow.FindName("CoreInstallButton")).Content.ToString()=="Install Core…"&&((Button)setupWindow.FindName("SamplesInstallButton")).Content.ToString()=="Install Samples…","Each component has its own named install action");
                Save(setupWindow,Path.Combine(folder,"dependency-setup.png"));setupWindow.Close();
                Save(careerWindow,Path.Combine(folder,"career-welcome.png"));
                var screen=(CareerScreen)careerWindow.FindName("Onboarding");
                Button CareerButton(string content)=>Descendants<Button>(screen).Single(b=>b.IsVisible&&b.Content?.ToString()==content);
                CareerButton("New Career").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                var pilot=(TextBox)screen.FindName("PilotName");pilot.Text="Matt";pilot.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                CareerButton("Continue to region").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                await Task.Delay(1200);Pump();Save(careerWindow,Path.Combine(folder,"career-region.png"));
                ((ListBox)screen.FindName("RegionsList")).SelectedIndex=0;Pump();
                CareerButton("Explore employment opportunities").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(400);Pump();
                Check(careerStore.Load().Stage==CareerStage.Opportunities&&!careerStore.Load().IsActive,"UI persists draft without activating employer");
                Check(((ListBox)screen.FindName("EmployersList")).Items.Count==4,"All four employers are visible");
                Save(careerWindow,Path.Combine(folder,"career-employers.png"));
                ((ListBox)screen.FindName("EmployersList")).SelectedIndex=2;Pump();Save(careerWindow,Path.Combine(folder,"career-offshore.png"));
                CareerButton("Back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(careerStore.Load().Stage==CareerStage.Region,"Back navigation preserves draft");
                var diagnostic=new DestinationDiagnosticsWindow(careerRoot){ShowInTaskbar=false};diagnostic.Show();Pump();
                Check(Descendants<DataGrid>(diagnostic).Single().Items.Count==53,"Diagnostics retains all 53 memberships");
                Save(diagnostic,Path.Combine(folder,"employer-destinations.png"));diagnostic.Close();
                foreach(var employer in CareerCatalogue.Employers)
                {
                    var isolated=new CareerStore(Path.Combine(careerRoot,employer.Id));isolated.SaveDraft(CareerStage.Opportunities,"Test Pilot","namibia");
                    var careerModel=new CareerViewModel(isolated){SelectedEmployer=employer};var control=new CareerScreen();control.Show(careerModel);
                    var host=new Window{Content=control,Width=1200,Height=850,ShowInTaskbar=false};host.Show();Pump();
                    var accepted=false;control.EmploymentAccepted+=()=>accepted=true;
                    Descendants<Button>(control).Single(b=>b.Content?.ToString()=="Accept employment").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                    Check(accepted&&isolated.Load().Employment?.EmployerId==employer.Id,"Actual acceptance button persists "+employer.Name);host.Close();
                }
                CareerButton("Explore employment opportunities").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                ((ListBox)screen.FindName("EmployersList")).SelectedIndex=1;
                CareerButton("Accept employment").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(500);Pump();
                var employedWindow=(MainWindow)Application.Current.MainWindow;
                var employedModel=(BuilderViewModel)employedWindow.DataContext;
                Check(employedModel.Jobs.Offers.Count>0&&employedModel.Jobs.Offers.All(x=>x.Employer=="Fly Eros"),"MainWindow acceptance enters isolated employer gameplay and initializes its board");
                ((TabControl)employedWindow.FindName("MainTabs")).SelectedItem=((TabControl)employedWindow.FindName("MainTabs")).Items.Cast<TabItem>().Single(x=>x.Header?.ToString()=="Jobs");Pump();
                var offerGrid=Descendants<DataGrid>(employedWindow).Single(x=>x.GetBindingExpression(ItemsControl.ItemsSourceProperty)?.ParentBinding.Path.Path=="Offers");
                offerGrid.SelectedItem=employedModel.Jobs.Offers.First(x=>x.CargoMassKilograms==160);Pump();
                var dispatch=Descendants<ComboBox>(employedWindow).Single(x=>x.GetBindingExpression(ItemsControl.ItemsSourceProperty)?.ParentBinding.Path.Path=="CompanyAircraftChoices");
                Check(dispatch.Items.Count==1,"160 kg industrial fixture permits Caravan only");dispatch.SelectedIndex=0;Pump();
                Save(employedWindow,Path.Combine(folder,"company-jobs.png"));
                var reserve=Descendants<Button>(employedWindow).Single(x=>x.Content?.ToString()=="Accept and reserve job");reserve.Command.Execute(reserve.CommandParameter);await Task.Delay(250);Pump();
                Check(employedModel.Jobs.ReservedJobs.Count==1&&employedModel.Jobs.ReservedJobs[0].CompanyAircraftTypeId=="c208","Aircraft selector and bound accept command reserve company dispatch: "+employedModel.Jobs.Error);
                employedWindow.Close();
                await CheckPassengerTransfers(folder);
                result=0;
            }
            catch(Exception e){Console.Error.WriteLine(e);}
            finally{app.Shutdown(result);}
        };
        app.Run();return result;
    }
    private static async Task CheckPassengerTransfers(string folder)
    {
        var now = DateTimeOffset.Parse("2026-10-29T12:00:00Z"); var clock = now;
        var employer = CareerCatalogue.Employers.Single(x => x.Category == "Offshore");
        var destinations = new EmployerDestinationCatalogue([new("FYWB", "Approved", "FYWB", -23, 15, "Synthetic WPF passenger fixture", now, [])]);
        var service = new PassengerTransfersService(new MemoryEconomyStateStore(), employer, destinations,
            () => [new("Synthetic Vision Jet", "SF50", "vision-jet")], () => clock);
        var vm = new PassengerTransfersViewModel(service); var control = new PassengerTransfersControl { DataContext = vm };
        var host = new Window { Content = control, Width = 1250, Height = 850, ShowInTaskbar = false }; host.Show();
        await vm.RefreshAsync(); Pump();
        var grid = (DataGrid)control.FindName("TransfersGrid");
        grid.SelectedItem = vm.Transfers.First(x => x.Destination.Name.Contains("FYAR")); Pump();
        var assigned = vm.SelectedTransfer!;
        var aircraft = (ComboBox)control.FindName("PassengerAircraft"); aircraft.SelectedIndex = 0; Pump();
        var reserve = (Button)control.FindName("AcceptTransfer");
        Check(reserve.Command.CanExecute(null), "Passenger reserve command is bound to eligible aircraft"); reserve.Command.Execute(null);
        while (vm.IsBusy) await Task.Delay(10); Pump();
        Check(vm.SelectedTransfer?.State == PassengerTransferState.Reserved, "Passenger reservation uses the actual bound control: " + vm.Error);
        async Task Sample(int second, bool destination = false, bool ground = true)
        {
            clock = now.AddSeconds(second);
            await vm.ObserveAsync(new(clock, "Synthetic Vision Jet", "SF50", destination ? assigned.Destination.Latitude : assigned.Origin.Latitude,
                destination ? assigned.Destination.Longitude : assigned.Origin.Longitude, ground ? 500 : 2000, 0, ground ? 0 : 100, ground)); Pump();
        }
        for (var i = 0; i <= 10; i++) await Sample(i);
        Check(vm.SelectedTransfer?.Id == assigned.Id && (grid.SelectedItem as PassengerTransfer)?.Id == assigned.Id,
            "Actual passenger row selection survives telemetry snapshot refresh");
        Check((aircraft.SelectedItem as CompanyAircraftType)?.Id == "vision-jet", "Passenger aircraft selection survives telemetry refresh");
        var board = (Button)control.FindName("BoardPassengers"); Check(board.Command.CanExecute(null), "Passenger boarding control qualifies at origin"); board.Command.Execute(null);
        while (vm.IsBusy) await Task.Delay(10); Pump();
        Check(vm.SelectedTransfer?.State == PassengerTransferState.Boarded, "Passenger boarding persists via production command: " + vm.Error);
        for (var i = 11; i <= 71; i++) await Sample(i, i > 11, false);
        for (var i = 72; i <= 82; i++) await Sample(i, true);
        var disembark = (Button)control.FindName("DisembarkPassengers"); Check(disembark.Command.CanExecute(null), "Passenger disembark control requires qualified arrival");
        disembark.Command.Execute(null); while (vm.IsBusy) await Task.Delay(10); Pump();
        Check(vm.SelectedTransfer?.State == PassengerTransferState.Delivered && service.Balance == 13.33m, "Passenger completion renders one hourly wage: " + vm.Error);
        Check(!disembark.Command.CanExecute(null), "Completed passenger control prevents repeated disembarkation");
        Save(host, Path.Combine(folder, "offshore-passenger-completed.png")); host.Close();
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    private static void Save(Window window,string path){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);png.Save(stream);}
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}}
}
