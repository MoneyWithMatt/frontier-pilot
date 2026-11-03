using System.Text.Json;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>Authored observation parameters; location comes from the researched platform registry.</summary>
public static class OffshoreMissionDefinitions
{
    public static IReadOnlyList<OffshoreOperatingArea> Areas { get; } = Load();
    private sealed record Definition(string Id, string Name, string PlatformId, double RadiusNauticalMiles, int ObservationSeconds);
    private static IReadOnlyList<OffshoreOperatingArea> Load()
    {
        using var stream = typeof(OffshoreMissionDefinitions).Assembly.GetManifestResourceStream(
            "FrontierAirportBuilder.Workflow.OffshoreObservationDefinitions.json") ?? throw new InvalidOperationException("Missing Offshore observation definitions.");
        var definitions = JsonSerializer.Deserialize<Definition[]>(stream) ?? throw new InvalidOperationException("Empty Offshore observation definitions.");
        if (definitions.Length == 0 || definitions.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
            throw new InvalidOperationException("Offshore observation definitions require unique identifiers.");
        return definitions.Select(x =>
        {
            var platform = OffshorePlatformRegistry.Find(x.PlatformId);
            if (!platform.CanLocateObservationArea) throw new InvalidOperationException("Observation geography must be visually confirmed.");
            var area = new OffshoreOperatingArea(x.Id, x.Name, platform.Latitude, platform.Longitude, x.RadiusNauticalMiles, x.ObservationSeconds);
            area.Validate();
            return area;
        }).ToArray();
    }
}
