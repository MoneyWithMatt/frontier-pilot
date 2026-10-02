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
                foreach(var index in new[]{5,1,2,3,4}){tabs.SelectedIndex=index;Pump();Save(window,Path.Combine(folder,$"page-{index}.png"));}
                edit.NewAirport();window.Close();result=0;
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
