namespace FrontierAirportBuilder.Core;

public enum PassengerEndpointKind { Airport, Platform }
public enum PassengerTransferState { Offered, Reserved, Boarded, Delivered, Expired }

/// <summary>Immutable route snapshot. Platform integration is reserved, never airport-radius qualified.</summary>
public sealed record PassengerEndpoint(string Id, string Name, double Latitude, double Longitude,
    PassengerEndpointKind Kind = PassengerEndpointKind.Airport)
{
    // There is deliberately no user-settable "qualified" flag. A future platform adapter and
    // exact live qualification record must replace this gate before platforms can generate work.
    public bool CanGenerate => Kind == PassengerEndpointKind.Airport;
    public void EnsureValid()
    {
        EconomyValidation.Id(Id, "passenger endpoint"); EconomyValidation.Text(Name, "passenger endpoint name", 128);
        if (!Enum.IsDefined(Kind) || !double.IsFinite(Latitude) || !double.IsFinite(Longitude) ||
            Latitude is <= -90 or >= 90 || Longitude is < -180 or > 180)
            throw new InvalidDataException("Invalid passenger endpoint.");
    }
}

public sealed record PassengerTransfer(string Id, string EmployerId, string PlayerId, PassengerEndpoint Origin,
    PassengerEndpoint Destination, int PassengerCount, string[] EligibleAircraftTypes, decimal HourlyRate,
    DateTimeOffset OfferedUtc, DateTimeOffset ExpiresUtc, PassengerTransferState State = PassengerTransferState.Offered,
    string? AircraftTypeId = null, DateTimeOffset? AcceptedUtc = null, FlightAttempt? Flight = null,
    DateTimeOffset? DisembarkedUtc = null, PilotWage? CompletedWage = null, Guid? AirframeId = null)
{
    public string Route => Origin.Name + " → " + Destination.Name;
    public string People => $"{PassengerCount} specialist passenger (prototype)";
    public string Status => State == PassengerTransferState.Boarded ? Flight?.DeliveryProof is null ? "Aboard / flight in progress" : "Ready to disembark" : State.ToString();
    public string Pay => CompletedWage is {} wage
        ? $"{DollarDisplay.Format(wage.Amount)} · {TimeSpan.FromTicks(wage.AirborneTicks).TotalMinutes:0.##} min @ {DollarDisplay.Format(wage.HourlyRate)}/hr"
        : DollarDisplay.Format(HourlyRate) + " / flight hour";

    public void EnsureValid()
    {
        EconomyValidation.Id(Id, "passenger transfer"); EconomyValidation.Id(EmployerId, "passenger employer"); EconomyValidation.Id(PlayerId, "passenger player");
        if (Origin is null || Destination is null) throw new InvalidDataException("Passenger endpoints are missing.");
        Origin.EnsureValid(); Destination.EnsureValid();
        if (!Enum.IsDefined(State) || !Origin.CanGenerate || !Destination.CanGenerate || Origin.Id == Destination.Id ||
            PassengerCount != 1 || HourlyRate <= 0 || OfferedUtc == default || ExpiresUtc <= OfferedUtc ||
            EligibleAircraftTypes is null || EligibleAircraftTypes.Length == 0 || EligibleAircraftTypes.Distinct().Count() != EligibleAircraftTypes.Length)
            throw new InvalidDataException("Passenger transfer route, count or policy is invalid. Platforms remain unavailable.");
        foreach (var type in EligibleAircraftTypes) EconomyValidation.Id(type, "passenger aircraft type");
        if (State is PassengerTransferState.Offered or PassengerTransferState.Expired)
        {
            if (AircraftTypeId is not null || AcceptedUtc is not null || Flight is not null || DisembarkedUtc is not null || CompletedWage is not null || AirframeId is not null)
                throw new InvalidDataException("Unaccepted passenger offer contains flight state.");
            return;
        }
        if (AcceptedUtc is null || AcceptedUtc < OfferedUtc || AcceptedUtc >= ExpiresUtc || AircraftTypeId is null || !EligibleAircraftTypes.Contains(AircraftTypeId))
            throw new InvalidDataException("Passenger reservation is inconsistent.");
        if (State == PassengerTransferState.Reserved && Flight is not null || State != PassengerTransferState.Reserved && Flight is null)
            throw new InvalidDataException("Passenger boarding state is inconsistent.");
        if (AirframeId == Guid.Empty || State == PassengerTransferState.Reserved && AirframeId is not null)
            throw new InvalidDataException("Passenger airframe association is inconsistent.");
        if (Flight is {} flight)
        {
            flight.EnsureValid();
            if (flight.Id != FlightAttemptIds.Attempt(Id) || flight.JobId != Id || flight.ShipmentId != Id ||
                flight.OriginLocationId != Origin.Id || flight.DestinationLocationId != Destination.Id || flight.LoadedUtc < AcceptedUtc ||
                (State == PassengerTransferState.Delivered) != (flight.State == FlightAttemptState.Delivered) || !Enum.IsDefined(flight.State) ||
                !flight.TransitBeginsAtDeparture || flight.LoadedPosition.GroundSpeedKnots > 5 ||
                WorldTracking.DistanceMeters(flight.LoadedPosition.Latitude, flight.LoadedPosition.Longitude, Origin.Latitude, Origin.Longitude) > 2778)
                throw new InvalidDataException("Passenger flight identity/state is inconsistent.");
            if (flight.DeliveryProof is {} proof && (!proof.ArrivalPosition.IsOnGround || proof.ArrivalPosition.GroundSpeedKnots > 5 ||
                WorldTracking.DistanceMeters(proof.ArrivalPosition.Latitude, proof.ArrivalPosition.Longitude, Destination.Latitude, Destination.Longitude) > 2778 ||
                flight.OriginDepartedUtc is null || flight.MaximumDistanceFromOriginNauticalMiles < flight.RequiredMaximumDistanceNauticalMiles))
                throw new InvalidDataException("Passenger arrival proof does not qualify at its accepted endpoint.");
        }
        if (State == PassengerTransferState.Delivered)
        {
            if (DisembarkedUtc is null || DisembarkedUtc < Flight!.DeliveryProof!.ReadyUtc || CompletedWage is null ||
                CompletedWage.EnsureValid().EmployerId != EmployerId || CompletedWage.HourlyRate != HourlyRate ||
                CompletedWage.AirborneTicks != Flight.VerifiedAirborneTicks || CompletedWage.FlightId != Flight.Id)
                throw new InvalidDataException("Passenger completion receipt is inconsistent.");
        }
        else if (DisembarkedUtc is not null || CompletedWage is not null)
            throw new InvalidDataException("Incomplete passenger transfer contains a completion receipt.");
    }
}

public static class PassengerTransferKernel
{
    public static string OfferId(string employerId, string destinationId, DateTimeOffset cycle) =>
        EconomyIds.Derive("passenger-", employerId + "-" + destinationId + "-" + cycle.UtcTicks);
    public static void Validate(EconomyWorld world)
    {
        EconomyValidation.Collection(world.PassengerTransfers, "passenger transfers");
        if (world.PassengerTransfers.Any(x => x is null)) throw new InvalidDataException("Passenger transfer record is missing.");
        EconomyValidation.Unique(world.PassengerTransfers.Select(x => x.Id), "passenger transfer");
        foreach (var t in world.PassengerTransfers)
        {
            t.EnsureValid();
            if (world.Jobs.Any(x => x.Id == t.Id) || !world.Businesses.Any(x => x.Id == t.EmployerId) ||
                !world.Players.Any(x => x.Id == t.PlayerId && x.EmployerBusinessId == t.EmployerId))
                throw new InvalidDataException("Passenger assignment identity/employment is inconsistent.");
            var payments = world.FinancialLedger.Where(x => x.JobId == t.Id).ToArray();
            if (t.State == PassengerTransferState.Delivered)
            {
                if (payments.Length != 1) throw new InvalidDataException("Passenger delivery requires exactly one wage receipt.");
                ValidatePayment(world, t, payments[0]);
            }
            else if (payments.Length != 0) throw new InvalidDataException("Undelivered passengers cannot be paid.");
        }
        foreach (var group in world.PassengerTransfers.Where(x => x.State == PassengerTransferState.Boarded).GroupBy(x => x.PlayerId))
            if (group.Count() > 1 || world.Jobs.Any(j => j.PlayerId == group.Key && world.Shipments.Any(s => s.JobId == j.Id && s.State is ShipmentState.Loaded or ShipmentState.InTransit)))
                throw new InvalidDataException("Complete the current passenger or cargo flight before boarding another load.");
    }

    public static void ValidatePayment(EconomyWorld world, PassengerTransfer t, FinancialTransaction payment)
    {
        var player = world.Players.Single(x => x.Id == t.PlayerId);
        if (t.State != PassengerTransferState.Delivered || payment.Kind != FinancialTransactionKind.PilotWage || payment.Wage != t.CompletedWage ||
            payment.Id != EconomyIds.Derive("wage-", t.Flight!.Id) || payment.EvidenceId != t.Flight.DeliveryProof!.EvidenceId ||
            payment.SiteId is not null || payment.FromEntityId != t.EmployerId || payment.ToEntityId != t.PlayerId ||
            payment.CurrencyCode != player.CurrencyCode || payment.OccurredUtc != t.Flight.DeliveryProof.ReadyUtc || payment.Amount != t.CompletedWage!.Amount ||
            world.FinancialLedger.Count(x => x.Kind == FinancialTransactionKind.PilotWage && x.Wage?.FlightId == t.Flight.Id) != 1)
            throw new InvalidDataException("Passenger wage audit/payment is inconsistent.");
    }

    public static EconomyTransition Accept(EconomyWorld world, string id, string type, DateTimeOffset utc)
    {
        world.EnsureValid(); var t = world.PassengerTransfers.Single(x => x.Id == id);
        if (t.State is PassengerTransferState.Reserved or PassengerTransferState.Boarded or PassengerTransferState.Delivered) return new(world, false);
        if (t.State != PassengerTransferState.Offered || utc < t.OfferedUtc || utc >= t.ExpiresUtc || !t.EligibleAircraftTypes.Contains(type))
            throw new InvalidOperationException("Choose an eligible aircraft for an available passenger transfer.");
        return Replace(world, t with { State = PassengerTransferState.Reserved, AcceptedUtc = utc, AircraftTypeId = type });
    }

    public static EconomyTransition Board(EconomyWorld world, string id, FlightAttempt flight, Guid? airframeId = null)
    {
        world.EnsureValid(); var t = world.PassengerTransfers.Single(x => x.Id == id);
        if (t.State == PassengerTransferState.Boarded) return new(world, false);
        if (t.State != PassengerTransferState.Reserved) throw new InvalidOperationException("Reserve the passenger transfer before boarding.");
        return Replace(world, t with { State = PassengerTransferState.Boarded, Flight = flight, AirframeId = airframeId });
    }

    public static EconomyTransition Complete(EconomyWorld world, string id, DateTimeOffset utc)
    {
        world.EnsureValid(); var t = world.PassengerTransfers.Single(x => x.Id == id);
        if (t.State == PassengerTransferState.Delivered) return new(world, false);
        if (t.State != PassengerTransferState.Boarded || t.Flight?.DeliveryProof is not {} proof || utc < proof.ReadyUtc)
            throw new InvalidOperationException("Passenger disembarkation requires the persisted qualified arrival.");
        var employer = world.Businesses.Single(x => x.Id == t.EmployerId);
        var wage = new PilotWage(t.Flight.Id, t.EmployerId, employer.Name, t.Flight.VerifiedAirborneTicks, t.HourlyRate).EnsureValid();
        var player = world.Players.Single(x => x.Id == t.PlayerId);
        var payment = new FinancialTransaction(EconomyIds.Derive("wage-", t.Flight.Id), FinancialTransactionKind.PilotWage, t.Id, proof.EvidenceId,
            t.EmployerId, t.PlayerId, wage.Amount, player.CurrencyCode, proof.ReadyUtc) { Wage = wage };
        var updated = t with { State = PassengerTransferState.Delivered, DisembarkedUtc = utc, CompletedWage = wage,
            Flight = t.Flight with { State = FlightAttemptState.Delivered, DeliveredUtc = proof.ReadyUtc } };
        var interim = world with { PassengerTransfers = world.PassengerTransfers.Select(x => x.Id == id ? updated : x).ToArray(),
            FinancialLedger = world.FinancialLedger.Append(payment).ToArray() };
        return new((interim with { Revision = world.Revision + 1, Players = interim.Players.Select(x => x.Id == player.Id
            ? x with { CachedBalance = EconomyLedger.Balance(interim, x.Id, x.CurrencyCode) } : x).ToArray() }).EnsureValid(), true);
    }

    public static EconomyTransition Replace(EconomyWorld world, PassengerTransfer transfer) =>
        new((world with { Revision = world.Revision + 1, PassengerTransfers = world.PassengerTransfers.Select(x => x.Id == transfer.Id ? transfer : x).ToArray() }).EnsureValid(), true);
}
