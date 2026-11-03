using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed record DestinationResearch(string Career, string Location, string VerifiedName, string Identifier,
    string AirfieldExistence, string RunwayDataVerified, string RunwayDetail, string RealWorldCaseStrength,
    string VerificationNote, string ProposedMissions, string SourceUrl, string ReviewStatus)
{
    public string? StableKey { get; init; }
    public string? PublishedReference { get; init; }
    public string Key => StableKey ?? (string.IsNullOrWhiteSpace(Identifier)
        ? "candidate-" + Regex.Replace(Location.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-')
        : Identifier);
}

public sealed record DestinationReview(string Key, string Status, string Identifier, double? Latitude,
    double? Longitude, string Evidence, DateTimeOffset RecordedUtc, string[] CompatibleAircraft)
{
    public DateTimeOffset? ApprovedUtc {get;init;}
    public string? ApprovedEvidence {get;init;}
}
public sealed record DestinationReviewDocument(int SchemaVersion, DestinationReview[] Reviews);
public sealed record DestinationDiagnostic(DestinationResearch Research, NamibiaLocation? Resolved,
    string Recognition, bool Enabled, string Reason, string[] CompatibleAircraft)
{
    public string[] OriginCompatibleAircraft { get; init; } = [];
    public string Employer => Research.Career;
    public string Airport => Research.VerifiedName;
    public string Identifier => Resolved?.AirportIdentifier.Value ?? (Research.Identifier.Length > 0 ? Research.Identifier : Research.PublishedReference ?? "Identifier unconfirmed");
    public string Runway => Research.RunwayDataVerified + " · " + Research.RunwayDetail;
    public string Notes => Reason + " · " + Research.VerificationNote + " · " + Research.ReviewStatus;
}

/// <summary>Research is not simulator evidence. Local reviews are explicit human inspection records,
/// independent of career saves and never derived from last-known telemetry.</summary>
public sealed partial class EmployerDestinationCatalogue
{
    public static IReadOnlyList<DestinationResearch> Research {get;}=LoadResearch();
    private readonly NamibiaLocationCatalogueDocument legacy;
    public IReadOnlyList<DestinationReview> Reviews {get;}
    public SimulatorAirportCache? SimulatorCache {get; private set;}
    public void ApplySimulatorCache(SimulatorAirportCache cache) => SimulatorCache = cache;
    public EmployerDestinationCatalogue(IEnumerable<DestinationReview>? reviews=null, NamibiaLocationCatalogueDocument? legacy=null, SimulatorAirportCache? simulatorCache=null)
    {
        SimulatorCache=simulatorCache;
        this.legacy=(legacy??NamibiaLocationCatalogue.Load()).EnsureValid();
        Reviews=(reviews??[]).ToArray();
        if(Reviews.Select(x=>x.Key).Distinct().Count()!=Reviews.Count)throw new InvalidDataException("Duplicate destination reviews.");
        foreach(var review in Reviews)ValidateReview(review);
        var resolved=Research.Select(Resolve).Where(x=>x is not null).DistinctBy(x=>x!.StableId).ToArray();
        var identified=resolved.Where(x=>x!.AirportIdentifier.Value is not null).ToArray();
        if(identified.Select(x=>x!.AirportIdentifier.Value).Distinct().Count()!=identified.Length)throw new InvalidDataException("Destination identifiers conflict. Reviews preserved.");
    }
    private static IReadOnlyList<DestinationResearch> LoadResearch()
    {
        using var stream=typeof(EmployerDestinationCatalogue).Assembly.GetManifestResourceStream("FrontierAirportBuilder.Workflow.NamibiaDestinationResearch.json")!;
        var rows=JsonSerializer.Deserialize<DestinationResearch[]>(stream,new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower})??throw new IOException("Destination research is missing.");
        if(rows.Length!=53||rows.Select(x=>x.Career+"/"+x.Key).Distinct().Count()!=rows.Length||rows.Any(x=>!new[]{"Missionary","Industrial","Safari","Offshore"}.Contains(x.Career)))throw new IOException("Destination research membership is invalid.");
        return CuratedNamibiaDestinations.Extend(rows);
    }
    public static void ValidateReview(DestinationReview review)
    {
        if(!Research.Any(x=>x.Key==review.Key)||!new[]{"Approved","Rejected","Needs scenery","Needs investigation"}.Contains(review.Status)||
            string.IsNullOrWhiteSpace(review.Evidence)||review.Evidence.Length>400||review.RecordedUtc==default||review.CompatibleAircraft is null||
            review.CompatibleAircraft.Distinct().Count()!=review.CompatibleAircraft.Length||review.CompatibleAircraft.Any(x=>!CompanyAircraft.Types.Any(t=>t.Id==x&&t.Enabled)))
            throw new InvalidDataException("Invalid destination review; supply an inspection note and known aircraft types.");
        if((review.Latitude is null)!=(review.Longitude is null)||review.Latitude is {} lat&&(!double.IsFinite(lat)||lat<=-90||lat>=90)||review.Longitude is {} lon&&(!double.IsFinite(lon)||lon<-180||lon>180))
            throw new InvalidDataException("Invalid destination coordinates.");
        if(review.Status=="Approved"&&(review.Latitude is null||!Regex.IsMatch(review.Identifier??"", "\\A[A-Z0-9]{3,8}\\z")))
            throw new InvalidDataException("Approval requires the identifier and WGS84 coordinates inspected in MSFS.");
        if((review.ApprovedUtc is null)!=(review.ApprovedEvidence is null)||review.ApprovedUtc==default(DateTimeOffset)||review.ApprovedEvidence is {Length:>400})throw new InvalidDataException("Invalid retained inspection provenance.");
    }
    public NamibiaLocation? Resolve(DestinationResearch row)
    {
        var prior=legacy.Locations.SingleOrDefault(x=>!string.IsNullOrWhiteSpace(row.Identifier)&&x.AirportIdentifier.Value==row.Identifier);
        if(prior?.VerificationState==LocationVerificationState.MsfsVerified)return prior;
        var review=Reviews.SingleOrDefault(x=>x.Key==row.Key);
        if(review?.Latitude is not {} lat||review.Longitude is not {} lon||string.IsNullOrWhiteSpace(review.Identifier)||(review.Status!="Approved"&&review.ApprovedUtc is null))
            return ResolveExploratory(row, prior);
        var automatic=false;
        var inspected=review.ApprovedUtc??review.RecordedUtc;var inspection=review.ApprovedEvidence??review.Evidence;
        var id=prior?.StableId??"namibia.curated-"+row.Key.ToLowerInvariant();
        var minutes=Math.Max(1,(int)Math.Round(Geodesy.Inverse(new(-22.604920,17.079002),new(lat,lon)).DistanceMeters/1852/120*60));
        return new(id,row.VerifiedName,row.Career=="Industrial"?NamibiaLocationCategory.Mining:row.Career=="Safari"?NamibiaLocationCategory.Safari:NamibiaLocationCategory.SmallCommunity,false,
            new(review.Identifier,AirportIdentifierConfidence.ConfirmedIcao),new(lat,lon),
            new(CoordinateSourceKind.SimulatorDerived,automatic?"MSFS airport database":"Local MSFS inspection",automatic?"SimConnect_RequestFacilitiesList":"User-recorded MSFS airport inspection",DateOnly.FromDateTime(inspected.UtcDateTime),inspection),
            minutes,row.VerificationNote,[row.ProposedMissions],row.ReviewStatus,[string.IsNullOrWhiteSpace(row.SourceUrl)?"User-supplied destination research CSV":row.SourceUrl],
            LocationVerificationState.MsfsVerified,new(automatic?"SimConnectAirportCatalogue":"ManualInSimulatorInspection",DateOnly.FromDateTime(inspected.UtcDateTime),inspection),CustomerActivationState.ActiveCustomer,false);
    }
    public NamibiaLocation? Find(string id)=>Research.Select(Resolve).FirstOrDefault(x=>x?.StableId==id);
    public NamibiaLocation? Base(CareerEmployer employer)
    {
        var code=CareerCatalogue.Bases.Single(x=>x.Id==employer.BaseId).Identifier;
        return legacy.Locations.FirstOrDefault(x=>x.AirportIdentifier.Value==code&&x.VerificationState==LocationVerificationState.MsfsVerified)
            ??Research.Where(x=>x.Identifier==code).Select(Resolve).FirstOrDefault(x=>x is not null);
    }
    public IReadOnlyList<DestinationDiagnostic> Diagnose(string? category=null)
    {
        return Research.Where(x=>category is null||x.Career==category).Select(row=>
        {
            var employer=CareerCatalogue.Employers.Single(x=>x.Category==row.Career);
            var location=Resolve(row);var review=Reviews.SingleOrDefault(x=>x.Key==row.Key);var origin=Base(employer);
            var baseCode=CareerCatalogue.Bases.Single(x=>x.Id==employer.BaseId).Identifier;
            var baseReview=Reviews.FirstOrDefault(x=>x.Key==baseCode||x.Identifier==baseCode);
            var recognition=TechnicalProblem(row) is {} conflict?"Held: "+conflict:location is null?"Held: airport identity or coordinates unresolved":
                location.VerificationState==LocationVerificationState.MsfsVerified?"Prior manual inspection":SimulatorCache?.Routes.Any(x=>x.Airport.Identifier==row.Identifier)==true?"Facility lookup only; runway unverified":"Published reference; simulator unverified";
            var latest=LatestVisit(row.Key);var baseVisit=LatestVisit(baseCode);
            var technical=TechnicalProblem(row);var baseTechnical=Research.FirstOrDefault(r=>r.Identifier==baseCode) is {} baseRow?TechnicalProblem(baseRow):null;
            var reason=IsHeld(row)?recognition:review is {Status:not "Approved"}?review.Status:latest?.Result==AirportVisitResult.Problematic?"Problematic: "+latest.Issue+" — "+latest.Note:
                location is null?recognition:origin is null?"Employer base unresolved":origin.StableId==location.StableId?"Base / return only; no same-airport job":
                technical is not null && !technical.StartsWith("Not found")?"Held: "+technical:baseTechnical is not null && !baseTechnical.StartsWith("Not found")?"Held base: "+baseTechnical:
                baseReview is {Status:not "Approved"}?"Employer base review: "+baseReview.Status:baseVisit?.Result==AirportVisitResult.Problematic?"Employer base reported problematic":
                "Eligible route; "+Describe(location.StableId);
            var diagnostic=new DestinationDiagnostic(row,location,recognition,reason.StartsWith("Eligible route",StringComparison.Ordinal),reason,review?.CompatibleAircraft??[])
                {OriginCompatibleAircraft=baseReview?.CompatibleAircraft??[]};
            if(diagnostic.Enabled&&CompanyAircraft.Choices(employer,diagnostic,0).Count==0)
                diagnostic=diagnostic with{Enabled=false,Reason="No compatible aircraft in this employer's enabled fleet for both origin and destination"};
            return diagnostic;
        }).ToArray();
    }
}

public sealed class DestinationReviewStore(string root)
{
    private readonly string path=Path.Combine(Path.GetFullPath(root),"destination-reviews.json");
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    private void Safe()
    {
        for(var directory=new DirectoryInfo(Path.GetDirectoryName(path)!);directory is not null;directory=directory.Parent)
            if(directory.Exists&&directory.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new IOException("Destination reviews cannot use filesystem links.");
        if(File.Exists(path)&&(File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)||new FileInfo(path).Length>1_000_000))throw new IOException("Destination review file is unsafe or oversized. Preserved.");
    }
    public EmployerDestinationCatalogue Load()
    {
        Safe();
        if(!File.Exists(path)){var empty=new EmployerDestinationCatalogue(simulatorCache:new SimulatorAirportStore(root).Load());empty.ApplyVisits(new AirportVisitStore(root).Load());return empty;}
        var document=JsonSerializer.Deserialize<DestinationReviewDocument>(File.ReadAllText(path),Json)??throw new IOException("Empty destination reviews. File preserved.");
        if(document.SchemaVersion!=1||document.Reviews is null)throw new IOException("Unsupported destination reviews. File preserved.");
        var result=new EmployerDestinationCatalogue(document.Reviews,simulatorCache:new SimulatorAirportStore(root).Load());result.ApplyVisits(new AirportVisitStore(root).Load());return result;
    }
    public void Save(DestinationReview review)
    {
        Safe();
        EmployerDestinationCatalogue.ValidateReview(review);
        using var gate=new Mutex(false,"Local\\FrontierDestinationReviews-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..24]);
        if(!gate.WaitOne(TimeSpan.FromSeconds(10)))throw new IOException("Another destination review is being saved.");
        try
        {
            var current=Load();var previous=current.Reviews.SingleOrDefault(x=>x.Key==review.Key);
            review=review with {ApprovedUtc=previous?.ApprovedUtc??(review.Status=="Approved"?review.RecordedUtc:null),ApprovedEvidence=previous?.ApprovedEvidence??(review.Status=="Approved"?review.Evidence:null)};
            var existingLocation=current.Resolve(EmployerDestinationCatalogue.Research.First(x=>x.Key==review.Key));
            if(review.Status=="Approved"&&existingLocation?.ApproximateCoordinates is {} known&&
                (known.Latitude!=review.Latitude||known.Longitude!=review.Longitude||existingLocation.AirportIdentifier.Value!=review.Identifier))
                throw new IOException("Existing route reference coordinates/identifier are immutable to protect accepted flights. Record a problem or investigation instead of moving the route.");
            if(previous?.Latitude is not null&&(previous.Latitude!=review.Latitude||previous.Longitude!=review.Longitude||previous.Identifier!=review.Identifier))
                throw new IOException("Previously recorded route coordinates are immutable to protect accepted flights. Mark this destination for investigation instead.");
            var rows=current.Reviews.Where(x=>x.Key!=review.Key).Append(review).ToArray();_ = new EmployerDestinationCatalogue(rows,simulatorCache:current.SimulatorCache);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,new DestinationReviewDocument(1,rows),Json);stream.Flush(true);}File.Move(temporary,path,true);}
            finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        finally {gate.ReleaseMutex();}
    }
}
