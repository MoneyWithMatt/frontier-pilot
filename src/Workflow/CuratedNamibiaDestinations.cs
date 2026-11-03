using System.Text.Json;

namespace FrontierAirportBuilder.Workflow;

public sealed record CuratedNamibiaDestination(string Id, string Name, string Identifier, string Reference, string? LegacyKey,
    double Latitude, double Longitude, string Source, string Notes, string Caution, string[] Careers, string[] PriorityCareers, bool Held);

/// <summary>Dated PR #4 research, not operational or simulator qualification.</summary>
public static class CuratedNamibiaDestinations
{
    public static IReadOnlyList<CuratedNamibiaDestination> All { get; } = Load();
    public static CuratedNamibiaDestination? Find(string key) => All.FirstOrDefault(x => x.Id == key || x.LegacyKey == key);
    private static IReadOnlyList<CuratedNamibiaDestination> Load()
    {
        using var stream = typeof(CuratedNamibiaDestinations).Assembly.GetManifestResourceStream("FrontierAirportBuilder.Workflow.CuratedNamibiaDestinations.json")!;
        var rows = JsonSerializer.Deserialize<CuratedNamibiaDestination[]>(stream) ?? throw new IOException("Curated Namibia research missing.");
        if (rows.Length == 0 || rows.Select(x => x.Id).Distinct().Count() != rows.Length || rows.Any(x => !double.IsFinite(x.Latitude) || !double.IsFinite(x.Longitude) || x.Latitude is < -30 or > -16 || x.Longitude is < 12 or > 26 || string.IsNullOrWhiteSpace(x.Source)))
            throw new IOException("Invalid curated Namibia research; no destinations enabled.");
        return rows;
    }
    public static IReadOnlyList<DestinationResearch> Extend(IReadOnlyList<DestinationResearch> original)
    {
        var result = original.Select(r => Find(r.Key) is {} c ? r with { StableKey = r.Key,
            Identifier = string.IsNullOrEmpty(r.Identifier) ? c.Identifier : r.Identifier, PublishedReference = c.Reference } : r).ToList();
        foreach (var c in All.Where(c => !c.Held))
            foreach (var career in c.PriorityCareers)
                if (!result.Any(r => r.Career == career && (r.Key == c.Id || r.Key == c.LegacyKey || r.Identifier.Length > 0 && r.Identifier == c.Identifier)))
                    result.Add(new(career, c.Name, c.Name, c.Identifier, "Published research", "Unverified", c.Caution,
                        "Research-supported purpose", c.Notes, career == "Missionary" ? "community supplies and visiting community personnel" : career == "Safari" ? "lodge and conservation supplies" : "existing company support work",
                        c.Source, "Exploratory playtesting") { StableKey = c.Id, PublishedReference = c.Reference });
        return result;
    }
}
