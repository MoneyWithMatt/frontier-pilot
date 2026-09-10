using System.Text.RegularExpressions;

namespace FrontierAirportBuilder.Core;

public enum SurfacePresetAvailability { Native, TemporaryFallback, Unavailable }

public sealed record SurfaceColourAdjustment(double Red, double Green, double Blue)
{
    public void EnsureValid()
    {
        if (new[] { Red, Green, Blue }.Any(x => !double.IsFinite(x) || x < 0 || x > 2))
            throw new InvalidDataException("Surface colour multipliers must be finite and between 0 and 2.");
    }
}

public sealed record SurfaceMaterialLayer(string MaterialReference, string BlendType,
    double TilingU, double TilingV, double? FalloffMeters = null, byte Opacity = 255)
{
    public void EnsureValid()
    {
        if (!Guid.TryParse(MaterialReference, out var materialGuid) || materialGuid == Guid.Empty || string.IsNullOrWhiteSpace(BlendType) ||
            !double.IsFinite(TilingU) || TilingU <= 0 || !double.IsFinite(TilingV) || TilingV <= 0 ||
            FalloffMeters is { } falloff && (!double.IsFinite(falloff) || falloff < 0))
            throw new InvalidDataException("Surface material layer metadata is invalid.");
    }
}

/// <summary>Authored runway appearance metadata. SdkSurfaceReference is either a compiler-proven
/// native surface token or a GUID supplied by an explicit package material.</summary>
public sealed record RunwaySurfacePreset(string Id, string DisplayName, string? SdkSurfaceReference,
    SurfacePresetAvailability Availability, RunwaySurface PhysicalSurface, bool GroundMerging,
    bool DrawCentreLine, string Evidence, string? FallbackNote = null,
    SurfaceColourAdjustment? ColourAdjustment = null, double? TilingMeters = null,
    double? FalloffMeters = null, IReadOnlyList<SurfaceMaterialLayer>? AdditionalLayers = null)
{
    private static readonly HashSet<string> NativeReferences = new(StringComparer.Ordinal)
        { "ASPHALT", "DIRT", "GRASS", "SHORT_GRASS", "GRAVEL", "SAND" };

    public IReadOnlyList<SurfaceMaterialLayer> Layers => AdditionalLayers ?? [];
    public bool CanGenerate => Availability != SurfacePresetAvailability.Unavailable;
    public override string ToString() => DisplayName + (Availability == SurfacePresetAvailability.TemporaryFallback ? " · temporary fallback" : Availability == SurfacePresetAvailability.Unavailable ? " · unavailable" : "");

    public RunwaySurfacePreset EnsureValid()
    {
        if (!Regex.IsMatch(Id ?? "", "\\A[a-z][a-z0-9-]{2,63}\\z", RegexOptions.CultureInvariant) ||
            string.IsNullOrWhiteSpace(DisplayName) || string.IsNullOrWhiteSpace(Evidence))
            throw new InvalidDataException("Surface preset identity metadata is invalid.");
        if (!Enum.IsDefined(Availability) || !Enum.IsDefined(PhysicalSurface))
            throw new InvalidDataException("Surface preset enum metadata is invalid.");
        if (Availability == SurfacePresetAvailability.Unavailable)
        {
            if (SdkSurfaceReference is not null) throw new InvalidDataException("An unavailable surface preset cannot have a material reference.");
        }
        else if (SdkSurfaceReference is null || !(NativeReferences.Contains(SdkSurfaceReference) ||
            Guid.TryParse(SdkSurfaceReference, out var materialGuid) && materialGuid != Guid.Empty))
            throw new InvalidDataException($"Surface preset '{Id}' has an invalid material reference.");
        if (Availability == SurfacePresetAvailability.TemporaryFallback && string.IsNullOrWhiteSpace(FallbackNote))
            throw new InvalidDataException("A temporary surface fallback must explain the substitution.");
        ColourAdjustment?.EnsureValid();
        if (TilingMeters is { } tiling && (!double.IsFinite(tiling) || tiling <= 0) ||
            FalloffMeters is { } falloff && (!double.IsFinite(falloff) || falloff < 0))
            throw new InvalidDataException("Surface tiling or falloff metadata is invalid.");
        foreach (var layer in Layers) layer.EnsureValid();
        return this;
    }
}

public static class RunwaySurfacePresets
{
    public const string CompactedDirtId = "compacted-dirt";
    public const string SandyDirtId = "sandy-dirt";
    public const string GravelId = "gravel";
    public const string DryGrassId = "dry-grass";
    public const string GreenGrassId = "green-grass";
    public const string WeatheredAsphaltId = "weathered-asphalt";
    public const string ModernAsphaltId = "modern-asphalt";
    private const string Probe = "SDK 1.2.4 official surface identifier; disposable Package Tool matrix 2026-09-10 exit 0 with layout.json.";

    public static IReadOnlyList<RunwaySurfacePreset> All { get; } =
    [
        new(CompactedDirtId, "Compacted Dirt", "DIRT", SurfacePresetAvailability.Native, RunwaySurface.Dirt, true, false, Probe),
        new(SandyDirtId, "Sandy Dirt", "SAND", SurfacePresetAvailability.Native, RunwaySurface.Dirt, true, false, Probe),
        new(GravelId, "Gravel", "GRAVEL", SurfacePresetAvailability.Native, RunwaySurface.Dirt, true, false, Probe),
        new(DryGrassId, "Dry Grass", "GRASS", SurfacePresetAvailability.TemporaryFallback, RunwaySurface.Dirt, true, false, Probe,
            "Uses native GRASS until a reliably referenced dry-colour material is established."),
        new(GreenGrassId, "Green Grass", "SHORT_GRASS", SurfacePresetAvailability.Native, RunwaySurface.Dirt, true, false, Probe),
        new(WeatheredAsphaltId, "Weathered Asphalt", "ASPHALT", SurfacePresetAvailability.TemporaryFallback, RunwaySurface.Asphalt, false, true, Probe,
            "Uses native ASPHALT until a reliably referenced weathered material is established."),
        new(ModernAsphaltId, "Modern Asphalt", "ASPHALT", SurfacePresetAvailability.Native, RunwaySurface.Asphalt, false, true, Probe)
    ];

    static RunwaySurfacePresets()
    {
        foreach (var preset in All) preset.EnsureValid();
        if (All.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != All.Count)
            throw new InvalidDataException("Duplicate runway surface preset ID.");
    }

    public static RunwaySurfacePreset? Find(string? id) => All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
    public static RunwaySurfacePreset Get(string id) => Find(id) ?? throw new ArgumentException($"Unsupported runway surface preset '{id}'.");
}
