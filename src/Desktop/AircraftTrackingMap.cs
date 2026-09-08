using FrontierAirportBuilder.Core;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Wpf;

namespace FrontierAirportBuilder.Desktop;

/// <summary>The map view owns projection and rendering only. Aircraft and player world state
/// remain separate in TrackingViewModel and ITrackingTelemetrySource.</summary>
public sealed class AircraftTrackingMap : MapControl
{
    private readonly MemoryLayer aircraftLayer;
    private readonly MemoryLayer playerLayer;
    private MPoint? aircraftPosition;
    private MPoint? playerPosition;
    private bool initiallyCentered;
    private bool lastWasLive;

    public AircraftTrackingMap()
    {
        var map = new Map { CRS = "EPSG:3857" };
        map.Layers.Add(OpenStreetMap.CreateTileLayer("FrontierPilotPrototype/0.1"));
        aircraftLayer = new MemoryLayer("User aircraft");
        map.Layers.Add(aircraftLayer);
        playerLayer = new MemoryLayer("Walking player");
        map.Layers.Add(playerLayer);
        Map = map;
    }

    public void Show(AircraftTelemetry? telemetry, PlayerTelemetry? player, bool isLive)
    {
        if (telemetry is null)
        {
            aircraftPosition = null; playerPosition = null;
            initiallyCentered = false; lastWasLive = false;
            aircraftLayer.Features = []; playerLayer.Features = [];
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
        if (player is not null)
        {
            playerPosition = Project(player.Longitude, player.Latitude);
            var playerMarker = new PointFeature(playerPosition);
            playerMarker.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = .72,
                Fill = new Brush(new Color(38, 112, 176)),
                Outline = new Pen(Color.White, 3)
            });
            playerMarker.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Triangle,
                SymbolScale = .32,
                SymbolRotation = player.TrueHeadingDegrees,
                Fill = new Brush(Color.White),
                Outline = new Pen(new Color(32, 60, 53), 1)
            });
            playerLayer.Features = [playerMarker];
        }
        else
        {
            playerPosition = null;
            playerLayer.Features = [];
        }
        Map.RefreshGraphics();
        bool shouldCenter = !initiallyCentered || isLive && !lastWasLive;
        lastWasLive = isLive;
        if (shouldCenter)
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

    public bool CenterOnPlayer()
    {
        if (playerPosition is null) return false;
        Map.Navigator.CenterOn(playerPosition, 250);
        return true;
    }

    private static MPoint Project(double longitude, double latitude) =>
        SphericalMercator.FromLonLat(longitude, Math.Clamp(latitude, -85.05112878, 85.05112878)).ToMPoint();
}
