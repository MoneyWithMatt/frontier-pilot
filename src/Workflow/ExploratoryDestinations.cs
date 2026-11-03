using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed partial class EmployerDestinationCatalogue
{
    private IReadOnlyList<AirportVisit> visits = [];
    public IReadOnlyList<AirportVisit> Visits => visits;
    public void ApplyVisits(IReadOnlyList<AirportVisit> observations) { foreach (var visit in observations) visit.Validate(); visits = observations.ToArray(); }
    public bool IsHeld(DestinationResearch row) => CuratedNamibiaDestinations.Find(row.Key)?.Held == true || row.Key is "candidate-geluk" or "candidate-zannier-sonop";
    public string? TechnicalProblem(DestinationResearch row)
    {
        if(IsHeld(row))return "research identity/location conflict";
        var problem=SimulatorCache?.Problem(row.Identifier);
        if(problem is not null&&!problem.StartsWith("Not found"))return problem;
        if(SimulatorCache?.Routes.SingleOrDefault(x=>x.Airport.Identifier==row.Identifier) is {} saved && CuratedNamibiaDestinations.Find(row.Key) is {} reference &&
            WorldTracking.DistanceMeters(saved.Airport.Latitude,saved.Airport.Longitude,reference.Latitude,reference.Longitude)>5*1852)
            return "facility point disagrees with published geography by over 5 NM; reconcile identity before new jobs";
        return null;
    }
    public bool CanGenerateAtBase(CareerEmployer employer)
    {
        var code=CareerCatalogue.Bases.Single(b=>b.Id==employer.BaseId).Identifier;
        var row=Research.FirstOrDefault(r=>r.Identifier==code);
        return Base(employer) is not null && (row is null || TechnicalProblem(row) is null) &&
            Reviews.All(r=>(r.Key!=code&&r.Identifier!=code)||r.Status=="Approved") && UnresolvedProblem(code) is null;
    }
    public bool HasTechnicalBaseReference(CareerEmployer employer)
    {
        var code=CareerCatalogue.Bases.Single(b=>b.Id==employer.BaseId).Identifier;
        return CanGenerateAtBase(employer) && (Reviews.Any(r=>r.Status=="Approved"&&r.Identifier==code) || SimulatorCache?.Routes.Any(r=>r.Airport.Identifier==code)==true);
    }
    public DestinationResearch? ResearchFor(string locationId)
    {
        lock(airportIndexGate)
        {
            researchByLocation??=Research.Select(r=>(Row:r,Place:Resolve(r))).Where(x=>x.Place is not null)
                .GroupBy(x=>x.Place!.StableId).ToDictionary(g=>g.Key,g=>g.First().Row);
            return researchByLocation.GetValueOrDefault(locationId);
        }
    }
    public AirportVisit? LatestVisit(string key) => visits.Where(v => v.AirportKey == key).OrderByDescending(v => v.RecordedUtc).ThenByDescending(v => v.Id).FirstOrDefault();
    // AircraftUnsuitable is retained for schema-1 history only. It never blocks jobs.
    public AirportVisit? UnresolvedProblem(string key)=>visits.Where(p=>p.AirportKey==key&&p.Result==AirportVisitResult.Problematic&&p.Issue!=AirportIssue.AircraftUnsuitable&&
        !visits.Any(v=>v.AirportKey==key&&v.Result==AirportVisitResult.Verified&&v.RecordedUtc>p.RecordedUtc)).OrderByDescending(p=>p.RecordedUtc).FirstOrDefault();
    public bool IsPlayerVerified(string key)=>UnresolvedProblem(key) is null&&visits.Any(v=>v.AirportKey==key&&v.Result==AirportVisitResult.Verified);
    public string Describe(string? locationId, string? aircraftType = null)
    {
        if (locationId is null || ResearchFor(locationId) is not {} row) return "Airport review unavailable for this operating area.";
        if (TechnicalProblem(row) is {} conflict) return "Held — "+conflict+"; not available for generated jobs.";
        if (UnresolvedProblem(row.Key) is {} problem) return $"Problematic — {problem.Issue} ({problem.AircraftDescription}): {problem.Note}. New jobs withheld; accepted work/history retained.";
        if (IsPlayerVerified(row.Key))
        {
            var shown=visits.Where(v=>v.AirportKey==row.Key&&v.Result==AirportVisitResult.Verified).OrderByDescending(v=>v.RecordedUtc).First();
            return $"Verified by player — airport representation confirmed on {shown.RecordedUtc:dd MMM yyyy}. Aircraft observed: {shown.AircraftDescription} (context only). {shown.Note}";
        }
        if (Resolve(row)?.VerificationState == LocationVerificationState.MsfsVerified) return "Prior airport inspection recorded. Aircraft suitability and performance remain the pilot's responsibility.";
        return "Unverified — not yet visited; help verify this airstrip. Published position/facility lookup is not runway acceptance.";
    }
    private NamibiaLocation? ResolveExploratory(DestinationResearch row, NamibiaLocation? prior)
    {
        var saved = SimulatorCache?.Routes.SingleOrDefault(x => x.Airport.Identifier == row.Identifier);
        var curated = CuratedNamibiaDestinations.Find(row.Key);
        if (saved is null && (curated is null || IsHeld(row))) return null;
        var lat = saved?.Airport.Latitude ?? curated!.Latitude; var lon = saved?.Airport.Longitude ?? curated!.Longitude;
        var code = saved?.Airport.Identifier ?? curated!.Identifier;
        var reference = curated?.Reference ?? row.Identifier;
        var name = row.VerifiedName + (code.Length == 0 ? " [reference " + reference + "]" : "");
        var minutes = Math.Max(1, (int)Math.Round(Geodesy.Inverse(new(-22.604920, 17.079002), new(lat, lon)).DistanceMeters / 1852 / 120 * 60));
        return new(prior?.StableId ?? "namibia.curated-" + row.Key.ToLowerInvariant(), name,
            row.Career == "Industrial" ? NamibiaLocationCategory.Mining : row.Career == "Safari" ? NamibiaLocationCategory.Safari : NamibiaLocationCategory.SmallCommunity,
            false, new(code.Length == 0 ? null : code, code.Length == 0 ? AirportIdentifierConfidence.Unknown : AirportIdentifierConfidence.PublishedUnverified), new(lat, lon),
            new(saved is null ? CoordinateSourceKind.PublishedCatalogue : CoordinateSourceKind.SimulatorDerived,
                saved is null ? "PR #4 / OurAirports research snapshot" : "Saved MSFS facility lookup; runway uninspected", curated?.Source ?? "SimConnect airport list",
                saved is null ? new DateOnly(2026, 10, 29) : DateOnly.FromDateTime(saved.ObservedUtc.UtcDateTime), "Exploratory route reference only; not simulator landing/aircraft qualification."),
            minutes, row.VerificationNote, [row.ProposedMissions], (curated?.Notes ?? row.ReviewStatus), [curated?.Source ?? "Existing destination research"],
            LocationVerificationState.ResearchCandidate, null, CustomerActivationState.Inactive, false);
    }
}
