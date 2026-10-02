using System.Text.Json;
using System.Text.Json.Nodes;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
namespace FrontierAirportBuilder.Tests;

[Collection("Community installation")]
public sealed class AirportManagementTests
{
    private static async Task<(BuilderServices Service,BuilderViewModel Vm)> Ready(string id="FPMAN01",bool failBuild=false)
    {
        var service=BuilderServices.CreateDryRun(failCompilation:failBuild);var vm=new BuilderViewModel(service,"");
        await vm.CheckAsync();await vm.CaptureAsync();vm.AirportName="Persistent test";vm.Identifier=id;vm.SiteConfirmed=true;vm.HasRunway=true;
        vm.AddParkingAt(new(1200,200));vm.Layout.BeginTaxiway();vm.Layout.AddTaxiwayPoint(new(0,0));vm.Layout.AddTaxiwayPoint(new(100,100));vm.Layout.AddTaxiwayPoint(new(1200,200));vm.Layout.FinishTaxiway();
        return (service,vm);
    }
    private static string FileFor(BuilderServices s,Guid id)=>Path.Combine(s.LibraryDirectory,id.ToString("N")+".json");
    [Fact] public async Task SaveRestartLoadPreservesDefinitionAndSurveyWithoutRestoringBuildAuthority()
    {
        var (s,vm)=await Ready();Assert.True(vm.HasUnsavedAirport);await vm.SaveAirportAsync();Assert.False(vm.HasUnsavedAirport);
        var first=Assert.Single(vm.Airports);Assert.True(File.Exists(FileFor(s,first.Id)));
        await vm.ValidateAsync();await vm.BuildAsync();await vm.InstallAsync();Assert.Equal(WorkflowState.Installed,vm.State);
        var reopened=s.ReopenDryRun();var next=new BuilderViewModel(reopened,"");await next.RefreshAirportsAsync();Assert.Single(next.Airports);
        await next.LoadAirportAsync(first.Id);Assert.Equal(WorkflowState.SurveyCaptured,next.State);Assert.Null(next.Built);Assert.Equal("",next.InstalledPath);
        Assert.False(next.CaptureCommand.CanExecute(null));Assert.False(next.BuildCommand.CanExecute(null));Assert.False(next.InstallCommand.CanExecute(null));Assert.False(next.SiteConfirmed);
        Assert.Equal(first.Survey.Point,next.Survey);Assert.Equal(first.Name,next.AirportName);Assert.Equal("200",next.Length);Assert.Equal("20",next.Width);
        Assert.Equal(first.Definition.ParkingStands,next.Layout.Stands);Assert.Equal(JsonSerializer.Serialize(first.Definition.Taxiways),JsonSerializer.Serialize(next.Layout.Taxiways));Assert.False(next.HasUnsavedAirport);
        next.SiteConfirmed=true;await next.ValidateAsync();await next.BuildAsync();Assert.Equal(WorkflowState.Built,next.State);
        var evidence=Environment.GetEnvironmentVariable("FRONTIER_MANAGEMENT_EVIDENCE");
        if(evidence!=null)File.Copy(FileFor(s,first.Id),Path.Combine(evidence,"saved-airport-example.json"),true);
    }
    [Fact] public async Task SiteCanProgressFromNoAirfieldToRunwayAndParkingWithoutChangingIdentity()
    {
        var (service,vm)=await Ready();
        Assert.NotEmpty(vm.Layout.Stands);Assert.NotEmpty(vm.Layout.Taxiways);
        vm.HasRunway=false;
        Assert.False(vm.HasRunway);Assert.Empty(vm.Layout.Stands);Assert.Empty(vm.Layout.Taxiways);
        vm.Layout.AddObject(new(20,30),vm.LayoutRunwayHeading,SceneryAssets.PrototypeBaseLodgeId);
        await vm.SaveAirportAsync();var saved=Assert.Single(vm.Airports);
        Assert.False(saved.Definition.HasRunway);Assert.Contains("airfield not built",saved.Access);
        var reopened=new BuilderViewModel(service.ReopenDryRun(),"");await reopened.LoadAirportAsync(saved.Id);
        Assert.False(reopened.HasRunway);Assert.True(reopened.AddParkingCommand.CanExecute(null));
        Assert.Single(reopened.Layout.Objects);
        reopened.AddParkingCommand.Execute(null);Assert.True(reopened.HasRunway);await reopened.SaveAirportAsync();
        var upgraded=Assert.Single(reopened.Airports);Assert.Equal(saved.Id,upgraded.Id);Assert.True(upgraded.Definition.HasRunway);
        Assert.Single(upgraded.Definition.ParkingStands);Assert.Contains("Runway + parking",upgraded.Access);
    }

    [Fact] public async Task RunwayCanBePlacedOnMapAndSavedSiteReopensAsAnEditOfSameIdentity()
    {
        var (service,vm)=await Ready();vm.HasRunway=false;
        vm.BeginRunwayPlacement();Assert.True(vm.IsPlacingRunway);
        vm.PlaceRunwayEnd(new(100,0));
        Assert.True(vm.HasRunway);Assert.False(vm.IsPlacingRunway);Assert.Equal(200,vm.LayoutRunwayLength);Assert.Equal(90,vm.LayoutRunwayHeading,8);
        await vm.SaveAirportAsync();var saved=Assert.Single(vm.Airports);

        var reopened=new BuilderViewModel(service.ReopenDryRun(),"");await reopened.LoadAirportAsync(saved.Id);
        Assert.True(reopened.IsEditingSavedSite);Assert.Contains("Editing saved site",reopened.BuilderTitle);
        Assert.Contains("update this same site",reopened.BuilderGuidance);Assert.Equal(90,reopened.LayoutRunwayHeading,8);
        reopened.Width="22";await reopened.SaveAirportAsync();
        var edited=Assert.Single(reopened.Airports);Assert.Equal(saved.Id,edited.Id);Assert.Equal(saved.Revision+1,edited.Revision);Assert.Equal(22,edited.Definition.WidthMeters);
    }

    [Fact] public void NewSiteStartsBeforeAirfieldInfrastructure()
    {
        var service=BuilderServices.CreateDryRun();var vm=new BuilderViewModel(service,"");
        Assert.False(vm.HasRunway);Assert.Contains("airfield infrastructure not built",vm.AccessDescription);
    }
    [Fact] public async Task BuildAutomaticallySavesAndLoadedEditsRequireSavingAndRebuilding()
    {
        var (s,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();Assert.Single(vm.Airports);Assert.False(vm.HasUnsavedAirport);
        vm.Length="240";Assert.True(vm.HasUnsavedAirport);Assert.Null(vm.Built);await vm.SaveAirportAsync();Assert.False(vm.HasUnsavedAirport);
        var saved=Assert.Single((await s.ReopenDryRun().ListAirportsAsync(default)).Airports);Assert.Equal(240,saved.Definition.LengthMeters);Assert.Contains("rebuild",saved.LastBuild);
        vm.Identifier="OTHER";Assert.Equal("FPMAN01",vm.Identifier);
    }
    [Fact] public async Task TwoAirportsRestartDeleteOnePreservesOtherAndUnrelatedContent()
    {
        var (s,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();await vm.InstallAsync();var first=Assert.Single(vm.Airports);var firstProject=vm.Built!.ProjectDirectory;var firstInstall=vm.InstalledPath;
        vm.NewAirport();await vm.CheckAsync();await vm.CaptureAsync();vm.AirportName="Other airport";vm.Identifier="FPMAN02";vm.SiteConfirmed=true;
        await vm.ValidateAsync();await vm.BuildAsync();await vm.InstallAsync();var second=vm.Airports.Single(a=>a.Identifier=="FPMAN02");var secondProject=vm.Built!.ProjectDirectory;var secondInstall=vm.InstalledPath;
        var unrelated=Path.Combine(s.SandboxDirectory!,"Packages","Community2024","unrelated.txt");File.WriteAllText(unrelated,"KEEP");
        var r=s.ReopenDryRun();var plan=await r.PrepareDeleteAsync(first.Id,default);await r.DeleteAirportAsync(plan,true,default);
        Assert.False(File.Exists(FileFor(s,first.Id)));Assert.False(Directory.Exists(firstProject));Assert.False(Directory.Exists(firstInstall));
        Assert.True(File.Exists(FileFor(s,second.Id)));Assert.True(Directory.Exists(secondProject));Assert.True(Directory.Exists(secondInstall));Assert.Equal("KEEP",File.ReadAllText(unrelated));
        Assert.Equal(second.Id,Assert.Single((await r.ListAirportsAsync(default)).Airports).Id);
    }
    [Fact] public async Task ConfirmationCancellationAndForgedPlanCannotDelete()
    {
        var (s,vm)=await Ready();await vm.SaveAirportAsync();var saved=Assert.Single(vm.Airports);var plan=await s.PrepareDeleteAsync(saved.Id,default);
        await s.DeleteAirportAsync(plan,false,default);Assert.True(File.Exists(FileFor(s,saved.Id)));
        await Assert.ThrowsAsync<IOException>(()=>s.DeleteAirportAsync(plan with{Name="forged"},true,default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>s.DeleteAirportAsync(plan,true,new CancellationToken(true)));Assert.True(File.Exists(FileFor(s,saved.Id)));
    }
    [Fact] public async Task PartialFailedBuildCanBeDeletedSafely()
    {
        var (s,vm)=await Ready(failBuild:true);await vm.ValidateAsync();await vm.BuildAsync();Assert.Equal(WorkflowState.Error,vm.State);
        var saved=Assert.Single((await s.ListAirportsAsync(default)).Airports);Assert.Equal("failed or cancelled",saved.LastBuild);var project=s.ProjectDirectory;Assert.True(Directory.Exists(project));
        var plan=await s.PrepareDeleteAsync(saved.Id,default);await s.DeleteAirportAsync(plan,true,default);Assert.False(Directory.Exists(project));Assert.Empty((await s.ListAirportsAsync(default)).Airports);
    }
    [Fact] public async Task SavedOnlyAirportNeedsNoBuildForRemoval()
    {
        var (s,vm)=await Ready();await vm.SaveAirportAsync();var saved=Assert.Single(vm.Airports);
        var plan=await vm.PrepareDeleteAirportAsync(saved.Id);Assert.NotNull(plan);Assert.Empty(plan!.Projects);Assert.Null(plan.InstalledPackage);
        await vm.DeleteAirportAsync(plan,true);Assert.Empty(vm.Airports);Assert.Null(vm.Survey);Assert.Equal(WorkflowState.Disconnected,vm.State);
    }
    [Fact] public async Task ModifiedInstalledPackageBlocksAllDeletion()
    {
        var (s,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();await vm.InstallAsync();var saved=Assert.Single(vm.Airports);
        File.WriteAllText(Path.Combine(vm.InstalledPath,"user-file.txt"),"KEEP");
        await Assert.ThrowsAsync<IOException>(()=>s.PrepareDeleteAsync(saved.Id,default));Assert.True(File.Exists(FileFor(s,saved.Id)));Assert.True(Directory.Exists(vm.Built!.ProjectDirectory));
    }
    [Fact] public async Task UnrecognisedFileInGeneratedWorkspacePreventsCleanup()
    {
        var (s,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();var saved=Assert.Single(vm.Airports);var extra=Path.Combine(vm.Built!.ProjectDirectory,"user-notes.txt");File.WriteAllText(extra,"KEEP");
        await Assert.ThrowsAsync<IOException>(()=>s.PrepareDeleteAsync(saved.Id,default));Assert.Equal("KEEP",File.ReadAllText(extra));Assert.True(File.Exists(FileFor(s,saved.Id)));
    }
    [Fact] public async Task ChangesAfterConfirmationPreviewRequireNewPreview()
    {
        var (s,vm)=await Ready();await vm.SaveAirportAsync();var saved=Assert.Single(vm.Airports);var plan=await s.PrepareDeleteAsync(saved.Id,default);
        vm.Length="240";await vm.SaveAirportAsync();await Assert.ThrowsAsync<IOException>(()=>s.DeleteAirportAsync(plan,true,default));Assert.True(File.Exists(FileFor(s,saved.Id)));
    }
    [Fact] public async Task DuplicateIdentifiersAndConcurrentStaleSavesAreRefused()
    {
        var (s,vm)=await Ready();await vm.SaveAirportAsync();var first=Assert.Single(vm.Airports);
        var r=s.ReopenDryRun();var loaded=await r.LoadAirportAsync(first.Id,default);
        vm.Length="240";await vm.SaveAirportAsync();await Assert.ThrowsAsync<IOException>(()=>r.SaveAirportAsync(loaded.Definition with {WidthMeters=22},default));
        r.NewAirport();await r.CheckAsync("",default);await r.CaptureAsync("",default);
        await Assert.ThrowsAsync<IOException>(()=>r.SaveAirportAsync(loaded.Definition,default));Assert.Single((await r.ListAirportsAsync(default)).Airports);
    }
    [Fact] public async Task CorruptAndFutureDocumentsArePreservedAndReported()
    {
        var (s,vm)=await Ready();await vm.SaveAirportAsync();var saved=Assert.Single(vm.Airports);var path=FileFor(s,saved.Id);
        var json=JsonNode.Parse(File.ReadAllText(path))!;json["SchemaVersion"]=99;File.WriteAllText(path,json.ToJsonString());
        var list=await s.ReopenDryRun().ListAirportsAsync(default);Assert.Empty(list.Airports);Assert.Single(list.Warnings);Assert.True(File.Exists(path));
        await Assert.ThrowsAsync<IOException>(()=>s.PrepareDeleteAsync(saved.Id,default));
    }
    [Fact] public async Task TamperedSurveyCannotBeLoadedAndNoAircraftFallbackIsInvented()
    {
        var (s,vm)=await Ready();await vm.SaveAirportAsync();var saved=Assert.Single(vm.Airports);var path=FileFor(s,saved.Id);var json=JsonNode.Parse(File.ReadAllText(path))!;
        json["Survey"]!["Frames"]![0]!["Current"]!["ObjectId"]=0;File.WriteAllText(path,json.ToJsonString());
        var next=new BuilderViewModel(s.ReopenDryRun(),"");await next.LoadAirportAsync(saved.Id);Assert.Equal(WorkflowState.Error,next.State);Assert.Null(next.Survey);Assert.Null(next.Built);
    }
    [Fact] public async Task AnotherAirportsWorkspaceReferenceIsNotAuthorityToDeleteIt()
    {
        var (s,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();var first=Assert.Single(vm.Airports);
        vm.NewAirport();await vm.CheckAsync();await vm.CaptureAsync();vm.Identifier="FPMAN02";vm.AirportName="Second";vm.SiteConfirmed=true;await vm.ValidateAsync();await vm.BuildAsync();
        var second=vm.Airports.Single(a=>a.Identifier=="FPMAN02");var secondProject=vm.Built!.ProjectDirectory;
        var a=JsonNode.Parse(File.ReadAllText(FileFor(s,first.Id)))!;var b=JsonNode.Parse(File.ReadAllText(FileFor(s,second.Id)))!;a["Artifacts"]=b["Artifacts"]!.DeepClone();File.WriteAllText(FileFor(s,first.Id),a.ToJsonString());
        await Assert.ThrowsAsync<IOException>(()=>s.PrepareDeleteAsync(first.Id,default));Assert.True(Directory.Exists(secondProject));Assert.True(File.Exists(FileFor(s,second.Id)));
    }
    [Theory][InlineData("..\\outside")][InlineData("C:\\Windows")][InlineData("../../SDK")]
    public async Task PersistedWorkspacePathsCannotEscapeManagedRoot(string badKey)
    {
        var (s,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();var saved=Assert.Single(vm.Airports);var path=FileFor(s,saved.Id);var json=JsonNode.Parse(File.ReadAllText(path))!;
        json["Artifacts"]![0]!["Key"]=badKey;File.WriteAllText(path,json.ToJsonString());await Assert.ThrowsAsync<IOException>(()=>s.PrepareDeleteAsync(saved.Id,default));Assert.True(File.Exists(path));
    }
    [Fact] public async Task InterruptedUnreceiptedInstallIsPreserved()
    {
        var (s,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();var saved=Assert.Single(vm.Airports);
        var target=Path.Combine(s.SandboxDirectory!,"Packages","Community2024","frontier-airport-fpman01");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"partial.txt"),"KEEP");
        await Assert.ThrowsAsync<IOException>(()=>s.PrepareDeleteAsync(saved.Id,default));Assert.True(File.Exists(FileFor(s,saved.Id)));Assert.Equal("KEEP",File.ReadAllText(Path.Combine(target,"partial.txt")));
    }
    [Fact] public async Task MissingInstalledDirectoryStillAllowsReceiptAndDefinitionCleanup()
    {
        var (s,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();await vm.InstallAsync();var saved=Assert.Single(vm.Airports);
        // Simulates an external move entirely inside this test sandbox; does not touch real Community.
        var moved=Path.Combine(s.SandboxDirectory!,"externally-moved");Directory.Move(vm.InstalledPath,moved);
        var plan=await s.PrepareDeleteAsync(saved.Id,default);await s.DeleteAirportAsync(plan,true,default);
        Assert.True(Directory.Exists(moved));Assert.False(File.Exists(FileFor(s,saved.Id)));Assert.False(File.Exists(Path.Combine(s.SandboxDirectory!,"state","ownership","frontier-airport-fpman01.json")));
    }
    [Fact] public async Task LockedReceiptRollsBackStagedProjectsAndInstalledPackage()
    {
        var (s,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();await vm.InstallAsync();var saved=Assert.Single(vm.Airports);
        var plan=await s.PrepareDeleteAsync(saved.Id,default);var project=vm.Built!.ProjectDirectory;var installed=vm.InstalledPath;
        using var held=new FileStream(Path.Combine(s.SandboxDirectory!,"state","ownership","frontier-airport-fpman01.json"),FileMode.Open,FileAccess.Read,FileShare.Read);
        await Assert.ThrowsAnyAsync<IOException>(()=>s.DeleteAirportAsync(plan,true,default));
        Assert.True(File.Exists(FileFor(s,saved.Id)));Assert.True(Directory.Exists(project));Assert.True(Directory.Exists(installed));
    }
    [Fact] public async Task ReceiptCannotRedirectDeletionToAnotherDirectory()
    {
        var (s,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();await vm.InstallAsync();var saved=Assert.Single(vm.Airports);
        var other=Path.Combine(s.SandboxDirectory!,"other-files");Directory.CreateDirectory(other);File.WriteAllText(Path.Combine(other,"keep.txt"),"KEEP");
        var ledger=Path.Combine(s.SandboxDirectory!,"state","ownership","frontier-airport-fpman01.json");var json=JsonNode.Parse(File.ReadAllText(ledger))!;
        json["InstallPath"]=other;File.WriteAllText(ledger,json.ToJsonString());
        await Assert.ThrowsAsync<IOException>(()=>s.PrepareDeleteAsync(saved.Id,default));Assert.Equal("KEEP",File.ReadAllText(Path.Combine(other,"keep.txt")));Assert.True(File.Exists(FileFor(s,saved.Id)));
    }
    [Fact] public async Task DefinitionChangeWithoutRevisionBumpStillInvalidatesRemovalPreview()
    {
        var (s,vm)=await Ready();await vm.SaveAirportAsync();var saved=Assert.Single(vm.Airports);var plan=await s.PrepareDeleteAsync(saved.Id,default);
        var path=FileFor(s,saved.Id);var json=JsonNode.Parse(File.ReadAllText(path))!;json["Definition"]!["LengthMeters"]=240;File.WriteAllText(path,json.ToJsonString());
        await Assert.ThrowsAsync<IOException>(()=>s.DeleteAirportAsync(plan,true,default));Assert.True(File.Exists(path));
    }
    [Fact] public async Task ChangedSavedDefinitionCannotInstallEarlierIssuedBuild()
    {
        var (s,vm)=await Ready();await vm.ValidateAsync();await vm.BuildAsync();var built=vm.Built!;
        await s.SaveAirportAsync(built.Definition with{LengthMeters=240},default);
        await Assert.ThrowsAsync<IOException>(()=>s.InstallAsync(built,new Progress<string>(),default));
    }
}
