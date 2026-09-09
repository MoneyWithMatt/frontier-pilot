using System.Text;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Tests;

public sealed class NamibiaLocationCatalogueTests
{
    private static readonly string[] Recommended =
    [
        "namibia.aminuis", "namibia.arandis", "namibia.leonardville", "namibia.mount-etjo",
        "namibia.okonjima", "namibia.uis-mine", "namibia.windhoek-eros"
    ];
    private static readonly string[] ExplicitlyReportedIdentifiers = ["FYGI", "FYIB", "FYKD", "FYKO", "FYOK", "FYRZ", "FYSP", "FYSU"];
    private static readonly string[] ExpectedLocations =
    [
        "namibia.aminuis=FYAM", "namibia.arandis=FYAR", "namibia.aranos=FYAN", "namibia.dordabis-carpet=FYIB",
        "namibia.doro-nawas=FYDN", "namibia.etusis-lodge=FYET", "namibia.gibeon=FYGI", "namibia.gochas=FYGC",
        "namibia.kalkfeld=FYKD", "namibia.karibib=FYKA", "namibia.kombat=FYKO", "namibia.leonardville=FYLV",
        "namibia.maltahohe=FYMH", "namibia.mokuti-lodge=FYMO", "namibia.mount-etjo=FYME", "namibia.okakarara=FYOK",
        "namibia.okombahe=FYAH", "namibia.okonjima=FYKN", "namibia.okorusu-mine=FYKR", "namibia.ongava-lodge=FYNG",
        "namibia.otjikoto-mine=FYBM", "namibia.otjiwarongo=FYOW", "namibia.rostock-ritz=FYRZ", "namibia.sossusvlei-lodge=FYSU",
        "namibia.sossusvlei-mountain-lodge=FYSL", "namibia.stampriet-pan=FYSP", "namibia.swakopmund-municipal=FYSM",
        "namibia.tsumeb=FYTM", "namibia.twyfelfontein=FYTF", "namibia.uis-mine=FYUS", "namibia.windhoek-eros=FYWE"
    ];

    [Fact]
    public void LoadsLockedGatewayAndAllSection20Candidates()
    {
        var catalogue = NamibiaLocationCatalogue.Load();
        Assert.Equal(2, catalogue.SchemaVersion); Assert.Equal("0.2.0", catalogue.CatalogueVersion); Assert.Equal(31, catalogue.Locations.Count);
        Assert.Equal("17I6WG4KdUoUWDQVb7txi3O1zUipG0B4raJQVAHdgbHU", catalogue.SourceDocumentId);
        Assert.Single(catalogue.Locations.Where(x => x.Category == NamibiaLocationCategory.Gateway));
        Assert.Equal(10, catalogue.Locations.Count(x => x.Category == NamibiaLocationCategory.Mining));
        Assert.Equal(10, catalogue.Locations.Count(x => x.Category == NamibiaLocationCategory.SmallCommunity));
        Assert.Equal(10, catalogue.Locations.Count(x => x.Category == NamibiaLocationCategory.Safari));
        Assert.Equal(ExpectedLocations, catalogue.Locations.Select(x => $"{x.StableId}={x.AirportIdentifier.Value}").Order().ToArray());
        var eros = catalogue.Get("namibia.windhoek-eros");
        Assert.True(eros.IsLockedGateway); Assert.Equal("FYWE", eros.AirportIdentifier.Value);
        Assert.Equal(AirportIdentifierConfidence.ConfirmedIcao, eros.AirportIdentifier.Confidence);
        Assert.Equal(LocationVerificationState.MsfsVerified, eros.VerificationState);
        Assert.Equal("ManualInSimulatorInspection", eros.VerificationEvidence?.Method);
        Assert.Equal(CustomerActivationState.NotApplicable, eros.ActivationState);
    }

    [Fact]
    public void StableIdsAndRecommendedSubsetAreDeterministic()
    {
        var first = NamibiaLocationCatalogue.Load(); var second = NamibiaLocationCatalogue.Load();
        Assert.Equal(first.Locations.Select(x => x.StableId), second.Locations.Select(x => x.StableId));
        Assert.Equal(31, first.Locations.Select(x => x.StableId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Recommended, first.Locations.Where(x => x.RecommendedFirstPlayable).Select(x => x.StableId).Order().ToArray());
        Assert.Equal("namibia.uis-mine", first.Get("namibia.uis-mine").ToEconomicLocation().Id);
    }

    [Fact]
    public void RecommendationDoesNotImplyVerificationOrActivation()
    {
        var catalogue = NamibiaLocationCatalogue.Load();
        Assert.Equal(3, catalogue.Locations.Count(location => location.VerificationState == LocationVerificationState.MsfsVerified));
        Assert.Equal(["namibia.arandis", "namibia.uis-mine"], catalogue.Locations.Where(location => location.ActivationState == CustomerActivationState.ActiveCustomer).Select(x => x.StableId).Order().ToArray());
        Assert.Equal(28, catalogue.Locations.Count(location => location.ApproximateCoordinates is null));
        Assert.All(catalogue.Locations.Where(x => x.RecommendedFirstPlayable && x.StableId is not ("namibia.windhoek-eros" or "namibia.uis-mine" or "namibia.arandis")), location => Assert.Equal(CustomerActivationState.Inactive, location.ActivationState));
        Assert.Contains(catalogue.SourceNotes, note => note.Contains("not simulator-derived", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitlyReportedIdentifiersRemainDistinctFromPublishedAndConfirmedValues()
    {
        var catalogue = NamibiaLocationCatalogue.Load();
        Assert.Equal(ExplicitlyReportedIdentifiers,
            catalogue.Locations.Where(x => x.AirportIdentifier.Confidence == AirportIdentifierConfidence.ReportedUnverified)
                .Select(x => x.AirportIdentifier.Value!).Order().ToArray());
        Assert.All(catalogue.Locations.Where(x => ExplicitlyReportedIdentifiers.Contains(x.AirportIdentifier.Value)),
            location => Assert.Equal(AirportIdentifierConfidence.ReportedUnverified, location.AirportIdentifier.Confidence));
        Assert.Equal(AirportIdentifierConfidence.ConfirmedIcao, catalogue.Get("namibia.windhoek-eros").AirportIdentifier.Confidence);
    }

    [Fact]
    public void VerifiedAirportsKeepPublishedCoordinatesSeparateFromSimulatorEvidence()
    {
        var catalogue = NamibiaLocationCatalogue.Load();
        var expected = new Dictionary<string, (double Latitude, double Longitude)>
        {
            ["namibia.windhoek-eros"] = (-22.604920, 17.079002),
            ["namibia.uis-mine"] = (-21.230032, 14.866888),
            ["namibia.arandis"] = (-22.461875, 14.979322)
        };
        foreach (var pair in expected)
        {
            var location = catalogue.Get(pair.Key);
            Assert.Equal(pair.Value.Latitude, location.ApproximateCoordinates!.Latitude, 6);
            Assert.Equal(pair.Value.Longitude, location.ApproximateCoordinates.Longitude, 6);
            Assert.Equal(CoordinateSourceKind.PublishedCatalogue, location.CoordinateSource!.Kind);
            Assert.Equal("OurAirports", location.CoordinateSource.Publisher);
            Assert.Contains("ourairports.com", location.CoordinateSource.Reference);
            Assert.Equal("ManualInSimulatorInspection", location.VerificationEvidence!.Method);
            Assert.Equal(new DateOnly(2026, 9, 9), location.VerificationEvidence.VerifiedOn);
        }
    }

    [Fact]
    public void DuplicateStableIdsAndAirportIdentifiersAreRejected()
    {
        var json = EmbeddedJson();
        var duplicateId = json.Replace("namibia.arandis", "namibia.uis-mine", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => NamibiaLocationCatalogue.Load(Stream(duplicateId)));
        var duplicateAirport = json.Replace("\"FYAR\"", "\"FYUS\"", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => NamibiaLocationCatalogue.Load(Stream(duplicateAirport)));
    }

    [Fact]
    public void StatusTransitionRequiresVerificationBeforeActivation()
    {
        var candidate = TestCandidate();
        Assert.Throws<InvalidOperationException>(() => NamibiaLocationLifecycle.ActivateCustomer(candidate));
        var verified = NamibiaLocationLifecycle.MarkMsfsVerified(candidate, "TST1", new(-1.0, 1.0), CoordinateSource(), Verification());
        Assert.Equal(LocationVerificationState.MsfsVerified, verified.VerificationState);
        Assert.Equal(CustomerActivationState.Inactive, verified.ActivationState);
        Assert.Equal(AirportIdentifierConfidence.ConfirmedIcao, verified.AirportIdentifier.Confidence);
        var active = NamibiaLocationLifecycle.ActivateCustomer(verified);
        Assert.Equal(CustomerActivationState.ActiveCustomer, active.ActivationState); Assert.True(active.CanGenerateOrdinaryLiveJobs);
    }

    [Fact]
    public void OrdinaryLiveJobGuardRequiresBothVerifiedAndActive()
    {
        var catalogue = NamibiaLocationCatalogue.Load(); var source = catalogue.Get("namibia.uis-mine");
        Assert.Equal(EconomicLocationStatus.ActiveCustomer, NamibiaJobGenerationGuard.RequireEligibleDestination(catalogue, source.StableId).Status);
        var test = TestCandidate() with { StableId = source.StableId };
        var verified = NamibiaLocationLifecycle.MarkMsfsVerified(test, "TST1", new(-1.0, 1.0), CoordinateSource(), Verification());
        var verifiedOnly = catalogue with { Locations = catalogue.Locations.Select(x => x.StableId == source.StableId ? verified : x).ToArray() };
        Assert.Throws<InvalidOperationException>(() => NamibiaJobGenerationGuard.RequireEligibleDestination(verifiedOnly, source.StableId));
        var active = NamibiaLocationLifecycle.ActivateCustomer(verified);
        var eligible = catalogue with { Locations = catalogue.Locations.Select(x => x.StableId == source.StableId ? active : x).ToArray() };
        var economic = NamibiaJobGenerationGuard.RequireEligibleDestination(eligible, source.StableId);
        Assert.Equal(source.StableId, economic.Id); Assert.Equal(EconomicLocationStatus.ActiveCustomer, economic.Status);
    }

    [Fact]
    public void GatewayCannotBeActivatedAsDestinationCustomer()
    {
        var eros = NamibiaLocationCatalogue.Load().Get("namibia.windhoek-eros");
        Assert.Throws<InvalidOperationException>(() => NamibiaLocationLifecycle.ActivateCustomer(eros));
    }

    private static string EmbeddedJson()
    {
        using var stream = typeof(NamibiaLocationCatalogue).Assembly.GetManifestResourceStream("FrontierAirportBuilder.Core.Catalogues.namibia-locations-v0.json")!;
        using var reader = new StreamReader(stream, Encoding.UTF8); return reader.ReadToEnd();
    }
    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));
    private static NamibiaLocation TestCandidate() => new("namibia.test-only-candidate", "TEST ONLY Candidate", NamibiaLocationCategory.SmallCommunity, false,
        new("TST1", AirportIdentifierConfidence.ReportedUnverified), null, null, 10, "TEST ONLY economic role.", ["TEST ONLY cargo"],
        "TEST ONLY status-transition fixture; not a real location.", ["Automated test fixture"], LocationVerificationState.ResearchCandidate,
        null, CustomerActivationState.Inactive, false);
    private static LocationCoordinateSource CoordinateSource() => new(CoordinateSourceKind.PublishedCatalogue, "TEST ONLY publisher",
        "https://example.invalid/test-only", new(2026, 9, 9), "TEST ONLY published coordinate fixture.");
    private static MsfsVerificationEvidence Verification() => new("AutomatedTestFixture", new(2026, 9, 9), "TEST ONLY verification evidence.");
}
