using System.Diagnostics;
using System.Windows;
namespace FrontierAirportBuilder.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if(e.Args.Length==2&&e.Args[0]=="--career-reset-wait"&&int.TryParse(e.Args[1],out var pid))
        {
            try {using var previous=Process.GetProcessById(pid);if(!previous.WaitForExit(15000)){MessageBox.Show("The previous Frontier Pilot session has not closed. Relaunch after closing it to finish career reset.");Shutdown();return;}}
            catch(ArgumentException) { } // previous session already exited
        }
        base.OnStartup(e);
    }
}
