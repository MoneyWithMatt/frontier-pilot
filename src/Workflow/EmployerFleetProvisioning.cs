using System.Security.Cryptography;
using System.Text;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed record EmployerFleetResult(AircraftFleet Fleet, IReadOnlyList<string> Reconciliation);

/// <summary>Allocates records in the existing physical fleet, never simulator objects or economic assets.</summary>
public static class EmployerFleetProvisioning
{
    public static string Key(Guid player, string employer, string type) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{player:N}/{employer}/{type}"))).ToLowerInvariant();

    public static EmployerFleetResult Provision(AircraftFleet source, Guid player, CareerEmployer employer, DateTimeOffset utc)
    {
        source.Validate();
        if (player==Guid.Empty || utc==default) throw new InvalidDataException("A durable player identity and allocation date are required.");
        var aircraft=source.Aircraft.ToList();var problems=new List<string>();
        var basis=CareerCatalogue.Bases.Single(x=>x.Id==employer.BaseId);
        foreach (var typeId in employer.Fleet)
        {
            var type=CompanyAircraft.Types.Single(x=>x.Id==typeId);
            var key=Key(player,employer.Id,typeId);
            var existing=aircraft.Where(a=>a.OwnerEntityId==employer.Id && a.AssignedPlayerId==player && a.CompanyTypeId==typeId).ToArray();
            if(existing.Length>1){problems.Add($"{type.Name}: several assigned company aircraft exist; records preserved, no allocation added.");continue;}
            if(existing.Length==1)continue;
            var ambiguous=aircraft.Where(a=>(a.OwnerEntityId is null && a.Operator==employer.Id || a.OwnerEntityId==employer.Id&&a.AssignedPlayerId is null) && (a.CompanyTypeId==typeId || a.CompanyTypeId is null)).ToArray();
            if(ambiguous.Length>0){problems.Add($"{type.Name}: {ambiguous.Length} legacy company-labelled record(s) need explicit assignment confirmation; no duplicate created.");continue;}
            var id=Guid.NewGuid();var registration="FP-"+id.ToString("N").ToUpperInvariant();
            while(aircraft.Any(a=>a.Id==id || a.Registration.Equals(registration,StringComparison.OrdinalIgnoreCase)))
            {id=Guid.NewGuid();registration="FP-"+id.ToString("N").ToUpperInvariant();}
            var identity=AircraftConditionRecord.Create(registration,type.Name,type.Id,"Unbound company allocation") with {AirframeId=id};
            var a=new PhysicalAirframe(identity,registration,employer.Id,type.Id,[],[],[],[],
                [new(Guid.NewGuid(),utc,"Company allocation","Employer-owned starting airframe. Historical hours/condition unknown; base is an allocation, not observed placement. Simulator type must be confirmed.")])
            {OwnerEntityId=employer.Id,AssignedPlayerId=player,AllocationKey=key,AllocatedTypeName=type.Name,
                AllocatedBase=new(basis.Id,basis.Name,basis.Identifier,basis.Latitude,basis.Longitude)};
            aircraft.Add(a);
        }
        var next=(source with {SchemaVersion=2,Aircraft=aircraft}).Validate();
        return new(next,problems);
    }

    public static EmployerFleetResult Ensure(AircraftFleetStore store, Guid player, CareerEmployer employer, DateTimeOffset utc)
    {
        // Revision checking plus the store's cross-process lease protects simultaneous startup.
        for(var attempt=0;;attempt++)
        {
            var prior=store.Load();var result=Provision(prior,player,employer,utc);
            if(prior.SchemaVersion==result.Fleet.SchemaVersion && prior.Aircraft.SequenceEqual(result.Fleet.Aircraft))return result;
            try{return result with {Fleet=store.Commit(result.Fleet with{Revision=prior.Revision+1},prior.Revision)};}
            catch(IOException) when(attempt<2 && store.Load().Revision!=prior.Revision) { }
        }
    }

    public static PhysicalAirframe ConfirmLegacy(PhysicalAirframe a, Guid player, CareerEmployer employer, DateTimeOffset utc, string? confirmedType = null)
    {
        var type=a.CompanyTypeId??confirmedType;
        if(a.OwnerEntityId is not null&&a.OwnerEntityId!=employer.Id || a.AssignedPlayerId is not null || a.OwnerEntityId is null&&a.Operator!=employer.Id || type is null || !employer.Fleet.Contains(type) || a.CompanyTypeId is not null&&confirmedType is not null&&a.CompanyTypeId!=confirmedType)
            throw new InvalidOperationException("Only an unassigned legacy record explicitly labelled for this company/type may be reconciled.");
        var basis=CareerCatalogue.Bases.Single(x=>x.Id==employer.BaseId);
        return FleetOperations.Event(a with {CompanyTypeId=type,OwnerEntityId=employer.Id,AssignedPlayerId=player,AllocationKey=Key(player,employer.Id,type),
            AllocatedTypeName=CompanyAircraft.Types.Single(t=>t.Id==type).Name,AllocatedBase=new(basis.Id,basis.Name,basis.Identifier,basis.Latitude,basis.Longitude)},
            Guid.NewGuid(),utc,"Company assignment confirmed","Player explicitly reconciled existing company-labelled aircraft; identity, registration, condition, location and history retained.");
    }
}
