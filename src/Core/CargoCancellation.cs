namespace FrontierAirportBuilder.Core;

public sealed record CargoCancellationReceipt(string Id, string LocationId, DateTimeOffset Utc,
    decimal ReturnedToOrigin, decimal Abandoned, string? ManifestId, string? OperationId);

/// <summary>Playtest policy: no financial penalty anywhere. Physical custody must be reconciled first.</summary>
public static class CargoCancellationKernel
{
    public static void Validate(EconomyWorld world)
    {
        foreach(var job in world.Jobs.Where(j=>j.CargoCancellation is not null))
        {
            var receipt=job.CargoCancellation!;
            var shipment=world.Shipments.Single(s=>s.JobId==job.Id);
            var atOrigin=receipt.LocationId==shipment.OriginLocationId;
            if(receipt.Id!=EconomyIds.Derive("cancel-cargo-",job.Id) || receipt.Utc!=job.CancelledUtc ||
                !world.Locations.Any(l=>l.Id==receipt.LocationId) || receipt.ReturnedToOrigin<0 || receipt.Abandoned<0 ||
                receipt.ReturnedToOrigin+receipt.Abandoned!=shipment.Quantity-shipment.DeliveredQuantity ||
                shipment.LoadedQuantity!=0 || shipment.CancelledUtc!=receipt.Utc ||
                shipment.CurrentLocationId!=receipt.LocationId ||
                shipment.State!=(atOrigin?ShipmentState.Cancelled:ShipmentState.Forfeited) ||
                job.State!=(atOrigin?EconomyJobState.Cancelled:EconomyJobState.Forfeited) ||
                atOrigin && receipt.Abandoned!=0 || world.FinancialLedger.Any(t=>t.JobId==job.Id))
                throw new InvalidDataException("Cargo cancellation audit is inconsistent.");
            var release=world.InventoryLedger.Where(t=>t.JobId==job.Id&&t.Id==EconomyIds.Derive("inventory-cancel-",job.Id)).ToArray();
            if(receipt.ReturnedToOrigin==0?release.Length!=0:release.Length!=1 || release[0].LocationId!=shipment.OriginLocationId ||
                release[0].ResourceId!=shipment.ResourceId || release[0].Kind!=InventoryTransactionKind.ShipmentReservationReleased ||
                release[0].QuantityDelta!=receipt.ReturnedToOrigin || release[0].OccurredUtc!=receipt.Utc)
                throw new InvalidDataException("Cancellation origin stock release is inconsistent.");
            if(receipt.ManifestId is {} manifest && !world.CargoAirframes.Any(m=>m.Id==manifest&&m.ManagedJobs.Contains(job.Id)))
                throw new InvalidDataException("Cancellation aircraft ownership is inconsistent.");
            if(receipt.OperationId is {} operation && (receipt.ManifestId is null || !world.CargoAirframes.Any(m=>m.Id==receipt.ManifestId&&
                m.History.Any(h=>h.Id==operation&&h.CancelledJobId==job.Id&&h.Changes.Length==1&&h.Changes[0].JobId==job.Id&&h.Changes[0].After==0))))
                throw new InvalidDataException("Cancellation lacks its physical unload receipt.");
            if(!atOrigin && receipt.Abandoned>0 && receipt.OperationId is null)
                throw new InvalidDataException("Abandoned cargo requires a physical unload receipt.");
        }
    }

    public static EconomyTransition Commit(EconomyWorld world,string jobId,string locationId,DateTimeOffset utc,
        string? manifestId=null,string? operationId=null)
    {
        world.EnsureValid();
        var job=world.Jobs.Single(j=>j.Id==jobId);
        if(job.CargoCancellation is not null)return new(world,false);
        if(utc==default || !world.Locations.Any(l=>l.Id==locationId) || job.State!=EconomyJobState.Accepted ||
            world.LogisticsContracts.Any(c=>c.JobId==jobId) || world.FinancialLedger.Any(t=>t.JobId==jobId))
            throw new InvalidOperationException("Only incomplete ordinary cargo jobs can be cancelled here. Player-company contract cancellation is not supported.");
        var shipment=world.Shipments.Single(s=>s.JobId==jobId);
        if(shipment.LoadedQuantity==0 && shipment.DeliveredQuantity==0 && shipment.State is ShipmentState.Loaded or ShipmentState.InTransit)
            throw new InvalidOperationException("Legacy cargo quantities need their existing recovery; ownership cannot be inferred for cancellation.");
        var owner=world.CargoAirframes.SingleOrDefault(m=>m.ManagedJobs.Contains(jobId));
        var physical=owner?.Pending;
        if(shipment.LoadedQuantity>0 && (manifestId!=owner?.Id || physical is not {Verified:true} ||
            physical.Id!=operationId || physical.CancelJobId!=jobId || physical.LocationId!=locationId ||
            physical.Changes.Length!=1 || physical.Changes[0]!=new CargoChange(jobId,shipment.LoadedQuantity,0)))
            throw new InvalidOperationException("Loaded cargo requires its retained, verified cancellation unload intent.");
        if(shipment.LoadedQuantity==0 && (operationId is not null || owner?.Pending is not null))
            throw new InvalidOperationException("Resolve pending physical cargo handling first.");
        var remaining=shipment.Quantity-shipment.DeliveredQuantity;
        var atOrigin=locationId==shipment.OriginLocationId;
        var returned=atOrigin?remaining:remaining-shipment.LoadedQuantity;
        var receipt=new CargoCancellationReceipt(EconomyIds.Derive("cancel-cargo-",jobId),locationId,utc,returned,remaining-returned,owner?.Id,operationId);
        const string reason="Player cancelled cargo job. No cancellation penalty during playtesting; no delivery credit or pay.";
        var inventories=world.Inventories.ToList();var ledger=world.InventoryLedger.ToList();
        if(returned>0)
        {
            var index=inventories.FindIndex(i=>i.LocationId==shipment.OriginLocationId&&i.ResourceId==shipment.ResourceId);
            if(index<0)inventories.Add(new(shipment.OriginLocationId,shipment.ResourceId,returned));
            else inventories[index]=inventories[index] with {AvailableQuantity=inventories[index].AvailableQuantity+returned};
            ledger.Add(new(EconomyIds.Derive("inventory-cancel-",jobId),InventoryTransactionKind.ShipmentReservationReleased,
                shipment.OriginLocationId,shipment.ResourceId,returned,jobId,utc,"Cancelled cargo returned/released at origin; remote unloaded cargo receives no delivery credit."));
        }
        var updated=world with {Revision=world.Revision+1,Inventories=inventories,InventoryLedger=ledger,
            Jobs=world.Jobs.Select(j=>j.Id==jobId?j with {State=atOrigin?EconomyJobState.Cancelled:EconomyJobState.Forfeited,
                CancelledUtc=utc,CancellationReason=reason,CargoCancellation=receipt}:j).ToArray(),
            Shipments=world.Shipments.Select(s=>s.JobId==jobId?s with {State=atOrigin?ShipmentState.Cancelled:ShipmentState.Forfeited,
                CurrentLocationId=locationId,LoadedQuantity=0,ForfeitedQuantity=atOrigin?0:remaining,CancelledUtc=utc,CancellationReason=reason}:s).ToArray(),
            Requirements=world.Requirements.Select(r=>r.Id==job.RequirementId?r with {State=TransportRequirementState.Cancelled,CancelledUtc=utc,CancellationReason=reason}:r).ToArray()};
        if(owner is not null)
        {
            var cargo=owner.Cargo.Where(c=>c.JobId!=jobId).ToArray();
            var next=owner with {Revision=owner.Revision+1,Cargo=cargo,Pending=null,
                Stations=operationId is null?owner.Stations:physical!.Targets,
                Legs=owner.Legs.Select(l=>CargoJourneyKernel.IsActive(l)?l with {AbortedUtc=utc}:l).ToArray(),
                History=operationId is null?owner.History:owner.History.Append(new CargoHandlingReceipt(operationId,locationId,utc,physical!.Changes,cargo.Sum(c=>c.Kilograms)){CancelledJobId=jobId}).ToArray()};
            updated=updated with {CargoAirframes=updated.CargoAirframes.Select(m=>m.Id==owner.Id?next:m).ToArray()};
        }
        return new(updated.EnsureValid(),true);
    }
}
