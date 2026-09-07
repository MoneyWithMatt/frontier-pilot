using System.Text.Json;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

[Collection("Community installation")]
public sealed class OfflineWorkflowTests
{
    private static async Task<(BuilderServices Service, BuilderViewModel Vm)> Ready()
    {
        var service = BuilderServices.CreateDryRun();
        var vm = new BuilderViewModel(service,"SDK deliberately unavailable");
        Assert.Equal(WorkflowState.Disconnected,vm.State);
        Assert.False(vm.CaptureCommand.CanExecute(null));
        await vm.CheckAsync(); Assert.Equal(WorkflowState.ReadyToSurvey,vm.State);
        await vm.CaptureAsync(); Assert.Equal(WorkflowState.SurveyCaptured,vm.State);
        vm.AirportName="Offline Test"; vm.Identifier="FPDRY01"; vm.SiteConfirmed=true;
        return (service,vm);
    }
    [Fact] public async Task RecordedSurveyThroughGenerationBuildAndSafeReinstall()
    {
        var (service,vm)=await Ready();
        Assert.False(vm.BuildCommand.CanExecute(null));
        await vm.ValidateAsync();Assert.Equal(WorkflowState.Validated,vm.State);
        await vm.BuildAsync();Assert.Equal(WorkflowState.Built,vm.State);
        Assert.True(File.Exists(Path.Combine(vm.Built!.ProjectDirectory,"PackageSources","data","airport.xml")));
        Assert.Contains("frontier_test_only",File.ReadAllText(Path.Combine(vm.Built.PackageDirectory,"manifest.json")));
        Assert.True(File.Exists(Path.Combine(vm.Built.ProjectDirectory,"survey-reference.json")));
        var community=Path.Combine(service.SandboxDirectory!,"Packages","Community2024");
        var unrelated=Path.Combine(community,"unrelated");Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated,"keep.txt"),"must remain");
        var old=vm.Built;
        await vm.InstallAsync();Assert.Equal(WorkflowState.Installed,vm.State);
        Assert.StartsWith(service.SandboxDirectory!,vm.InstalledPath);
        Assert.Contains("not flyable",vm.SuccessInstructions);
        vm.Length="240";Assert.Equal(WorkflowState.SurveyCaptured,vm.State);Assert.False(vm.InstallCommand.CanExecute(null));
        await vm.ValidateAsync();await vm.BuildAsync();await vm.InstallAsync();
        Assert.Equal(WorkflowState.Installed,vm.State);
        Assert.Equal("must remain",File.ReadAllText(Path.Combine(unrelated,"keep.txt")));
        var receipt=JsonSerializer.Deserialize<InstallationReceipt>(File.ReadAllText(Path.Combine(service.SandboxDirectory!,"state","ownership","frontier-airport-fpdry01.json")))!;
        Assert.NotNull(receipt.BackupPath);Assert.True(File.Exists(Path.Combine(receipt.BackupPath!,"airport.bgl")));
        Assert.NotEqual(File.ReadAllText(Path.Combine(old.PackageDirectory,"airport.bgl")),File.ReadAllText(Path.Combine(vm.InstalledPath,"airport.bgl")));
        var evidence=Environment.GetEnvironmentVariable("FRONTIER_TEST_EVIDENCE");
        if(evidence!=null)
        {
            File.WriteAllText(Path.Combine(evidence,"offline-workflow.json"),JsonSerializer.Serialize(new { mode="DRY RUN",state=vm.State.ToString(),sandbox=service.SandboxDirectory,receipt,log=vm.TechnicalLog },new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    [Fact] public async Task UnknownDuplicateIdentifierFailsValidationWithoutProject()
    {
        var (service,vm)=await Ready();
        Directory.CreateDirectory(Path.Combine(service.SandboxDirectory!,"Packages","Community2024","frontier-airport-fpdry01"));
        await vm.ValidateAsync();
        Assert.Equal(WorkflowState.Error,vm.State);Assert.Equal(WorkflowState.SurveyCaptured,vm.RecoveryState);
        Assert.Contains("not owned",vm.Error);Assert.False(vm.BuildCommand.CanExecute(null));Assert.Equal("",service.ProjectDirectory);
        vm.Identifier="FPDRY02";await vm.ValidateAsync();Assert.Equal(WorkflowState.Validated,vm.State);
    }
    [Fact] public async Task RecordedAircraftFallbackIsRefused()
    {
        var source=BuilderServices.CreateDryRun(); await source.CheckAsync("",default); await source.CaptureAsync("",default);
        var frames=JsonSerializer.Deserialize<SurveyFrame[]>(File.ReadAllText(Path.Combine(source.EvidenceDirectory,"recorded-frames.json")))!;
        frames[0]=frames[0] with { Current=frames[0].Aircraft };
        var service=BuilderServices.CreateDryRun(JsonSerializer.Serialize(frames));
        var vm=new BuilderViewModel(service,"");await vm.CheckAsync();await vm.CaptureAsync();
        Assert.Equal(WorkflowState.Error,vm.State);Assert.Null(vm.Survey);Assert.Contains("explicit avatar",vm.Error);
        Assert.False(vm.ValidateCommand.CanExecute(null));
    }
    [Fact] public async Task TamperedTestPackageFailsInstallationWithRecovery()
    {
        var (service,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();
        File.AppendAllText(Path.Combine(vm.Built!.PackageDirectory,"airport.bgl"),"tamper");
        await vm.InstallAsync();Assert.Equal(WorkflowState.Error,vm.State);Assert.Equal(WorkflowState.Built,vm.RecoveryState);
        Assert.Contains("changed",vm.Error);Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(service.SandboxDirectory!,"Packages","Community2024")));
    }
    [Fact] public async Task ServiceRejectsBuildNotIssuedByThisSession()
    {
        var (service,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();
        var forged=vm.Built! with { LogPath="changed" };
        await Assert.ThrowsAsync<IOException>(()=>service.InstallAsync(forged,new Progress<string>(),default));
    }
    [Fact] public async Task ServiceRejectsSubstitutedSurveyCoordinates()
    {
        var (service,vm)=await Ready();
        var d=new AirstripDefinition { Name="Test", Identifier="FPDRY01", Latitude=0,Longitude=0,AltitudeMeters=0,TrueHeadingDegrees=0,MagneticVariationDegreesEast=0,LengthMeters=200,WidthMeters=20,Surface=RunwaySurface.Dirt };
        await Assert.ThrowsAsync<IOException>(()=>service.BuildAsync("",d,new Progress<string>(),default));
        Assert.Equal("",service.ProjectDirectory);
    }
    [Fact] public async Task CompilerFailurePreservesLogAndCannotInstall()
    {
        var service=BuilderServices.CreateDryRun(failCompilation:true);
        var vm=new BuilderViewModel(service,"");await vm.CheckAsync();await vm.CaptureAsync();
        vm.AirportName="Failure test";vm.Identifier="FPFAIL";vm.SiteConfirmed=true;
        await vm.ValidateAsync();await vm.BuildAsync();await vm.InstallAsync();
        Assert.Equal(WorkflowState.Error,vm.State);Assert.Equal(WorkflowState.Validated,vm.RecoveryState);
        Assert.Null(vm.Built);Assert.Contains("Simulated compiler failure",vm.Error);
        Assert.Contains("injected compiler failure",File.ReadAllText(service.LogPath));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(service.SandboxDirectory!,"Packages","Community2024")));
    }
    [Fact] public async Task LiveInstallerRefusesSyntheticPackage()
    {
        var (service,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();
        var root=service.SandboxDirectory!;
        var installer=new CommunityInstaller(Path.Combine(root,"Packages"),Path.Combine(root,"other-state"),()=>{});
        Assert.Throws<IOException>(()=>installer.Install(vm.Identifier,vm.Built!.PackageDirectory,vm.Built.Files));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root,"Packages","Community2024")));
    }
}
public sealed class DestinationSafetyTests : TemporaryDirectoryTest
{
    [Theory]
    [InlineData("")][InlineData("relative")][InlineData(@"C:\")][InlineData(@"\\server\share")][InlineData(@"C:\temp\..\unsafe")]
    [InlineData(@"C:\temp\bad.")][InlineData(@"C:\temp\bad ")][InlineData(@"C:\temp:stream")]
    public void InvalidDestinationRefused(string path)
    {
        var installer=new CommunityInstaller(path,Path.Combine(Root,"state"),()=>{});
        Assert.Throws<IOException>(()=>installer.ValidateDestination());
    }
    [Fact] public void NestedStateAndCommunityRefused()
    {
        Directory.CreateDirectory(Path.Combine(Root,"Community2024"));
        Assert.Throws<IOException>(()=>new CommunityInstaller(Root,Path.Combine(Root,"state"),()=>{}).ValidateDestination());
    }
    [Fact] public void MissingCommunityRefused() => Assert.Throws<IOException>(()=>new CommunityInstaller(Path.Combine(Root,"missing"),Path.Combine(Root,"state"),()=>{}).ValidateDestination());
    [Fact] public void DryRunCannotTargetArbitraryCommunity()
    {
        var p=Path.Combine(Root,"packages");Directory.CreateDirectory(Path.Combine(p,"Community2024"));
        Assert.Throws<IOException>(()=>new CommunityInstaller(p,Path.Combine(Root,"state"),()=>{},true).ValidateDestination());
    }
    [Fact] public async Task SavingLogNeverOverwritesExistingFile()
    {
        var path=Path.Combine(Root,"diagnostic.txt");await DiagnosticLog.SaveNewAsync(path,"original");
        await Assert.ThrowsAsync<IOException>(()=>DiagnosticLog.SaveNewAsync(path,"replacement"));
        Assert.Equal("original",File.ReadAllText(path));
    }
    [Theory][InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(10000)]
    public void InvalidSurveyAltitudeRejected(double altitude) => Assert.NotNull(SurveyCapture.ValidatePoint(new(20,14,altitude,90,altitude,0,0)));
    [Fact] public void InconsistentSurveyOffsetRejected() => Assert.NotNull(SurveyCapture.ValidatePoint(new(20,14,10,90,11,0,0)));
}


