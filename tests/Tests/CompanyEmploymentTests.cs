using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class CompanyEmploymentTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"FrontierEmployerTests",Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-15T12:00:00Z");
    private static CareerEmployer Employer(string category)=>CareerCatalogue.Employers.Single(x=>x.Category==category);
    private static DestinationReview Synthetic(string code,double latitude=-22,double longitude=16,string[]? aircraft=null)=>new(code,"Approved",code,latitude,longitude,"Synthetic automated fixture; not live MSFS evidence",Now,aircraft??[]);
    [Theory]
    [InlineData("Missionary","eros","c152,c208")]
    [InlineData("Industrial","eros","c208,baron-g58")]
    [InlineData("Safari","eros","bonanza,pa28,savage-cub")]
    [InlineData("Offshore","walvis-bay","vision-jet,h125")]
    public void FourProfilesHaveRequestedBasesFleetsAndCargoCategories(string category,string baseId,string fleet)
    {
        var employer=Employer(category);Assert.Equal(baseId,employer.BaseId);Assert.Equal(fleet.Split(','),employer.Fleet);
        Assert.Equal(3,employer.CargoMissions.Length);Assert.True(employer.PreferredMinimumMinutes<=employer.PreferredMaximumMinutes);
        Assert.All(employer.Fleet,id=>Assert.Contains(CompanyAircraft.Types,x=>x.Id==id));
    }
    [Fact] public void ResearchRetainsAllMembershipsUnknownRunwaysAndSharedAirports()
    {
        var rows=EmployerDestinationCatalogue.Research;Assert.Equal(53,rows.Count);
        Assert.Equal(16,rows.Count(x=>x.Career=="Missionary"));Assert.Equal(12,rows.Count(x=>x.Career=="Industrial"));
        Assert.Equal(17,rows.Count(x=>x.Career=="Safari"));Assert.Equal(8,rows.Count(x=>x.Career=="Offshore"));
        Assert.Equal(48,rows.Select(x=>x.Key).Distinct().Count());
        Assert.Equal(new[]{"Industrial","Offshore"},rows.Where(x=>x.Identifier=="FYAR").Select(x=>x.Career));
        Assert.Contains(rows,x=>x.RunwayDataVerified=="Unknown"&&x.Identifier=="FYUS");
        Assert.Contains(rows,x=>x.Location=="Ehomba"&&x.Identifier=="");
    }
    [Fact] public void RecognitionDoesNotPromoteRealWorldResearchOrBaseMapMarker()
    {
        var catalogue=new EmployerDestinationCatalogue();var diagnostics=catalogue.Diagnose();
        Assert.Equal(4,diagnostics.Count(x=>x.Resolved is not null)); // FYAR belongs to two pools.
        Assert.Equal(2,diagnostics.Count(x=>x.Enabled));Assert.All(diagnostics.Where(x=>x.Enabled),x=>Assert.Equal("Industrial",x.Employer));
        Assert.Null(catalogue.Base(Employer("Offshore")));
        Assert.All(diagnostics.Where(x=>x.Resolved is null),x=>Assert.False(x.Enabled));
    }
    [Theory][InlineData("Missionary")][InlineData("Industrial")][InlineData("Safari")][InlineData("Offshore")]
    public void AcceptancePersistsAssignmentAndAccessWithoutCreatingOwnedAircraft(string category)
    {
        var store=new CareerStore(root);var vm=new CareerViewModel(store);vm.Continue();vm.PilotName="Test Pilot";vm.Continue();vm.SelectedRegion=vm.Regions[0];vm.Continue();
        vm.SelectedEmployer=Employer(category);vm.AcceptEmployment();var saved=new CareerStore(root).Load();
        Assert.True(saved.IsActive);Assert.Equal("namibia",saved.RegionId);Assert.Equal(Employer(category).Id,saved.Employment!.EmployerId);
        Assert.Equal(Employer(category).Fleet,saved.Employment.FleetAccess);Assert.False(Directory.Exists(Path.Combine(root,"tracking")));
        Assert.False(Directory.Exists(Path.Combine(root,"economy")));Assert.Throws<IOException>(()=>store.AcceptEmployment(Employer("Industrial").Id));
    }
    [Theory][InlineData("Missionary","FYOJ")][InlineData("Industrial","FYAR")][InlineData("Safari","FYNG")][InlineData("Offshore","FYWE")]
    public async Task SharedGeneratorUsesEmployerPoolCategoriesAndNormalReservation(string category,string destination)
    {
        var catalogue=new EmployerDestinationCatalogue(new[]{Synthetic("FYOJ"),Synthetic("FYNG"),Synthetic("FYWB",-23,14.6)});
        var store=new LocalEconomyStateStore(Path.Combine(root,"economy-state.json"));
        var service=new EmployedCargoJobsService(store,utcNow:()=>Now,employer:Employer(category),destinations:catalogue);
        var board=await service.LoadAsync();Assert.NotEmpty(board.Offers);
        Assert.All(board.Offers,job=>
        {
            Assert.Equal(Employer(category).Name,job.Employer);Assert.Contains(job.CargoCategory,Employer(category).CargoMissions);
            Assert.Contains(catalogue.Diagnose(category),d=>d.Enabled&&d.Resolved?.StableId==job.DestinationLocationId);
            Assert.DoesNotContain("passenger",job.CargoCategory,StringComparison.OrdinalIgnoreCase);
        });
        var selected=board.Offers.First(x=>x.Destination.Contains(destination));
        var choice=service.AircraftChoices(selected).First();Assert.NotEqual("h125",choice.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.AcceptAsync(selected.JobId));
        service.SelectedAircraftTypeId=choice.Id;await service.AcceptAsync(selected.JobId);
        var world=(await store.LoadAsync())!;Assert.Single(world.Shipments);Assert.Empty(world.FinancialLedger);
        Assert.Equal(choice.Id,world.Jobs.Single(x=>x.Id==selected.JobId).CompanyAircraftTypeId);
        var resumed=new EmployedCargoJobsService(new LocalEconomyStateStore(store.StoragePath),utcNow:()=>Now,employer:Employer(category),destinations:catalogue);
        Assert.Equal(choice.Id,Assert.Single((await resumed.LoadAsync()).ReservedJobs).CompanyAircraftTypeId);
    }
    [Theory][InlineData("Missionary")][InlineData("Safari")][InlineData("Offshore")]
    public async Task UnresolvedPoolsCannotCreateBrokenJobs(string category)
    {
        var service=new EmployedCargoJobsService(new MemoryEconomyStateStore(),utcNow:()=>Now,employer:Employer(category));
        Assert.Empty((await service.LoadAsync()).Offers);
    }
    [Fact] public async Task LegacyGeneratorAndExistingHistoryRemainUnchanged()
    {
        var service=new EmployedCargoJobsService(new MemoryEconomyStateStore(),utcNow:()=>Now);
        var board=await service.LoadAsync();Assert.Equal(2,board.Offers.Count);await service.AcceptAsync(board.Offers[0].JobId);
        Assert.Null(Assert.Single((await service.LoadAsync()).ReservedJobs).CompanyAircraftTypeId);
    }
    [Fact] public async Task RejectedDestinationBlocksNewAcceptanceButPreservesReservedCargo()
    {
        var employer=Employer("Industrial");var store=new MemoryEconomyStateStore();
        var first=new EmployedCargoJobsService(store,utcNow:()=>Now,employer:employer);var board=await first.LoadAsync();
        var job=board.Offers.First(x=>x.Destination.Contains("FYAR"));first.SelectedAircraftTypeId="c208";await first.AcceptAsync(job.JobId);
        var before=(await store.LoadAsync())!;
        var rejected=new EmployerDestinationCatalogue([new("FYAR","Rejected","FYAR",null,null,"Synthetic rejection fixture",Now,[])]);
        var next=new EmployedCargoJobsService(store,utcNow:()=>Now,employer:employer,destinations:rejected);
        var after=await next.LoadAsync();Assert.DoesNotContain(after.Offers,x=>x.Destination.Contains("FYAR"));Assert.Single(after.ReservedJobs);
        Assert.Equal(before.Shipments,(await store.LoadAsync())!.Shipments);Assert.Equal(before.FinancialLedger,(await store.LoadAsync())!.FinancialLedger);
    }
    [Fact] public void CuratedAircraftConstraintsAndExactAddonMappingsDoNotGuessAliases()
    {
        var catalogue=new EmployerDestinationCatalogue([Synthetic("FYNG",aircraft:["savage-cub"])]);
        var destination=catalogue.Diagnose("Safari").Single(x=>x.Identifier=="FYNG");
        Assert.Equal("savage-cub",Assert.Single(CompanyAircraft.Choices(Employer("Safari"),destination,10)).Id);
        Assert.Empty(CompanyAircraft.Choices(Employer("Safari"),destination,20));
        Assert.Null(CompanyAircraft.Resolve(new("Addon Caravan","C208"),[]));
        AircraftTypeBinding[] mappings=[new("Addon Caravan","C208","c208")];
        Assert.Equal("c208",CompanyAircraft.Resolve(new("Addon Caravan","C208"),mappings));
        Assert.Null(CompanyAircraft.Resolve(new("Other Caravan","C208"),mappings));
    }
    [Fact] public void ReviewStorePreservesCorruptFutureAndImmutableApprovedCoordinates()
    {
        var store=new DestinationReviewStore(root);store.Save(Synthetic("FYNG"));Assert.NotNull(store.Load().Find("namibia.ongava-lodge"));
        Assert.Throws<IOException>(()=>store.Save(Synthetic("FYNG",-21,15)));
        var path=Path.Combine(root,"destination-reviews.json");File.WriteAllText(path,"{\"SchemaVersion\":999,\"Reviews\":[]}");
        Assert.Throws<IOException>(()=>store.Save(Synthetic("FYNG")));Assert.Contains("999",File.ReadAllText(path));
    }
    [Fact] public void RejectionRetainsOriginalInspectionAndResetRetainsCuration()
    {
        var store=new DestinationReviewStore(root);store.Save(Synthetic("FYNG"));
        store.Save(Synthetic("FYNG") with {Status="Rejected",Evidence="Synthetic later rejection",RecordedUtc=Now.AddDays(1)});
        var catalogue=store.Load();Assert.False(catalogue.Diagnose("Safari").Single(x=>x.Identifier=="FYNG").Enabled);
        Assert.Equal("Synthetic automated fixture; not live MSFS evidence",catalogue.Find("namibia.ongava-lodge")!.VerificationEvidence!.Notes);
        var career=new CareerStore(root);career.RequestReset(true);career.Load();Assert.Single(store.Load().Reviews);
    }
    [Fact] public void ResearchWithUnapprovedCoordinatesDoesNotBecomeSimulatorEvidence()
    {
        var catalogue=new EmployerDestinationCatalogue([Synthetic("FYNG") with {Status="Needs investigation"}]);
        Assert.Null(catalogue.Find("namibia.ongava-lodge"));
        var rejectedBase=new EmployerDestinationCatalogue([new("FYWE","Rejected","",null,null,"Synthetic rejected base",Now,[])]);
        Assert.Empty(rejectedBase.Diagnose("Industrial").Where(x=>x.Enabled));
    }
    [Fact] public void TimeEnvelopeIsABiasNotAHardExclusion()
    {
        var catalogue=new EmployerDestinationCatalogue([Synthetic("FYOJ",-18,16)]);var employer=Employer("Missionary");
        var world=CompanyEmployment.Create(employer,catalogue,Now);Assert.NotEmpty(world.Jobs);
        Assert.True(CompanyEmployment.TimePenalty(catalogue.Base(employer)!,catalogue.Find("namibia.curated-fyoj")!,employer)>0);
    }
    [Fact] public async Task CuratedRouteUsesExistingTrackerProofAndSingleLedgerPayment()
    {
        var catalogue=new EmployerDestinationCatalogue([Synthetic("FYOJ")]);var employer=Employer("Missionary");
        var store=new MemoryEconomyStateStore();var service=new EmployedCargoJobsService(store,utcNow:()=>Now,employer:employer,destinations:catalogue);
        var offer=(await service.LoadAsync()).Offers.First(x=>x.CargoMassKilograms==10);service.SelectedAircraftTypeId="c152";await service.AcceptAsync(offer.JobId);
        var tracker=new FlightAttemptTracker(store,new MemoryFlightAttemptStore(),utcNow:()=>Now.AddMinutes(1)){AdditionalLocationResolver=catalogue.Find};
        await tracker.InitializeAsync();await tracker.SelectJobsAsync([offer.JobId]);
        var a=catalogue.Base(employer)!.ApproximateCoordinates!;var b=catalogue.Find(offer.DestinationLocationId!)!.ApproximateCoordinates!;
        AircraftTelemetry At(int second,double lat,double lon,bool ground=true)=>new(Now.AddSeconds(second),"Synthetic C152","C152",lat,lon,ground?500:1500,0,ground?0:100,ground);
        for(var t=0;t<=10;t++)await tracker.ObserveAsync(At(t,a.Latitude,a.Longitude));
        Assert.True(tracker.Current.CanLoadCargo);await tracker.LoadCargoAsync();
        await tracker.ObserveAsync(At(11,a.Latitude,a.Longitude,false));
        await tracker.ObserveAsync(At(12,(a.Latitude+b.Latitude)/2,(a.Longitude+b.Longitude)/2,false));
        for(var t=13;t<=24;t++)await tracker.ObserveAsync(At(t,b.Latitude,b.Longitude));
        Assert.True(tracker.Current.CanUnloadCargo);await tracker.UnloadCargoAsync();await tracker.UnloadCargoAsync();
        var completed=(await store.LoadAsync())!;Assert.Single(completed.FinancialLedger);Assert.Equal(ShipmentState.Delivered,Assert.Single(completed.Shipments).State);
        Assert.Equal(offer.PaymentAmount,completed.Players.Single().CachedBalance);
    }
    [Fact] public async Task DispatchRefusesDifferentTypesAndOverBudgetGroupedLoads()
    {
        var service=new EmployedCargoJobsService(new MemoryEconomyStateStore(),utcNow:()=>Now,employer:Employer("Industrial"));
        var job=(await service.LoadAsync()).Offers.First(x=>x.CargoMassKilograms==80) with {CompanyAircraftTypeId="baron-g58"};
        AircraftTypeBinding[] bindings=[new("Synthetic Baron","BE58","baron-g58")];var identity=new FlightAircraftIdentity("Synthetic Baron","BE58");
        Assert.Null(CompanyAircraft.CheckDispatch(identity,[job],bindings));
        Assert.NotNull(CompanyAircraft.CheckDispatch(identity,[job,job],bindings));
        Assert.NotNull(CompanyAircraft.CheckDispatch(identity,[job with {CompanyAircraftTypeId="c208"}],bindings));
        Assert.NotNull(CompanyAircraft.CheckDispatch(identity,[job],[]));
    }
    [Fact] public async Task SchemaSevenEconomyMigratesWithoutAlteringJobsOrMoney()
    {
        var store=new LocalEconomyStateStore(Path.Combine(root,"economy-state.json"));var service=new EmployedCargoJobsService(store,utcNow:()=>Now);
        await service.LoadAsync();var before=(await store.LoadAsync())!;
        var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(store.StoragePath))!;json["SchemaVersion"]=7;File.WriteAllText(store.StoragePath,json.ToJsonString());
        var after=(await new LocalEconomyStateStore(store.StoragePath).LoadAsync())!;
        Assert.Equal(before.Jobs,after.Jobs);Assert.Equal(before.FinancialLedger,after.FinancialLedger);Assert.Equal(before.Inventories,after.Inventories);
        Assert.Equal(8,System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(store.StoragePath))!["SchemaVersion"]!.GetValue<int>());
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
