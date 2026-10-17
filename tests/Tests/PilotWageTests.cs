using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using System.Text.Json.Nodes;

namespace FrontierAirportBuilder.Tests;

public sealed class PilotWageTests
{
    private static readonly DateTimeOffset Start=DateTimeOffset.Parse("2026-10-17T10:00:00Z");
    [Theory][InlineData("Missionary",400)][InlineData("Safari",400)][InlineData("Industrial",650)][InlineData("Offshore",800)]
    public void ConfiguredRatesAndOnboardingUseSameValue(string category,decimal rate)
    {
        var employer=CareerCatalogue.Employers.Single(x=>x.Category==category);
        Assert.Equal(rate,employer.HourlyWage);Assert.Equal(DollarDisplay.Format(rate)+" / flight hour",employer.WageDisplay);
    }
    [Theory][InlineData(400,15,100)][InlineData(400,30,200)][InlineData(400,45,300)][InlineData(400,60,400)]
    [InlineData(400,90,600)][InlineData(650,30,325)][InlineData(650,47,509.17)][InlineData(650,90,975)][InlineData(800,30,400)]
    public void WageProratesWithoutRoundingHours(decimal rate,double minutes,decimal expected)
        =>Assert.Equal(expected,new PilotWage("flight-test","employer","Test",TimeSpan.FromMinutes(minutes).Ticks,rate).Amount);
    [Theory][InlineData("Missionary",200)][InlineData("Industrial",325)][InlineData("Offshore",400)]
    public async Task ThirtyMinuteFlightPaysOnceThroughTrackerAndLedger(string category,decimal amount)
    {
        var h=await Harness.Create(category);await h.Load();await h.Fly(1800);await h.Tracker.UnloadCargoAsync();
        var paid=(await h.Economy.LoadAsync())!;var transaction=Assert.Single(paid.FinancialLedger);
        Assert.Equal(FinancialTransactionKind.PilotWage,transaction.Kind);Assert.Equal(amount,transaction.Amount);
        Assert.Equal(TimeSpan.FromMinutes(30).Ticks,transaction.Wage!.AirborneTicks);Assert.Equal(h.Employer.Id,transaction.FromEntityId);
        Assert.Equal(h.Offer.JobId,transaction.JobId);Assert.Equal(amount,paid.Players.Single().CachedBalance);
        Assert.Equal(1800,TimeSpan.FromTicks(h.Tracker.Current.Attempt!.VerifiedAirborneTicks).TotalSeconds);
        await h.Tracker.UnloadCargoAsync();h.Restart();await h.Tracker.InitializeAsync();await h.Tracker.UnloadCargoAsync();
        Assert.Single((await h.Economy.LoadAsync())!.FinancialLedger);
        var history=Assert.Single((await h.Service.LoadAsync()).RecentCompletedJobs);
        Assert.Contains("Pilot wages",history.Payment);Assert.Contains("30 min",history.Payment);Assert.Contains(h.Offer.JobId,history.Payment);
    }
    [Fact] public async Task GroundInvalidDisconnectedGapWrongAircraftAndRestartTimeAreNotPaid()
    {
        var h=await Harness.Create("Missionary");await h.Load();
        for(int t=11;t<=70;t++)await h.Tracker.ObserveAsync(h.At(t,false,true));
        Assert.Equal(0,h.Tracker.Current.Attempt!.VerifiedAirborneTicks);
        await h.Tracker.ObserveAsync(h.At(71));await h.Tracker.ObserveAsync(h.At(72));
        await h.Tracker.ObserveAsync(null);await h.Tracker.ObserveAsync(h.At(90));await h.Tracker.ObserveAsync(h.At(91));
        await h.Tracker.ObserveAsync(h.At(100));await h.Tracker.ObserveAsync(h.At(101));
        await h.Tracker.ObserveAsync(h.At(102) with {AircraftTitle="Wrong aircraft"});
        await h.Tracker.ObserveAsync(h.At(103));await h.Tracker.ObserveAsync(h.At(104));
        await h.Tracker.ObserveAsync(h.At(105) with {Latitude=double.NaN});
        await h.Tracker.ObserveAsync(h.At(106));await h.Tracker.ObserveAsync(h.At(107));
        await h.Tracker.ObserveAsync(h.At(107));await h.Tracker.ObserveAsync(h.At(108));await h.Tracker.ObserveAsync(h.At(109));
        Assert.Equal(TimeSpan.FromSeconds(6).Ticks,h.Tracker.Current.Attempt!.VerifiedAirborneTicks);
        h.Restart();await h.Tracker.InitializeAsync();await h.Tracker.ObserveAsync(h.At(200));await h.Tracker.ObserveAsync(h.At(201));
        Assert.Equal(TimeSpan.FromSeconds(7).Ticks,h.Tracker.Current.Attempt!.VerifiedAirborneTicks);
    }
    [Fact] public async Task GroupedPhysicalDeliveriesShareOneFlightWageAndRetryIsIdempotent()
    {
        var h=await Harness.Create("Industrial");
        var second=(await h.Service.LoadAsync()).Offers.First(x=>x.DestinationLocationId==h.Offer.DestinationLocationId&&x.CargoMassKilograms<=20);
        await h.Service.AcceptAsync(second.JobId);await h.Tracker.InitializeAsync();await h.Tracker.SelectJobsAsync([h.Offer.JobId,second.JobId]);
        for(int t=0;t<=10;t++)await h.Tracker.ObserveAsync(h.At(t,false,true));
        var loads=new Dictionary<string,decimal>{{h.Offer.JobId,h.Offer.CargoMassKilograms},{second.JobId,second.CargoMassKilograms}};
        await h.Tracker.ApplyCargoTransfersAsync("wage-load","wage-load",loads,h.Offer.OriginLocationId,h.Offer.OriginLatitude!.Value,h.Offer.OriginLongitude!.Value,false,AircraftLoadingMode.MsfsPayloadIntegration);
        await h.Fly(120);
        var unload=loads.ToDictionary(x=>x.Key,x=>0m);
        await h.Tracker.ApplyCargoTransfersAsync("wage-unload","wage-unload",unload,h.Offer.DestinationLocationId,h.Offer.DestinationLatitude!.Value,h.Offer.DestinationLongitude!.Value,false,AircraftLoadingMode.MsfsPayloadIntegration);
        var world=(await h.Economy.LoadAsync())!;
        Assert.Equal(2,world.Jobs.Count(x=>x.State==EconomyJobState.Completed));Assert.Equal(21.67m,Assert.Single(world.FinancialLedger).Amount);
        Assert.Single(world.Jobs.Where(x=>x.CompletedWage is not null).Select(x=>x.CompletedWage!.FlightId).Distinct());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Tracker.ApplyCargoTransfersAsync("wage-unload","wage-unload",unload,h.Offer.DestinationLocationId,h.Offer.DestinationLatitude.Value,h.Offer.DestinationLongitude.Value,false,AircraftLoadingMode.MsfsPayloadIntegration));
        Assert.Single((await h.Economy.LoadAsync())!.FinancialLedger);
    }
    [Fact] public async Task CompanyOffersShowHourlyRateAndLegacyFreelanceRetainsContractReward()
    {
        var h=await Harness.Create("Safari");Assert.Equal("$400 / flight hour",h.Offer.Payment);
        var companyView=new EmployedCargoJobsViewModel(h.Service,h.Tracker);await companyView.InitializeAsync();Assert.False(companyView.ShowOfferCompensation);Assert.False(companyView.ShowReservedCompensation);
        var legacy=await new EmployedCargoJobsService(new MemoryEconomyStateStore()).LoadAsync();
        Assert.All(legacy.Offers,x=>{Assert.Null(x.HourlyWage);Assert.Equal(DollarDisplay.Format(x.PaymentAmount),x.Payment);Assert.True(x.PaymentAmount>0);});
        Assert.Equal("$2,500",DollarDisplay.Format(2500));Assert.Equal("$509.17",DollarDisplay.Format(509.17m));
    }
    [Fact] public async Task WageAuditTamperingCannotChangeAuthoritativeBalance()
    {
        var h=await Harness.Create("Industrial");await h.Load();await h.Fly(60);await h.Tracker.UnloadCargoAsync();
        var world=(await h.Economy.LoadAsync())!;var payment=Assert.Single(world.FinancialLedger);
        Assert.Throws<InvalidDataException>(()=>(world with {FinancialLedger=[payment with {Amount=payment.Amount+1}]}).EnsureValid());
        Assert.Throws<InvalidDataException>(()=>(world with {FinancialLedger=[payment with {Wage=payment.Wage! with {AirborneTicks=TimeSpan.FromHours(1).Ticks}}]}).EnsureValid());
        Assert.Throws<InvalidDataException>(()=>(world with {FinancialLedger=[]}).EnsureValid());
    }
    [Fact] public async Task FractionalSecondsRemainExactAndNoWageIsPaidBeforeDelivery()
    {
        var h=await Harness.Create("Missionary");await h.Load();
        await h.Tracker.ObserveAsync(h.At(11,false));await h.Tracker.ObserveAsync(h.At(11.25));await h.Tracker.ObserveAsync(h.At(11.5));
        Assert.Equal(TimeSpan.FromSeconds(.5).Ticks,h.Tracker.Current.Attempt!.VerifiedAirborneTicks);
        Assert.Equal(.06m,new PilotWage("flight","employer","Test",h.Tracker.Current.Attempt.VerifiedAirborneTicks,400).Amount);
        Assert.Empty((await h.Economy.LoadAsync())!.FinancialLedger);
    }
    [Fact] public async Task OldAttemptMigrationDoesNotInferAirborneDurationFromTimestamps()
    {
        var root=Path.Combine(Path.GetTempPath(),"PilotWageMigration",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var h=await Harness.Create("Missionary");await h.Load();await h.Fly(60);
            var path=Path.Combine(root,"flight-attempts.json");var store=new LocalFlightAttemptStore(path);await store.SaveAsync(h.Tracker.Current.Attempt!);
            var json=JsonNode.Parse(File.ReadAllText(path))!;json["SchemaVersion"]=4;
            foreach(var entry in json["Attempts"]!.AsArray()){entry!.AsObject().Remove("VerifiedAirborneTicks");entry.AsObject().Remove("WageObservedUtc");entry.AsObject().Remove("WageFlightId");}
            File.WriteAllText(path,json.ToJsonString());var migrated=Assert.Single(await store.LoadAsync());
            Assert.Equal(0,migrated.VerifiedAirborneTicks);Assert.Equal(h.Tracker.Current.Attempt!.DeliveryProof,migrated.DeliveryProof);
            Assert.Equal(5,JsonNode.Parse(File.ReadAllText(path))!["SchemaVersion"]!.GetValue<int>());
            await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveAsync(migrated with {VerifiedAirborneTicks=TimeSpan.FromSeconds(1).Ticks,WageObservedUtc=Start.AddSeconds(50),WageFlightId=migrated.Id}));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact] public async Task PersistedWagesRecoverOnceAndOldBalancesAreNotConverted()
    {
        var root=Path.Combine(Path.GetTempPath(),"PilotWageTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var h=await Harness.Create("Industrial");await h.Load();await h.Fly(60);await h.Tracker.UnloadCargoAsync();
            var world=(await h.Economy.LoadAsync())!;var path=Path.Combine(root,"economy-state.json");var file=new LocalEconomyStateStore(path);await file.InitializeAsync(world);
            var loaded=(await new LocalEconomyStateStore(path).LoadAsync())!;Assert.Equal(world.FinancialLedger,loaded.FinancialLedger);Assert.Equal(world.Players,loaded.Players);
            var attempts=new LocalFlightAttemptStore(Path.Combine(root,"flight-attempts.json"));await attempts.SaveAsync(h.Tracker.Current.Attempt!);
            var tracker=new FlightAttemptTracker(new LocalEconomyStateStore(path),attempts){AdditionalLocationResolver=h.Catalogue.Find};await tracker.InitializeAsync();await tracker.UnloadCargoAsync();
            Assert.Single((await file.LoadAsync())!.FinancialLedger);
            // An actual old, fixed-price completed world remains numerically unchanged after schema migration.
            var old=EconomyPrototypeSeed.Create();old=EconomyKernel.AcceptJob(old,"job-community",Start).World;
            old=EconomyKernel.CompleteDelivery(old,new("old-proof","test","job-community",old.Jobs.Single(x=>x.Id=="job-community").ShipmentId,"prototype-gateway","prototype-community",Start.AddHours(1),"Synthetic old delivery")).World;
            var legacyPath=Path.Combine(root,"legacy","economy-state.json");var legacyStore=new LocalEconomyStateStore(legacyPath);await legacyStore.InitializeAsync(old);
            var json=JsonNode.Parse(File.ReadAllText(legacyPath))!;json["SchemaVersion"]=8;File.WriteAllText(legacyPath,json.ToJsonString());
            var migrated=(await legacyStore.LoadAsync())!;Assert.Equal(old.Players,migrated.Players);Assert.Equal(old.FinancialLedger,migrated.FinancialLedger);
        }
        finally{Directory.Delete(root,true);}
    }
    private sealed class Harness
    {
        public MemoryEconomyStateStore Economy=new();public MemoryFlightAttemptStore Attempts=new();
        public required CareerEmployer Employer;public required EmployerDestinationCatalogue Catalogue;
        public required EmployedCargoJobsService Service;public required CargoJobDisplay Offer;public FlightAttemptTracker Tracker=null!;
        public static async Task<Harness> Create(string category)
        {
            var employer=CareerCatalogue.Employers.Single(x=>x.Category==category);
            var catalogue=new EmployerDestinationCatalogue(new[]{"FYOJ","FYNG","FYWB"}.Select((code,i)=>new DestinationReview(code,"Approved",code,-22+i*.1,16,"Synthetic wage test",Start,[])));
            var economy=new MemoryEconomyStateStore();var service=new EmployedCargoJobsService(economy,utcNow:()=>Start,employer:employer,destinations:catalogue);
            var offer=(await service.LoadAsync()).Offers.First(x=>x.CargoMassKilograms<=20);service.SelectedAircraftTypeId=service.AircraftChoices(offer).First().Id;
            await service.AcceptAsync(offer.JobId);var h=new Harness{Economy=economy,Employer=employer,Catalogue=catalogue,Service=service,Offer=offer};h.Restart();await h.Tracker.InitializeAsync();await h.Tracker.SelectJobsAsync([offer.JobId]);return h;
        }
        public void Restart()=>Tracker=new(Economy,Attempts,utcNow:()=>Start.AddHours(2)){AdditionalLocationResolver=Catalogue.Find};
        public AircraftTelemetry At(double second,bool destination=true,bool ground=false)=>new(Start.AddSeconds(second),"Synthetic aircraft","TEST",
            (destination?Offer.DestinationLatitude:Offer.OriginLatitude)!.Value,(destination?Offer.DestinationLongitude:Offer.OriginLongitude)!.Value,ground?500:2000,0,ground?0:100,ground);
        public async Task Load(){for(int t=0;t<=10;t++)await Tracker.ObserveAsync(At(t,false,true));await Tracker.LoadCargoAsync();}
        public async Task Fly(int seconds)
        {
            for(int t=0;t<=seconds;t++)await Tracker.ObserveAsync(At(11+t,t>0));
            for(int t=0;t<=11;t++)await Tracker.ObserveAsync(At(12+seconds+t,true,true));
            Assert.True(Tracker.Current.CanUnloadCargo);
        }
    }
}
