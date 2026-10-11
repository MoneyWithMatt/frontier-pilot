using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Desktop;

public partial class DependencySetupWindow : Window
{
    private readonly Func<string,bool> apply;
    private readonly Func<string,IEnumerable<string>> candidates;
    private CancellationTokenSource? cancellation;
    private readonly Dictionary<DependencyDownload,string> installers = new();
    private bool coreReady, sdkReady;
    private bool busy;
    public DependencySetupWindow(string sdkRoot, Func<string,bool> apply, Func<string,IEnumerable<string>>? candidates=null)
    {
        InitializeComponent(); this.apply = apply; this.candidates=candidates??SdkCandidates;SdkFolder.Text = sdkRoot;
        Loaded += (_, _) => { RestoreDownloadedInstaller();Detect(); };
        Closing += (_, e) => { if (busy) { cancellation?.Cancel(); Activity.Text="Wait for the download to cancel or the Microsoft installer to finish before closing."; e.Cancel=true; } };
    }
    internal static IEnumerable<string> SdkCandidates(string selected)
    {
        yield return selected;
        yield return Environment.GetEnvironmentVariable("MSFS2024_SDK", EnvironmentVariableTarget.User) ?? "";
        yield return Environment.GetEnvironmentVariable("MSFS2024_SDK", EnvironmentVariableTarget.Machine) ?? "";
        yield return Environment.GetEnvironmentVariable("MSFS2024_SDK") ?? "";
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            var found = new List<string>();
            try
            {
                using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);
                using var uninstall=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                foreach (var name in uninstall?.GetSubKeyNames() ?? [])
                {
                    using var entry=uninstall?.OpenSubKey(name);
                    if (entry?.GetValue("DisplayName") is string title && title.Contains("Flight Simulator 2024 SDK", StringComparison.OrdinalIgnoreCase)
                        && entry.GetValue("InstallLocation") is string path) found.Add(path);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            foreach (var path in found) yield return path;
        }
        yield return Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "MSFS 2024 SDK");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MSFS 2024 SDK");
    }
    private void Detect()
    {
        var status=DependencySetup.Detect(candidates(SdkFolder.Text),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft Flight Simulator 2024","UserCfg.opt"));
        SdkStatus.Text=status.SdkMessage;CommunityStatus.Text=status.CommunityMessage;CommunityPath.Text=status.CommunityPath;
        if(!string.IsNullOrWhiteSpace(status.SdkRoot))SdkFolder.Text=status.SdkRoot;
        sdkReady=status.SdkReady;
        coreReady=false;
        try {DependencySetup.VerifyCore(SdkFolder.Text);coreReady=true;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){ }
        UpdateActions();
        DetectionFeedback.Text=$"Checked at {DateTime.Now:HH:mm:ss}. "+(status.SdkReady?"SDK ready to use.":"SDK not ready; see the component status above.");
    }
    private void Recheck(object sender,RoutedEventArgs e)=>Detect();
    private void Cancel(object sender,RoutedEventArgs e)=>cancellation?.Cancel();
    private void UpdateActions()
    {
        bool Has(DependencyDownload package)=>installers.ContainsKey(package);
        CoreButton.IsEnabled=!busy&&!coreReady&&!Has(DependencySetup.Core);
        CoreInstallButton.IsEnabled=!busy&&!coreReady&&Has(DependencySetup.Core);
        SamplesButton.IsEnabled=!busy&&coreReady&&!sdkReady&&!Has(DependencySetup.Samples);
        SamplesInstallButton.IsEnabled=!busy&&coreReady&&!sdkReady&&Has(DependencySetup.Samples);
        UseButton.IsEnabled=!busy&&sdkReady;
        CoreButton.Content=coreReady?"Core installed":Has(DependencySetup.Core)?"Core downloaded":"Download Core";
        SamplesButton.Content=sdkReady?"Samples installed":Has(DependencySetup.Samples)?"Samples downloaded":"Download Samples";
        CoreStatus.Text=coreReady?"Installed and verified.":Has(DependencySetup.Core)?"Downloaded. Click Install Core to continue.":"Not ready. Download and install Core first.";
        SamplesStatus.Text=sdkReady?"Installed and verified.":!coreReady?"Waiting for Core to be installed and verified.":Has(DependencySetup.Samples)?"Downloaded. Click Install Samples to continue.":"Not installed. Download and install Samples next.";
        NextStep.Text=sdkReady?"Ready! Click Finish setup to save this SDK folder.":!coreReady?Has(DependencySetup.Core)?"Next: click Install Core and finish Microsoft's wizard.":"Next: download Core, then install it.":Has(DependencySetup.Samples)?"Next: click Install Samples and finish Microsoft's wizard.":"Next: download Samples, then install it.";
    }
    private void RestoreDownloadedInstaller()
    {
        var downloads=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FrontierAirportBuilder","dependency-downloads");
        if(!Directory.Exists(downloads))return;
        try
        {
            foreach(var file in Directory.EnumerateFiles(downloads,"MicrosoftSdkInstaller.msi",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    var package=MicrosoftSdkInstaller.Identify(file);
                    installers.TryAdd(package,file);
                }
                catch(IOException){ }
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Activity.Text="Previous downloads could not be checked: "+ex.Message;}
    }
    private void Browse(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFolderDialog{Title="Select the MSFS 2024 SDK folder"};
        if(dialog.ShowDialog(this)==true){SdkFolder.Text=dialog.FolderName;Detect();}
    }
    private void UseSdk(object sender,RoutedEventArgs e)
    {
        Detect(); if(!UseButton.IsEnabled)return;
        try
        {
            if(!apply(SdkFolder.Text)){Activity.Text="SDK change cancelled. Your current site edits are preserved.";return;}
            Environment.SetEnvironmentVariable("MSFS2024_SDK",SdkFolder.Text,EnvironmentVariableTarget.User);
            Activity.Text="Setup complete. SDK folder saved. You can close this window.";
            NextStep.Text="Setup complete. Connect to a loaded flight to check simulator compatibility.";
        }
        catch(Exception ex){Activity.Text="Could not save SDK configuration: "+ex.Message;}
    }
    private void SetBusy(bool value)
    {
        busy=value;BrowseButton.IsEnabled=RecheckButton.IsEnabled=!value;
        SdkFolder.IsEnabled=!value;UpdateActions();
        CancelButton.IsEnabled=value&&cancellation!=null;Progress.Visibility=value?Visibility.Visible:Visibility.Collapsed;
        CancelButton.Visibility=CancelButton.IsEnabled?Visibility.Visible:Visibility.Collapsed;
    }
    private async void DownloadCore(object sender,RoutedEventArgs e)=>await Download(DependencySetup.Core);
    private async void DownloadSamples(object sender,RoutedEventArgs e)=>await Download(DependencySetup.Samples);
    private async Task Download(DependencyDownload package)
    {
        if(busy)return;
        cancellation=new();SetBusy(true);
        try
        {
            Activity.Text=$"Downloading {package.Name} from Microsoft…";
            var installer=await DependencySetup.DownloadAsync(package,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FrontierAirportBuilder","dependency-downloads"),new Progress<string>(text=>Activity.Text=text),cancellation.Token);
            Activity.Text="Verifying the Microsoft installer signature…";
            await VerifyMicrosoftSignature(installer,cancellation.Token);
            if(MicrosoftSdkInstaller.Identify(installer)!=package)throw new IOException("The downloaded SDK component does not match the requested installer.");
            installers[package]=installer;
            Activity.Text=$"{package.Id} downloaded. Click Install {package.Id}, then complete Microsoft's wizard. Downloading alone does not install it.";
        }
        catch(OperationCanceledException){Activity.Text="Download cancelled. You can retry.";}
        catch(Exception ex){Activity.Text="Download was not accepted: "+ex.Message;}
        finally{cancellation.Dispose();cancellation=null;SetBusy(false);Detect();}
    }
    internal static async Task VerifyMicrosoftSignature(string path,CancellationToken token)
    {
        // The generated absolute path is passed as a quoted literal, never as executable command text.
        var literal=path.Replace("'","''");
        var script=$"$s=Get-AuthenticodeSignature -LiteralPath '{literal}'; if ($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -match 'O=Microsoft Corporation(?:,|$)') {{ exit 0 }} else {{ exit 1 }}";
        var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true};
        // A launcher running PowerShell 7 can otherwise pass incompatible modules to Windows PowerShell.
        info.Environment["PSModulePath"]=Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\Modules");
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))})info.ArgumentList.Add(arg);
        using var process=Process.Start(info)??throw new IOException("Could not start installer signature verification.");
        try {await process.WaitForExitAsync(token);}
        catch(OperationCanceledException){if(!process.HasExited)process.Kill();throw;}
        if(process.ExitCode!=0)throw new IOException("The installer does not have a trusted Microsoft signature. Installation was refused.");
    }
    private async void InstallCore(object sender,RoutedEventArgs e)=>await Install(DependencySetup.Core);
    private async void InstallSamples(object sender,RoutedEventArgs e)=>await Install(DependencySetup.Samples);
    private async Task Install(DependencyDownload package)
    {
        if(busy||!installers.TryGetValue(package,out var installer))return;
        SetBusy(true);
        try
        {
            BuilderServices.AssertSimulatorClosed();
            await VerifyMicrosoftSignature(installer,CancellationToken.None);
            if(MicrosoftSdkInstaller.Identify(installer)!=package)throw new IOException("The cached installer is not the selected component.");
            string? coreRoot=package==DependencySetup.Samples?DependencySetup.VerifyCore(SdkFolder.Text):null;
            // Core's installer changes the machine environment; the already-open app otherwise keeps stale values.
            var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe")){UseShellExecute=false};
            if(coreRoot!=null)info.Environment["MSFS2024_SDK"]=Path.TrimEndingDirectorySeparator(coreRoot);
            var log=Path.Combine(Path.GetDirectoryName(installer)!,"install-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".log");
            info.Arguments=SdkInstallerCommand.BuildArguments(installer,log,coreRoot);
            Activity.Text=coreRoot==null?"Complete the Microsoft Core installer, then recheck.":$"Installing Samples into {Path.Combine(coreRoot,"Samples")}. Complete the Microsoft installer, then recheck.";
            using var process=Process.Start(info)??throw new IOException("Could not open the Microsoft installer.");
            await process.WaitForExitAsync();
            Activity.Text=process.ExitCode switch {0=>"Installation completed. Recheck the SDK folder.",3010=>"Installation completed; Windows requires a restart.",1602=>"Installation cancelled. You can retry.",_=>$"Microsoft installer returned code {process.ExitCode}. Log: {log}"};
        }
        catch(Exception ex){Activity.Text="Installation did not complete: "+ex.Message;}
        finally{SetBusy(false);Detect();}
    }
}
