using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Wpf;
using NetTopologySuite.Geometries;

namespace FrontierAirportBuilder.Desktop;

/// <summary>Existing Mapsui/OSM presentation only. Coordinates come from job snapshots, not guessed airport positions.</summary>
public sealed class CareerJobMap : MapControl
{
    private readonly MemoryLayer routeLayer = new("Career assignment route");
    private string stamp = "";
    private MRect? bounds;
    public string? DisplayedJobKey { get; private set; }
    public int RoutePointCount { get; private set; }
    public bool HasObservationArea { get; private set; }
    public CareerJobMap()
    {
        Map = new Map { CRS = "EPSG:3857" };
        Map.Layers.Add(OpenStreetMap.CreateTileLayer("FrontierPilotPrototype/0.1")); Map.Layers.Add(routeLayer);
        Loaded += (_, _) => Fit();
    }
    public void Show(CareerJobRow? job)
    {
        var next = job?.Key + ":" + string.Join(";", job?.Route.Select(p => $"{p.Latitude:R},{p.Longitude:R}") ?? []) + ":" + job?.AreaRadiusNauticalMiles;
        if (next == stamp) return; stamp = next; DisplayedJobKey = job?.Key;
        RoutePointCount = job?.Route.Count ?? 0; HasObservationArea = false;
        var features = new List<IFeature>(); var coordinates = new List<Coordinate>();
        foreach (var p in job?.Route ?? [])
        {
            var xy = SphericalMercator.FromLonLat(p.Longitude, Math.Clamp(p.Latitude, -85.05112878, 85.05112878));
            coordinates.Add(new(xy.x, xy.y));
            var marker = new PointFeature(new MPoint(xy.x, xy.y)); marker["label"] = p.Name;
            marker.Styles.Add(new SymbolStyle { Fill = new Brush(new Color(83, 201, 155)), Outline = new Pen(Color.White, 2), SymbolScale = .8 });
            marker.Styles.Add(new LabelStyle { LabelColumn = "label", ForeColor = Color.White, BackColor = new Brush(new Color(26, 39, 50, 220)), Offset = new Offset(0, 18) });
            features.Add(marker);
        }
        if (coordinates.Count > 1)
        {
            var line = new GeometryFeature { Geometry = new LineString(coordinates.ToArray()) };
            line.Styles.Add(new VectorStyle { Line = new Pen(new Color(83, 201, 155), 3) }); features.Insert(0, line);
        }
        if (job is { AreaRadiusNauticalMiles: {} radius, Route.Count: >= 2 })
        {
            var centre = job.Route[1]; var ring = new List<Coordinate>();
            for (int bearing = 0; bearing <= 360; bearing += 10)
            {
                var point = new GeoPoint(centre.Latitude, centre.Longitude); double remaining = radius * 1852, direction = bearing;
                while (remaining > 0) { var distance = Math.Min(remaining, Geodesy.MaximumDistanceMeters); var d = Geodesy.Direct(point, direction, distance); point = d.Position; direction = d.ForwardBearingDegrees; remaining -= distance; }
                var xy = SphericalMercator.FromLonLat(point.Longitude, Math.Clamp(point.Latitude, -85.05112878, 85.05112878)); ring.Add(new(xy.x, xy.y));
            }
            ring[^1] = ring[0];
            var area = new GeometryFeature { Geometry = new Polygon(new LinearRing(ring.ToArray())) };
            area.Styles.Add(new VectorStyle { Fill = new Brush(new Color(83, 201, 155, 45)), Outline = new Pen(new Color(83, 201, 155), 2) });
            features.Insert(0, area); coordinates.AddRange(ring); HasObservationArea = true;
        }
        routeLayer.Features = features; Map.RefreshGraphics();
        bounds = coordinates.Count == 0 ? null : new(coordinates.Min(p => p.X) - 1500, coordinates.Min(p => p.Y) - 1500, coordinates.Max(p => p.X) + 1500, coordinates.Max(p => p.Y) + 1500);
        Fit();
    }
    public void Fit() { if (bounds is not null) Map.Navigator.ZoomToBox(bounds); }
}
