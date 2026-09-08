using System.ComponentModel;
using System.Windows;
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
        Closing += OnClosing;
        Loaded += async (_,_)=>await model.RefreshAirportsAsync();
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
        if (!model.IsBusy) {e.Cancel=!MayDiscardEdits();return;}
        model.Cancel();
        e.Cancel = true;
    }
}
