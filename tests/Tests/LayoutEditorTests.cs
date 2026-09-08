using System.Text.Json;
using System.Xml.Linq;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
namespace FrontierAirportBuilder.Tests;

public sealed class LayoutEditorTests
{
    [Theory][InlineData(0)][InlineData(90)][InlineData(108.6)][InlineData(270)]
    public void RunwayTransformRoundTrips(double heading)
    {
        var local=LocalCoordinates.FromRunway(35,-42,heading);
        var back=LocalCoordinates.ToRunway(local,heading);
        Assert.Equal(35,back.East,10);Assert.Equal(-42,back.North,10);
    }
    [Fact] public void GeographicOffsetsAreIndependentOfRunwayHeading()
    {
        var anchor=new GeoPoint(0,0);
        var east=LocalCoordinates.ToGeographic(anchor,new(100,0));
        var north=LocalCoordinates.ToGeographic(anchor,new(0,100));
        Assert.Equal(100/111319.490793,east.Longitude,9);Assert.Equal(0,east.Latitude,9);
        Assert.Equal(100/110574.2758,north.Latitude,8);Assert.Equal(0,north.Longitude,9);
    }
    [Fact] public void AntimeridianPlacementNormalizesLongitude()
    {
        var p=LocalCoordinates.ToGeographic(new(0,179.9999),new(100,0));
        Assert.InRange(p.Longitude,-180,-179.99);
    }
    [Fact] public void ZoomAndPanDoNotChangeGeographicPlacement()
    {
        var view=new LayoutViewport();var point=new LocalPoint(35,-80);
        var pixel=view.ToScreen(point,600,430);
        Assert.Equal(point,view.ToLocal(pixel.X,pixel.Y,600,430));
        view.Pan(73,-15);view.Zoom(4,150,170,600,430);
        pixel=view.ToScreen(point,600,430);
        var recovered=view.ToLocal(pixel.X,pixel.Y,600,430);
        Assert.Equal(point.East,recovered.East,10);Assert.Equal(point.North,recovered.North,10);
        var anchor=new GeoPoint(-22,14);
        Assert.Equal(LocalCoordinates.ToGeographic(anchor,point),LocalCoordinates.ToGeographic(anchor,recovered));
    }
    [Fact] public void ZoomKeepsCursorWorldPointFixed()
    {
        var view=new LayoutViewport();var before=view.ToLocal(105,210,600,430);
        view.Zoom(2,105,210,600,430);
        Assert.Equal(before,view.ToLocal(105,210,600,430));
    }
    [Fact] public void HistoryRestoresPlacementMoveRotationTypeNameAndDeletion()
    {
        var e=new LayoutEditor(()=>true);e.Add(new(35,0),350);
        var original=e.Stands[0];
        e.Move(1,new(60,40));e.Rotate(30);Assert.Equal(20,e.Stands[0].TrueHeadingDegrees);
        e.Type=ParkingStandType.RAMP_GA_MEDIUM;e.Name=ParkingStandName.N_PARKING;e.Apply();
        var final=e.Stands[0];e.Delete();Assert.Empty(e.Stands);
        e.Undo();Assert.Equal(final,e.Stands[0]);e.Undo();e.Undo();e.Undo();Assert.Equal(original,e.Stands[0]);
        e.Undo();Assert.Empty(e.Stands);
        for(int i=0;i<5;i++)e.Redo();Assert.Empty(e.Stands);
    }
    [Fact] public void NewActionAfterUndoDiscardsRedo()
    {
        var e=new LayoutEditor(()=>true);e.Add(new(35,0),0);e.Move(1,new(50,0));e.Undo();
        e.Rotate(90);Assert.False(e.CanRedo);Assert.Equal(35,e.Stands[0].EastMeters);
    }
    [Fact] public void DisabledEditorCannotMutateOrUndo()
    {
        bool enabled=true;var e=new LayoutEditor(()=>enabled);e.Add(new(35,0),0);
        enabled=false;e.Move(1,new(50,0));e.Delete();e.Undo();e.Rotate(90);e.Add(new(60,0),0);
        Assert.Single(e.Stands);Assert.Equal(35,e.Stands[0].EastMeters);Assert.False(e.CanUndo);
    }
    [Fact] public void DirtyStateTracksSavedSnapshotAndUndo()
    {
        var e=new LayoutEditor(()=>true);e.Add(new(35,0),0);e.MarkSaved(e.Stands);
        Assert.False(e.IsDirty);e.Move(1,new(40,0));Assert.True(e.IsDirty);e.Undo();Assert.False(e.IsDirty);
    }
}
[Collection("Community installation")]
public sealed class LayoutWorkflowTests : TemporaryDirectoryTest
{
    private static async Task<BuilderViewModel> Ready()
    {
        var vm=new BuilderViewModel(BuilderServices.CreateDryRun(),"");
        await vm.CheckAsync();await vm.CaptureAsync();vm.AirportName="Layout test";vm.Identifier="FPLAY01";vm.SiteConfirmed=true;
        vm.AddParkingCommand.Execute(null);return vm;
    }
    [Fact] public async Task VisuallyPlacedCoordinatesReachXmlAndSaveLoadUndoInvalidateBuilds()
    {
        var vm=await Ready();var anchor=vm.Survey;
        // Canvas uses the same viewport conversion followed by this editor operation.
        var viewport=new LayoutViewport();viewport.Zoom(3,200,200,600,430);
        var target=LocalCoordinates.FromRunway(60,30,vm.LayoutRunwayHeading);
        var screen=viewport.ToScreen(target,600,430);
        vm.Layout.Move(1,viewport.ToLocal(screen.X,screen.Y,600,430));
        vm.Layout.Heading="420";vm.Layout.Type=ParkingStandType.RAMP_GA_MEDIUM;vm.Layout.Name=ParkingStandName.N_PARKING;vm.Layout.Apply();
        var savedStand=vm.Layout.Stands[0];
        var path=Path.Combine(Root,"layout.json");await vm.SaveLayoutAsync(path);Assert.False(vm.Layout.IsDirty);
        await vm.ValidateAsync();await vm.BuildAsync();Assert.Equal(WorkflowState.Built,vm.State);
        var xml=XDocument.Load(Path.Combine(vm.Built!.ProjectDirectory,"PackageSources","data","airport.xml"));
        var parking=xml.Descendants("TaxiwayParking").Single();
        var geo=LocalCoordinates.ToGeographic(new(anchor!.Latitude,anchor.Longitude),new(savedStand.EastMeters,savedStand.NorthMeters));
        Assert.Equal(geo.Latitude,(double)parking.Attribute("lat")!,10);
        Assert.Equal(geo.Longitude,(double)parking.Attribute("lon")!,10);
        Assert.Equal("60",(string?)parking.Attribute("heading"));
        Assert.Equal("N_PARKING",(string?)parking.Attribute("name"));Assert.Equal("RAMP_GA_MEDIUM",(string?)parking.Attribute("type"));
        await vm.InstallAsync();Assert.Equal(WorkflowState.Installed,vm.State);
        vm.Layout.Delete();Assert.Null(vm.Built);Assert.Equal("",vm.InstalledPath);Assert.True(vm.Layout.IsDirty);
        vm.Layout.Undo();Assert.Equal(savedStand,vm.Layout.Stands[0]);Assert.False(vm.BuildCommand.CanExecute(null));
        vm.Layout.Rotate(15);await vm.LoadLayoutAsync(path);Assert.Equal(savedStand,vm.Layout.Stands[0]);Assert.Equal(anchor,vm.Survey);
        Assert.Equal(WorkflowState.SurveyCaptured,vm.State);
        var evidence=Environment.GetEnvironmentVariable("FRONTIER_LAYOUT_EVIDENCE");
        if(evidence!=null) {File.Copy(path,Path.Combine(evidence,"layout.json"),true);File.WriteAllText(Path.Combine(evidence,"airport.xml"),xml.ToString());}
    }
    [Fact] public async Task UnappliedPropertiesBlockValidationAndSavingDoesNotOverwrite()
    {
        var vm=await Ready();await vm.ValidateAsync();vm.Layout.East="oops";
        Assert.False(vm.BuildCommand.CanExecute(null));Assert.False(vm.ValidateCommand.CanExecute(null));vm.Layout.Apply();Assert.NotEmpty(vm.Layout.Message);
        vm.Layout.Select(1);var path=Path.Combine(Root,"layout.json");await vm.SaveLayoutAsync(path);
        var before=File.ReadAllText(path);await vm.SaveLayoutAsync(path);Assert.Equal(before,File.ReadAllText(path));Assert.Equal(WorkflowState.Error,vm.State);
    }
    [Fact] public async Task InvalidNumericDraftCannotEscapeSaveErrorHandling()
    {
        var vm=await Ready();vm.Length="invalid";
        var path=Path.Combine(Root,"invalid.json");
        await vm.SaveLayoutAsync(path);Assert.Equal(WorkflowState.Error,vm.State);Assert.False(File.Exists(path));
        vm.Length="200";vm.Layout.East="oops";
        await vm.SaveLayoutAsync(path);Assert.Equal(WorkflowState.Error,vm.State);Assert.False(File.Exists(path));
    }
    [Fact] public async Task LayoutFromDifferentAnchorIsRefusedWithoutChangingStands()
    {
        var vm=await Ready();var before=vm.Layout.Stands.ToArray();
        var path=Path.Combine(Root,"wrong.json");
        await LayoutFiles.SaveNewAsync(path,new(1,vm.Survey! with { Latitude=0 },200,20,[]));
        await vm.LoadLayoutAsync(path);Assert.Equal(WorkflowState.Error,vm.State);Assert.Equal(before,vm.Layout.Stands);
    }
}

