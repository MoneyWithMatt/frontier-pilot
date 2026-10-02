using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Projections;
using Mapsui.Tiling;
using Mapsui.UI.Wpf;

namespace FrontierAirportBuilder.Desktop;

/// <summary>Georeferenced basemap beneath the metre-accurate authoring overlay. The workflow
/// continues to store surveyed WGS84 coordinates and local metre offsets; map pixels are view-only.</summary>
public sealed class SiteLayoutMap : Grid
{
    private readonly MapControl mapControl;
    private readonly AirportLayoutCanvas editor;
    private BuilderViewModel? model;

    public SiteLayoutMap()
    {
        ClipToBounds=true;
        var map=new Map {CRS="EPSG:3857"};
        map.Layers.Add(OpenStreetMap.CreateTileLayer("FrontierPilotPrototype/0.1"));
        mapControl=new MapControl {Map=map,IsHitTestVisible=false};
        editor=new AirportLayoutCanvas();
        Children.Add(mapControl);Children.Add(editor);
        editor.ViewChanged+=(_,_)=>SynchronizeMap();
        DataContextChanged+=OnDataContextChanged;
        SizeChanged+=(_,_)=>SynchronizeMap();
    }

    public void Fit()=>editor.Fit();

    private void OnDataContextChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        if(model!=null)model.PropertyChanged-=ModelChanged;
        model=e.NewValue as BuilderViewModel;
        if(model!=null)model.PropertyChanged+=ModelChanged;
        SynchronizeMap();
    }

    private void ModelChanged(object? sender,PropertyChangedEventArgs e)=>SynchronizeMap();

    private void SynchronizeMap()
    {
        if(model?.Survey is not {} survey||ActualWidth<1||ActualHeight<1)return;
        var (localCentre,pixelsPerMetre)=editor.ViewState;
        if(!double.IsFinite(pixelsPerMetre)||pixelsPerMetre<=0)return;
        var geographic=LocalCoordinates.ToGeographic(new(survey.Latitude,survey.Longitude),localCentre);
        var projected=SphericalMercator.FromLonLat(geographic.Longitude,Math.Clamp(geographic.Latitude,-85.05112878,85.05112878)).ToMPoint();
        var cosine=Math.Max(.01,Math.Cos(geographic.Latitude*Math.PI/180));
        mapControl.Map.Navigator.CenterOnAndZoomTo(projected,1/(pixelsPerMetre*cosine));
    }
}
