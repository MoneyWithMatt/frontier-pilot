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
        model = new(dryRun ? BuilderServices.CreateDryRun() : new BuilderServices(), Environment.GetEnvironmentVariable("MSFS2024_SDK", EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable("MSFS2024_SDK") ?? "");
        DataContext = model;
        Closing += OnClosing;
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
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!model.IsBusy) return;
        model.Cancel();
        e.Cancel = true;
    }
}
