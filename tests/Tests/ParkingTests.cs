using System.Globalization;
using System.Xml.Linq;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
using FrontierAirportBuilder.Workflow;
namespace FrontierAirportBuilder.Tests;

public sealed class ParkingTests : TemporaryDirectoryTest
{
    private static AirstripDefinition Strip => Fixtures.Strip with { Identifier="FPPARK",Latitude=0,Longitude=0,TrueHeadingDegrees=0,LengthMeters=200,WidthMeters=20, ParkingStands=[new(1,35,0,180,10)] };
    [Fact] public void RightOfNorthFacingRunwayIsEast()
    {
        var p=ParkingGeometry.Calculate(Strip,Strip.ParkingStands[0]);
        Assert.Equal(0,p.Position.Latitude,8);Assert.Equal(35/111319.490793,p.Position.Longitude,9);Assert.Equal(180,p.TrueHeadingDegrees);
    }
    [Fact] public void ForwardOfEastFacingRunwayIsEastAndRightIsSouth()
    {
        var d=Strip with { TrueHeadingDegrees=90 };
        var p=ParkingGeometry.Calculate(d,new(1,100,0,90,10));
        Assert.Equal(100/111319.490793,p.Position.Longitude,9);Assert.Equal(90,p.TrueHeadingDegrees);
        Assert.True(ParkingGeometry.Calculate(d,new(1,0,-35,0,10)).Position.Latitude<0);
    }
    [Fact] public void ValidStandsPass() => Assert.Empty(Strip.Validate());
    [Theory][InlineData(0,0,10)][InlineData(0,21,10)][InlineData(0,35,0)][InlineData(0,35,31)][InlineData(0,4000,10)]
    [InlineData(double.NaN,35,10)][InlineData(0,double.PositiveInfinity,10)]
    public void InvalidParkingRefusesGenerationBeforeWrites(double forward,double right,double radius)
    {
        var d=Strip with {ParkingStands=[new(1,right,forward,0,radius)]};var path=Path.Combine(Root,"invalid");
        Assert.Throws<ArgumentException>(()=>ProjectGenerator.Generate(d,path));Assert.False(Directory.Exists(path));
    }
    [Fact] public void CirclesCannotOverlapOrShareNumbers()
    {
        Assert.NotEmpty((Strip with {ParkingStands=[new(1,0,35,0,10),new(2,0,40,0,10)]}).Validate());
        Assert.NotEmpty((Strip with {ParkingStands=[new(1,0,35,0,10),new(1,40,35,0,10)]}).Validate());
    }
    [Fact] public void NullListAndInvalidHeadingAreRefused()
    {
        Assert.NotEmpty((Strip with {ParkingStands=null!}).Validate());
        Assert.NotEmpty((Strip with {ParkingStands=[new(1,0,35,double.NaN,10)]}).Validate());
    }
    [Fact] public void GeneratedParkingIsDeterministicAndDoesNotInventAltitudeOrTaxiways()
    {
        var d=Strip with {ParkingStands=[new(2,35,40,-90,10),new(1,35,0,180,10)]};
        string Generate(AirstripDefinition input,string name) { var p=ProjectGenerator.Generate(input,Path.Combine(Root,name));return File.ReadAllText(Path.Combine(p.Directory,"PackageSources","data","airport.xml")); }
        var xml=Generate(d,"a");
        var culture=CultureInfo.CurrentCulture;try { CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");Assert.Equal(xml,Generate(d with {ParkingStands=d.ParkingStands.Reverse().ToArray()},"b")); } finally {CultureInfo.CurrentCulture=culture;}
        var root=XDocument.Parse(xml);var spots=root.Descendants("TaxiwayParking").ToArray();
        Assert.Equal(2,spots.Length);Assert.Equal("1",(string?)spots[0].Attribute("index"));
        Assert.Equal("RAMP_GA_SMALL",(string?)spots[0].Attribute("type"));Assert.Equal("270",(string?)spots[1].Attribute("heading"));
        Assert.All(spots,s=>Assert.Null(s.Attribute("alt")));
        Assert.Empty(root.Descendants("TaxiwayPath"));Assert.Equal(2,root.Descendants("RunwayStart").Count());
    }
}
[Collection("Community installation")]
public sealed class ParkingWorkflowTests
{
    [Fact] public async Task EditorParkingTravelsThroughOfflineInstallAndEditsInvalidateBuild()
    {
        var service=BuilderServices.CreateDryRun();var vm=new BuilderViewModel(service,"");
        Assert.False(vm.AddParkingCommand.CanExecute(null));
        await vm.CheckAsync();await vm.CaptureAsync();vm.AirportName="Parking test";vm.Identifier="FPSTAND";vm.SiteConfirmed=true;vm.HasRunway=true;
        vm.AddParkingCommand.Execute(null);Assert.Single(vm.Layout.Stands);
        vm.Layout.Heading="180";vm.Layout.Apply();await vm.ValidateAsync();await vm.BuildAsync();
        Assert.Equal(WorkflowState.Built,vm.State);Assert.Single(vm.Built!.Definition.ParkingStands);
        var xml=XDocument.Load(Path.Combine(vm.Built.ProjectDirectory,"PackageSources","data","airport.xml"));
        Assert.Single(xml.Descendants("TaxiwayParking"));
        await vm.InstallAsync();Assert.Equal(WorkflowState.Installed,vm.State);
        vm.Layout.Move(1,new(0,0));Assert.Equal(WorkflowState.SurveyCaptured,vm.State);Assert.False(vm.BuildCommand.CanExecute(null));Assert.False(vm.ValidateCommand.CanExecute(null));
        vm.Layout.Undo();Assert.True(vm.ValidateCommand.CanExecute(null));
        vm.RemoveParkingCommand.Execute(null);Assert.Empty(vm.Layout.Stands);
        await vm.ValidateAsync();await vm.BuildAsync();Assert.Empty(vm.Built!.Definition.ParkingStands);
    }
    [Fact] public async Task RecaptureClearsStandsAndBadEditorTextNeverBuilds()
    {
        var service=BuilderServices.CreateDryRun();var vm=new BuilderViewModel(service,"");await vm.CheckAsync();await vm.CaptureAsync();vm.HasRunway=true;
        vm.AddParkingCommand.Execute(null);vm.Layout.Radius="oops";vm.Layout.Apply();Assert.Contains("finite",vm.Layout.Message);
        await vm.CaptureAsync();Assert.Empty(vm.Layout.Stands);
    }
}
