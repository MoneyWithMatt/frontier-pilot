using FrontierAirportBuilder.Core;
namespace FrontierAirportBuilder.Workflow;

/// <summary>Durable intent -> authoritative ledger -> fleet completion; retries never pay or complete twice.</summary>
public sealed class AircraftMaintenanceService(AircraftFleetStore fleetStore, IEconomyStateStore economy)
{
    public async Task<AircraftFleet> ReconcileAsync(DateTimeOffset utc, CancellationToken token=default)
    {
        var fleet=fleetStore.Load();
        foreach(var original in fleet.Aircraft)
        foreach(var order in original.Work.Where(w=>w.State!=WorkOrderState.Completed))
        {
            var a=fleet.Aircraft.Single(x=>x.Id==original.Id);
            if(order.State==WorkOrderState.AwaitingSettlement)
            {
                var charge=new MaintenanceCharge(order.Id.ToString("N"),a.Id.ToString("N"),order.Payer,order.Payee,order.Quote,order.Currency,order.StartedUtc);
                await economy.ApplyAsync(w=>MaintenanceEconomy.Charge(w,charge),token);
                a=AircraftMaintenance.Settled(a,order.Id);
                fleet=Commit(fleet,a);
            }
            if(utc>=order.DueUtc && order.State!=WorkOrderState.AwaitingSimulatorRepair)
            {
                a=AircraftMaintenance.Complete(a,order.Id,utc);
                fleet=Commit(fleet,a);
            }
        }
        return fleet;
    }
    private AircraftFleet Commit(AircraftFleet prior,PhysicalAirframe a)=>fleetStore.Commit(prior with
        {Revision=prior.Revision+1,Aircraft=prior.Aircraft.Select(x=>x.Id==a.Id?a:x).ToArray()},prior.Revision);
}

public sealed record QualifiedCommandCapability(string Action, string EvidenceId, string PackageVersion, bool ExplicitSet);
public sealed record SimulatorEffectReadback(bool? State, bool OperationalEffectConfirmed, bool Conflict);
/// <summary>No native implementation is enabled until exact aircraft/version retention tests pass.</summary>
public interface IAircraftEffectAdapter
{
    QualifiedCommandCapability Capability { get; }
    Task<SimulatorEffectReadback> ReadAsync(PhysicalAirframe aircraft,CancellationToken token);
    Task SetAsync(PhysicalAirframe aircraft,bool desired,CancellationToken token);
}
public sealed class AircraftFaultSynchronization(AircraftFleetStore store)
{
    private static readonly HashSet<string> Allowlist=["engine-failed","alternator-failed","gear-failed","flaps-failed"];
    public async Task<AircraftFleet> SynchronizeAsync(Guid airframeId,Guid faultId,string action,bool desired,
        IAircraftEffectAdapter adapter,bool playerEnabled,bool bindingConfirmed,CancellationToken token=default)
    {
        var fleet=store.Load();var a=fleet.Aircraft.Single(x=>x.Id==airframeId);var fault=a.Faults.Single(x=>x.Id==faultId);
        if(desired && fault.Resolved)throw new InvalidOperationException("Cannot apply a resolved fault.");
        var component=a.Components.Single(c=>c.Id==fault.ComponentId);
        var expected=component.Type switch {"engine"=>"engine-failed","electrical"=>"alternator-failed","gear"=>"gear-failed","flaps"=>"flaps-failed",_=>"unsupported"};
        if(action!=expected)throw new InvalidOperationException("Effect does not match this component.");
        var existing=a.CommandIntents.LastOrDefault(x=>x.FaultId==faultId&&x.Desired==desired);
        if(existing is not null)return fleet; // Explicit reconciliation/new intent is required after every uncertain outcome.
        var capability=adapter.Capability;
        var supported=playerEnabled&&bindingConfirmed&&Allowlist.Contains(action)&&capability.Action==action&&capability.ExplicitSet&&
            !string.IsNullOrWhiteSpace(capability.EvidenceId)&&a.Bindings.Any(b=>b.PackageVersion==capability.PackageVersion);
        var intent=new AircraftCommandIntent(Guid.NewGuid(),faultId,action,desired,capability.EvidenceId,false,
            supported?FaultSyncState.Pending:FaultSyncState.UnableToApply,supported?"Intent persisted before readback":"Disabled, unverified binding or unsupported capability; no write.");
        a=a with {CommandIntents=a.CommandIntents.Append(intent).ToArray()};fleet=Commit(fleet,a);
        if(!supported)return fleet;
        var read=await adapter.ReadAsync(a,token);
        if(read.Conflict)return Finish(FaultSyncState.Conflict,"Adapter reports external authority conflict.");
        if(read.State is null)return Finish(FaultSyncState.UnableToApply,"Reliable readback unavailable.");
        if(read.State!=desired)
        {
            intent=intent with {Attempted=true,Detail="Single explicit set attempt; interrupted outcomes must be reconciled manually."};
            a=a with {CommandIntents=a.CommandIntents.Select(x=>x.Id==intent.Id?intent:x).ToArray()};fleet=Commit(fleet,a);
            await adapter.SetAsync(a,desired,token);read=await adapter.ReadAsync(a,token);
        }
        return Finish(read.Conflict?FaultSyncState.Conflict:read.State==desired&&read.OperationalEffectConfirmed?FaultSyncState.Applied:FaultSyncState.Pending,
            "Readback and operational-effect result retained; no blind retry.");
        AircraftFleet Finish(FaultSyncState state,string detail)
        {
            intent=intent with {State=state,Detail=detail};
            a=a with {CommandIntents=a.CommandIntents.Select(x=>x.Id==intent.Id?intent:x).ToArray(),Faults=a.Faults.Select(f=>f.Id==faultId?f with {Synchronization=state}:f).ToArray()};
            return Commit(fleet,a);
        }
    }
    private AircraftFleet Commit(AircraftFleet prior,PhysicalAirframe a)=>store.Commit(prior with
        {Revision=prior.Revision+1,Aircraft=prior.Aircraft.Select(x=>x.Id==a.Id?a:x).ToArray()},prior.Revision);
}
