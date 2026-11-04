using System.Text.Json.Nodes;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;
public sealed class CompanyOfferRefreshTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"FrontierManualBoard",Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-11-03T12:00:00Z");
    private static CareerEmployer Employer(string category)=>CareerCatalogue.Employers.Single(e=>e.Category==category);
    [Theory][InlineData("Missionary")][InlineData("Industrial")][InlineData("Safari")][InlineData("Offshore")]
    public async Task ManualRefreshReplacesOnlyAvailableWorkAndPersistsIdempotently(string category)
    {
        var store=new LocalEconomyStateStore(Path.Combine(root,"economy-state.json"));var employer=Employer(category);
        var service=new EmployedCargoJobsService(store,employer:employer,utcNow:()=>Now);
        var first=await service.LoadAsync();var accepted=first.Offers.First();service.SelectedAircraftTypeId=service.AircraftChoices(accepted).First().Id;
        await service.AcceptAsync(accepted.JobId);await store.ApplyAsync(w=>EconomyKernel.MarkShipmentLoaded(w,accepted.JobId));
        var before=(await store.LoadAsync())!;var available=before.Jobs.Where(j=>j.State==EconomyJobState.Offered).Select(j=>j.Id).ToHashSet();
        var refreshed=await service.RefreshOffersAsync("manual-one");var after=(await store.LoadAsync())!;
        Assert.NotEmpty(refreshed.Offers);Assert.DoesNotContain(refreshed.Offers,j=>available.Contains(j.JobId));
        Assert.All(after.Jobs.Where(j=>available.Contains(j.Id)),j=>Assert.Equal(EconomyJobState.Expired,j.State));
        Assert.Equal(before.Jobs.Single(j=>j.Id==accepted.JobId),after.Jobs.Single(j=>j.Id==accepted.JobId));Assert.Equal(before.Shipments,after.Shipments);
        Assert.Equal(before.FinancialLedger,after.FinancialLedger);Assert.Equal(before.CargoTransfers,after.CargoTransfers);Assert.Equal(before.CargoAirframes,after.CargoAirframes);
        Assert.Equal(before.RealTime,after.RealTime);Assert.Equal(1,CompanyOfferRefresh.Sequence(after,employer.Id));
        var resumed=new EmployedCargoJobsService(new LocalEconomyStateStore(store.StoragePath),employer:employer,utcNow:()=>Now);
        await resumed.RefreshOffersAsync("manual-one");var retried=(await store.LoadAsync())!;
        Assert.Equal(after.Revision,retried.Revision);Assert.Equal(after.Jobs,retried.Jobs);Assert.Equal(after.CompanyOfferRefreshes,retried.CompanyOfferRefreshes);
        Assert.Equal(after.InventoryLedger,retried.InventoryLedger);Assert.Equal(after.FinancialLedger,retried.FinancialLedger);
        var reloaded=await resumed.LoadAsync();Assert.Equal(refreshed.Offers.Select(j=>j.JobId),reloaded.Offers.Select(j=>j.JobId));
        var second=await resumed.RefreshOffersAsync("manual-two");Assert.DoesNotContain(second.Offers,j=>refreshed.Offers.Any(old=>old.JobId==j.JobId));
        Assert.Equal(2,CompanyOfferRefresh.Sequence((await store.LoadAsync())!,employer.Id));
    }
    [Fact] public async Task PassengerReservationsSurviveAndNewPersonnelOffersUseTheNewRefreshSequence()
    {
        var memory=new MemoryEconomyStateStore();var employer=Employer("Missionary");var catalogue=new EmployerDestinationCatalogue();
        var passengers=new PassengerTransfersService(memory,employer,catalogue,utcNow:()=>Now);await passengers.RefreshAsync();
        passengers.SelectedId=passengers.Transfers.First().Id;await passengers.AcceptAsync("c208");
        var before=(await memory.LoadAsync())!;var reserved=before.PassengerTransfers.Single(t=>t.State==PassengerTransferState.Reserved);
        var offers=before.PassengerTransfers.Where(t=>t.State==PassengerTransferState.Offered).Select(t=>t.Id).ToHashSet();
        var cargo=new EmployedCargoJobsService(memory,employer:employer,destinations:catalogue,utcNow:()=>Now);await cargo.RefreshOffersAsync("personnel-refresh");
        await passengers.RefreshAsync();var after=(await memory.LoadAsync())!;
        Assert.Equal(reserved,after.PassengerTransfers.Single(t=>t.Id==reserved.Id));Assert.Equal(before.PassengerTransports,after.PassengerTransports);
        Assert.All(after.PassengerTransfers.Where(t=>offers.Contains(t.Id)),t=>Assert.Equal(PassengerTransferState.Expired,t.State));
        Assert.NotEmpty(after.PassengerTransfers.Where(t=>t.State==PassengerTransferState.Offered));Assert.DoesNotContain(after.PassengerTransfers.Where(t=>t.State==PassengerTransferState.Offered),t=>offers.Contains(t.Id));
        Assert.Equal(before.FinancialLedger,after.FinancialLedger);
    }
    [Fact] public async Task OffshoreAvailableObservationIsReplacedButAcceptedObservationIsPreserved()
    {
        var memory=new MemoryEconomyStateStore();var employer=Employer("Offshore");var catalogue=new EmployerDestinationCatalogue();var frame=Guid.NewGuid();
        var offshore=new OffshoreMissionsService(memory,employer,catalogue,()=>[],()=>frame,()=>Now){SelectedCompanyType=()=>"vision-jet"};
        await offshore.RefreshAsync();var old=Assert.Single(offshore.Missions);var cargo=new EmployedCargoJobsService(memory,employer:employer,destinations:catalogue,utcNow:()=>Now);
        await cargo.RefreshOffersAsync("offshore-one");await offshore.RefreshAsync();var fresh=Assert.Single(offshore.Missions);Assert.NotEqual(old.Id,fresh.Id);
        offshore.SelectedId=fresh.Id;await offshore.AcceptAsync();var before=(await memory.LoadAsync())!;
        await cargo.RefreshOffersAsync("offshore-two");await offshore.RefreshAsync();var after=(await memory.LoadAsync())!;
        Assert.Equal(before.OffshoreMissions.Single(m=>m.Id==fresh.Id),after.OffshoreMissions.Single(m=>m.Id==fresh.Id));Assert.Empty(after.FinancialLedger);
    }
    [Fact] public async Task RefreshDoesNotCreateWorkForHeldOrReportedProblematicAirports()
    {
        var memory=new MemoryEconomyStateStore();var catalogue=new EmployerDestinationCatalogue();var employer=Employer("Missionary");
        var row=EmployerDestinationCatalogue.Research.First(r=>r.Key=="FYAM");var place=catalogue.Resolve(row)!;var p=place.ApproximateCoordinates!;
        catalogue.ApplyVisits([new(Guid.NewGuid(),row.Key,place.StableId,row.VerifiedName,row.Identifier,p.Latitude,p.Longitude,"c208","Synthetic Caravan",null,"",Now,AirportVisitResult.Problematic,AirportIssue.AirportMissing,"Synthetic missing airport")]);
        var service=new EmployedCargoJobsService(memory,employer:employer,destinations:catalogue,utcNow:()=>Now);await service.LoadAsync();
        var board=await service.RefreshOffersAsync("held-check");Assert.DoesNotContain(board.Offers,j=>j.DestinationLocationId==place.StableId);
        Assert.All(board.Offers,j=>Assert.Contains(catalogue.Diagnose("Missionary"),d=>d.Enabled&&d.Resolved?.StableId==j.DestinationLocationId));
    }
    [Fact] public async Task SchemaFifteenLoadsWithoutInventingRefreshReceiptsAndFutureStateIsPreserved()
    {
        var store=new LocalEconomyStateStore(Path.Combine(root,"economy-state.json"));var service=new EmployedCargoJobsService(store,employer:Employer("Missionary"),utcNow:()=>Now);await service.LoadAsync();
        var before=(await store.LoadAsync())!;var node=JsonNode.Parse(File.ReadAllText(store.StoragePath))!;node["SchemaVersion"]=15;node["World"]!.AsObject().Remove("CompanyOfferRefreshes");File.WriteAllText(store.StoragePath,node.ToJsonString());
        var loaded=(await store.LoadAsync())!;Assert.Empty(loaded.CompanyOfferRefreshes);Assert.Equal(before.Jobs,loaded.Jobs);Assert.Equal(before.FinancialLedger,loaded.FinancialLedger);Assert.Equal(before.Shipments,loaded.Shipments);
        File.WriteAllText(store.StoragePath,"{\"SchemaVersion\":999,\"World\":{}}");await Assert.ThrowsAsync<IOException>(()=>service.RefreshOffersAsync("future"));Assert.Contains("999",File.ReadAllText(store.StoragePath));
    }
    [Fact] public async Task LostCommitResponseRetriesTheSameReceiptWithoutReplacingOffersAgain()
    {
        var store=new LostResponseStore();var employer=Employer("Missionary");var service=new EmployedCargoJobsService(store,employer:employer,utcNow:()=>Now);await service.LoadAsync();
        store.Fail=true;await Assert.ThrowsAsync<IOException>(()=>service.RefreshOffersAsync("retry-this-command"));
        var committed=(await store.LoadAsync())!;Assert.Single(committed.CompanyOfferRefreshes);
        await service.RefreshOffersAsync("retry-this-command");var recovered=(await store.LoadAsync())!;
        Assert.Equal(committed.Revision,recovered.Revision);Assert.Equal(committed.Jobs,recovered.Jobs);Assert.Single(recovered.CompanyOfferRefreshes);Assert.Empty(recovered.FinancialLedger);
    }
    private sealed class LostResponseStore : IEconomyStateStore
    {
        private readonly MemoryEconomyStateStore inner=new();public bool Fail;
        public string StoragePath=>inner.StoragePath;
        public Task<EconomyWorld?> LoadAsync(CancellationToken cancellationToken=default)=>inner.LoadAsync(cancellationToken);
        public Task InitializeAsync(EconomyWorld initial,CancellationToken cancellationToken=default)=>inner.InitializeAsync(initial,cancellationToken);
        public async Task<EconomyTransition> ApplyAsync(Func<EconomyWorld,EconomyTransition> mutation,CancellationToken cancellationToken=default)
        {
            var result=await inner.ApplyAsync(mutation,cancellationToken);
            if(Fail&&result.World.CompanyOfferRefreshes.Count>0){Fail=false;throw new IOException("Synthetic lost commit response.");}
            return result;
        }
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
