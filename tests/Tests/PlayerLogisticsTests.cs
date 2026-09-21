using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class PlayerLogisticsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    private const string Airport = "83f20e8a145a44afacb8b8db3a124081";

    [Fact]
    public void PublicationReservesRealCargoAndFundsAndPreventsDoubleAllocation()
    {
        var world = World(); var operation = world.Operations.Single();
        var before = Inventory(world, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RationsResourceId);
        var request = Request("one", operation.Id, OperationLogisticsDirection.Import, OperationArchetypeCatalogue.RationsResourceId, 10, 20, null);
        var published = PlayerLogisticsKernel.Publish(world, request).World;
        Assert.Equal(before - 10, Inventory(published, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RationsResourceId));
        Assert.Equal(25, Assert.Single(published.FinancialReservations).Amount);
        Assert.Equal(EconomyJobState.Offered, published.Jobs.Single(x => x.Id == published.LogisticsContracts.Single().JobId).State);
        Assert.False(PlayerLogisticsKernel.Publish(published, request).Changed);
        Assert.Throws<InvalidOperationException>(() => PlayerLogisticsKernel.Publish(published, Request("capacity", operation.Id,
            OperationLogisticsDirection.Import, OperationArchetypeCatalogue.RationsResourceId, 1, 1, null)));
        Assert.Throws<InvalidOperationException>(() => PlayerLogisticsKernel.Publish(published, Request("too-much", operation.Id,
            OperationLogisticsDirection.Import, OperationArchetypeCatalogue.RationsResourceId, 500, 1, null)));
        Assert.Throws<InvalidOperationException>(() => PlayerLogisticsKernel.Publish(published, Request("overspend", operation.Id,
            OperationLogisticsDirection.Export, OperationArchetypeCatalogue.RawMineralsResourceId, 5, 100, null)));
    }

    [Fact]
    public void CancellationAndPayoutEditingAdjustReservationsSafely()
    {
        var world = World(); var operation = world.Operations.Single(); var before = Inventory(world, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RationsResourceId);
        world = PlayerLogisticsKernel.Publish(world, Request("cancel", operation.Id, OperationLogisticsDirection.Import, OperationArchetypeCatalogue.RationsResourceId, 10, 20, 1)).World;
        var contract = world.LogisticsContracts.Single(); var old = world.FinancialReservations.Single().Amount;
        world = PlayerLogisticsKernel.EditPayout(world, contract.Id, 30, T0.AddMinutes(1)).World;
        Assert.True(world.FinancialReservations.Single().Amount >= old);
        world = PlayerLogisticsKernel.EditPublishedTerms(world, contract.Id, 30, null, T0.AddMinutes(1).AddSeconds(1)).World;
        Assert.Null(world.LogisticsContracts.Single().FallbackAtUtc);
        world = PlayerLogisticsKernel.Cancel(world, contract.Id, T0.AddMinutes(2)).World;
        Assert.Equal(before, Inventory(world, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RationsResourceId));
        Assert.Equal(FinancialReservationState.Released, world.FinancialReservations.Single().State);
        Assert.Equal(EconomyJobState.Cancelled, world.Jobs.Single(x => x.Id == contract.JobId).State);
    }

    [Fact]
    public void AirImportSettlesInventoryAndSelfFulfilmentCannotCreateMoney()
    {
        var world = World(); var operation = world.Operations.Single(); var site = world.Sites.Single();
        world = PlayerLogisticsKernel.Publish(world, Request("air", operation.Id, OperationLogisticsDirection.Import, OperationArchetypeCatalogue.RationsResourceId, 10, 20, null)).World;
        var contract = world.LogisticsContracts.Single(); var balance = EconomyLedger.Balance(world, EmployedCargoPrototype.PlayerId, EmployedCargoPrototype.Currency);
        world = EconomyKernel.AcceptJob(world, contract.JobId, T0.AddMinutes(1)).World;
        Assert.Throws<InvalidOperationException>(() => PlayerLogisticsKernel.Cancel(world, contract.Id, T0.AddMinutes(2)));
        world = EconomyKernel.MarkShipmentInTransit(world, contract.JobId).World;
        var evidence = new VerifiedDeliveryEvidence("evidence-contract-air", "test", contract.JobId, contract.ShipmentId,
            contract.OriginLocationId, contract.DestinationLocationId, T0.AddHours(1), "TEST verified delivery.");
        world = EconomyKernel.CompleteDelivery(world, evidence).World;
        Assert.Equal(10, world.InventoryLedger.Single(x => x.JobId == contract.JobId && x.Kind == InventoryTransactionKind.DeliveryReceived).QuantityDelta);
        Assert.Equal(balance - contract.CargoValue, EconomyLedger.Balance(world, EmployedCargoPrototype.PlayerId, EmployedCargoPrototype.Currency));
        Assert.Equal(70, Inventory(world, site.LocationId, OperationArchetypeCatalogue.RationsResourceId));
        Assert.False(EconomyKernel.CompleteDelivery(world, evidence).Changed);
    }

    [Fact]
    public void RoadFallbackAndArrivalAreTimedRestartSafeAndIdempotent()
    {
        var world = World(); var operation = world.Operations.Single();
        world = PlayerLogisticsKernel.Publish(world, Request("road", operation.Id, OperationLogisticsDirection.Import,
            OperationArchetypeCatalogue.RationsResourceId, 5, 10, 1)).World;
        var c = world.LogisticsContracts.Single();
        var started = PlayerLogisticsKernel.ProcessRoad(world, c.FallbackAtUtc!.Value.AddSeconds(1)); Assert.True(started.Changed);
        world = started.World; c = world.LogisticsContracts.Single(); Assert.Equal(OperationLogisticsContractState.RoadInTransit, c.State);
        Assert.Equal(EconomyJobState.RoadInTransit, world.Jobs.Single(x => x.Id == c.JobId).State);
        Assert.False(PlayerLogisticsKernel.ProcessRoad(world, c.RoadEtaUtc!.Value.AddSeconds(-1)).Changed);
        world = PlayerLogisticsKernel.ProcessRoad(world, c.RoadEtaUtc.Value.AddSeconds(1)).World;
        Assert.Equal(OperationLogisticsContractState.Completed, world.LogisticsContracts.Single().State);
        Assert.False(PlayerLogisticsKernel.ProcessRoad(world, c.RoadEtaUtc.Value.AddHours(1)).Changed);
        Assert.Single(world.FinancialLedger, x => x.Kind == FinancialTransactionKind.RoadTransportCost && x.JobId == c.JobId);
    }

    [Fact]
    public void AirExportTransfersRealMineralsAndCreditsSaleOnlyAfterDelivery()
    {
        var world = World(); var operation = world.Operations.Single();
        var beforeGateway = Inventory(world, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RawMineralsResourceId);
        var beforeMoney = EconomyLedger.Balance(world, EmployedCargoPrototype.PlayerId, EmployedCargoPrototype.Currency);
        world = PlayerLogisticsKernel.Publish(world, Request("export", operation.Id, OperationLogisticsDirection.Export,
            OperationArchetypeCatalogue.RawMineralsResourceId, 10, 12, null)).World;
        var c = world.LogisticsContracts.Single();
        Assert.Equal(beforeMoney, EconomyLedger.Balance(world, EmployedCargoPrototype.PlayerId, EmployedCargoPrototype.Currency));
        world = EconomyKernel.AcceptJob(world, c.JobId, T0.AddMinutes(1)).World;
        world = EconomyKernel.MarkShipmentInTransit(world, c.JobId).World;
        world = EconomyKernel.CompleteDelivery(world, new("evidence-contract-export", "test", c.JobId, c.ShipmentId,
            c.OriginLocationId, c.DestinationLocationId, T0.AddHours(1), "TEST verified export delivery.")).World;
        Assert.Equal(beforeGateway + 10, Inventory(world, EmployedCargoPrototype.ErosId, OperationArchetypeCatalogue.RawMineralsResourceId));
        Assert.Equal(beforeMoney + c.CargoValue, EconomyLedger.Balance(world, EmployedCargoPrototype.PlayerId, EmployedCargoPrototype.Currency));
        Assert.Single(world.FinancialLedger, x => x.Kind == FinancialTransactionKind.LogisticsSaleRevenue && x.JobId == c.JobId);
    }

    [Fact]
    public async Task RoadDeadlineAndEtaSurviveStoreRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "frontier-logistics-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "economy-state.json"); var first = new LocalEconomyStateStore(path); await first.InitializeAsync(World());
            var service = new EconomyService(first); var operation = (await service.LoadAsync())!.Operations.Single();
            await service.PublishOperationLogisticsAsync(Request("restart", operation.Id, OperationLogisticsDirection.Import,
                OperationArchetypeCatalogue.RationsResourceId, 5, 10, 1));
            var saved = (await service.LoadAsync())!.LogisticsContracts.Single();
            var reopened = new EconomyService(new LocalEconomyStateStore(path));
            await reopened.ProcessRoadLogisticsAsync(saved.FallbackAtUtc!.Value.AddMinutes(saved.RoadDurationMinutes).AddSeconds(1));
            Assert.Equal(OperationLogisticsContractState.Completed, (await new LocalEconomyStateStore(path).LoadAsync())!.LogisticsContracts.Single().State);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PublishedContractAppearsInExistingJobsMarketplaceAndAcceptsNormally()
    {
        var world = World(); var operation = world.Operations.Single();
        world = PlayerLogisticsKernel.Publish(world, Request("market", operation.Id, OperationLogisticsDirection.Import,
            OperationArchetypeCatalogue.RationsResourceId, 5, 10, null)).World;
        var contract = world.LogisticsContracts.Single(); var store = new MemoryEconomyStateStore(); await store.InitializeAsync(world);
        var service = new EmployedCargoJobsService(store, utcNow: () => T0.AddMinutes(2));
        var board = await service.LoadAsync(); Assert.Contains(board.Offers, x => x.JobId == contract.JobId);
        var accepted = await service.AcceptAsync(contract.JobId); Assert.Contains(accepted.ReservedJobs, x => x.JobId == contract.JobId);
        Assert.Equal(OperationLogisticsContractState.Accepted, (await store.LoadAsync())!.LogisticsContracts.Single().State);
    }

    [Fact]
    public void WrongLocationUnloadForfeitsContractAndNeverPays()
    {
        var world = World(); var operation = world.Operations.Single();
        world = PlayerLogisticsKernel.Publish(world, Request("forfeit", operation.Id, OperationLogisticsDirection.Import,
            OperationArchetypeCatalogue.RationsResourceId, 5, 10, null)).World;
        var c = world.LogisticsContracts.Single(); world = EconomyKernel.AcceptJob(world, c.JobId, T0.AddMinutes(1)).World;
        world = EconomyKernel.ApplyCargoTransfer(world, new("load-contract-forfeit", "load-command-forfeit", c.JobId, 5,
            c.OriginLocationId, -22.6, 17.08, T0.AddMinutes(2))).World;
        world = EconomyKernel.MarkShipmentInTransit(world, c.JobId).World;
        Assert.Throws<InvalidOperationException>(() => EconomyKernel.ApplyCargoTransfer(world, new("unload-refused", "unload-refused-command", c.JobId, 0,
            null, -22.4, 16.5, T0.AddHours(1))));
        world = EconomyKernel.ApplyCargoTransfer(world, new("unload-forfeit", "unload-forfeit-command", c.JobId, 0,
            null, -22.4, 16.5, T0.AddHours(1), ConfirmForfeit: true)).World;
        Assert.Equal(OperationLogisticsContractState.Forfeited, world.LogisticsContracts.Single().State);
        Assert.Equal(FinancialReservationState.Released, world.FinancialReservations.Single().State);
        Assert.DoesNotContain(world.FinancialLedger, x => x.JobId == c.JobId);
    }

    [Theory]
    [InlineData(OperationLogisticsDirection.Import)]
    [InlineData(OperationLogisticsDirection.Export)]
    public async Task GroupedPhysicalUnloadAfterArrivalSettlesOnceAcrossRestart(OperationLogisticsDirection direction)
    {
        var world=World();var operation=world.Operations.Single();
        var resource=direction==OperationLogisticsDirection.Import?OperationArchetypeCatalogue.RationsResourceId:OperationArchetypeCatalogue.RawMineralsResourceId;
        for(var i=0;i<2;i++)world=PlayerLogisticsKernel.Publish(world,Request("delayed-"+i,operation.Id,direction,resource,5,10,null)).World;
        var contracts=world.LogisticsContracts.ToArray();
        foreach(var c in contracts)
        {
            world=EconomyKernel.AcceptJob(world,c.JobId,T0.AddMinutes(1)).World;
            world=EconomyKernel.ApplyCargoTransfer(world,new("load-"+c.Id,"load-"+c.Id,c.JobId,5,c.OriginLocationId,-22.5,15,T0.AddMinutes(2))).World;
            world=EconomyKernel.MarkShipmentInTransit(world,c.JobId).World;
        }
        var beforeInventory=Inventory(world,contracts[0].DestinationLocationId,resource);
        var beforeMoney=EconomyLedger.Balance(world,EmployedCargoPrototype.PlayerId,EmployedCargoPrototype.Currency);
        var requests=contracts.Select((c,i)=>new ShipmentCargoTransferRequest("unload-"+i,"unload-command-"+i,c.JobId,0,c.DestinationLocationId,-22.5,15,T0.AddHours(2),
            CompletionEvidence:new VerifiedDeliveryEvidence("proof-"+i,"test",c.JobId,c.ShipmentId,c.OriginLocationId,c.DestinationLocationId,T0.AddHours(1),"TEST arrival before unload."))).ToArray();
        var root=Path.Combine(Path.GetTempPath(),"frontier-delayed-unload-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var path=Path.Combine(root,"economy-state.json");var store=new LocalEconomyStateStore(path);await store.InitializeAsync(world);
            await new EconomyService(store).ApplyCargoTransfersAsync(requests);
            var reopened=new LocalEconomyStateStore(path);var result=(await reopened.LoadAsync())!;
            Assert.Equal(beforeInventory+10,Inventory(result,contracts[0].DestinationLocationId,resource));
            Assert.Equal(beforeMoney+(direction==OperationLogisticsDirection.Import?-1:1)*contracts.Sum(x=>x.CargoValue),EconomyLedger.Balance(result,EmployedCargoPrototype.PlayerId,EmployedCargoPrototype.Currency));
            foreach(var c in contracts)
            {
                Assert.Equal(EconomyJobState.Completed,result.Jobs.Single(x=>x.Id==c.JobId).State);
                Assert.Equal(T0.AddHours(1),Assert.Single(result.FinancialLedger,x=>x.JobId==c.JobId&&x.Kind==FinancialTransactionKind.JobPayment).OccurredUtc);
                Assert.Equal(T0.AddHours(2),Assert.Single(result.CargoTransfers,x=>x.JobId==c.JobId&&x.Kind==ShipmentCargoTransferKind.UnloadedAtDestination).OccurredUtc);
            }
            Assert.False((await new EconomyService(reopened).ApplyCargoTransfersAsync(requests)).Changed);
        }
        finally{Directory.Delete(root,true);}
    }

    private static PublishOperationLogisticsRequest Request(string id, string operation, OperationLogisticsDirection direction,
        string resource, decimal quantity, decimal payout, int? fallback) => new("contract-command-" + id, operation, direction,
            EmployedCargoPrototype.ErosId, resource, quantity, payout, fallback, 12, 30, T0);
    private static decimal Inventory(EconomyWorld world, string location, string resource) => world.Inventories.Single(x => x.LocationId == location && x.ResourceId == resource).AvailableQuantity;
    private static EconomyWorld World()
    {
        var world = EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load());
        world = EconomyKernel.AcceptJob(world, "job-uis-supplies-v0", T0.AddHours(-3)).World;
        world = EconomyKernel.MarkShipmentInTransit(world, "job-uis-supplies-v0").World;
        world = EconomyKernel.CompleteDelivery(world, new("evidence-funds-logistics", "test", "job-uis-supplies-v0", "shipment-uis-supplies-v0",
            EmployedCargoPrototype.ErosId, EmployedCargoPrototype.UisId, T0.AddHours(-2), "TEST verified delivery.")).World;
        var candidate = new AirportSiteCandidate(Airport, "FPLC", "Logistics Camp", -22.5, 14.5, true, [ConstructionCapability.BaseLodge]);
        var presence = new VerifiedSitePresenceEvidence("presence-logistics", "test", Airport, 1, -22.5, 14.5, 0, 250, T0.AddHours(-1), T0.AddHours(-1));
        world = ProspectingCampKernel.Found(world, candidate, presence, "found-logistics", T0.AddHours(-1)).World;
        world = ProspectingCampKernel.AdvanceDay(world, world.Operations.Single().Id, "day-logistics", T0.AddMinutes(-30)).World;
        return PersistentEconomyKernel.EnsurePrototypeGatewayStock(world, new(), T0).World;
    }
}
