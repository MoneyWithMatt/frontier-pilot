using System.Text.Json;
using FrontierAirportBuilder.Workflow;
namespace FrontierAirportBuilder.Tests;
public sealed class CareerOnboardingTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"FrontierCareerTests",Guid.NewGuid().ToString("N"));
    private CareerStore Store()=>new(root);
    private void Put(string path,string content="fixture")
    {var target=Path.Combine(root,path);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.WriteAllText(target,content);}
    [Fact] public void NoCareerStartsAtWelcomeWithoutCreatingEconomy()
    {var vm=new CareerViewModel(Store());Assert.Equal(CareerStage.New,vm.Stage);Assert.False(vm.IsActive);Assert.False(Directory.Exists(Path.Combine(root,"economy")));}
    [Fact] public void ExistingPrototypeCareerLoadsWithoutChangingGameplayFiles()
    {Put("economy/economy-state.json","existing-ledger");var vm=new CareerViewModel(Store());Assert.True(vm.IsActive);Assert.Equal("existing-ledger",File.ReadAllText(Path.Combine(root,"economy/economy-state.json")));Assert.True(new CareerViewModel(Store()).IsActive);}
    [Theory][InlineData("")][InlineData(" ")][InlineData("A")][InlineData("1234")][InlineData("Matt\nPilot")][InlineData("<Matt>")]
    public void InvalidNamesCannotContinue(string name)
    {var vm=new CareerViewModel(Store());Assert.True(vm.Continue());vm.PilotName=name;Assert.False(vm.Continue());Assert.Equal(CareerStage.PilotName,vm.Stage);}
    [Theory][InlineData("Matt")][InlineData("Élodie Smith")][InlineData("Anne-Marie O'Neil")]
    public void ValidNamesPersistWithDraftAndNeverActivateEmployment(string name)
    {var vm=new CareerViewModel(Store());vm.Continue();vm.PilotName=name;Assert.True(vm.Continue());vm.SelectedRegion=Assert.Single(vm.Regions);Assert.True(vm.Continue());Assert.Equal(CareerStage.Opportunities,vm.Stage);Assert.False(vm.Continue());var resumed=new CareerViewModel(Store());Assert.Equal(name,resumed.PilotName);Assert.Equal("namibia",resumed.SelectedRegion!.Id);Assert.False(resumed.IsActive);Assert.False(File.Exists(Path.Combine(root,"economy/economy-state.json")));}
    [Fact] public void DefinitionsContainThreeComingSoonEmployersAtTwoBases()
    {Assert.Equal("Namibia",Assert.Single(CareerCatalogue.Regions).Name);Assert.Equal(3,CareerCatalogue.Employers.Count);Assert.Contains(CareerCatalogue.Employers,e=>e.Name=="Eros Community Aviation"&&e.Aircraft=="PA-28 · Savage Cub");Assert.Contains(CareerCatalogue.Employers,e=>e.Name=="Fly Eros"&&e.Aircraft=="C172 · Baron");Assert.Contains(CareerCatalogue.Employers,e=>e.Name=="Skeleton Coast Petroleum"&&e.Aircraft=="Vision Jet · H125");Assert.All(CareerCatalogue.Employers,e=>Assert.Equal("COMING SOON",e.Availability));Assert.Equal(2,CareerCatalogue.Employers.Count(e=>e.BaseId=="eros"));Assert.All(CareerCatalogue.Employers,e=>Assert.Contains(CareerCatalogue.Bases,b=>b.Id==e.BaseId));}
    [Fact] public void BackNavigationRetainsDraftNameAndRegion()
    {var vm=new CareerViewModel(Store());vm.Continue();vm.PilotName="Matt";vm.Continue();vm.SelectedRegion=vm.Regions[0];vm.Continue();vm.Back();Assert.Equal(CareerStage.Region,vm.Stage);vm.Back();Assert.Equal(CareerStage.PilotName,vm.Stage);Assert.Equal("Matt",vm.PilotName);Assert.Equal("namibia",vm.SelectedRegion!.Id);}
    [Fact] public void OnboardingCannotReplaceLegacyCareerOrInventActiveState()
    {Assert.Throws<IOException>(()=>Store().SaveDraft(CareerStage.LegacyActive,"Matt","namibia"));Put("economy/economy-state.json");Assert.Throws<IOException>(()=>Store().SaveDraft(CareerStage.Region,"Matt",null));}
    [Fact] public void CancelledResetChangesNothing()
    {Put("economy/economy-state.json");Assert.False(Store().RequestReset(false));Assert.False(File.Exists(Path.Combine(root,"career-reset.json")));Assert.Equal("fixture",File.ReadAllText(Path.Combine(root,"economy/economy-state.json")));}
    [Fact] public void ResetArchivesOnlyExplicitCareerFilesAndRetainsUnrelatedConfiguration()
    {
        foreach(var file in CareerStore.ResetFiles)Put(file,"old-career-bytes");
        string[] preserved=["sdk-settings.json","airports/site.json","ownership/package.json","installations/receipt.json","diagnostics/log.txt","economy/unknown.json","economy/economy-state.json.backup","tracking/diagnostics.json"];
        foreach(var file in preserved)Put(file,"keep");
        Assert.True(Store().RequestReset(true));Assert.Equal("old-career-bytes",File.ReadAllText(Path.Combine(root,"economy/economy-state.json")));
        Assert.Equal(CareerStage.New,Store().Load().Stage);
        foreach(var file in CareerStore.ResetFiles.Skip(1))Assert.False(File.Exists(Path.Combine(root,file)));
        foreach(var file in preserved)Assert.Equal("keep",File.ReadAllText(Path.Combine(root,file)));
        Assert.Equal(5,Directory.GetFiles(Path.Combine(root,"career-archives"),"*.json",SearchOption.AllDirectories).Length);
        Assert.False(File.Exists(Path.Combine(root,"career-reset.json")));Assert.Equal(CareerStage.New,Store().Load().Stage);
    }
    [Theory][InlineData("career.json")][InlineData("economy/economy-state.json")][InlineData("economy/aircraft-loading.json")][InlineData("clean-profile")]
    public void InterruptedResetResumesExactlyOnce(string checkpoint)
    {foreach(var file in CareerStore.ResetFiles)Put(file,"original");Store().RequestReset(true);var failing=new CareerStore(root,f=>{if(f==checkpoint)throw new IOException("simulated interruption");});Assert.Throws<IOException>(()=>failing.Load());Assert.Equal(CareerStage.New,Store().Load().Stage);Assert.Equal(CareerStage.New,Store().Load().Stage);Assert.Single(Directory.GetDirectories(Path.Combine(root,"career-archives")));}
    [Fact] public void InvalidFutureCareerIsPreservedUntilExplicitReset()
    {Put("career.json","{\"SchemaVersion\":999,\"Stage\":\"New\",\"PilotName\":\"\",\"RegionId\":null}");Assert.Throws<IOException>(()=>Store().Load());Assert.Contains("999",File.ReadAllText(Path.Combine(root,"career.json")));Store().RequestReset(true);Assert.Equal(CareerStage.New,Store().Load().Stage);Assert.Contains("999",File.ReadAllText(Directory.GetFiles(Path.Combine(root,"career-archives"),"career.json",SearchOption.AllDirectories).Single()));}
    [Fact] public void InvalidResetIntentCannotAddressArbitraryPaths()
    {Put("career-reset.json",JsonSerializer.Serialize("../../outside"));Put("economy/economy-state.json");Assert.Throws<IOException>(()=>Store().Load());Assert.True(File.Exists(Path.Combine(root,"economy/economy-state.json")));}
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
