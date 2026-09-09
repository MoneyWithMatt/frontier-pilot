using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FrontierAirportBuilder.Core;

public enum NamibiaLocationCategory { Gateway, Mining, SmallCommunity, Safari }
public enum AirportIdentifierConfidence { ConfirmedIcao, PublishedUnverified, ReportedUnverified, Unknown }
public enum LocationVerificationState { ResearchCandidate, MsfsVerified }
public enum CustomerActivationState { NotApplicable, Inactive, ActiveCustomer, DormantCandidate }

public sealed record AuthoredAirportIdentifier(string? Value, AirportIdentifierConfidence Confidence);
public sealed record ApproximateCoordinates(double Latitude, double Longitude);

public sealed record NamibiaLocation(string StableId, string Name, NamibiaLocationCategory Category, bool IsLockedGateway,
    AuthoredAirportIdentifier AirportIdentifier, ApproximateCoordinates? ApproximateCoordinates,
    int? ApproximateFlightMinutesFromErosAt120Knots, string EconomicRole, IReadOnlyList<string> ProposedJobs,
    string ResearchNotes, IReadOnlyList<string> ResearchSources, LocationVerificationState VerificationState,
    CustomerActivationState ActivationState, bool RecommendedFirstPlayable)
{
    public bool CanGenerateOrdinaryLiveJobs => !IsLockedGateway && VerificationState == LocationVerificationState.MsfsVerified && ActivationState == CustomerActivationState.ActiveCustomer;
    public EconomicLocation ToEconomicLocation() => new(StableId, Name, Category switch
    {
        NamibiaLocationCategory.Gateway => EconomicLocationKind.Gateway,
        NamibiaLocationCategory.Mining => EconomicLocationKind.Mine,
        NamibiaLocationCategory.SmallCommunity => EconomicLocationKind.Community,
        NamibiaLocationCategory.Safari => EconomicLocationKind.Lodge,
        _ => throw new InvalidDataException("Unknown Namibia location category.")
    }, EconomicOwnerKind.System, ActivationState == CustomerActivationState.ActiveCustomer ? EconomicLocationStatus.ActiveCustomer :
        VerificationState == LocationVerificationState.MsfsVerified ? EconomicLocationStatus.MsfsVerified :
        ActivationState == CustomerActivationState.DormantCandidate ? EconomicLocationStatus.DormantCandidate : EconomicLocationStatus.ResearchCandidate);
}

public sealed record NamibiaLocationCatalogueDocument(int SchemaVersion, string CatalogueId, string CatalogueVersion,
    string SourceDocument, string SourceDocumentId, string SourceRevision, string SourceSection,
    IReadOnlyList<string> SourceNotes, IReadOnlyList<NamibiaLocation> Locations)
{
    public NamibiaLocationCatalogueDocument EnsureValid()
    {
        if (SchemaVersion != 1 || CatalogueId != "namibia-starting-customer-network" || string.IsNullOrWhiteSpace(CatalogueVersion))
            throw new InvalidDataException("Namibia catalogue identity or version is invalid.");
        Text(SourceDocument, nameof(SourceDocument), 160); Text(SourceDocumentId, nameof(SourceDocumentId), 96);
        Text(SourceRevision, nameof(SourceRevision), 512); Text(SourceSection, nameof(SourceSection), 160); TextList(SourceNotes, nameof(SourceNotes));
        if (Locations is null || Locations.Count != 31) throw new InvalidDataException("Namibia catalogue must contain the locked gateway and all thirty section 20 candidates.");
        Unique(Locations.Select(x => x.StableId), "stable location ID", StringComparer.Ordinal);
        Unique(Locations.Where(x => x.AirportIdentifier.Value is not null).Select(x => x.AirportIdentifier.Value!), "airport identifier", StringComparer.OrdinalIgnoreCase);
        foreach (var location in Locations) ValidateLocation(location);
        var gateways = Locations.Where(x => x.IsLockedGateway).ToArray();
        if (gateways.Length != 1 || gateways[0].StableId != "namibia.windhoek-eros" || gateways[0].AirportIdentifier.Value != "FYWE")
            throw new InvalidDataException("Windhoek Eros/FYWE must be the sole locked Namibia gateway.");
        return this;
    }

    public NamibiaLocation Get(string stableId) => Locations.SingleOrDefault(x => x.StableId == stableId) ??
        throw new KeyNotFoundException($"Namibia catalogue location '{stableId}' does not exist.");

    internal static void ValidateLocation(NamibiaLocation location)
    {
        if (!Regex.IsMatch(location.StableId ?? "", "\\Anamibia\\.[a-z0-9][a-z0-9.-]{0,54}\\z")) throw new InvalidDataException("Namibia stable location ID is invalid.");
        Text(location.Name, "location name", 96); Text(location.EconomicRole, "economic role", 512); Text(location.ResearchNotes, "research notes", 768);
        TextList(location.ProposedJobs, "proposed jobs"); TextList(location.ResearchSources, "research sources"); ValidateIdentifier(location.AirportIdentifier);
        if (location.ApproximateCoordinates is { } coordinates &&
            (!double.IsFinite(coordinates.Latitude) || coordinates.Latitude is < -90 or > 90 || !double.IsFinite(coordinates.Longitude) || coordinates.Longitude is < -180 or > 180))
            throw new InvalidDataException("Namibia approximate coordinates are invalid.");
        if (location.ApproximateFlightMinutesFromErosAt120Knots is <= 0 or > 600) throw new InvalidDataException("Namibia approximate flight time is invalid.");
        if ((location.IsLockedGateway && location.ApproximateFlightMinutesFromErosAt120Knots is not null) ||
            (!location.IsLockedGateway && location.ApproximateFlightMinutesFromErosAt120Knots is null))
            throw new InvalidDataException("Namibia gateway/destination flight-time data is incomplete.");
        if (location.IsLockedGateway && (location.Category != NamibiaLocationCategory.Gateway || location.ActivationState != CustomerActivationState.NotApplicable))
            throw new InvalidDataException("Locked gateway status is inconsistent.");
        if (!location.IsLockedGateway && location.Category == NamibiaLocationCategory.Gateway) throw new InvalidDataException("An unlocked destination cannot use gateway category.");
        if (!location.IsLockedGateway && location.ActivationState == CustomerActivationState.NotApplicable) throw new InvalidDataException("Customer activation status is missing.");
        if (location.VerificationState == LocationVerificationState.MsfsVerified &&
            (location.ApproximateCoordinates is null || location.AirportIdentifier.Confidence != AirportIdentifierConfidence.ConfirmedIcao))
            throw new InvalidDataException("MSFS-verified locations require confirmed identifier and coordinates.");
        if (location.ActivationState == CustomerActivationState.ActiveCustomer && location.VerificationState != LocationVerificationState.MsfsVerified)
            throw new InvalidDataException("An active customer must first be MSFS verified.");
    }

    private static void ValidateIdentifier(AuthoredAirportIdentifier identifier)
    {
        if (identifier is null) throw new InvalidDataException("Airport identifier metadata is missing.");
        if (identifier.Confidence == AirportIdentifierConfidence.Unknown)
        { if (identifier.Value is not null) throw new InvalidDataException("Unknown airport identifier must not contain an invented value."); return; }
        if (identifier.Value is null || !Regex.IsMatch(identifier.Value, "\\A[A-Z0-9]{3,8}\\z")) throw new InvalidDataException("Airport identifier is invalid.");
    }
    private static void TextList(IReadOnlyList<string>? values, string name)
    { if (values is null || values.Count is < 1 or > 32) throw new InvalidDataException($"{name} are invalid."); foreach (var value in values) Text(value, name, 512); }
    private static void Text(string? value, string name, int maximum)
    { if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > maximum) throw new InvalidDataException($"{name} is invalid."); }
    private static void Unique(IEnumerable<string> values, string name, StringComparer comparer)
    { var all = values.ToArray(); if (all.Distinct(comparer).Count() != all.Length) throw new InvalidDataException($"Duplicate {name}."); }
}

public static class NamibiaLocationCatalogue
{
    private const string ResourceName = "FrontierAirportBuilder.Core.Catalogues.namibia-locations-v0.json";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter() }
    };
    public static NamibiaLocationCatalogueDocument Load()
    { using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName) ?? throw new IOException("Embedded Namibia catalogue is missing."); return Load(stream); }
    public static NamibiaLocationCatalogueDocument Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try { return (JsonSerializer.Deserialize<NamibiaLocationCatalogueDocument>(stream, Json) ?? throw new IOException("Namibia catalogue is empty.")).EnsureValid(); }
        catch (JsonException error) { throw new IOException("Namibia catalogue is corrupt or unsupported.", error); }
    }
}

public static class NamibiaLocationLifecycle
{
    public static NamibiaLocation MarkMsfsVerified(NamibiaLocation source, string confirmedIcaoIdentifier, ApproximateCoordinates verifiedCoordinates)
    {
        var updated = source with { AirportIdentifier = new(confirmedIcaoIdentifier, AirportIdentifierConfidence.ConfirmedIcao),
            ApproximateCoordinates = verifiedCoordinates, VerificationState = LocationVerificationState.MsfsVerified,
            ActivationState = source.IsLockedGateway ? CustomerActivationState.NotApplicable : CustomerActivationState.Inactive };
        NamibiaLocationCatalogueDocument.ValidateLocation(updated); return updated;
    }
    public static NamibiaLocation ActivateCustomer(NamibiaLocation source)
    {
        if (source.IsLockedGateway) throw new InvalidOperationException("The locked gateway is not a destination customer.");
        if (source.VerificationState != LocationVerificationState.MsfsVerified) throw new InvalidOperationException("Customer must be MSFS verified before activation.");
        var updated = source with { ActivationState = CustomerActivationState.ActiveCustomer };
        NamibiaLocationCatalogueDocument.ValidateLocation(updated); return updated;
    }
}

/// <summary>Mandatory guard for future ordinary live-job generation. Prototype-only economy fixtures use a separate path.</summary>
public static class NamibiaJobGenerationGuard
{
    public static EconomicLocation RequireEligibleDestination(NamibiaLocationCatalogueDocument catalogue, string stableLocationId)
    {
        var location = catalogue.EnsureValid().Get(stableLocationId);
        if (!location.CanGenerateOrdinaryLiveJobs)
            throw new InvalidOperationException($"{location.Name} is not both MSFS verified and an active customer; ordinary live jobs are refused.");
        return location.ToEconomicLocation();
    }
}
