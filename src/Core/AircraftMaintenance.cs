namespace FrontierAirportBuilder.Core;

public sealed record MaintenanceFacility(string Id, string Name, double Latitude, double Longitude, double RadiusMeters,
    IReadOnlyList<MaintenanceAction> Services, string PayeeId);
public sealed record MaintenanceQuote(MaintenanceAction Action, decimal Amount, TimeSpan Downtime, string Version);
public static class MaintenancePrototypePolicy
{
    public static MaintenanceQuote Quote(MaintenanceAction action) => action switch
    {
        MaintenanceAction.Inspection => new(action, 10, TimeSpan.FromMinutes(5), "prototype-v1"),
        MaintenanceAction.Service => new(action, 20, TimeSpan.FromMinutes(10), "prototype-v1"),
        MaintenanceAction.Repair => new(action, 50, TimeSpan.FromMinutes(30), "prototype-v1"),
        MaintenanceAction.Overhaul => new(action, 150, TimeSpan.FromHours(2), "prototype-v1"),
        MaintenanceAction.Replacement => new(action, 200, TimeSpan.FromHours(1), "prototype-v1"),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
public sealed record MaintenanceCharge(string WorkOrderId, string AirframeId, string Payer, string Payee, decimal Amount, string Currency, DateTimeOffset Utc);
public static class AircraftMaintenance
{
    public static PhysicalAirframe Book(PhysicalAirframe a, Guid orderId, Guid componentId, MaintenanceQuote quote,
        MaintenanceFacility facility, string payer, DateTimeOffset now, bool enginesConfirmedStopped)
    {
        if(a.Work.Any(w=>w.Id==orderId))return a;
        if(orderId==Guid.Empty || quote!=MaintenancePrototypePolicy.Quote(quote.Action) || now==default || string.IsNullOrWhiteSpace(payer))throw new InvalidDataException("Invalid quote or payer.");
        var c=a.Components.Single(x=>x.Id==componentId&&!x.Removed);
        if(a.Work.Any(w=>w.State!=WorkOrderState.Completed))throw new InvalidOperationException("Finish existing work first.");
        if(a.Latest is not {} p || p.State!=AirframePositionState.StationaryGround || a.LastStationary!=p ||
            now-p.Telemetry.ObservedUtc>TimeSpan.FromSeconds(15) || now<p.Telemetry.ObservedUtc || !enginesConfirmedStopped ||
            !facility.Services.Contains(quote.Action) || facility.RadiusMeters is <=0 or >2778 ||
            WorldTracking.DistanceMeters(p.Telemetry.Latitude,p.Telemetry.Longitude,facility.Latitude,facility.Longitude)>facility.RadiusMeters)
            throw new InvalidOperationException("Work requires fresh qualified dry stationary contact at the facility and engines stopped.");
        if(quote.Action!=MaintenanceAction.Inspection && c.Authority!=ComponentAuthority.Frontier)
            throw new InvalidOperationException("Unknown or vendor component authority requires a qualified assessment/repair adapter.");
        var work=new MaintenanceWork(orderId,componentId,quote.Action,facility.Id,payer,quote.Amount,quote.Version,now,now+quote.Downtime,
            WorkOrderState.AwaitingSettlement,a.Faults.Where(f=>f.ComponentId==componentId&&!f.Resolved).Select(f=>f.Id.ToString()).ToArray()) {Payee=facility.PayeeId};
        return FleetOperations.Event(a with {Work=a.Work.Append(work).ToArray()},orderId,now,"Maintenance booked",
            $"{quote.Action}; payer {payer}; {quote.Amount} prototype credits; facility {facility.Name}; due {work.DueUtc:u}",componentId);
    }
    public static PhysicalAirframe Settled(PhysicalAirframe a,Guid orderId) => a with
        {Work=a.Work.Select(w=>w.Id==orderId&&w.State==WorkOrderState.AwaitingSettlement?w with {State=WorkOrderState.InProgress}:w).ToArray()};
    public static PhysicalAirframe Complete(PhysicalAirframe a,Guid orderId,DateTimeOffset now,bool simulatorRepairVerified=false)
    {
        var w=a.Work.Single(x=>x.Id==orderId); if(w.State==WorkOrderState.Completed)return a;
        if(w.State==WorkOrderState.AwaitingSettlement || now<w.DueUtc)throw new InvalidOperationException("Work unpaid or not yet due.");
        var c=a.Components.Single(x=>x.Id==w.ComponentId&&!x.Removed);
        if(w.Action!=MaintenanceAction.Inspection && c.Authority!=ComponentAuthority.Frontier)throw new InvalidOperationException("Authority changed during work.");
        if(w.Action is MaintenanceAction.Repair or MaintenanceAction.Overhaul or MaintenanceAction.Replacement && w.FaultIds.Length>0 && !simulatorRepairVerified)
            return a with {Work=a.Work.Select(x=>x.Id==orderId?x with {State=WorkOrderState.AwaitingSimulatorRepair}:x).ToArray()};
        var components=a.Components.ToList();var index=components.FindIndex(x=>x.Id==c.Id);
        if(w.Action==MaintenanceAction.Inspection || w.Action==MaintenanceAction.Service)
            components[index]=c with {InspectionRequired=false}; // Unknown health stays unknown; inspection/service never repairs a fault.
        else if(w.Action==MaintenanceAction.Replacement)
        {
            components[index]=c with {Removed=true};
            components.Add(FleetOperations.Component(c.Key,c.Type) with {Authority=ComponentAuthority.Frontier,Health=1,InspectionRequired=false,ModelVersion="prototype-new-v1"});
        }
        else components[index]=c with {InspectionRequired=false,Health=w.Action==MaintenanceAction.Overhaul?1:c.Health,
            Hazard=0,Threshold=FleetOperations.Component(c.Key,c.Type).Threshold}; // Explicit repair lifecycle, never reload, renews persisted risk.
        var faults=a.Faults.Select(f=>w.Action is MaintenanceAction.Repair or MaintenanceAction.Overhaul or MaintenanceAction.Replacement && w.FaultIds.Contains(f.Id.ToString()) ? f with {Resolved=true,Synchronization=FaultSyncState.Cleared}:f).ToArray();
        return FleetOperations.Event(a with {Components=components,Faults=faults,Work=a.Work.Select(x=>x.Id==orderId?x with {State=WorkOrderState.Completed}:x).ToArray()},
            CompletionId(orderId),now,"Maintenance completed",$"{w.Action} for {c.Key}; other components, fuel and cargo unchanged.",c.Id);
    }
    public static Guid CompletionId(Guid order) { var bytes=order.ToByteArray();bytes[0]^=0x80;return new(bytes); }
}
public static class MaintenanceEconomy
{
    public static EconomyTransition Charge(EconomyWorld source,MaintenanceCharge charge)
    {
        source.EnsureValid();var existing=source.MaintenanceCharges.SingleOrDefault(x=>x.WorkOrderId==charge.WorkOrderId);
        if(existing is not null){if(existing!=charge)throw new InvalidOperationException("Conflicting settlement.");return new(source,false);}
        if(charge.Amount<=0||charge.Utc==default||!source.Businesses.Any(x=>x.Id==charge.Payee)||charge.Payer==charge.Payee)throw new InvalidDataException("Invalid payee or quote.");
        var player=source.Players.SingleOrDefault(x=>x.Id==charge.Payer);
        if(player is null&&!source.Businesses.Any(x=>x.Id==charge.Payer&&x.Owner==EconomicOwnerKind.System))throw new InvalidOperationException("Payer must be a funded player or the explicit SYSTEM employer maintenance abstraction.");
        if(player is not null&&(player.CurrencyCode!=charge.Currency||PlayerLogisticsKernel.AvailableFunds(source,player.Id,charge.Currency)<charge.Amount))throw new InvalidOperationException("Insufficient unreserved funds.");
        var ledger=source.FinancialLedger.Append(new FinancialTransaction("maintenance-"+charge.WorkOrderId,FinancialTransactionKind.AircraftMaintenance,null,charge.WorkOrderId,charge.Payer,charge.Payee,charge.Amount,charge.Currency,charge.Utc)).ToArray();
        var next=source with {Revision=source.Revision+1,MaintenanceCharges=source.MaintenanceCharges.Append(charge).ToArray(),FinancialLedger=ledger};
        next=next with {Players=next.Players.Select(p=>p with {CachedBalance=EconomyLedger.Balance(next,p.Id,p.CurrencyCode)}).ToArray()};
        return new(next.EnsureValid(),true);
    }
}
