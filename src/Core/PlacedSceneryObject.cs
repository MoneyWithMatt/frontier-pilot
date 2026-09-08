namespace FrontierAirportBuilder.Core;

/// <summary>A library asset placed in local east/north metres from the surveyed airport anchor.</summary>
public sealed record PlacedSceneryObject(
    int Number,
    string AssetId,
    double EastMeters,
    double NorthMeters,
    double TrueHeadingDegrees);

public sealed record SceneryObjectPlacement(PlacedSceneryObject Object, GeoPoint Position, double TrueHeadingDegrees);

/// <summary>The deliberately small prototype catalogue. Asset IDs are Frontier-owned stable keys;
/// LibraryGuid is the MSFS library model reference emitted into scenery XML.</summary>
public sealed record SceneryAsset(string Id, string DisplayName, Guid LibraryGuid, double PreviewWidthMeters, double PreviewLengthMeters)
{
    public double FootprintRadiusMeters => Math.Sqrt(PreviewWidthMeters * PreviewWidthMeters + PreviewLengthMeters * PreviewLengthMeters) / 2;
}

public static class SceneryAssets
{
    public const string GenericHangar56Id = "asobo-generic-hangar-56";
    public static readonly SceneryAsset GenericHangar56 = new(
        GenericHangar56Id,
        "Generic hangar (Gen_Hangar56)",
        new Guid("FD8AA200-1485-46C7-BA8F-B9B69C31E0F7"),
        32,
        24);

    public static IReadOnlyList<SceneryAsset> All { get; } = [GenericHangar56];
    public static SceneryAsset? Find(string? id) => All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
}

public static class SceneryObjectGeometry
{
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
            if (asset is not null && !site.Contains(new(item.EastMeters, item.NorthMeters), asset.FootprintRadiusMeters))
                errors.Add($"Scenery object {item.Number}: {AirportSite.BoundaryMessage}");
        }
        return errors;
    }
}
