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
                Check(((ListBox)screen.FindName("EmployersList")).Items.Count==3,"All three employers are visible");
                Save(careerWindow,Path.Combine(folder,"career-employers.png"));
                ((ListBox)screen.FindName("EmployersList")).SelectedIndex=2;Pump();Save(careerWindow,Path.Combine(folder,"career-offshore.png"));
                CareerButton("Back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(careerStore.Load().Stage==CareerStage.Region,"Back navigation preserves draft");
                careerWindow.Close();result=0;
            }
            catch(Exception e){Console.Error.WriteLine(e);}
            finally{app.Shutdown(result);}
        };
        app.Run();return result;
    }
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    private static void Save(Window window,string path){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);png.Save(stream);}
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}}
}
