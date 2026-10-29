namespace FrontierAirportBuilder.Core;

public sealed record CargoPlacement(string JobId, string ShipmentId, decimal Kilograms, decimal DeliveredBefore = 0);
public sealed record CargoChange(string JobId, decimal Before, decimal After);
public sealed record CargoOperation(string Id, string LocationId, DateTimeOffset CreatedUtc, CargoChange[] Changes,
    PayloadStationReading[] BeforeStations, PlannedPayloadStation[] Targets, FlightPosition Position,
    bool Verified = false, string? Problem = null)
{
    public FuelTankReading[] FuelBefore { get; init; } = [];
    public DateTimeOffset? AuthorizedUtc { get; init; }
    public DateTimeOffset? VerifiedUtc { get; init; }
    public bool WriteStarted { get; init; }
    public decimal? ExpectedGrossKilograms { get; init; }
}
public sealed record CargoHandlingReceipt(string Id, string LocationId, DateTimeOffset Utc, CargoChange[] Changes, decimal KilogramsAboard);
public sealed record CargoFlightLeg(string Id, PassengerEndpoint Origin, PassengerEndpoint Destination,
    DateTimeOffset CreatedUtc, CargoPlacement[] Carried, FlightAttempt? Flight = null, DateTimeOffset? SettledUtc = null,
    string? EmployerId = null, decimal? HourlyRate = null);
public sealed record CargoAirframeManifest(string Id, Guid AirframeId, FlightAircraftIdentity Aircraft, string PlayerId,
    int Revision, string LocationId, CargoPlacement[] Cargo, PlannedPayloadStation[] Stations, string[] ManagedJobs,
    CargoOperation? Pending, CargoFlightLeg[] Legs, CargoHandlingReceipt[] History)
{ public PayloadStationReading[] StationLayout { get; init; } = []; }

public static class CargoJourneyKernel
{
    public static bool Manages(EconomyWorld w, string job) => w.CargoAirframes.Any(m => m.ManagedJobs.Contains(job));
    public static string ManifestId(Guid id) => "aircargo-" + id.ToString("N");
    public static EconomyTransition Replace(EconomyWorld w, CargoAirframeManifest m) => new((w with {
        Revision = w.Revision + 1, CargoAirframes = w.CargoAirframes.Where(x => x.Id != m.Id).Append(m).ToArray() }).EnsureValid(), true);
    public static bool Occupied(EconomyWorld w) => w.CargoAirframes.Any(x => x.Cargo.Length > 0 || x.Pending is not null || x.Legs.Any(l => l.SettledUtc is null));

    public static void Validate(EconomyWorld w)
    {
        EconomyValidation.Collection(w.CargoAirframes, "aircraft cargo manifests");
        if (w.CargoAirframes.Any(m=>m is null || m.ManagedJobs is null || m.Legs is null || m.Cargo is null)) throw new InvalidDataException("Cargo manifest collections are missing.");
        EconomyValidation.Unique(w.CargoAirframes.Select(x => x.Id), "aircraft cargo manifest");
        EconomyValidation.Unique(w.CargoAirframes.SelectMany(x => x.ManagedJobs), "managed consignment ownership");
        EconomyValidation.Unique(w.CargoAirframes.SelectMany(x => x.Legs.Select(l => l.Id)), "global cargo leg identity");
        EconomyValidation.Unique(w.CargoAirframes.SelectMany(x => x.Cargo.Select(c => c.ShipmentId)), "aboard shipment ownership");
        foreach (var m in w.CargoAirframes)
        {
            if (m.AirframeId == Guid.Empty || m.Id != ManifestId(m.AirframeId) || m.Revision < 1 || m.Cargo is null || m.Stations is null || m.Legs is null || m.History is null || m.ManagedJobs is null)
                throw new InvalidDataException("Aircraft cargo ownership is incomplete.");
            m.Aircraft.EnsureValid();
            if (!w.Players.Any(x => x.Id == m.PlayerId) || !w.Locations.Any(x => x.Id == m.LocationId)) throw new InvalidDataException("Unknown cargo owner/location.");
            EconomyValidation.Unique(m.ManagedJobs, "managed cargo job");
            foreach (var id in m.ManagedJobs)
                if (w.Jobs.SingleOrDefault(x => x.Id == id) is not {} job || job.PlayerId != m.PlayerId) throw new InvalidDataException("Managed consignment belongs to another player or is missing.");
            if (m.Pending is null && m.ManagedJobs.Any(id => w.Shipments.Single(x => x.JobId == id).LoadedQuantity > 0 && !m.Cargo.Any(c => c.JobId == id)))
                throw new InvalidDataException("Loaded consignment is missing its airframe placement.");
            if (m.StationLayout is null || m.StationLayout.Length == 0) throw new InvalidDataException("Cargo station layout is missing.");
            foreach (var station in m.StationLayout) station.EnsureValid();
            EconomyValidation.Unique(m.StationLayout.Select(x => x.Index.ToString()), "cargo station layout");
            EconomyValidation.Unique(m.Cargo.Select(x => x.JobId), "aboard cargo job");
            EconomyValidation.Unique(m.Stations.Select(x => x.StationIndex.ToString()), "cargo station");
            foreach (var c in m.Cargo)
            {
                var shipment = w.Shipments.SingleOrDefault(x => x.Id == c.ShipmentId);
                if (c.Kilograms <= 0 || shipment?.JobId != c.JobId || !m.ManagedJobs.Contains(c.JobId) ||
                m.Pending is null && shipment.LoadedQuantity != c.Kilograms) throw new InvalidDataException("Cargo placement and shipment disagree.");
            }
            if (m.Stations.Any(s => s.StationIndex is < 1 or > 15 || s.BaselineKilograms < 0 || s.FrontierCargoKilograms < 0 || s.TargetKilograms != s.BaselineKilograms + s.FrontierCargoKilograms) ||
                Math.Abs(m.Stations.Sum(x => x.FrontierCargoKilograms) - m.Cargo.Sum(x => x.Kilograms)) > .001m)
                throw new InvalidDataException("Cargo station claims do not match consignment mass.");
            if (m.Pending is {} p)
            {
                EconomyValidation.Id(p.Id, "cargo operation");
                p.Position.EnsureValid();
                if (!p.Position.IsOnGround || p.Position.GroundSpeedKnots > 5 || p.CreatedUtc == default || p.AuthorizedUtc < p.CreatedUtc || p.LocationId != m.LocationId || p.Changes.Length == 0 || p.Changes.Any(x => x.Before < 0 || x.After < 0 || x.Before == x.After || !m.ManagedJobs.Contains(x.JobId)))
                    throw new InvalidDataException("Invalid pending cargo operation.");
                EconomyValidation.Unique(p.Changes.Select(x => x.JobId), "cargo operation consignment");
                EconomyValidation.Unique(p.Targets.Select(x => x.StationIndex.ToString()), "cargo operation station");
                if (p.ExpectedGrossKilograms is <= 0 || p.Verified && (!p.WriteStarted || p.VerifiedUtc is null || p.VerifiedUtc < p.CreatedUtc || p.Targets.Any(s=>s.ReadBackKilograms is not {} actual || Math.Abs(actual-s.TargetKilograms)>.25m)))
                    throw new InvalidDataException("Verified cargo operation lacks retained station readback.");
                if (!p.BeforeStations.Select(s => s.Index).Order().SequenceEqual(m.StationLayout.Select(s => s.Index).Order()) ||
                    !p.Targets.Select(s => s.StationIndex).Order().SequenceEqual(p.BeforeStations.Select(s=>s.Index).Order()) ||
                    p.Targets.Any(s => s.BaselineKilograms < 0 || s.FrontierCargoKilograms < 0 || s.TargetKilograms != s.BaselineKilograms + s.FrontierCargoKilograms) ||
                    Math.Abs(p.Targets.Sum(s => s.FrontierCargoKilograms) - m.Cargo.Sum(c=>c.Kilograms) - p.Changes.Sum(c=>c.After-c.Before)) > .001m)
                    throw new InvalidDataException("Cargo operation target mass/layout does not match explicit consignments.");
            }
            EconomyValidation.Unique(m.History.Select(x => x.Id), "cargo handling receipt");
            EconomyValidation.Unique(m.Legs.Select(x => x.Id), "flight leg");
            if (m.Legs.Count(x => x.SettledUtc is null) > 1) throw new InvalidDataException("Only one current leg may operate one airframe.");
            foreach (var l in m.Legs)
            {
                EconomyValidation.Id(l.Id, "cargo leg"); l.Origin.EnsureValid(); l.Destination.EnsureValid(); l.Flight?.EnsureValid();
                if (l.CreatedUtc == default || !l.Origin.CanGenerate || !l.Destination.CanGenerate || l.Origin.Id == l.Destination.Id || l.Carried.Length == 0 || l.HourlyRate is <= 0 || (l.EmployerId is null) != (l.HourlyRate is null))
                    throw new InvalidDataException("Invalid cargo leg booking.");
                EconomyValidation.Unique(l.Carried.Select(c=>c.JobId), "leg carried consignment");
                foreach (var c in l.Carried)
                {
                    var job = w.Jobs.SingleOrDefault(j=>j.Id==c.JobId);
                    var shipment = w.Shipments.SingleOrDefault(s=>s.Id==c.ShipmentId);
                    if (job is null || shipment?.JobId != c.JobId || !m.ManagedJobs.Contains(c.JobId) || c.Kilograms <= 0 || c.DeliveredBefore < 0 ||
                        c.Kilograms+c.DeliveredBefore > shipment.Quantity || job.AcceptedUtc > l.CreatedUtc ||
                        l.Flight is {} boarded && boarded.LoadedUtc < l.CreatedUtc ||
                        !w.LogisticsContracts.Any(x=>x.JobId==c.JobId) && w.Businesses.Single(b=>b.Id==job.EmployerBusinessId).PilotHourlyRate is not null && job.EmployerBusinessId != l.EmployerId)
                        throw new InvalidDataException("Cargo leg consignment snapshot is inconsistent.");
                }
                if (l.EmployerId is {} employer && (!w.Businesses.Any(b=>b.Id==employer) || w.Players.Single(p=>p.Id==m.PlayerId).EmployerBusinessId != employer))
                    throw new InvalidDataException("Cargo leg employer differs from pilot employment.");
                if (l.Flight is {} f && (f.Id != FlightAttemptIds.Attempt(l.Id) || f.JobId != l.Id || f.ShipmentId != l.Id || f.Aircraft != m.Aircraft || f.OriginLocationId != l.Origin.Id || f.DestinationLocationId != l.Destination.Id ||
                    !f.TransitBeginsAtDeparture || !f.LoadedPosition.IsOnGround || f.LoadedPosition.GroundSpeedKnots > 5 || WorldTracking.DistanceMeters(f.LoadedPosition.Latitude,f.LoadedPosition.Longitude,l.Origin.Latitude,l.Origin.Longitude)>2778))
                    throw new InvalidDataException("Cargo leg flight identity/origin mismatch.");
                if (l.Flight?.DeliveryProof is {} proof && (!proof.ArrivalPosition.IsOnGround || proof.ArrivalPosition.GroundSpeedKnots>5 ||
                    WorldTracking.DistanceMeters(proof.ArrivalPosition.Latitude,proof.ArrivalPosition.Longitude,l.Destination.Latitude,l.Destination.Longitude)>2778 ||
                    l.Flight.OriginDepartedUtc is null || l.Flight.MaximumDistanceFromOriginNauticalMiles<l.Flight.RequiredMaximumDistanceNauticalMiles || l.SettledUtc < proof.ReadyUtc))
                    throw new InvalidDataException("Cargo leg arrival proof does not qualify at its endpoint.");
                if (l.SettledUtc is not null && l.Flight?.DeliveryProof is null) throw new InvalidDataException("Settled cargo leg lacks qualified flight proof.");
            }
        }
    }

    public static void ValidateWage(EconomyWorld w, FinancialTransaction t)
    {
        var owner = w.CargoAirframes.SingleOrDefault(m => m.Legs.Any(l => l.Id == t.EvidenceId));
        var leg = owner?.Legs.Single(x => x.Id == t.EvidenceId);
        if (owner is null || leg?.SettledUtc is null || leg.Flight?.DeliveryProof is null || t.Wage is not {} wage ||
            wage.FlightId != leg.Id || wage.EmployerId != leg.EmployerId || wage.HourlyRate != leg.HourlyRate || wage.AirborneTicks != leg.Flight.VerifiedAirborneTicks ||
            t.Id != EconomyIds.Derive("leg-wage-", leg.Id) || t.JobId is not null || t.SiteId is not null || t.ToEntityId != owner.PlayerId ||
            t.FromEntityId != leg.EmployerId || t.Amount != wage.Amount || t.OccurredUtc < leg.SettledUtc ||
            t.CurrencyCode != w.Players.Single(x => x.Id == owner.PlayerId).CurrencyCode || w.FinancialLedger.Count(x => x.Kind == t.Kind && x.EvidenceId == leg.Id) != 1)
            throw new InvalidDataException("Cargo leg wage audit is inconsistent.");
        wage.EnsureValid();
        if (!leg.Carried.Any(c => w.Jobs.Single(j=>j.Id==c.JobId).State==EconomyJobState.Completed ||
            owner.Pending is {Verified:true} p && p.LocationId==w.Shipments.Single(s=>s.JobId==c.JobId).DestinationLocationId &&
            p.Changes.Any(change=>change.JobId==c.JobId && change.After==0 && w.Shipments.Single(s=>s.JobId==c.JobId) is {} shipment && shipment.DeliveredQuantity+change.Before>=shipment.Quantity)))
            throw new InvalidDataException("Cargo leg wages require successful full consignment delivery.");
    }

    public static EconomyTransition SettleLeg(EconomyWorld w, CargoAirframeManifest m, CargoFlightLeg leg, DateTimeOffset utc)
    {
        if (leg.Flight?.DeliveryProof is null) throw new InvalidOperationException("Flight leg has not qualified arrival.");
        if (m.Legs.Single(x => x.Id == leg.Id).SettledUtc is not null) return new(w, false);
        var completed = leg with { SettledUtc = utc };
        m = m with { LocationId = leg.Destination.Id, Revision = m.Revision + 1, Legs = m.Legs.Select(x => x.Id == leg.Id ? completed : x).ToArray() };
        return new((w with { Revision=w.Revision+1, CargoAirframes = w.CargoAirframes.Select(x => x.Id == m.Id ? m : x).ToArray() }).EnsureValid(), true);
    }

    private static EconomyWorld ReleaseDeliveryWages(EconomyWorld w, CargoAirframeManifest m, string jobId, DateTimeOffset utc)
    {
        var updated=w;
        foreach (var leg in m.Legs.Where(l=>l.SettledUtc is not null && l.EmployerId is not null && l.Carried.Any(c=>c.JobId==jobId) &&
            !w.FinancialLedger.Any(t=>t.Kind==FinancialTransactionKind.CargoLegWage&&t.EvidenceId==l.Id)))
        {
            var employer=leg.EmployerId!;
            var player = w.Players.Single(x => x.Id == m.PlayerId);
            var wage = new PilotWage(leg.Id, employer, w.Businesses.Single(x => x.Id == employer).Name, leg.Flight!.VerifiedAirborneTicks, leg.HourlyRate!.Value);
            var receipt = new FinancialTransaction(EconomyIds.Derive("leg-wage-", leg.Id), FinancialTransactionKind.CargoLegWage, null, leg.Id,
                employer, m.PlayerId, wage.Amount, player.CurrencyCode, utc) { Wage = wage };
            updated = updated with { FinancialLedger = updated.FinancialLedger.Append(receipt).ToArray() };
            updated = updated with { Players = updated.Players.Select(x => x.Id == player.Id ? x with { CachedBalance = EconomyLedger.Balance(updated, x.Id, x.CurrencyCode) } : x).ToArray() };
        }
        return updated;
    }

    public static CargoFlightLeg DeliveryLeg(EconomyWorld w, string manifestId, string jobId)
    {
        var m = w.CargoAirframes.Single(x => x.Id == manifestId);
        var shipment = w.Shipments.Single(x => x.JobId == jobId);
        return m.Legs.LastOrDefault(l => l.SettledUtc is not null && l.Destination.Id == shipment.DestinationLocationId && l.Carried.Any(c => c.JobId == jobId && c.Kilograms > 0))
            ?? throw new InvalidOperationException("This consignment has no eligible arrival leg at its destination.");
    }

    public static EconomyTransition CommitOperation(EconomyWorld w, string manifestId, string operationId,
        Func<EconomyWorld, string, VerifiedDeliveryEvidence?> evidence)
    {
        var originalRevision = w.Revision;
        var m = w.CargoAirframes.Single(x => x.Id == manifestId);
        if (m.History.Any(x => x.Id == operationId)) return new(w, false);
        var p = m.Pending is { Verified: true } verified && verified.Id == operationId ? verified : throw new InvalidOperationException("Payload operation has not been verified.");
        foreach (var c in p.Changes)
        {
            var shipment=w.Shipments.Single(s=>s.JobId==c.JobId);
            if (c.After==0 && c.Before>0 && p.LocationId==shipment.DestinationLocationId && shipment.DeliveredQuantity+c.Before>=shipment.Quantity &&
                w.Businesses.Single(b=>b.Id==w.Jobs.Single(j=>j.Id==c.JobId).EmployerBusinessId).PilotHourlyRate is not null && !w.LogisticsContracts.Any(contract=>contract.JobId==c.JobId))
                w=ReleaseDeliveryWages(w,m,c.JobId,p.CreatedUtc);
            var request = new ShipmentCargoTransferRequest(EconomyIds.Derive("transfer-", p.Id + c.JobId),
                EconomyIds.Derive("cargo-command-", p.Id + c.JobId), c.JobId, c.After, p.LocationId,
                p.Position.Latitude, p.Position.Longitude, p.CreatedUtc, CompletionEvidence: evidence(w, c.JobId)) { AirframeManifestId = m.Id };
            w = EconomyKernel.ApplyCargoTransfer(w, request).World;
        }
        var cargo = m.ManagedJobs.Select(id => w.Shipments.Single(x => x.JobId == id)).Where(x => x.LoadedQuantity > 0)
            .Select(s => new CargoPlacement(s.JobId, s.Id, s.LoadedQuantity, s.DeliveredQuantity)).ToArray();
        m = m with { Revision = m.Revision + 1, Cargo = cargo, Stations = p.Targets, Pending = null,
            History = m.History.Append(new(p.Id, p.LocationId, p.CreatedUtc, p.Changes, cargo.Sum(x => x.Kilograms))).ToArray() };
        return new((w with { Revision = originalRevision + 1, CargoAirframes = w.CargoAirframes.Select(x => x.Id == m.Id ? m : x).ToArray() }).EnsureValid(), true);
    }
}
