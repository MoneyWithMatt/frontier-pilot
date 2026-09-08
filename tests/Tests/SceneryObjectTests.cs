using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class SceneryObjectTests : TemporaryDirectoryTest
{
    [Fact]
    public void InstalledModelBoundsDefineTrueScaleOriginAwareFootprint()
    {
        var asset=SceneryAssets.GenericHangar56;var f=asset.Footprint;
        Assert.Equal(46.18267783437614,f.WidthMeters,10);Assert.Equal(94.40472412109375,f.DepthMeters,10);
        Assert.Equal(-2.3623965613275537,f.CentreRightMeters,10);Assert.Equal(-2.1556987304687567,f.CentreForwardMeters,10);
        Assert.Contains("LOD0 glTF accessor bounds",asset.DimensionsProvenance);Assert.Equal(2,asset.Foundation!.MarginMeters);Assert.Equal(8,asset.Foundation.FalloffMeters);
    }

    [Fact]
    public void FootprintUsesMetresRotatesAroundPlacementOriginAndPreservesDimensions()
    {
        var asset=SceneryAssets.GenericHangar56;
        var north=new PlacedSceneryObject(1,asset.Id,100,200,0);var a=SceneryObjectGeometry.FootprintCorners(north,asset);
        Assert.Equal(asset.Footprint.WidthMeters,TaxiwayGeometry.Distance(a[0],a[1]),9);
        Assert.Equal(asset.Footprint.DepthMeters,TaxiwayGeometry.Distance(a[1],a[2]),9);
        Assert.Equal(100+asset.Footprint.MinimumRightMeters,a[0].East,9);Assert.Equal(200+asset.Footprint.MinimumForwardMeters,a[0].North,9);
        var east=north with{TrueHeadingDegrees=90};var b=SceneryObjectGeometry.FootprintCorners(east,asset);
        Assert.Equal(100+asset.Footprint.MinimumForwardMeters,b[0].East,9);Assert.Equal(200-asset.Footprint.MinimumRightMeters,b[0].North,9);
        Assert.True(SceneryObjectGeometry.ContainsFootprint(east,asset,SceneryObjectGeometry.TransformModelPoint(east,0,0)));
        Assert.False(SceneryObjectGeometry.ContainsFootprint(east,asset,new(1000,1000)));
    }

    [Fact]
    public void PositionAndHeadingUseSharedLocalGeometry()
    {
        var item = new PlacedSceneryObject(1, SceneryAssets.GenericHangar56Id, 750, -320, 725);
        var placement = SceneryObjectGeometry.Calculate(Fixtures.Strip, item);
        var expected = LocalCoordinates.ToGeographic(new(Fixtures.Strip.Latitude, Fixtures.Strip.Longitude), new(750, -320));
        Assert.Equal(expected, placement.Position);
        Assert.Equal(5, placement.TrueHeadingDegrees);
        Assert.Empty((Fixtures.Strip with { PlacedObjects = [item] }).Validate());
    }

    [Fact]
    public void UnsupportedNonFiniteDuplicateAndOutOfSiteObjectsAreRejectedBeforeWriting()
    {
        var valid = new PlacedSceneryObject(1, SceneryAssets.GenericHangar56Id, 100, 100, 0);
        foreach (var objects in new PlacedSceneryObject[][]
        {
            [valid with { AssetId = "unknown" }],
            [valid with { EastMeters = double.NaN }],
            [valid, valid with { EastMeters = 200 }],
            [valid with { EastMeters = 9000 }]
        })
        {
            var path = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Assert.Throws<ArgumentException>(() => ProjectGenerator.Generate(Fixtures.Strip with { PlacedObjects = objects }, path));
            Assert.False(Directory.Exists(path));
        }
    }

    [Fact]
    public void GeneratedLibraryObjectsAreDeterministicTerrainSnappedAndSiblingToAirport()
    {
        var objects = new[]
        {
            new PlacedSceneryObject(2,SceneryAssets.GenericHangar56Id,800,120,-90),
            new PlacedSceneryObject(1,SceneryAssets.GenericHangar56Id,100,-50,450)
        };
        string Generate(IReadOnlyList<PlacedSceneryObject> input,string folder) => File.ReadAllText(Path.Combine(
            ProjectGenerator.Generate(Fixtures.Strip with { PlacedObjects = input }, Path.Combine(Root,folder)).Directory,
            "PackageSources","data","airport.xml"));
        var first=Generate(objects,"one");
        var culture=CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");Assert.Equal(first,Generate(objects.Reverse().ToArray(),"two")); }
        finally { CultureInfo.CurrentCulture=culture; }
        var xml=XDocument.Parse(first);var root=xml.Root!;var nodes=root.Elements("SceneryObject").ToArray();
        Assert.Equal(2,nodes.Length);Assert.Single(root.Elements("Airport"));Assert.Empty(root.Element("Airport")!.Elements("SceneryObject"));
        Assert.Equal("TRUE",nodes[0].Attribute("altitudeIsAgl")!.Value);Assert.Equal("TRUE",nodes[0].Attribute("snapToGround")!.Value);
        Assert.Equal("0",nodes[0].Attribute("alt")!.Value);Assert.Equal("90",nodes[0].Attribute("heading")!.Value);
        Assert.Equal("{FD8AA200-1485-46C7-BA8F-B9B69C31E0F7}",nodes[0].Element("LibraryObject")!.Attribute("name")!.Value);
        Assert.Equal("1",nodes[0].Element("LibraryObject")!.Attribute("scale")!.Value);
        var foundations=root.Elements("Polygon").ToArray();Assert.Equal(2,foundations.Length);
        var foundation=foundations[0];Assert.Equal(Fixtures.Strip.AltitudeMeters.ToString("0.###############",CultureInfo.InvariantCulture),foundation.Attribute("altitude")!.Value);
        Assert.Equal("8",foundation.Elements("Attribute").Single(a=>(string?)a.Attribute("name")=="FlattenFalloff").Attribute("value")!.Value);
        Assert.Equal("0",foundation.Elements("Attribute").Single(a=>(string?)a.Attribute("name")=="FlattenMode").Attribute("value")!.Value);
        Assert.Equal("1",foundation.Elements("Attribute").Single(a=>(string?)a.Attribute("name")=="ForceElevation").Attribute("value")!.Value);
        Assert.Equal("1",foundation.Elements("Attribute").Single(a=>(string?)a.Attribute("name")=="ExcludeSecondaryHeightmap").Attribute("value")!.Value);
        Assert.Equal(4,foundation.Elements("Vertex").Count());
        Assert.Equal(new[]{"Airport","Polygon","SceneryObject","Polygon","SceneryObject"},root.Elements().Select(e=>e.Name.LocalName));
    }

    [Fact]
    public void EditorMovesRotatesDeletesMultipleObjectsAndUndoRedoRestoresThem()
    {
        var editor=new LayoutEditor(()=>true,()=>new(800,30,20));
        editor.AddObject(new(100,50),350);editor.AddObject(new(200,75),10);
        Assert.Equal(2,editor.Objects.Count);Assert.Equal(2,editor.SelectedObject!.Number);
        editor.MoveObject(2,new(700,-200));editor.Rotate(25);
        Assert.Equal(new LocalPoint(700,-200),new(editor.SelectedObject.EastMeters,editor.SelectedObject.NorthMeters));Assert.Equal(35,editor.SelectedObject.TrueHeadingDegrees);
        editor.Delete();Assert.Single(editor.Objects);editor.Undo();Assert.Equal(2,editor.Objects.Count);editor.Undo();editor.SelectObject(2);Assert.Equal(10,editor.SelectedObject!.TrueHeadingDegrees);
        editor.Redo();Assert.Equal(35,editor.SelectedObject!.TrueHeadingDegrees);editor.SelectObject(1);editor.Delete();Assert.Single(editor.Objects);Assert.Equal(2,editor.Objects[0].Number);
    }

    [Fact]
    public async Task ObjectPersistsAcrossAppInstancesAndAnEditInvalidatesBuiltAuthority()
    {
        var service=BuilderServices.CreateDryRun();var vm=new BuilderViewModel(service,"");
        await vm.CheckAsync();await vm.CaptureAsync();vm.AirportName="Object persistence";vm.Identifier="FPOBJ1";vm.SiteConfirmed=true;
        vm.Layout.AddObject(new(650,125),123);await vm.SaveAirportAsync();var saved=Assert.Single(vm.Airports);
        var reopened=new BuilderViewModel(service.ReopenDryRun(),"");await reopened.RefreshAirportsAsync();await reopened.LoadAirportAsync(saved.Id);
        var loaded=Assert.Single(reopened.Layout.Objects);Assert.Equal(650,loaded.EastMeters);Assert.Equal(125,loaded.NorthMeters);Assert.Equal(123,loaded.TrueHeadingDegrees);Assert.False(reopened.HasUnsavedAirport);
        reopened.SiteConfirmed=true;await reopened.ValidateAsync();await reopened.BuildAsync();Assert.Equal(WorkflowState.Built,reopened.State);
        reopened.Layout.SelectObject(1);reopened.Layout.Rotate(15);Assert.Equal(WorkflowState.SurveyCaptured,reopened.State);Assert.Null(reopened.Built);Assert.True(reopened.HasUnsavedAirport);
        await reopened.ValidateAsync();await reopened.BuildAsync();
        var xml=XDocument.Load(Path.Combine(reopened.Built!.ProjectDirectory,"PackageSources","data","airport.xml"));Assert.Single(xml.Descendants("LibraryObject"));
    }

    [Fact]
    public async Task LayoutFileRoundTripIncludesObjectsAndOldFilesWithoutObjectsRemainCompatible()
    {
        var anchor=new SurveyPoint(1,2,3,4,3,0,5);
        var item=new PlacedSceneryObject(1,SceneryAssets.GenericHangar56Id,40,50,60);
        var path=Path.Combine(Root,"layout.json");await LayoutFiles.SaveNewAsync(path,new(1,anchor,200,20,[]){PlacedObjects=[item]});
        Assert.Equal(item,Assert.Single((await LayoutFiles.ReadAsync(path)).PlacedObjects));
        var legacy=Path.Combine(Root,"legacy.json");
        var json=JsonSerializer.Serialize(new LayoutSnapshot(1,anchor,200,20,[]));
        await File.WriteAllTextAsync(legacy,json);Assert.Empty((await LayoutFiles.ReadAsync(legacy)).PlacedObjects);
    }

    [Fact]
    public void ExistingSavedDefinitionsWithoutPlacedObjectsLoadAsEmpty()
    {
        var json=JsonSerializer.SerializeToNode(Fixtures.Strip)!.AsObject();
        Assert.True(json.Remove("PlacedObjects"));
        var loaded=json.Deserialize<AirstripDefinition>()!;
        Assert.Empty(loaded.PlacedObjects);Assert.Empty(loaded.Validate());
    }
}
