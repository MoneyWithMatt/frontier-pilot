namespace FrontierAirportBuilder.Core;

/// <summary>A library asset placed in local east/north metres from the surveyed airport anchor.</summary>
public sealed record PlacedSceneryObject(
    int Number,
    string AssetId,
    double EastMeters,
    double NorthMeters,
    double TrueHeadingDegrees);

public sealed record SceneryObjectPlacement(PlacedSceneryObject Object, GeoPoint Position, double TrueHeadingDegrees);

/// <summary>Model-local metre bounds relative to the MSFS placement origin. Right maps to
/// glTF X and forward maps to glTF Z before applying the true heading.</summary>
public sealed record SceneryFootprint(double MinimumRightMeters, double MaximumRightMeters,
    double MinimumForwardMeters, double MaximumForwardMeters)
{
    public double WidthMeters => MaximumRightMeters-MinimumRightMeters;
    public double DepthMeters => MaximumForwardMeters-MinimumForwardMeters;
    public double CentreRightMeters => (MinimumRightMeters+MaximumRightMeters)/2;
    public double CentreForwardMeters => (MinimumForwardMeters+MaximumForwardMeters)/2;
    public double FurthestCornerMeters => new[]{
        Math.Sqrt(MinimumRightMeters*MinimumRightMeters+MinimumForwardMeters*MinimumForwardMeters),
        Math.Sqrt(MinimumRightMeters*MinimumRightMeters+MaximumForwardMeters*MaximumForwardMeters),
        Math.Sqrt(MaximumRightMeters*MaximumRightMeters+MinimumForwardMeters*MinimumForwardMeters),
        Math.Sqrt(MaximumRightMeters*MaximumRightMeters+MaximumForwardMeters*MaximumForwardMeters)}.Max();
}

public sealed record TerrainFoundation(double MarginMeters,double FalloffMeters,bool ExcludeSecondaryHeightmap=true);

/// <summary>The deliberately small prototype catalogue. Asset IDs are Frontier-owned stable keys;
/// LibraryGuid is the MSFS library model reference emitted into scenery XML.</summary>
public sealed record SceneryAsset(string Id,string DisplayName,Guid LibraryGuid,SceneryFootprint Footprint,
    TerrainFoundation? Foundation,string DimensionsProvenance)
{
    public double SiteRadiusMeters=>Footprint.FurthestCornerMeters+(Foundation?.MarginMeters??0)+(Foundation?.FalloffMeters??0);
}

public static class SceneryAssets
{
    public const string GenericHangar56Id = "asobo-generic-hangar-56";
    public static readonly SceneryAsset GenericHangar56 = new(
        GenericHangar56Id,
        "Generic hangar (Gen_Hangar56)",
        new Guid("FD8AA200-1485-46C7-BA8F-B9B69C31E0F7"),
        // Full LOD0 bounds after applying all glTF node transforms. The lowest Y is
        // effectively zero; only the horizontal X/Z bounds are needed for placement.
        new(-25.453735478515625,20.728942355860518,-49.35806079101563,45.04666333007812),
        new(2,8),
        "Installed generic.BGL Gen_Hangar56 LOD0 glTF accessor bounds and node transforms; SHA-256 0185952D0B0D73C231DBD8C0DE65404F4B86FC32FE58664DC6D91AED93D261D3");

    public static IReadOnlyList<SceneryAsset> All { get; } = [GenericHangar56];
    public static SceneryAsset? Find(string? id) => All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
}

public static class SceneryObjectGeometry
{
    public static LocalPoint TransformModelPoint(PlacedSceneryObject item,double rightMeters,double forwardMeters)
    {
        var radians=Headings.Normalize(item.TrueHeadingDegrees)*Math.PI/180;
        var sin=Math.Sin(radians);var cos=Math.Cos(radians);
        return new(item.EastMeters+rightMeters*cos+forwardMeters*sin,
            item.NorthMeters-rightMeters*sin+forwardMeters*cos);
    }

    public static IReadOnlyList<LocalPoint> FootprintCorners(PlacedSceneryObject item,SceneryAsset asset,double marginMeters=0)
    {
        if(!double.IsFinite(marginMeters)||marginMeters<0)throw new ArgumentOutOfRangeException(nameof(marginMeters));
        var f=asset.Footprint;
        return [
            TransformModelPoint(item,f.MinimumRightMeters-marginMeters,f.MinimumForwardMeters-marginMeters),
            TransformModelPoint(item,f.MaximumRightMeters+marginMeters,f.MinimumForwardMeters-marginMeters),
            TransformModelPoint(item,f.MaximumRightMeters+marginMeters,f.MaximumForwardMeters+marginMeters),
            TransformModelPoint(item,f.MinimumRightMeters-marginMeters,f.MaximumForwardMeters+marginMeters)];
    }

    public static bool ContainsFootprint(PlacedSceneryObject item,SceneryAsset asset,LocalPoint point,double toleranceMeters=0)
    {
        var radians=Headings.Normalize(item.TrueHeadingDegrees)*Math.PI/180;
        var sin=Math.Sin(radians);var cos=Math.Cos(radians);
        var east=point.East-item.EastMeters;var north=point.North-item.NorthMeters;
        var right=east*cos-north*sin;var forward=east*sin+north*cos;var f=asset.Footprint;
        return right>=f.MinimumRightMeters-toleranceMeters&&right<=f.MaximumRightMeters+toleranceMeters&&
            forward>=f.MinimumForwardMeters-toleranceMeters&&forward<=f.MaximumForwardMeters+toleranceMeters;
    }

    public static SceneryObjectPlacement Calculate(AirstripDefinition airport, PlacedSceneryObject item)
    {
        var position = LocalCoordinates.ToGeographic(
            new(airport.Latitude, airport.Longitude),
            new(item.EastMeters, item.NorthMeters));
        return new(item, position, Headings.Normalize(item.TrueHeadingDegrees));
    }

    public static IReadOnlyList<string> Validate(IReadOnlyList<PlacedSceneryObject>? objects, AirportSite site)
    {
        if (objects is null) return ["Placed scenery objects cannot be null."];
        var errors = new List<string>();
        if (objects.Count > 64) errors.Add("This prototype supports up to 64 placed scenery objects.");
        var numbers = new HashSet<int>();
        foreach (var item in objects)
        {
            if (item is null) { errors.Add("Placed scenery object is missing."); continue; }
            if (item.Number is < 1 or > 999 || !numbers.Add(item.Number))
                errors.Add("Scenery object numbers must be unique, from 1 to 999.");
            var asset = SceneryAssets.Find(item.AssetId);
            if (asset is null) errors.Add($"Scenery object {item.Number}: unsupported asset identifier '{item.AssetId}'.");
            if (new[] { item.EastMeters, item.NorthMeters, item.TrueHeadingDegrees }.Any(v => !double.IsFinite(v)))
            {
                errors.Add($"Scenery object {item.Number}: position and heading must be finite.");
                continue;
            }
            if (asset is not null && !site.Contains(new(item.EastMeters, item.NorthMeters), asset.SiteRadiusMeters))
                errors.Add($"Scenery object {item.Number}: {AirportSite.BoundaryMessage}");
        }
        return errors;
    }
}
