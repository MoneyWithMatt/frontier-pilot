using FrontierAirportBuilder.Core;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Wpf;

namespace FrontierAirportBuilder.Desktop;

/// <summary>The map view owns projection and rendering only. Aircraft acquisition and state
/// remain in TrackingViewModel and IAircraftTelemetrySource.</summary>
public sealed class AircraftTrackingMap : MapControl
{
    private readonly MemoryLayer aircraftLayer;
    private MPoint? aircraftPosition;
    private bool initiallyCentered;

    public AircraftTrackingMap()
    {
        var map = new Map { CRS = "EPSG:3857" };
        map.Layers.Add(OpenStreetMap.CreateTileLayer("FrontierPilotPrototype/0.1"));
        aircraftLayer = new MemoryLayer("User aircraft");
        map.Layers.Add(aircraftLayer);
        Map = map;
    }

    public void ShowAircraft(AircraftTelemetry? telemetry)
    {
        if (telemetry is null)
        {
            aircraftPosition = null;
            initiallyCentered = false;
            aircraftLayer.Features = [];
            Map.RefreshGraphics();
            return;
        }

        // Web Mercator has no finite representation at the poles. Preserve the real latitude
        // in telemetry while placing the marker at the nearest representable map coordinate.
        var mapLatitude = Math.Clamp(telemetry.Latitude, -85.05112878, 85.05112878);
        aircraftPosition = SphericalMercator.FromLonLat(telemetry.Longitude, mapLatitude).ToMPoint();
        var marker = new PointFeature(aircraftPosition);
        marker.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Triangle,
            SymbolScale = 1.05,
            SymbolRotation = telemetry.TrueHeadingDegrees,
            Fill = new Brush(new Color(224, 117, 45)),
            Outline = new Pen(new Color(32, 60, 53), 2)
        });
        marker.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.22,
            Fill = new Brush(Color.White),
            Outline = new Pen(new Color(32, 60, 53), 1)
        });
        aircraftLayer.Features = [marker];
        Map.RefreshGraphics();
        if (!initiallyCentered)
        {
            initiallyCentered = true;
            Map.Navigator.CenterOnAndZoomTo(aircraftPosition, Map.Navigator.Resolutions[15]);
        }
    }

    public bool CenterOnAircraft()
    {
        if (aircraftPosition is null) return false;
        Map.Navigator.CenterOn(aircraftPosition, 250);
        return true;
    }
}
