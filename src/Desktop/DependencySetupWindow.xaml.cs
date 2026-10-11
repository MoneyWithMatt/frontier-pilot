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
    private CancellationTokenSource? cancellation;
    private string? installer;
    private DependencyDownload? installerPackage;
    private bool busy;
    public DependencySetupWindow(string sdkRoot, Func<string,bool> apply)
    {
        InitializeComponent(); this.apply = apply; SdkFolder.Text = sdkRoot;
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
        var status=DependencySetup.Detect(SdkCandidates(SdkFolder.Text),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft Flight Simulator 2024","UserCfg.opt"));
        SdkStatus.Text=status.SdkMessage;CommunityStatus.Text=status.CommunityMessage;CommunityPath.Text=status.CommunityPath;
        if(!string.IsNullOrWhiteSpace(status.SdkRoot))SdkFolder.Text=status.SdkRoot;
        UseButton.IsEnabled=status.SdkReady&&!busy;
        DetectionFeedback.Text=$"Checked at {DateTime.Now:HH:mm:ss}. "+(status.SdkReady?"SDK ready to use.":"SDK not ready; see the component status above.");
    }
    private void Recheck(object sender,RoutedEventArgs e)=>Detect();
    private void Cancel(object sender,RoutedEventArgs e)=>cancellation?.Cancel();
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
                    if(package!=DependencySetup.Samples)continue;
                    installer=file;installerPackage=package;InstallButton.IsEnabled=true;
                    Activity.Text="A previous Samples download is available. Choose Install downloaded component to retry; its signature will be checked again.";
                    break;
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
            Activity.Text="SDK folder saved. Connect to a loaded flight to verify simulator compatibility.";
        }
        catch(Exception ex){Activity.Text="Could not save SDK configuration: "+ex.Message;}
    }
    private void SetBusy(bool value)
    {
        busy=value;CoreButton.IsEnabled=SamplesButton.IsEnabled=BrowseButton.IsEnabled=RecheckButton.IsEnabled=!value;
        SdkFolder.IsEnabled=!value;UseButton.IsEnabled=false;InstallButton.IsEnabled=!value&&installer!=null;
        CancelButton.IsEnabled=value&&cancellation!=null;Progress.Visibility=value?Visibility.Visible:Visibility.Collapsed;
    }
    private async void DownloadCore(object sender,RoutedEventArgs e)=>await Download(DependencySetup.Core);
    private async void DownloadSamples(object sender,RoutedEventArgs e)=>await Download(DependencySetup.Samples);
    private async Task Download(DependencyDownload package)
    {
        if(busy)return;
        cancellation=new();installer=null;installerPackage=null;SetBusy(true);
        try
        {
            Activity.Text=$"Downloading {package.Name} from Microsoft…";
            installer=await DependencySetup.DownloadAsync(package,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FrontierAirportBuilder","dependency-downloads"),new Progress<string>(text=>Activity.Text=text),cancellation.Token);
            Activity.Text="Verifying the Microsoft installer signature…";
            await VerifyMicrosoftSignature(installer,cancellation.Token);
            installerPackage=MicrosoftSdkInstaller.Identify(installer);
            if(installerPackage!=package)throw new IOException("The downloaded SDK component does not match the requested installer.");
            Activity.Text=$"{package.Name} downloaded and Microsoft signature verified. Choose Install downloaded component to open the installer.";
        }
        catch(OperationCanceledException){installer=null;Activity.Text="Download cancelled. You can retry.";}
        catch(Exception ex){installer=null;Activity.Text="Download was not accepted: "+ex.Message;}
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
    private async void Install(object sender,RoutedEventArgs e)
    {
        if(busy||installer==null)return;
        SetBusy(true);
        try
        {
            BuilderServices.AssertSimulatorClosed();
            await VerifyMicrosoftSignature(installer,CancellationToken.None);
            installerPackage=MicrosoftSdkInstaller.Identify(installer);
            string? coreRoot=installerPackage==DependencySetup.Samples?DependencySetup.VerifyCore(SdkFolder.Text):null;
            // Core's installer changes the machine environment; the already-open app otherwise keeps stale values.
            var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe")){UseShellExecute=false};
            info.ArgumentList.Add("/i");info.ArgumentList.Add(installer);
            if(coreRoot!=null){info.Environment["MSFS2024_SDK"]=coreRoot;info.ArgumentList.Add("MSFS_SDK="+coreRoot);}
            var log=Path.Combine(Path.GetDirectoryName(installer)!,"install-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".log");
            info.ArgumentList.Add("/L*v");info.ArgumentList.Add(log);
            Activity.Text=coreRoot==null?"Complete the Microsoft Core installer, then recheck.":$"Installing Samples into {Path.Combine(coreRoot,"Samples")}. Complete the Microsoft installer, then recheck.";
            using var process=Process.Start(info)??throw new IOException("Could not open the Microsoft installer.");
            await process.WaitForExitAsync();
            Activity.Text=process.ExitCode switch {0=>"Installation completed. Recheck the SDK folder.",3010=>"Installation completed; Windows requires a restart.",1602=>"Installation cancelled. You can retry.",_=>$"Microsoft installer returned code {process.ExitCode}. Log: {log}"};
        }
        catch(Exception ex){Activity.Text="Installation did not complete: "+ex.Message;}
        finally{SetBusy(false);Detect();}
    }
}
