using System.Text.Json.Nodes;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.MsfsSdk;

namespace FrontierAirportBuilder.Tests;

public sealed class NamibiaPlaytestingTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"FrontierNamibiaPlaytesting",Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-11-03T12:00:00Z");
    private static CareerEmployer Employer(string category)=>CareerCatalogue.Employers.Single(e=>e.Category==category);
    private static DestinationDiagnostic Destination(EmployerDestinationCatalogue c,string key,string category="Missionary")=>c.Diagnose(category).Single(d=>d.Research.Key==key);
    private static AirportVisit Visit(EmployerDestinationCatalogue c,string key,AirportVisitResult result=AirportVisitResult.Verified,string type="c208",DateTimeOffset? time=null)
    {
        var row=EmployerDestinationCatalogue.Research.First(r=>r.Key==key);var airport=c.Resolve(row)!;var p=airport.ApproximateCoordinates!;
        return new(Guid.NewGuid(),key,airport.StableId,row.VerifiedName,row.PublishedReference??row.Identifier,p.Latitude,p.Longitude,type,"Synthetic player report in "+type,null,
            "Synthetic MSFS version; not live evidence",time??Now,result,result==AirportVisitResult.Verified?AirportIssue.None:AirportIssue.RunwayUnusable,
            result==AirportVisitResult.Verified?"Synthetic visit":"Synthetic unusable runway report");
    }
    [Theory][InlineData("Missionary")][InlineData("Industrial")][InlineData("Safari")][InlineData("Offshore")]
    public async Task EveryCareerHasSixDifferentSuitableDestinationsBeforeAnySimulatorVisit(string category)
    {
        var c=new EmployerDestinationCatalogue();var e=Employer(category);
        var jobs=await new EmployedCargoJobsService(new MemoryEconomyStateStore(),employer:e,destinations:c,utcNow:()=>Now).LoadAsync();
        Assert.Equal(6,jobs.Offers.Select(j=>j.DestinationLocationId).Distinct().Count());
        Assert.All(jobs.Offers,j=>{Assert.Equal(e.Name,j.Employer);Assert.Equal(e.HourlyWage,j.HourlyWage);Assert.Contains(j.CargoCategory,e.CargoMissions);Assert.Contains(c.Diagnose(category),d=>d.Enabled&&d.Resolved!.StableId==j.DestinationLocationId);});
        Assert.Contains(c.Diagnose(category),d=>d.Enabled&&d.Resolved!.VerificationState==LocationVerificationState.ResearchCandidate);
        Assert.Empty(c.Visits);Assert.False(CompanyAircraft.Types.Single(t=>t.Id=="h125").Enabled);
    }
    [Theory][InlineData("FYAM",-23.6558,19.3517)][InlineData("NA-0114",-23.51502,18.789507)][InlineData("NA-0014",-22.95439910888672,17.639699935913086)]
    public void CommunityRoutesPreserveSourcedCoordinatesAndDatabaseKeysNeverBecomeIcao(string key,double lat,double lon)
    {
        var c=new EmployerDestinationCatalogue();var d=Destination(c,key);Assert.True(d.Enabled);var a=d.Resolved!;
        Assert.Equal(lat,a.ApproximateCoordinates!.Latitude);Assert.Equal(lon,a.ApproximateCoordinates.Longitude);
        Assert.Equal(LocationVerificationState.ResearchCandidate,a.VerificationState);Assert.Null(a.VerificationEvidence);
        if(key.StartsWith("NA-")){Assert.Null(a.AirportIdentifier.Value);Assert.Contains(key,a.Name);Assert.Equal(AirportIdentifierConfidence.Unknown,a.AirportIdentifier.Confidence);}
        Assert.DoesNotContain(c.Diagnose("Industrial"),d=>d.Research.Key==key);
    }
    [Theory][InlineData("FYKW")][InlineData("FYHV")][InlineData("FYHO")][InlineData("FYNG")][InlineData("FYAD")][InlineData("FYGK")][InlineData("candidate-geluk")][InlineData("FYRP")][InlineData("FYNP")]
    public void KnownIdentityClosureAndCoordinateConflictsStayHeldEvenWithApprovedLegacyReview(string key)
    {
        var review=key.StartsWith("candidate-")?Array.Empty<DestinationReview>():[new DestinationReview(key,"Approved",key,-22,16,"Synthetic manual record cannot clear research conflict",Now,[])];
        var c=new EmployerDestinationCatalogue(review);var rows=c.Diagnose().Where(d=>d.Research.Key==key).ToArray();
        Assert.NotEmpty(rows);Assert.All(rows,d=>{Assert.False(d.Enabled);Assert.Contains("Held",d.Reason);});
    }
    [Fact] public void RotationIsRepeatableWithinCadenceAndVariedAcrossCadencesWithoutBreakingAcceptedRouteEligibility()
    {
        var c=new EmployerDestinationCatalogue();var e=Employer("Missionary");var all=c.Diagnose("Missionary");
        var first=CompanyEmployment.SelectDestinations(e,all,Now).Select(d=>d.Research.Key).ToArray();
        Assert.Equal(first,CompanyEmployment.SelectDestinations(e,all,Now.AddMinutes(10)).Select(d=>d.Research.Key));
        var chosen=Enumerable.Range(0,10).SelectMany(i=>CompanyEmployment.SelectDestinations(e,all,Now.AddHours(3*i))).Select(d=>d.Research.Key).Distinct().ToArray();
        Assert.True(chosen.Length>6);Assert.Contains("FYAM",first);Assert.Contains("NA-0114",first);Assert.Contains("NA-0014",first);
        Assert.All(chosen,key=>Assert.True(all.Single(d=>d.Research.Key==key).Enabled));
    }
    [Fact] public async Task RotationUsesTheExistingPersistedOfferClockInsteadOfPrematureUtcHourBoundaries()
    {
        var now=Now.AddHours(1).AddMinutes(15);var store=new MemoryEconomyStateStore();var service=new EmployedCargoJobsService(store,employer:Employer("Missionary"),utcNow:()=>now);
        var first=(await service.LoadAsync()).Offers.Select(j=>j.DestinationLocationId).Distinct().Order().ToArray();
        now=Now.AddHours(3).AddMinutes(59); // 15:59; before the saved 16:15 cadence, past a UTC three-hour boundary.
        var same=(await service.LoadAsync()).Offers.Select(j=>j.DestinationLocationId).Distinct().Order().ToArray();Assert.Equal(first,same);
    }
    [Fact] public async Task ScheduledRefreshShowsFreshOffersFirstWithoutReplacingAcceptedOrStillValidWork()
    {
        var now=Now;var store=new MemoryEconomyStateStore();var service=new EmployedCargoJobsService(store,employer:Employer("Missionary"),utcNow:()=>now);
        var first=await service.LoadAsync();var accepted=first.Offers.First();service.SelectedAircraftTypeId="c208";await service.AcceptAsync(accepted.JobId);
        var before=(await store.LoadAsync())!;now=Now.AddHours(3).AddSeconds(1);
        var next=await service.LoadAsync();Assert.NotEmpty(next.Offers);
        Assert.True(next.Offers[0].ExpiresUtc>first.Offers[0].ExpiresUtc);
        Assert.Equal(next.Offers.Select(j=>j.ExpiresUtc).OrderByDescending(x=>x),next.Offers.Select(j=>j.ExpiresUtc));
        Assert.Contains(next.Offers,j=>first.Offers.Any(old=>old.JobId==j.JobId));
        Assert.Contains(next.Offers,j=>first.Offers.All(old=>old.JobId!=j.JobId));Assert.Contains(next.ReservedJobs,j=>j.JobId==accepted.JobId);
        var after=(await store.LoadAsync())!;Assert.Equal(before.Jobs.Single(j=>j.Id==accepted.JobId),after.Jobs.Single(j=>j.Id==accepted.JobId));
        Assert.Equal(before.Shipments,after.Shipments);Assert.Equal(before.FinancialLedger,after.FinancialLedger);
    }
    [Fact] public void VisitPersistenceIsAirportLevelWithAircraftAsContextOnly()
    {
        var c=new EmployerDestinationCatalogue();var store=new AirportVisitStore(root);var report=Visit(c,"FYAM");
        var before=c.Resolve(EmployerDestinationCatalogue.Research.First(r=>r.Key=="FYAM"))!;
        store.Append(report);store.Append(report);Assert.Single(new AirportVisitStore(root).Load());
        var resumed=new DestinationReviewStore(root).Load();
        Assert.Contains("Verified by player",resumed.Describe(before.StableId,"c208"));Assert.Equal(resumed.Describe(before.StableId,"c208"),resumed.Describe(before.StableId,"c152"));
        var resolved=resumed.Find(before.StableId)!;Assert.Equal(before.ApproximateCoordinates,resolved.ApproximateCoordinates);Assert.Equal(before.VerificationState,resolved.VerificationState);Assert.Null(resolved.VerificationEvidence);Assert.False(Directory.Exists(Path.Combine(root,"economy")));
        Assert.Empty(resumed.Reviews);Assert.Throws<IOException>(()=>store.Append(report with{Note="Conflicting retry"}));
        new CareerStore(root).RequestReset(true);new CareerStore(root).Load();Assert.Single(store.Load());
    }
    [Fact] public void AircraftContextNeverScopesAirportVerificationAndPlayerReportsCannotClearTechnicalHolds()
    {
        var c=new EmployerDestinationCatalogue();var a=Visit(c,"FYAM");var b=Visit(c,"FYAM",type:"c152",time:Now.AddMinutes(1));
        c.ApplyVisits([a,b]);var id=a.LocationId;
        Assert.Contains("Verified by player",c.Describe(id,"c208"));Assert.Contains("Verified by player",c.Describe(id,"c152"));Assert.Contains("Verified by player",c.Describe(id,"baron-g58"));
        var held=new EmployerDestinationCatalogue([new("FYNG","Approved","FYNG",-19,16,"Synthetic retained route",Now,[])]);
        held.ApplyVisits([Visit(held,"FYNG",type:"h125")]);Assert.False(Destination(held,"FYNG","Safari").Enabled);Assert.Contains("Held",held.Describe(held.Visits.Single().LocationId,"h125"));
        Assert.False(CompanyAircraft.Types.Single(t=>t.Id=="h125").Enabled);
    }
    [Fact] public void LegacyAircraftUnsuitableReportsNeverBlockJobsAndGoodVisitsApplyToTheAirport()
    {
        var c=new EmployerDestinationCatalogue();var bad=Visit(c,"FYAM",AirportVisitResult.Problematic) with{Issue=AirportIssue.AircraftUnsuitable};
        c.ApplyVisits([bad]);Assert.True(Destination(c,"FYAM").Enabled);Assert.Null(c.UnresolvedProblem("FYAM"));
        var goodOther=Visit(c,"FYAM",type:"c152",time:Now.AddMinutes(1));c.ApplyVisits([bad,goodOther]);
        Assert.True(Destination(c,"FYAM").Enabled);Assert.Contains("Verified by player",c.Describe(bad.LocationId,"c208"));
        c.ApplyVisits([bad,goodOther,Visit(c,"FYAM",type:"c208",time:Now.AddMinutes(2))]);Assert.True(Destination(c,"FYAM").Enabled);
        Assert.Contains("Verified by player",c.Describe(bad.LocationId,"c208"));
    }
    [Fact] public async Task ProblemReportWithdrawsFutureOffersAndPreservesAcceptedCargoAndFinancialHistoryAcrossRestart()
    {
        var c=new EmployerDestinationCatalogue();var e=Employer("Missionary");var path=Path.Combine(root,"economy","economy-state.json");var store=new LocalEconomyStateStore(path);
        var service=new EmployedCargoJobsService(store,employer:e,destinations:c,utcNow:()=>Now);var board=await service.LoadAsync();
        var airport=Destination(c,"FYAM").Resolved!;var job=board.Offers.First(j=>j.DestinationLocationId==airport.StableId);service.SelectedAircraftTypeId="c208";await service.AcceptAsync(job.JobId);
        var before=(await store.LoadAsync())!;new AirportVisitStore(root).Append(Visit(c,"FYAM",AirportVisitResult.Problematic));
        var resumed=new DestinationReviewStore(root).Load();Assert.False(Destination(resumed,"FYAM").Enabled);
        var next=new EmployedCargoJobsService(new LocalEconomyStateStore(path),employer:e,destinations:resumed,utcNow:()=>Now);var after=await next.LoadAsync();
        Assert.DoesNotContain(after.Offers,j=>j.DestinationLocationId==airport.StableId);Assert.Contains(after.ReservedJobs,j=>j.JobId==job.JobId);
        var world=(await store.LoadAsync())!;Assert.Equal(before.Shipments,world.Shipments);Assert.Equal(before.FinancialLedger,world.FinancialLedger);
        Assert.Equal(before.Jobs.Single(j=>j.Id==job.JobId),world.Jobs.Single(j=>j.Id==job.JobId));Assert.Equal(airport.ApproximateCoordinates,resumed.Find(airport.StableId)!.ApproximateCoordinates);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>next.AcceptAsync(before.Jobs.First(j=>j.State==EconomyJobState.Offered&&before.Requirements.Single(r=>r.Id==j.RequirementId).DestinationLocationId==airport.StableId).Id));
    }
    [Fact] public void CorruptAndFutureVisitFilesArePreserved()
    {
        Directory.CreateDirectory(root);var store=new AirportVisitStore(root);File.WriteAllText(store.StoragePath,"{\"SchemaVersion\":999,\"Visits\":[]}");
        Assert.Throws<IOException>(()=>store.Append(Visit(new(),"FYAM")));Assert.Contains("999",File.ReadAllText(store.StoragePath));
        File.WriteAllText(store.StoragePath,"broken");Assert.Throws<IOException>(()=>store.Load());Assert.Equal("broken",File.ReadAllText(store.StoragePath));
    }
    [Fact] public void SchemaOneReviewsRemainIntactAndDismissalMigrationIsIdempotentWithoutAddingVisits()
    {
        Directory.CreateDirectory(root);var store=new AirportVisitStore(root);var report=Visit(new(),"FYAM",AirportVisitResult.Problematic) with {Issue=AirportIssue.AircraftUnsuitable};
        var json=new System.Text.Json.JsonSerializerOptions{Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
        File.WriteAllText(store.StoragePath,System.Text.Json.JsonSerializer.Serialize(new {SchemaVersion=1,Visits=new[]{report}},json));
        var original=File.ReadAllText(store.StoragePath);Assert.Equal(report,Assert.Single(store.Load()));Assert.Empty(store.LoadDismissals());Assert.Equal(original,File.ReadAllText(store.StoragePath));
        store.Dismiss(new("FYAM",Now));store.Dismiss(new("FYAM",Now.AddMinutes(1)));
        var resumed=new AirportVisitStore(root);Assert.Single(resumed.LoadDismissals());Assert.Equal(report,Assert.Single(resumed.Load()));Assert.Equal(2,JsonNode.Parse(File.ReadAllText(store.StoragePath))!["SchemaVersion"]!.GetValue<int>());
        var catalogue=new DestinationReviewStore(root).Load();Assert.True(Destination(catalogue,"FYAM").Enabled);
    }
    [Fact] public void InconsistentFacilityPointIsHeldAndDatabasePresenceNeverClaimsRunwayAcceptance()
    {
        var r=CuratedNamibiaDestinations.Find("FYAM")!;
        var scan=new SimulatorAirportScan(Now,"Synthetic lookup",[new("FYAM",r.Latitude,r.Longitude)]);
        var c=new EmployerDestinationCatalogue(simulatorCache:new(1,scan,[new(scan.Airports[0],Now,"Synthetic lookup")]));
        Assert.True(Destination(c,"FYAM").Enabled);Assert.Null(Destination(c,"FYAM").Resolved!.VerificationEvidence);
        var wrong=scan.Airports[0] with {Latitude=-18};c.ApplySimulatorCache(new(1,scan with {Airports=[wrong]},[new(wrong,Now,"Synthetic first wrong association")]));
        Assert.False(Destination(c,"FYAM").Enabled);Assert.Contains("5 NM",Destination(c,"FYAM").Reason);
    }
    [Fact] public async Task SchemaFourteenUpgradeRetainsJobsInventoryWagesAndPhysicalOwnership()
    {
        var store=new LocalEconomyStateStore(Path.Combine(root,"economy-state.json"));var service=new EmployedCargoJobsService(store,utcNow:()=>Now);await service.LoadAsync();var before=(await store.LoadAsync())!;
        var json=JsonNode.Parse(File.ReadAllText(store.StoragePath))!;json["SchemaVersion"]=14;File.WriteAllText(store.StoragePath,json.ToJsonString());
        var after=(await new LocalEconomyStateStore(store.StoragePath).LoadAsync())!;
        Assert.Equal(before.Jobs,after.Jobs);Assert.Equal(before.Inventories,after.Inventories);Assert.Equal(before.FinancialLedger,after.FinancialLedger);Assert.Equal(before.Shipments,after.Shipments);Assert.Equal(before.CargoAirframes,after.CargoAirframes);Assert.Equal(before.PassengerTransports,after.PassengerTransports);
        Assert.Equal(LocalEconomyStateStore.CurrentSchemaVersion,JsonNode.Parse(File.ReadAllText(store.StoragePath))!["SchemaVersion"]!.GetValue<int>());
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
