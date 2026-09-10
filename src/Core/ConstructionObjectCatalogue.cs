namespace FrontierAirportBuilder.Core;

public enum ConstructionObjectCategory
{
    BaseLodge, StorageShed, StaffAccommodation, Warehouse, Workshop, FuelTank, WaterTank, AirfieldMarker
}

public enum ConstructionObjectAvailability { PrototypePlaceholder, Unavailable }
public enum ConstructionCapability { BaseLodge }

public sealed record ConstructionObjectDefinition(string Id, ConstructionObjectCategory Category,
    string DisplayName, ConstructionObjectAvailability Availability, string? SceneryAssetId, string Notes)
{
    public IReadOnlyList<ConstructionCapability> Capabilities { get; init; } = [];
    public bool CanPlace => Availability == ConstructionObjectAvailability.PrototypePlaceholder;
    public ConstructionObjectDefinition EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(Notes) ||
            !Enum.IsDefined(Category) || !Enum.IsDefined(Availability))
            throw new InvalidDataException("Construction-object catalogue metadata is invalid.");
        var asset = SceneryAssets.Find(SceneryAssetId);
        if (CanPlace && (asset is null || asset.ConstructionCategory != Category || !asset.IsPrototypePlaceholder))
            throw new InvalidDataException($"Placeable construction object '{Id}' does not resolve to its prototype scenery asset.");
        if (!CanPlace && SceneryAssetId is not null)
            throw new InvalidDataException($"Unavailable construction object '{Id}' cannot reference a scenery asset.");
        if (Capabilities is null || Capabilities.Distinct().Count() != Capabilities.Count)
            throw new InvalidDataException($"Construction object '{Id}' has invalid capabilities.");
        return this;
    }
}

public static class ConstructionObjectCatalogue
{
    public const string BaseLodgeId = "construction.base-lodge";
    public const string StorageShedId = "construction.storage-shed";
    public const string StaffAccommodationId = "construction.staff-accommodation";
    public const string WarehouseId = "construction.warehouse";
    public const string WorkshopId = "construction.workshop";
    public const string FuelTankId = "construction.fuel-tank";
    public const string WaterTankId = "construction.water-tank";
    public const string AirfieldMarkerId = "construction.airfield-marker";

    public static IReadOnlyList<ConstructionObjectDefinition> All { get; } =
    [
        new ConstructionObjectDefinition(BaseLodgeId, ConstructionObjectCategory.BaseLodge, "Prototype Base Lodge placeholder",
            ConstructionObjectAvailability.PrototypePlaceholder, SceneryAssets.PrototypeBaseLodgeId,
            "Uses stock Gen_Hangar56 only as a technical placeholder; no lodge gameplay or final artwork.") { Capabilities = [ConstructionCapability.BaseLodge] },
        Unavailable(StorageShedId, ConstructionObjectCategory.StorageShed, "Storage Shed"),
        Unavailable(StaffAccommodationId, ConstructionObjectCategory.StaffAccommodation, "Staff Accommodation"),
        Unavailable(WarehouseId, ConstructionObjectCategory.Warehouse, "Warehouse"),
        Unavailable(WorkshopId, ConstructionObjectCategory.Workshop, "Workshop"),
        Unavailable(FuelTankId, ConstructionObjectCategory.FuelTank, "Fuel Tank"),
        Unavailable(WaterTankId, ConstructionObjectCategory.WaterTank, "Water Tank"),
        Unavailable(AirfieldMarkerId, ConstructionObjectCategory.AirfieldMarker, "Airfield Marker")
    ];

    static ConstructionObjectCatalogue()
    {
        foreach (var item in All) item.EnsureValid();
        if (All.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != All.Count ||
            All.Select(x => x.Category).Distinct().Count() != Enum.GetValues<ConstructionObjectCategory>().Length)
            throw new InvalidDataException("Construction-object catalogue IDs and categories must be unique and complete.");
    }

    public static ConstructionObjectDefinition Get(string id) => All.Single(x => x.Id == id);
    public static bool AssetProvides(string assetId, ConstructionCapability capability) =>
        All.Any(x => x.SceneryAssetId == assetId && x.Capabilities.Contains(capability));
    private static ConstructionObjectDefinition Unavailable(string id, ConstructionObjectCategory category, string name) =>
        new(id, category, name, ConstructionObjectAvailability.Unavailable, null,
            "No reliable SDK-native reference or Frontier-owned production asset is established in V0.");
}
