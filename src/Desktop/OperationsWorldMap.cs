using FrontierAirportBuilder.Workflow;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Wpf;

namespace FrontierAirportBuilder.Desktop;

/// <summary>Operations-map presentation adapter. Economy and location selection remain in the
/// workflow layer; this control can be replaced with another tile provider without changing them.</summary>
public sealed class OperationsWorldMap : MapControl
{
    private readonly MemoryLayer locationsLayer;
    public event Action<string>? LocationSelected;

    public OperationsWorldMap()
    {
        var map = new Map { CRS = "EPSG:3857" };
        map.Layers.Add(OpenStreetMap.CreateTileLayer("FrontierPilotPrototype/0.1"));
        locationsLayer = new MemoryLayer("Frontier world locations");
        map.Layers.Add(locationsLayer); Map = map;
        Map.Info += (_, args) =>
        {
            var feature = args.GetMapInfo([locationsLayer]).Feature;
            if (feature?["location-key"] is string key) LocationSelected?.Invoke(key);
        };
    }

    private bool centered;
    public void Show(IReadOnlyList<OperationsMapLocation> locations, bool recenter = true)
    {
        locationsLayer.Features = locations.Select(CreateFeature).ToArray();
        Map.RefreshGraphics();
        if (locations.Count == 0) return;
        if (centered && !recenter) return;
        centered = true;
        var centreLongitude = locations.Average(x => x.Longitude);
        var centreLatitude = locations.Average(x => x.Latitude);
        var centre = SphericalMercator.FromLonLat(centreLongitude, Math.Clamp(centreLatitude, -85.05112878, 85.05112878)).ToMPoint();
        Map.Navigator.CenterOnAndZoomTo(centre, Map.Navigator.Resolutions[7]);
    }

    public bool CenterOn(OperationsMapLocation? location)
    {
        if (location is null) return false;
        var point = SphericalMercator.FromLonLat(location.Longitude, Math.Clamp(location.Latitude, -85.05112878, 85.05112878)).ToMPoint();
        Map.Navigator.CenterOnAndZoomTo(point, Map.Navigator.Resolutions[11]); return true;
    }

    private static PointFeature CreateFeature(OperationsMapLocation location)
    {
        var point = SphericalMercator.FromLonLat(location.Longitude, Math.Clamp(location.Latitude, -85.05112878, 85.05112878)).ToMPoint();
        var feature = new PointFeature(point);
        feature["location-key"] = location.Key; feature["label"] = location.Identifier;
        var (fill, shape, scale) = location.Kind switch
        {
            OperationsMapLocationKind.Gateway => (new Color(224, 117, 45), SymbolType.Rectangle, 1.15),
            OperationsMapLocationKind.SystemLocation => (new Color(62, 104, 145), SymbolType.Ellipse, .72),
            OperationsMapLocationKind.PlayerAirport => (new Color(244, 194, 66), SymbolType.Triangle, .9),
            OperationsMapLocationKind.PlayerOperation => (new Color(45, 145, 91), SymbolType.Rectangle, 1.0),
            _ => (Color.Gray, SymbolType.Ellipse, .7)
        };
        feature.Styles.Add(new SymbolStyle { SymbolType = shape, SymbolScale = scale, Fill = new Brush(fill), Outline = new Pen(Color.White, 2) });
        feature.Styles.Add(new LabelStyle { LabelColumn = "label", ForeColor = new Color(32, 60, 53),
            BackColor = new Brush(new Color(255, 255, 255, 220)), Halo = new Pen(Color.White, 2),
            Offset = new Offset(0, 18), CollisionDetection = true });
        return feature;
    }
}
