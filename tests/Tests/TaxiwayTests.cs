using System.Globalization;
using System.Xml.Linq;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
using FrontierAirportBuilder.Workflow;
namespace FrontierAirportBuilder.Tests;

public sealed class AirportSiteTests
{
    [Theory][InlineData(501)][InlineData(1000)][InlineData(2500)]
    public void ParkingBeyondOldLimitWorksThroughBothEditorAndCore(double east)
    {
        var e=new LayoutEditor(()=>true);e.Add(new(east,0),0);
        Assert.Single(e.Stands);Assert.Empty(ParkingGeometry.Validate(e.Stands,200,20));
        e.East=(east+1).ToString(CultureInfo.InvariantCulture);e.Apply();Assert.Equal(east+1,e.Stands[0].EastMeters);
    }
    [Theory][InlineData(0)][InlineData(73)][InlineData(270)]
    public void BoundaryFollowsLongRunwayRatherThanAnchorRadius(double heading)
    {
        var site=new AirportSite(8000,45,heading);
        Assert.True(site.Contains(LocalCoordinates.FromRunway(100,5000,heading),15));
        Assert.False(site.Contains(LocalCoordinates.FromRunway(4000,0,heading),15));
    }
    [Theory][InlineData(double.NaN,0)][InlineData(0,double.PositiveInfinity)][InlineData(20000,0)][InlineData(4000,4000)]
    public void InvalidOrRemotePlacementsRefused(double e,double n)=>Assert.False(new AirportSite(200,20,0).Contains(new(e,n),10));
    [Fact] public void FullFootprintMustFit()
    {
        var site=new AirportSite(200,20,0);Assert.True(site.Contains(new(3000,0),10));Assert.False(site.Contains(new(3000,0),11));
        Assert.False(new AirportSite(30000,20,0).Contains(new(0,10001))); // Geometry ceiling stays enforced.
    }
    [Fact] public void LongOffsetsStayZoomIndependentAndGeodesic()
    {
        var v=new LayoutViewport();v.Zoom(.05,100,100,800,430);v.Pan(-100,50);
        var target=new LocalPoint(1500,900);var pixel=v.ToScreen(target,800,430);var result=v.ToLocal(pixel.X,pixel.Y,800,430);
        Assert.Equal(target.East,result.East,8);Assert.Equal(target.North,result.North,8);
        Assert.Equal(1500/111319.490793,LocalCoordinates.ToGeographic(new(0,0),new(1500,0)).Longitude,9);
    }
}

public sealed class TaxiwayTests : TemporaryDirectoryTest
{
    private static AirstripDefinition Strip=>Fixtures.Strip with {Identifier="FPTAXI",TrueHeadingDegrees=0,LengthMeters=200,WidthMeters=20,
        ParkingStands=[new(1,1000,200,180,10)],Taxiways=[new(1,"A",12,[new(0,0),new(100,0),new(100,200),new(0,0,1)])]};
    [Fact] public void DeterministicSegmentsUseOrderedNodesAndParkingReferenceAndPreserveRunway()
    {
        var d=Strip with {Taxiways=[..Strip.Taxiways,new(2,"B",15,[new(100,200),new(400,350)])]};
        string Generate(AirstripDefinition a,string folder)=>File.ReadAllText(Path.Combine(ProjectGenerator.Generate(a,Path.Combine(Root,folder)).Directory,"PackageSources/data/airport.xml"));
        var text=Generate(d,"a");var culture=CultureInfo.CurrentCulture;
        try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");Assert.Equal(text,Generate(d with {Taxiways=d.Taxiways.Reverse().ToArray()},"b"));}finally{CultureInfo.CurrentCulture=culture;}
        var xml=XDocument.Parse(text);var points=xml.Descendants("TaxiwayPoint").ToArray();var paths=xml.Descendants("TaxiwayPath").ToArray();
        Assert.Equal(4,points.Length);Assert.Equal(4,paths.Length);
        Assert.Equal("1",(string?)paths[2].Attribute("end"));Assert.Equal("PARKING",(string?)paths[2].Attribute("type"));
        Assert.Equal((string?)paths[1].Attribute("end"),(string?)paths[3].Attribute("start"));
        Assert.All(paths,p=>Assert.Equal((string?)xml.Descendants("Runway").Single().Attribute("surface"),(string?)p.Attribute("surface")));
        Assert.All(paths,p=>Assert.Equal("0",(string?)p.Attribute("weightLimit")));
        Assert.All(points,p=>Assert.Null(p.Attribute("alt")));
        var baseline=XDocument.Parse(Generate(d with{Taxiways=[]},"baseline"));
        Assert.True(XNode.DeepEquals(baseline.Descendants("Runway").Single(),xml.Descendants("Runway").Single()));
        Assert.True(XNode.DeepEquals(baseline.Descendants("TaxiwayParking").Single(),xml.Descendants("TaxiwayParking").Single()));
        var children=xml.Root!.Element("Airport")!.Elements().Select(e=>e.Name.LocalName).ToList();
        Assert.True(children.LastIndexOf("TaxiwayPoint")<children.IndexOf("TaxiwayParking"));Assert.True(children.IndexOf("TaxiwayParking")<children.IndexOf("TaxiName"));Assert.True(children.LastIndexOf("TaxiName")<children.IndexOf("TaxiwayPath"));
    }
    [Fact] public void EverySegmentCarriesCompilerRequiredUnlimitedWeightLimit()
    {
        var points=Enumerable.Range(0,10).Select(i=>new TaxiwayVertex(i*100,i*25)).ToArray();
        var definition=Strip with{Taxiways=[new(1,"T1",12,points)]};
        var project=ProjectGenerator.Generate(definition,Path.Combine(Root,"weight-limit"));
        var paths=XDocument.Load(Path.Combine(project.Directory,"PackageSources/data/airport.xml")).Descendants("TaxiwayPath").ToArray();
        Assert.Equal(9,paths.Length);Assert.All(paths,path=>Assert.Equal("0",path.Attribute("weightLimit")?.Value));
    }
    [Theory][InlineData(0)][InlineData(61)][InlineData(double.NaN)]
    public void InvalidWidthRefusedBeforeWrites(double width)=>Reject(Strip with{Taxiways=[Strip.Taxiways[0] with{WidthMeters=width}]});
    [Fact] public void InvalidPointsAndReferencesRefusedBeforeWrites()
    {
        foreach(var points in new TaxiwayVertex[][] {[],[new(0,0)],[new(0,0),new(.1,.1)],[new(0,0),new(4000,0)],[new(0,0),new(0,0,99)],[new(0,0),new(0,0,1),new(100,0)],[new(0,0),new(double.NaN,0)]})
            Reject(Strip with{Taxiways=[new(1,"A",12,points)]});
    }
    [Fact] public void NullAndDuplicateNamesAndIdsAreRejected()
    {
        Reject(Strip with{Taxiways=null!});Reject(Strip with{Taxiways=[null!]});Reject(Strip with{Taxiways=[new(1,"A",12,null!)]});
        Reject(Strip with{Taxiways=[..Strip.Taxiways,Strip.Taxiways[0]]});
        Reject(Strip with{Taxiways=[..Strip.Taxiways,Strip.Taxiways[0] with{Number=2}]});
        Reject(Strip with{Taxiways=[Strip.Taxiways[0] with{Name="too long !"}]});
    }
    private void Reject(AirstripDefinition d){var path=Path.Combine(Root,Guid.NewGuid().ToString());Assert.Throws<ArgumentException>(()=>ProjectGenerator.Generate(d,path));Assert.False(Directory.Exists(path));}
    [Fact] public void DrawingSnapsParkingAndHistoryPreservesConnections()
    {
        var e=new LayoutEditor(()=>true);e.Add(new(1000,200),180);e.BeginTaxiway();
        e.AddTaxiwayPoint(new(3,0));e.AddTaxiwayPoint(new(100,0));e.AddTaxiwayPoint(new(100,200));e.AddTaxiwayPoint(new(1002,200));e.FinishTaxiway();
        var t=Assert.Single(e.Taxiways);Assert.Equal(0,t.Points[0].EastMeters);Assert.Equal(1,t.Points[^1].ParkingNumber);
        e.Select(1);e.Move(1,new(1500,200));Assert.Equal(new LocalPoint(1500,200),e.Taxiways[0].Points[^1].Resolve(e.Stands));
        e.Delete();Assert.Null(e.Taxiways[0].Points[^1].ParkingNumber);Assert.Equal(1500,e.Taxiways[0].Points[^1].EastMeters);
        e.Undo();Assert.Equal(1,e.Taxiways[0].Points[^1].ParkingNumber);e.Undo();Assert.Equal(1000,e.Stands[0].EastMeters);
        e.SelectTaxiway(1);e.TaxiwayName="B";e.TaxiwayWidth="18";e.ApplyTaxiway();e.Delete();Assert.Empty(e.Taxiways);
        e.Undo();Assert.Equal(18,e.Taxiways[0].WidthMeters);e.Undo();Assert.Equal("T1",e.Taxiways[0].Name);e.Redo();Assert.Equal("B",e.Taxiways[0].Name);
    }
    [Fact] public void BusyAndCancelledDrawingDoNotCommit()
    {
        bool enabled=true;var e=new LayoutEditor(()=>enabled);e.BeginTaxiway();e.AddTaxiwayPoint(new(0,0));e.AddTaxiwayPoint(new(100,0));
        enabled=false;e.FinishTaxiway();Assert.Empty(e.Taxiways);enabled=true;e.CancelTaxiway();Assert.False(e.HasPendingProperties);Assert.Empty(e.Taxiways);
    }
    [Fact] public void AddingStandClearsTaxiwaySelectionAndDraftCannotAddStands()
    {
        var e=new LayoutEditor(()=>true);e.BeginTaxiway();e.Add(new(40,0),0);Assert.Empty(e.Stands);
        e.AddTaxiwayPoint(new(0,0));e.AddTaxiwayPoint(new(100,0));e.FinishTaxiway();Assert.True(e.HasTaxiwaySelection);
        e.Add(new(40,0),0);Assert.Null(e.SelectedTaxiway);e.Delete();Assert.Empty(e.Stands);Assert.Single(e.Taxiways);
    }
    [Fact] public void ShrinkingSiteStillAllowsInvalidPathsToBeRemovedIndividually()
    {
        double length=8000;var e=new LayoutEditor(()=>true,()=>new(length,20,0));
        for(int i=0;i<2;i++){e.BeginTaxiway();e.AddTaxiwayPoint(new(0,5000+i*100));e.AddTaxiwayPoint(new(100,5000+i*100));e.FinishTaxiway();}
        Assert.Equal(2,e.Taxiways.Count);length=200;e.SelectTaxiway(1);e.Delete();Assert.Single(e.Taxiways);e.SelectTaxiway(2);e.Delete();Assert.Empty(e.Taxiways);
    }
}

[Collection("Community installation")]
public sealed class TaxiwayWorkflowTests : TemporaryDirectoryTest
{
    [Fact] public async Task FullDryRunContainsDrawnTaxiwayAndEveryEditInvalidates()
    {
        var vm=new BuilderViewModel(BuilderServices.CreateDryRun(),"");await vm.CheckAsync();await vm.CaptureAsync();
        vm.AirportName="Taxiway test";vm.Identifier="FPTAXI2";vm.SiteConfirmed=true;vm.HasRunway=true;vm.AddParkingAt(new(1000,300));
        vm.Layout.BeginTaxiway();vm.Layout.AddTaxiwayPoint(new(0,0));vm.Layout.AddTaxiwayPoint(new(100,100));vm.Layout.AddTaxiwayPoint(new(1000,300));
        Assert.False(vm.ValidateCommand.CanExecute(null));vm.Layout.FinishTaxiway();
        await vm.ValidateAsync();Assert.Equal(WorkflowState.Validated,vm.State);await vm.BuildAsync();Assert.Equal(WorkflowState.Built,vm.State);
        var xml=File.ReadAllText(Path.Combine(vm.Built!.ProjectDirectory,"PackageSources/data/airport.xml"));Assert.Equal(2,XDocument.Parse(xml).Descendants("TaxiwayPath").Count());
        await vm.InstallAsync();Assert.Equal(WorkflowState.Installed,vm.State);
        vm.Layout.TaxiwayWidth="18";Assert.False(vm.InstallCommand.CanExecute(null));vm.Layout.ApplyTaxiway();Assert.Null(vm.Built);Assert.Equal("",vm.InstalledPath);Assert.Equal(WorkflowState.SurveyCaptured,vm.State);
        var path=Path.Combine(Root,"existing-layout.json");await vm.SaveLayoutAsync(path);vm.Layout.Delete();await vm.LoadLayoutAsync(path);Assert.Single(vm.Layout.Taxiways);Assert.Equal(18,vm.Layout.Taxiways[0].WidthMeters);
        await vm.ValidateAsync();await vm.BuildAsync();vm.Layout.BeginTaxiway();Assert.Null(vm.Built);vm.Layout.CancelTaxiway();Assert.False(vm.BuildCommand.CanExecute(null));
        var evidence=Environment.GetEnvironmentVariable("FRONTIER_TAXIWAY_EVIDENCE");
        if(evidence!=null){File.WriteAllText(Path.Combine(evidence,"airport.xml"),xml);File.Copy(path,Path.Combine(evidence,"layout.json"),true);}
    }
}
