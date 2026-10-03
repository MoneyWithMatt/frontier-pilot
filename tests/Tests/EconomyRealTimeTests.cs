using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class EconomyRealTimeTests : TemporaryDirectoryTest
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-03T10:00:00Z");
    private static EconomyWorld Initial() => EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
    [Fact] public void ActivationPreservesStockHistoryAndAssignsLegacyOfferDeadlines()
    {
        var before=Initial();var after=EconomyRealTime.Advance(before,Start).World;
        Assert.Equal(before.Inventories,after.Inventories);Assert.Equal(before.FinancialLedger,after.FinancialLedger);
        Assert.Equal(before.Jobs.Select(x=>x.Id),after.Jobs.Select(x=>x.Id));
        Assert.All(after.Jobs,x=>Assert.Equal(Start.AddHours(6),x.ExpiresUtc));
        Assert.False(EconomyRealTime.Advance(after,Start).Changed);
        Assert.False(EconomyRealTime.Advance(after,Start.AddSeconds(30)).Changed);
    }
    [Fact] public void OffersGenerateAtThreeHoursAndExpireAtSixWithoutPaymentOrCargoLoss()
    {
        var world=EconomyRealTime.Advance(Initial(),Start).World;
        Assert.False(EconomyRealTime.Advance(world,Start.AddHours(3).AddTicks(-1)).Changed);
        world=EconomyRealTime.Advance(world,Start.AddHours(3)).World;
        Assert.Equal(4,world.Jobs.Count(x=>x.State==EconomyJobState.Offered));
        var stock=world.Inventories;world=EconomyRealTime.Advance(world,Start.AddHours(6)).World;
        Assert.Equal(2,world.Jobs.Count(x=>x.State==EconomyJobState.Expired));
        Assert.Equal(4,world.Jobs.Count(x=>x.State==EconomyJobState.Offered));
        Assert.Equal(stock,world.Inventories);Assert.Empty(world.FinancialLedger);Assert.Empty(world.Shipments);
        Assert.False(EconomyRealTime.Advance(world,Start.AddHours(6)).Changed);
    }
    [Fact] public void AcceptanceAtDeadlineIsRefusedAndAcceptedCargoNeverExpires()
    {
        var world=EconomyRealTime.Advance(Initial(),Start).World;
        Assert.Throws<InvalidOperationException>(()=>EconomyKernel.AcceptJob(world,"job-uis-supplies-v0",Start.AddHours(6)));
        world=EconomyKernel.AcceptJob(world,"job-uis-supplies-v0",Start.AddHours(1)).World;
        world=EconomyKernel.MarkShipmentLoaded(world,"job-uis-supplies-v0").World;
        world=EconomyRealTime.Advance(world,Start.AddDays(3)).World;
        Assert.Equal(EconomyJobState.Accepted,world.Jobs.Single(x=>x.Id=="job-uis-supplies-v0").State);
        Assert.Equal(ShipmentState.Loaded,Assert.Single(world.Shipments).State);
        Assert.Empty(world.FinancialLedger);
    }
    [Fact] public async Task RestartAndConcurrentRefreshDoNotDuplicateOffersOrWrites()
    {
        var path=Path.Combine(Root,"economy-state.json");var first=new EconomyService(new LocalEconomyStateStore(path));
        await first.InitializeAsync(Initial());await first.AdvanceRealTimeAsync(Start);
        var second=new EconomyService(new LocalEconomyStateStore(path));
        await Task.WhenAll(first.AdvanceRealTimeAsync(Start.AddHours(7)),second.AdvanceRealTimeAsync(Start.AddHours(7)));
        var world=(await second.LoadAsync())!;
        Assert.Equal(6,world.Jobs.Count);Assert.Equal(4,world.Jobs.Count(x=>x.State==EconomyJobState.Offered));Assert.Equal(2,world.Jobs.Count(x=>x.State==EconomyJobState.Expired));
        Assert.False((await second.AdvanceRealTimeAsync(Start.AddHours(7))).Changed);
        Assert.False((await second.AdvanceRealTimeAsync(Start.AddHours(-1))).Changed);
    }
    [Fact] public async Task FailedCheckpointPreservesPreviousClockAndOffers()
    {
        var path=Path.Combine(Root,"economy-state.json");var good=new EconomyService(new LocalEconomyStateStore(path));
        await good.InitializeAsync(Initial());await good.AdvanceRealTimeAsync(Start);
        var bytes=await File.ReadAllBytesAsync(path);
        var failing=new EconomyService(new LocalEconomyStateStore(path,_=>throw new IOException("Simulated interrupted replace")));
        await Assert.ThrowsAsync<IOException>(()=>failing.AdvanceRealTimeAsync(Start.AddHours(7)));
        Assert.Equal(bytes,await File.ReadAllBytesAsync(path));
        await good.AdvanceRealTimeAsync(Start.AddHours(7));
        Assert.False((await good.AdvanceRealTimeAsync(Start.AddHours(7))).Changed);
    }
    [Fact] public void OfflineCatchUpSkipsObsoleteBoardsAndProducesEachRealDayOnce()
    {
        var world=Initial();world=EconomyKernel.AcceptJob(world,"job-uis-supplies-v0",Start.AddHours(-2)).World;
        world=EconomyKernel.MarkShipmentInTransit(world,"job-uis-supplies-v0").World;
        world=EconomyKernel.CompleteDelivery(world,new("earned","test","job-uis-supplies-v0","shipment-uis-supplies-v0",EmployedCargoPrototype.ErosId,EmployedCargoPrototype.UisId,Start.AddHours(-1),"Test evidence")).World;
        var airport="11111111111111111111111111111111";
        world=ProspectingCampKernel.Found(world,new(airport,"CAMP1","Camp",-22.5,14.5,false,[ConstructionCapability.BaseLodge]),
            new("presence","test",airport,42,-22.5,14.5,0,250,Start,Start),"found",Start).World;
        world=EconomyRealTime.Advance(world,Start).World;
        world=EconomyRealTime.Advance(world,Start.AddDays(2)).World;
        Assert.Equal(2,world.Operations.Single().PrototypeDay);Assert.Equal(2,world.OperationTicks.Count);
        var site=world.Sites.Single();Assert.Equal(50,world.Inventories.Single(x=>x.LocationId==site.LocationId&&x.ResourceId=="rations").AvailableQuantity);
        Assert.Equal(40,world.Inventories.Single(x=>x.LocationId==site.LocationId&&x.ResourceId=="raw-minerals").AvailableQuantity);
        Assert.False(EconomyRealTime.Advance(world,Start.AddDays(2)).Changed);
        Assert.Equal(4,world.Jobs.Count(x=>x.State==EconomyJobState.Offered&&x.ExpiresUtc is not null));
    }
}
