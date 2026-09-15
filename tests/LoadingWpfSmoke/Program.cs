using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var document=XDocument.Load(args[0]);
        XNamespace p="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var window=new Window {Width=1000,Height=350,ShowInTaskbar=false,WindowStyle=WindowStyle.None};
        var stack=new StackPanel();window.Content=stack;
        var payload=new PayloadStationViewModel{Index=1,Name="Synthetic test station",CurrentKilograms=10,AssignmentMaximumKilograms=120};
        var fuel=new FuelTankViewModel{Id="test",Name="Synthetic test tank",CurrentGallons=20,CapacityGallons=50,PlannedGallons=20};
        foreach(var binding in new[]{"AssignedKilogramsText","PlannedGallonsText"})
        {
            var template=new XElement(document.Descendants(p+"DataTemplate").Single(x=>x.Descendants(p+"TextBox").Any(t=>(string?)t.Attribute("Text") is { } v&&v.Contains(binding))));
            // Window event handlers only select text; retain the production control tree and bindings.
            foreach(var box in template.Descendants(p+"TextBox")){box.Attribute("GotKeyboardFocus")?.Remove();box.Attribute("PreviewMouseLeftButtonDown")?.Remove();}
            var view=(FrameworkElement)((DataTemplate)XamlReader.Parse(template.ToString())).LoadContent();
            view.DataContext=binding=="AssignedKilogramsText"?payload:fuel;stack.Children.Add(view);
        }
        window.Show();Pump();
        var inputs=Descendants<TextBox>(stack).ToArray();
        inputs[0].Focus();inputs[0].SelectAll();inputs[0].SelectedText="12.";Pump();
        payload.RefreshReading(new(1,"Synthetic test station",10),120);Pump();
        Check(inputs[0].Text=="12."&&inputs[0].IsKeyboardFocused,"Payload draft/focus survived a live refresh");
        inputs[0].AppendText("5");Pump();Check(payload.AssignedKilograms==12.5m,"Exact payload reached model");
        inputs[1].Focus();inputs[1].SelectAll();inputs[1].SelectedText="7.25";Pump();
        fuel.RefreshReading(new("test","Synthetic test tank",19,50));Pump();
        Check(inputs[1].Text=="7.25"&&fuel.PlannedGallons==7.25m,"Fuel plan survived a live refresh");
        payload.ResetCommand.Execute(null);fuel.ResetCommand.Execute(null);Pump();
        Check(inputs.All(x=>x.Text=="0"),"Zero reset reached actual TextBox controls");
        window.Close();Console.WriteLine("WPF loading control smoke checks passed; synthetic data only.");return 0;
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    private static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}}
}
