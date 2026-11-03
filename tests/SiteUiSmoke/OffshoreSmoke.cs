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
        Check(Descendants<TextBlock>(control).Any(t => t.Text.Contains("WGS84 decimal degrees")), "Observation briefing provides navigable coordinates");
        Save(host, Path.Combine(folder, "offshore-observation-offered.png"));
        await Click("Accept mission");
        for (int i = 0; i <= 10; i++) await fixture.Observe(i);
        await vm.RefreshAsync(); await Click("Begin at base");
        Check(vm.Selected!.State == OffshoreMissionState.Outbound, "Production Offshore begin persists selected airframe/start");
        await fixture.Outbound(); await vm.RefreshAsync(); Pump();
        Check(vm.Selected!.State == OffshoreMissionState.Returning, "Production observation task completes only after outbound and continuous area samples");
        Check(Descendants<ProgressBar>(control).Single().Value == 10 && Descendants<TextBlock>(control).Any(t => t.Text == "Returning"), "Rendered observation stage and progress match committed mission");
        Save(host, Path.Combine(folder, "offshore-observation-return.png"));
        await fixture.Return(); await vm.RefreshAsync(); await Click("Complete mission");
        Check(vm.Selected!.State == OffshoreMissionState.Completed && fixture.Service.Balance == 30.89m, "Production Offshore completion and one hourly wage");
        Check(Descendants<TextBlock>(control).Any(t => t.Text.StartsWith("Mission completed.")), "Completed UI confirms settlement instead of prompting completion again");
        Save(host, Path.Combine(folder, "offshore-observation-completed.png"));
        var tabs = Descendants<TabControl>(control).Single(); tabs.SelectedIndex = 2; Pump();
        Check(Descendants<TextBlock>(control).Any(t => t.Text.Contains("Static deck contact tested")), "Platform qualification is visible, not an available mission");
        Save(host, Path.Combine(folder, "offshore-platform-gates.png"));
        var testRoot = Path.Combine(Path.GetTempPath(), "FrontierOffshoreSmoke", Guid.NewGuid().ToString("N"));
        var native = new SyntheticRoundTripSource();
        var diagnostic = new OffshorePlatformDiagnosticViewModel(() => throw new Exception("No native source in WPF smoke"),
            testRoot, () => native, () => SyntheticOffshoreRoundTrip.Origin, () => native.Clock);
        try
        {
            host.DataContext = new { OffshoreDiagnostic = diagnostic, Passengers = (object?)null };
            var experiment = Descendants<Expander>(control).Single(e => e.Header?.ToString() == "Experimental H125 contact test — no pay");
            experiment.IsExpanded = true; Pump();
            var button = Descendants<Button>(control).Single(b => b.Content?.ToString() == "Observe experimental base–platform–base flight");
            Check(button.Command?.CanExecute(null) == true, "Resolved base enables read-only round-trip test command");
            button.Command!.Execute(null);
            var deadline = DateTime.UtcNow.AddSeconds(40);
            while (!native.Holding.Task.IsCompleted && DateTime.UtcNow < deadline) { await Task.Delay(20); Pump(); }
            Check(native.Holding.Task.IsCompleted && diagnostic.Running && diagnostic.Status.Contains("Inspecting"), "Actual round-trip button reaches timed visit with synthetic native frames");
            Check(!button.Command.CanExecute(null), "Duplicate diagnostic start blocked while recording");
            experiment.BringIntoView(); Pump(); Save(host, Path.Combine(folder, "offshore-roundtrip-inspection.png"));
            native.Continue.TrySetResult();
            while (diagnostic.Running && DateTime.UtcNow < deadline) { await Task.Delay(20); Pump(); }
            Check(!diagnostic.Running && diagnostic.Status.Contains("Completed") && diagnostic.Status.Contains("6 / 6"), "Actual round-trip UI records all milestones without career pay");
            var review = ExperimentalOffshoreRoundTripRecording.Review(diagnostic.RecordingPath);
            Check(review.HistoricalStatus.Completed && !review.HistoricalStatus.CurrentBasePresence, "Closed UI recording replays history without live authority");
            // Public-safe synthetic fixture only, for hosted CLI command verification; never personal simulator data.
            var cliFixture = Path.Combine(folder, "synthetic-offshore-roundtrip.jsonl");
            File.Copy(diagnostic.RecordingPath, cliFixture, overwrite: false);
            File.Copy(diagnostic.RecordingPath + ".mission.json", cliFixture + ".mission.json", overwrite: false);
            Check(fixture.Service.Balance == 30.89m, "Experimental round trip cannot pay additional career wages");
            Save(host, Path.Combine(folder, "offshore-roundtrip-completed.png"));
        }
        finally { await diagnostic.ShutdownAsync(); if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); }
        await vm.ShutdownAsync(); host.Close();
    }
    private sealed class SyntheticRoundTripSource : IAircraftConditionSource
    {
        public DateTimeOffset Clock = SyntheticOffshoreRoundTrip.Epoch;
        public readonly TaskCompletionSource Holding = new(), Continue = new();
        public ConditionConnectionInfo ConnectionInfo => new("SYNTHETIC", "test-sim", "test-hash");
        public async IAsyncEnumerable<ConditionFrame> ReadAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            for (int i = 0; i <= 702; i++)
            {
                if (i == 351) { Holding.TrySetResult(); await Continue.Task.WaitAsync(token); }
                var frame = SyntheticOffshoreRoundTrip.Frame(i); Clock = frame.CompletedUtc; yield return frame; await Task.Yield();
            }
        }
    }
}
