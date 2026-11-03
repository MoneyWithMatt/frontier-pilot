using System.IO;
using System.Windows;
using System.Windows.Controls;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.Desktop;
using FrontierAirportBuilder.TestFixtures;

internal static partial class Program
{
    private static async Task CheckOffshoreMissions(string folder)
    {
        var fixture = new SyntheticOffshoreFlight();
        var vm = new OffshoreMissionsViewModel(fixture.Service, () => throw new Exception("Synthetic UI must not connect to MSFS"));
        await vm.RefreshAsync();
        var control = new OffshoreMissionsControl { DataContext = vm };
        var host = new Window { Content = control, Width = 1267, Height = 900, ShowInTaskbar = false };
        host.Show(); Pump();
        var list = Descendants<ListBox>(control).Single(); list.SelectedItem = vm.Missions.First(); Pump();
        async Task Click(string content)
        {
            var button = Descendants<Button>(control).Single(b => b.Content?.ToString() == content);
            Check(button.Command?.CanExecute(null) == true, "Offshore actual control enabled: " + content);
            button.Command!.Execute(null); await Task.Delay(100); Pump();
            Check(vm.Error == "", "Offshore production command succeeded: " + vm.Error);
        }
        Check(Descendants<TextBlock>(control).Any(t => t.Text.Contains("$800")), "Offshore employer hourly policy rendered without per-job reward");
        Save(host, Path.Combine(folder, "offshore-observation-offered.png"));
        await Click("Accept mission");
        for (int i = 0; i <= 10; i++) await fixture.Observe(i);
        await vm.RefreshAsync(); await Click("Begin at base");
        Check(vm.Selected!.State == OffshoreMissionState.Outbound, "Production Offshore begin persists selected airframe/start");
        await fixture.Outbound(); await vm.RefreshAsync();
        Check(vm.Selected!.State == OffshoreMissionState.Returning, "Production observation task completes only after outbound and continuous area samples");
        Save(host, Path.Combine(folder, "offshore-observation-return.png"));
        await fixture.Return(); await vm.RefreshAsync(); await Click("Complete mission");
        Check(vm.Selected!.State == OffshoreMissionState.Completed && fixture.Service.Balance == 30.89m, "Production Offshore completion and one hourly wage");
        Save(host, Path.Combine(folder, "offshore-observation-completed.png"));
        var tabs = Descendants<TabControl>(control).Single(); tabs.SelectedIndex = 2; Pump();
        Check(Descendants<TextBlock>(control).Any(t => t.Text.Contains("Static deck contact tested")), "Platform qualification is visible, not an available mission");
        Save(host, Path.Combine(folder, "offshore-platform-gates.png"));
        await vm.ShutdownAsync(); host.Close();
    }
}
