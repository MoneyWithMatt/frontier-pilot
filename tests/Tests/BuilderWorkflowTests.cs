using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
using FrontierAirportBuilder.Workflow;
namespace FrontierAirportBuilder.Tests;

public class BuilderViewModelTests
{
    private sealed class Fake : IBuilderServices
    {
        public string EvidenceDirectory => ""; public string ProjectDirectory => ""; public string LogPath => "";
        public int Builds, Installs; public bool Fail, Wait; public SurveyPoint? Point;
        public Task<string> CheckAsync(string s, CancellationToken t) => Task.FromResult("Connected");
        public async Task<SurveyPoint> CaptureAsync(string s, CancellationToken t) { if (Wait) await Task.Delay(Timeout.Infinite,t); if(Fail) throw new IOException("Avatar unavailable"); return Point ?? new(-22,14,10,90,10,0,0); }
        public Task<string> ValidateIdentifierAsync(string s, CancellationToken t) => Task.FromResult("Available");
        public Task<BuiltAirport> BuildAsync(string s, AirstripDefinition d, IProgress<string> p, CancellationToken t) { Builds++; if(Fail) throw new IOException("Compiler failed"); return Task.FromResult(new BuiltAirport(d,"project","package","log",[])); }
        public Task<string> InstallAsync(BuiltAirport b, IProgress<string> p, CancellationToken t) { Installs++; if(Fail) throw new IOException("MSFS running"); return Task.FromResult("installed"); }
        public void OpenPath(string p) { }
    }
    private static async Task<BuilderViewModel> Ready(Fake f) { var v=new BuilderViewModel(f,"sdk"); await v.CheckAsync(); await v.CaptureAsync(); v.AirportName="Test";v.Identifier="FPNEW";v.SiteConfirmed=true;await v.ValidateAsync();return v; }
    [Fact] public async Task InvalidInputNeverBuilds() { var f=new Fake(); var v=new BuilderViewModel(f,"sdk"); await v.BuildAsync();Assert.Equal(0,f.Builds);Assert.NotEmpty(v.Error); }
    [Fact] public async Task CompleteFlowAndEditsInvalidatePackage() { var f=new Fake();var v=await Ready(f);Assert.Equal("09 / 27",v.Designators);await v.BuildAsync();Assert.True(v.InstallCommand.CanExecute(null));await v.InstallAsync();Assert.Contains("Restart",v.Status);Assert.Equal(1,f.Installs);v.Length="250";Assert.Null(v.Built);Assert.False(v.InstallCommand.CanExecute(null)); }
    [Theory] [InlineData("NaN")] [InlineData("-1")] [InlineData("abc")] public async Task InvalidDimensionsRefuse(string value) {var f=new Fake();var v=await Ready(f);v.Length=value;await v.BuildAsync();Assert.Equal(0,f.Builds);}
    [Theory] [InlineData("FPT1")] [InlineData("FPSC")] public async Task ProofIdsProtected(string id) {var f=new Fake();var v=await Ready(f);v.Identifier=id;await v.BuildAsync();Assert.Equal(0,f.Builds);}
    [Fact] public async Task CaptureCancellationHasNoFallback() {var f=new Fake{Wait=true};var v=new BuilderViewModel(f,"sdk");await v.CheckAsync();var task=v.CaptureAsync();Assert.True(v.IsBusy);Assert.False(v.BuildCommand.CanExecute(null));v.Cancel();await task;Assert.False(v.IsBusy);Assert.Null(v.Survey);Assert.Contains("Cancelled",v.Status);}
    [Fact] public async Task CaptureFailureClearsOldSurvey() {var f=new Fake();var v=await Ready(f);f.Fail=true;await v.CaptureAsync();Assert.Null(v.Survey);Assert.Contains("Avatar unavailable",v.Error);}
    [Fact] public async Task BuildFailureCannotInstall() {var f=new Fake();var v=await Ready(f);f.Fail=true;await v.BuildAsync();Assert.Null(v.Built);Assert.Contains("Compiler failed",v.Error);Assert.False(v.IsBusy);}
    [Fact] public async Task InstallationFailureCanRetry() {var f=new Fake();var v=await Ready(f);await v.BuildAsync();f.Fail=true;await v.InstallAsync();Assert.Equal("",v.InstalledPath);Assert.Contains("MSFS running",v.Error);Assert.True(v.InstallCommand.CanExecute(null));}
    [Fact] public async Task InvalidPointFromServiceCannotEnableValidation()
    {
        var f=new Fake{Point=new(-22,14,double.NaN,90,10,0,0)};
        var v=new BuilderViewModel(f,"sdk");await v.CheckAsync();await v.CaptureAsync();
        Assert.Equal(WorkflowState.Error,v.State);Assert.Null(v.Survey);Assert.False(v.ValidateCommand.CanExecute(null));
    }
    [Fact] public async Task ReconnectingClearsPreviouslyValidatedWork()
    {
        var f=new Fake();var v=await Ready(f);await v.BuildAsync();await v.CheckAsync();
        Assert.Equal(WorkflowState.ReadyToSurvey,v.State);Assert.Null(v.Survey);Assert.Null(v.Built);
        Assert.False(v.BuildCommand.CanExecute(null));Assert.False(v.InstallCommand.CanExecute(null));
    }
    [Fact] public async Task BuildFailureRecoversFromValidatedState()
    {
        var f=new Fake();var v=await Ready(f);f.Fail=true;await v.BuildAsync();
        Assert.Equal(WorkflowState.Error,v.State);Assert.Equal(WorkflowState.Validated,v.RecoveryState);
        Assert.True(v.BuildCommand.CanExecute(null));Assert.Contains("IOException",v.TechnicalLog);
        f.Fail=false;await v.BuildAsync();Assert.Equal(WorkflowState.Built,v.State);
    }
    [Fact] public async Task BusyCaptureRefusesInputChangesAndReentry()
    {
        var f=new Fake{Wait=true};var v=new BuilderViewModel(f,"sdk");await v.CheckAsync();
        var pending=v.CaptureAsync();v.SdkRoot="other";await v.CheckAsync();
        Assert.Equal("sdk",v.SdkRoot);Assert.True(v.IsBusy);
        v.Cancel();await pending;Assert.Equal(WorkflowState.Cancelled,v.State);Assert.True(v.CaptureCommand.CanExecute(null));
    }
    [Fact] public async Task ChangingSdkRequiresNewConnectionAndSurvey() {var f=new Fake();var v=await Ready(f);v.SdkRoot="other";Assert.Null(v.Survey);Assert.False(v.CaptureCommand.CanExecute(null));}
}
[Collection("Community installation")]
public sealed class CommunityInstallerTests : TemporaryDirectoryTest
{
    private string Source => Path.Combine(Root,"build","frontier-airport-fpnew");
    private string Packages => Path.Combine(Root,"packages");
    private string State => Path.Combine(Root,"state");
    private CommunityInstaller Create(Action? closed=null)
    {
        Directory.CreateDirectory(Path.Combine(Packages,"Community2024"));
        Directory.CreateDirectory(Source);
        File.WriteAllText(Path.Combine(Source,"manifest.json"),"""{"creator":"Frontier Pilot","package_order_hint":"CUSTOM_AIRPORT"}""");
        File.WriteAllText(Path.Combine(Source,"airport.bgl"),"test");
        File.WriteAllText(Path.Combine(Source,"layout.json"),"""{"content":[{"path":"airport.bgl","size":4}]}""");
        return new(Packages,State,closed??(()=>{}));
    }
    [Fact] public void InstallAndReplaceKeepsBackupAndUnrelatedFiles()
    {
        var i=Create();var other=Path.Combine(i.Community,"unrelated.txt");File.WriteAllText(other,"keep");
        var first=i.Install("FPNEW",Source,CommunityInstaller.Inventory(Source));
        File.WriteAllText(Path.Combine(Source,"airport.bgl"),"next");
        var second=i.Install("FPNEW",Source,CommunityInstaller.Inventory(Source));
        Assert.Equal("test",File.ReadAllText(Path.Combine(second.BackupPath!,"airport.bgl")));
        Assert.Equal("next",File.ReadAllText(Path.Combine(first.InstallPath,"airport.bgl")));
        Assert.Equal("keep",File.ReadAllText(other));
    }
    [Fact] public void UnownedCollisionRefused() {var i=Create();Directory.CreateDirectory(Path.Combine(i.Community,"frontier-airport-fpnew"));Assert.Throws<IOException>(()=>i.CheckIdentifier("FPNEW"));}
    [Fact] public void EditedOwnedPackageRefused() {var i=Create();var r=i.Install("FPNEW",Source,CommunityInstaller.Inventory(Source));File.WriteAllText(Path.Combine(r.InstallPath,"airport.bgl"),"edit");Assert.Throws<IOException>(()=>i.CheckIdentifier("FPNEW"));}
    [Fact] public void ChangedBuildRefused() {var i=Create();var files=CommunityInstaller.Inventory(Source);File.WriteAllText(Path.Combine(Source,"airport.bgl"),"edit");Assert.Throws<IOException>(()=>i.Install("FPNEW",Source,files));Assert.False(Directory.Exists(State));}
    [Fact] public void CancellationBeforeInstallWritesNothing() {var i=Create();Assert.Throws<OperationCanceledException>(()=>i.Install("FPNEW",Source,CommunityInstaller.Inventory(Source),new CancellationToken(true)));Assert.False(Directory.Exists(State));}
    [Fact] public void RunningSimulatorRefused() {var i=Create(()=>throw new IOException("running"));Assert.Throws<IOException>(()=>i.Install("FPNEW",Source,CommunityInstaller.Inventory(Source)));Assert.False(Directory.Exists(State));}
    [Theory] [InlineData("FPT1")] [InlineData("FPSC")] [InlineData("../x")] public void ProtectedOrInvalidIdRefused(string id) {var i=Create();Assert.ThrowsAny<Exception>(()=>i.CheckIdentifier(id));}
    private sealed class InlineProgress(Action<string> callback) : IProgress<string> { public void Report(string value) => callback(value); }
    [Fact] public void FailedReceiptCommitRestoresPreviousPackage()
    {
        var i=Create();var first=i.Install("FPNEW",Source,CommunityInstaller.Inventory(Source));
        File.WriteAllText(Path.Combine(Source,"airport.bgl"),"next");
        FileStream? locked=null;
        try {
            Assert.Throws<UnauthorizedAccessException>(()=>i.Install("FPNEW",Source,CommunityInstaller.Inventory(Source),progress:new InlineProgress(message=>{
                if(message.StartsWith("Installing")) locked=File.Open(Path.Combine(State,"ownership","frontier-airport-fpnew.json"),FileMode.Open,FileAccess.Read,FileShare.None);
            })));
        } finally { locked?.Dispose(); }
        Assert.Equal("test",File.ReadAllText(Path.Combine(first.InstallPath,"airport.bgl")));
        Assert.Contains("Replace",i.CheckIdentifier("FPNEW"));
    }
    [Fact] public void CancelDuringStagingLeavesCommunityUntouched()
    {
        var i=Create();using var cancel=new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(()=>i.Install("FPNEW",Source,CommunityInstaller.Inventory(Source),cancel.Token,new InlineProgress(_=>cancel.Cancel())));
        Assert.Empty(Directory.GetFileSystemEntries(i.Community));
    }
    [Fact] public void PathEscapeRefused() => Assert.Throws<IOException>(()=>CommunityInstaller.Within(Path.Combine(Root,"other"),Path.Combine(Root,"stage")));
    [Fact] public void SimulatorStartingDuringStagingLeavesCommunityUntouched() {int calls=0;var i=Create(()=>{if(++calls==2)throw new IOException("started");});Assert.Throws<IOException>(()=>i.Install("FPNEW",Source,CommunityInstaller.Inventory(Source)));Assert.Empty(Directory.GetFileSystemEntries(i.Community));}
}
