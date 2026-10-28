using FrontierAirportBuilder.Core;
namespace FrontierAirportBuilder.Workflow;

public interface IAircraftPlacementAdapter
{
    string EvidenceId { get; }
    string PackageVersion { get; }
    Task<bool> ReadyAsync(PhysicalAirframe airframe,AirframePosition target,CancellationToken token);
    Task PlaceAsync(PhysicalAirframe airframe,AirframePosition target,CancellationToken token);
    Task<bool> VerifyAsync(PhysicalAirframe airframe,AirframePosition target,CancellationToken token);
}
/// <summary>No production adapter is registered. Does not restore payload/fuel or produce flight evidence.</summary>
public sealed class AircraftPlacementService(AircraftFleetStore store)
{
    public async Task<AircraftFleet> ReturnAsync(Guid airframeId,Guid requestId,IAircraftPlacementAdapter adapter,
        bool explicitlyAuthorized,bool bindingConfirmed,CancellationToken token=default)
    {
        var fleet=store.Load();var a=fleet.Aircraft.Single(x=>x.Id==airframeId);
        if(a.CommandIntents.Any(x=>x.Id==requestId))return fleet;
        if(requestId==Guid.Empty||!explicitlyAuthorized||!bindingConfirmed||string.IsNullOrWhiteSpace(adapter.EvidenceId)||
            !a.Bindings.Any(b=>b.PackageVersion==adapter.PackageVersion)||a.Latest is not {} target||target!=a.LastStationary||
            target.State!=AirframePositionState.StationaryGround||!target.Telemetry.IsOnGround||a.Availability is "Grounded" or "In maintenance")
            throw new InvalidOperationException("No qualified saved ground placement or verified binding/adapter. Use guided return.");
        if(!await adapter.ReadyAsync(a,target,token))throw new InvalidOperationException("Terrain, initialization or platform height is not ready.");
        var intent=new AircraftCommandIntent(requestId,null,"position",true,adapter.EvidenceId,true,FaultSyncState.Pending,"Authorized placement; persist before one attempt.");
        a=FleetOperations.Event(a with {CommandIntents=a.CommandIntents.Append(intent).ToArray()},requestId,DateTimeOffset.UtcNow,"Placement requested","Explicit return to qualified saved placement; no cargo/flight evidence.");
        fleet=Commit(fleet,a);
        await adapter.PlaceAsync(a,target,token);
        var verified=await adapter.VerifyAsync(a,target,token);
        a=a with {CommandIntents=a.CommandIntents.Select(x=>x.Id==requestId?x with {State=verified?FaultSyncState.Applied:FaultSyncState.Pending,Detail=verified?"Placement verified":"Unable to verify; do not replay"}:x).ToArray()};
        return Commit(fleet,a);
    }
    private AircraftFleet Commit(AircraftFleet previous,PhysicalAirframe a)=>store.Commit(previous with {Revision=previous.Revision+1,Aircraft=previous.Aircraft.Select(x=>x.Id==a.Id?a:x).ToArray()},previous.Revision);
}
