using System.Text.Json.Serialization;

namespace FrontierAirportBuilder.Core;

public sealed record PassengerPerson(string Id, decimal? WeightKilograms)
{
    public void EnsureValid()
    {
        EconomyValidation.Id(Id, "passenger identity");
        if (WeightKilograms is <= 0) throw new InvalidDataException("Passenger manifest weights must be positive kilograms.");
    }
}

/// <summary>People and their declared manifest weights; no employer, aircraft or payment policy.</summary>
public sealed record PassengerGroup(string Id, PassengerPerson[] People)
{
    [JsonIgnore] public int Count => People.Length;
    [JsonIgnore] public decimal? WeightKilograms => People.All(x => x.WeightKilograms is not null) ? People.Sum(x => x.WeightKilograms!.Value) : null;
    public void EnsureValid()
    {
        EconomyValidation.Id(Id, "passenger group");
        if (People is null || People.Length is < 1 or > 1000 || People.Any(x => x is null)) throw new InvalidDataException("Passenger group is missing or oversized.");
        EconomyValidation.Unique(People.Select(x => x.Id), "passenger identity");
        foreach (var person in People) person.EnsureValid();
    }
}

public enum PassengerCarriageState { Planned, Reserved, Boarding, Aboard, Arrived, Disembarking, Disembarked }
public enum PassengerPayloadAction { Board, Disembark }
public enum PassengerPayloadStage { Pending, Verified }

/// <summary>Configuration-backed cabin area. Seats are established by aircraft-specific evidence,
/// never by PAYLOAD STATION COUNT or default station mass. A station may represent several seats.</summary>
public sealed record PassengerStationConfiguration(int Index, int Seats, decimal? MaximumKilograms)
{
    public void EnsureValid()
    {
        if (Index is < 1 or > 15 || Seats is < 1 or > 1000 || MaximumKilograms is <= 0)
            throw new InvalidDataException("Passenger station configuration is invalid.");
    }
}

public sealed record PassengerAircraftConfiguration(string Id, string AircraftTypeId, FlightAircraftIdentity Aircraft,
    int StationCount, PassengerStationConfiguration[] Stations, decimal MaximumGrossKilograms,
    decimal? MaximumPayloadKilograms, string ConfigurationHash, string Evidence, string VisualSupport)
{
    [JsonIgnore] public int Capacity => Stations.Sum(x => x.Seats);
    public void EnsureValid()
    {
        EconomyValidation.Id(Id, "passenger aircraft configuration"); EconomyValidation.Id(AircraftTypeId, "passenger aircraft type");
        if (Aircraft is null) throw new InvalidDataException("Passenger aircraft identity is missing.");
        Aircraft.EnsureValid();
        if (StationCount is < 1 or > 15 || Stations is null || Stations.Length == 0 || Stations.Any(x => x is null) || MaximumGrossKilograms <= 0 || MaximumPayloadKilograms is <= 0 ||
            ConfigurationHash is null || ConfigurationHash.Length != 64 || ConfigurationHash.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Passenger capacity/configuration evidence is invalid.");
        foreach (var station in Stations) { station.EnsureValid(); if (station.Index > StationCount) throw new InvalidDataException("Passenger station exceeds configured count."); }
        EconomyValidation.Unique(Stations.Select(x => x.Index.ToString()), "passenger configured station");
        EconomyValidation.Text(Evidence, "passenger configuration evidence", 512); EconomyValidation.Text(VisualSupport, "passenger visual capability", 512);
    }
}

public sealed record PassengerOwnedStation(int Index, string Name, decimal BaselineKilograms, decimal PassengerKilograms, int People)
{
    [JsonIgnore] public decimal LoadedKilograms => BaselineKilograms + PassengerKilograms;
}
public sealed record PassengerProtectedStation(int Index, string Name, decimal BeforeKilograms);

/// <summary>Durable absolute targets and ownership, saved before any simulator write. No mission/payment fields.</summary>
public sealed record PassengerPayloadIntent(string Id, PassengerPayloadAction Action, PassengerPayloadStage Stage,
    Guid AirframeId, PassengerAircraftConfiguration Configuration, PassengerOwnedStation[] OwnedStations,
    PassengerProtectedStation[] ProtectedStations, decimal BeforeGrossKilograms, DateTimeOffset CreatedUtc,
    FlightPosition AuthorizedPosition, DateTimeOffset? VerifiedUtc = null, FlightPosition? VerifiedPosition = null,
    string? Problem = null)
{
    [JsonIgnore] public IReadOnlyDictionary<int, decimal> Targets => OwnedStations.ToDictionary(x => x.Index,
        x => Action == PassengerPayloadAction.Board ? x.LoadedKilograms : x.BaselineKilograms);
    public void EnsureValid()
    {
        EconomyValidation.Id(Id, "passenger payload intent");
        if (Configuration is null || AuthorizedPosition is null) throw new InvalidDataException("Passenger intent configuration/position is missing.");
        Configuration.EnsureValid(); AuthorizedPosition.EnsureValid();
        if (!Enum.IsDefined(Action) || !Enum.IsDefined(Stage) || AirframeId == Guid.Empty || CreatedUtc == default || BeforeGrossKilograms <= 0 || !AuthorizedPosition.IsOnGround ||
            OwnedStations is null || OwnedStations.Length == 0 || ProtectedStations is null || OwnedStations.Any(x => x is null) || ProtectedStations.Any(x => x is null))
            throw new InvalidDataException("Passenger payload intent is incomplete.");
        foreach (var station in OwnedStations)
            if (station.Index is < 1 or > 15 || string.IsNullOrWhiteSpace(station.Name) || station.BaselineKilograms < 0 || station.PassengerKilograms <= 0 || station.People <= 0 || !Configuration.Stations.Any(x => x.Index == station.Index && x.Seats >= station.People))
                throw new InvalidDataException("Passenger station ownership is invalid.");
        foreach (var station in ProtectedStations)
            if (station.Index is < 1 or > 15 || string.IsNullOrWhiteSpace(station.Name) || station.BeforeKilograms < 0)
                throw new InvalidDataException("Protected payload station is invalid.");
        EconomyValidation.Unique(OwnedStations.Select(x => x.Index.ToString()), "passenger owned station");
        EconomyValidation.Unique(ProtectedStations.Select(x => x.Index.ToString()), "protected payload station");
        if (OwnedStations.Any(x => ProtectedStations.Any(p => p.Index == x.Index)) || !OwnedStations.Select(x => x.Index).Concat(ProtectedStations.Select(x => x.Index)).Order().SequenceEqual(Enumerable.Range(1, Configuration.StationCount)))
            throw new InvalidDataException("Passenger ownership/protected stations overlap or are incomplete.");
        if ((Stage == PassengerPayloadStage.Verified) != (VerifiedUtc is not null && VerifiedPosition is not null) ||
            Stage == PassengerPayloadStage.Pending && (VerifiedUtc is not null || VerifiedPosition is not null))
            throw new InvalidDataException("Passenger verification receipt is inconsistent.");
        if (VerifiedPosition is {} position)
        {
            position.EnsureValid();
            if (position.ObservedUtc != VerifiedUtc || VerifiedUtc < CreatedUtc || !position.IsOnGround)
                throw new InvalidDataException("Passenger verification position/time is invalid.");
        }
    }
}

/// <summary>Reusable carriage aggregate. A future airline may reserve the same model without wages or an employed mission.</summary>
public sealed record PassengerTransport(string Id, PassengerGroup Group, PassengerCarriageState State,
    PassengerPayloadIntent? Payload = null, FlightPosition? Arrival = null)
{
    public void EnsureValid()
    {
        EconomyValidation.Id(Id, "passenger transport");
        if (Group is null) throw new InvalidDataException("Missing passenger group.");
        Group.EnsureValid();
        if (!Enum.IsDefined(State)) throw new InvalidDataException("Unknown passenger carriage state.");
        if (State is PassengerCarriageState.Planned or PassengerCarriageState.Reserved)
        { if (Payload is not null || Arrival is not null) throw new InvalidDataException("Unboarded transport contains payload ownership/arrival."); return; }
        if (Payload is null || Group.WeightKilograms is not {} mass) throw new InvalidDataException("Physical passenger carriage lacks its manifest/intent.");
        Payload.EnsureValid();
        if (Payload.OwnedStations.Sum(x => x.People) != Group.Count || Payload.OwnedStations.Sum(x => x.PassengerKilograms) != mass || Group.Count > Payload.Configuration.Capacity)
            throw new InvalidDataException("Passenger ownership does not match group count/weight/capacity.");
        var expected = State is PassengerCarriageState.Boarding or PassengerCarriageState.Aboard or PassengerCarriageState.Arrived ? PassengerPayloadAction.Board : PassengerPayloadAction.Disembark;
        if (Payload.Action != expected || (State is PassengerCarriageState.Aboard or PassengerCarriageState.Arrived or PassengerCarriageState.Disembarked) != (Payload.Stage == PassengerPayloadStage.Verified))
            throw new InvalidDataException("Carriage state does not match payload verification.");
        if (State is PassengerCarriageState.Arrived or PassengerCarriageState.Disembarking or PassengerCarriageState.Disembarked)
        {
            if (Arrival is null || !Arrival.EnsureValid().IsOnGround || Arrival.GroundSpeedKnots > 5 || Arrival.ObservedUtc < Payload.CreatedUtc && Payload.Action == PassengerPayloadAction.Board)
                throw new InvalidDataException("Passenger arrival authorization is missing or invalid.");
        }
        else if (Arrival is not null) throw new InvalidDataException("Unarrived carriage has arrival state.");
    }
}

public static class PassengerTransportKernel
{
    public static string TransportId(string missionId) => EconomyIds.Derive("transport-", missionId);
    public static PassengerTransport Plan(string missionId, int count, bool reserved = false)
    {
        if (count is < 1 or > 1000) throw new InvalidDataException("Passenger group size is invalid.");
        var id = TransportId(missionId);
        return new(id, new(EconomyIds.Derive("group-", id), Enumerable.Range(1, count).Select(i => new PassengerPerson(EconomyIds.Derive("person-", id + "-" + i), null)).ToArray()),
            reserved ? PassengerCarriageState.Reserved : PassengerCarriageState.Planned);
    }
    public static PassengerTransport WithWeights(PassengerTransport transport, IReadOnlyList<decimal> weights)
    {
        transport.EnsureValid();
        if (transport.State is not (PassengerCarriageState.Planned or PassengerCarriageState.Reserved) || weights.Count != transport.Group.Count)
            throw new InvalidOperationException("Enter one kilogram weight per reserved passenger before boarding.");
        var updated = transport with { Group = transport.Group with { People = transport.Group.People.Select((p, i) => p with { WeightKilograms = weights[i] }).ToArray() } };
        updated.EnsureValid(); return updated;
    }
    public static void Validate(EconomyWorld world)
    {
        EconomyValidation.Collection(world.PassengerTransports, "passenger carriage");
        if (world.PassengerTransports.Any(x => x is null)) throw new InvalidDataException("Missing passenger carriage.");
        foreach (var transport in world.PassengerTransports) transport.EnsureValid();
        EconomyValidation.Unique(world.PassengerTransports.Select(x => x.Id), "passenger carriage");
        EconomyValidation.Unique(world.PassengerTransports.Select(x => x.Group.Id), "passenger group");
        EconomyValidation.Unique(world.PassengerTransports.SelectMany(x => x.Group.People).Select(x => x.Id), "passenger identity");
        // Occupied Frontier passenger stations cannot be owned by two transports on one physical airframe.
        var active = world.PassengerTransports.Where(x => x.State is PassengerCarriageState.Boarding or PassengerCarriageState.Aboard or PassengerCarriageState.Arrived or PassengerCarriageState.Disembarking).ToArray();
        EconomyValidation.Unique(active.SelectMany(t => t.Payload!.OwnedStations.Select(s => t.Payload.AirframeId + "/" + s.Index)), "passenger seat ownership");
    }
}

public static class PassengerLoadPlanner
{
    public const decimal ToleranceKilograms = .05m;
    public static void CheckUnchangedBeforeWrite(AircraftLoadingSnapshot expected, AircraftLoadingSnapshot live)
    {
        expected.EnsureValid(); live.EnsureValid();
        if (live.Aircraft != expected.Aircraft || !live.Position.IsOnGround || live.Position.GroundSpeedKnots > 5 ||
            live.ObservedUtc < expected.ObservedUtc || WorldTracking.DistanceMeters(expected.Position.Latitude, expected.Position.Longitude, live.Position.Latitude, live.Position.Longitude) > 25 ||
            live.PayloadStations.Count != expected.PayloadStations.Count || expected.PayloadStations.Any(p => live.PayloadStations.SingleOrDefault(s => s.Index == p.Index) is not {} s || s.Name != p.Name || Math.Abs(s.CurrentWeightKilograms - p.CurrentWeightKilograms) > ToleranceKilograms) ||
            !expected.FuelTanks.Select(x => (x.Id, x.QuantityGallons)).SequenceEqual(live.FuelTanks.Select(x => (x.Id, x.QuantityGallons))))
            throw new InvalidOperationException("Aircraft, fuel or payload changed immediately before the passenger write. No payload write was issued; saved intent retained.");
    }
    public static PassengerPayloadIntent Board(PassengerTransport transport, PassengerAircraftConfiguration configuration,
        AircraftLoadingSnapshot supplied, Guid airframeId)
    {
        transport.EnsureValid(); configuration.EnsureValid(); var snapshot = supplied.EnsureValid();
        CheckAircraft(snapshot, configuration);
        if (transport.State != PassengerCarriageState.Reserved || transport.Group.WeightKilograms is not {} mass)
            throw new InvalidOperationException("Confirm every passenger's manifest weight before physical boarding.");
        var available = configuration.Stations.OrderBy(x => x.Index).Where(x => snapshot.PayloadStations.Single(s => s.Index == x.Index).CurrentWeightKilograms <= .001m).ToArray();
        if (transport.Group.Count > available.Sum(x => x.Seats))
            throw new InvalidOperationException("The verified cabin has insufficient empty passenger seats; existing occupants/payload are preserved.");
        CheckLimits(snapshot, configuration, mass);
        var remaining = transport.Group.People.ToList(); var owned = new List<PassengerOwnedStation>();
        foreach (var area in available)
        {
            if (remaining.Count == 0) break;
            var people = remaining.Take(area.Seats).ToArray(); remaining.RemoveRange(0, people.Length);
            var station = snapshot.PayloadStations.Single(x => x.Index == area.Index);
            var added = people.Sum(x => x.WeightKilograms!.Value);
            var limit = Minimum(area.MaximumKilograms, station.AuthoritativeMaximumKilograms);
            if (!station.IsWritable || limit is {} maximum && station.CurrentWeightKilograms + added > maximum + .001m)
                throw new InvalidOperationException("Passenger allocation exceeds a reported/configured station limit or station is not writable.");
            owned.Add(new(area.Index, station.Name, station.CurrentWeightKilograms, added, people.Length));
        }
        var protectedStations = snapshot.PayloadStations.Where(x => owned.All(o => o.Index != x.Index)).Select(x => new PassengerProtectedStation(x.Index, x.Name, x.CurrentWeightKilograms)).ToArray();
        var intent = new PassengerPayloadIntent("passenger-load-" + Guid.NewGuid().ToString("N"), PassengerPayloadAction.Board, PassengerPayloadStage.Pending,
            airframeId, configuration, owned.ToArray(), protectedStations, snapshot.Weights.CurrentGrossWeightKilograms!.Value, snapshot.ObservedUtc, snapshot.Position);
        intent.EnsureValid(); return intent;
    }
    public static PassengerPayloadIntent Disembark(PassengerTransport transport, AircraftLoadingSnapshot snapshot)
    {
        transport.EnsureValid(); snapshot.EnsureValid();
        if (transport.State != PassengerCarriageState.Arrived || transport.Payload is not {} load) throw new InvalidOperationException("Passengers require a mission-authorized arrival before unloading.");
        CheckAircraft(snapshot, load.Configuration);
        if (!MatchesOwned(load, snapshot, true)) throw new InvalidOperationException("Owned passenger stations changed in MSFS. No weight was removed; reconcile the discrepancy explicitly.");
        var intent = load with { Id = "passenger-unload-" + Guid.NewGuid().ToString("N"), Action = PassengerPayloadAction.Disembark, Stage = PassengerPayloadStage.Pending,
            BeforeGrossKilograms = snapshot.Weights.CurrentGrossWeightKilograms ?? throw new InvalidOperationException("Live gross weight is unavailable."),
            ProtectedStations = snapshot.PayloadStations.Where(x => load.OwnedStations.All(o => o.Index != x.Index)).Select(x => new PassengerProtectedStation(x.Index, x.Name, x.CurrentWeightKilograms)).ToArray(),
            CreatedUtc = snapshot.ObservedUtc, AuthorizedPosition = snapshot.Position, VerifiedUtc = null, VerifiedPosition = null, Problem = null };
        intent.EnsureValid(); return intent;
    }
    public static void CheckAircraft(AircraftLoadingSnapshot snapshot, PassengerAircraftConfiguration configuration)
    {
        if (snapshot.Aircraft != configuration.Aircraft || snapshot.PayloadStations.Count != configuration.StationCount ||
            !snapshot.PayloadStations.Select(x => x.Index).Order().SequenceEqual(Enumerable.Range(1, configuration.StationCount)))
            throw new InvalidOperationException("Live aircraft identity/station layout does not match the verified passenger configuration.");
        if (!snapshot.Position.IsOnGround || snapshot.Position.GroundSpeedKnots > 5) throw new InvalidOperationException("Physical passenger loading requires a stopped aircraft on the ground.");
    }
    public static void CheckLimits(AircraftLoadingSnapshot snapshot, PassengerAircraftConfiguration configuration, decimal additionalKilograms)
    {
        if (snapshot.Weights.CurrentGrossWeightKilograms is not {} gross || snapshot.Weights.MaximumGrossWeightKilograms is not {} liveMaximum)
            throw new InvalidOperationException("Live gross weight/limit is unavailable; passenger loading is blocked.");
        if (gross + additionalKilograms > Math.Min(liveMaximum, configuration.MaximumGrossKilograms) + .001m ||
            Minimum(snapshot.Weights.MaximumPayloadKilograms, configuration.MaximumPayloadKilograms) is {} payload && snapshot.PayloadStations.Sum(x => x.CurrentWeightKilograms) + additionalKilograms > payload + .001m)
            throw new InvalidOperationException("Passenger loading exceeds an aircraft gross/payload limit.");
    }
    public static bool MatchesOwned(PassengerPayloadIntent intent, AircraftLoadingSnapshot snapshot, bool loaded) => intent.OwnedStations.All(x =>
        snapshot.PayloadStations.SingleOrDefault(s => s.Index == x.Index) is {} station && station.Name == x.Name &&
        Math.Abs(station.CurrentWeightKilograms - (loaded ? x.LoadedKilograms : x.BaselineKilograms)) <= ToleranceKilograms);
    private static decimal? Minimum(decimal? a, decimal? b) => a is {} x && b is {} y ? Math.Min(x, y) : a ?? b;
}

/// <summary>Optimistic identity/layout/payload comparison in the same native session immediately before writing.</summary>
public interface IGuardedPassengerPayloadSimulator : IAircraftLoadingSimulator
{
    Task<AircraftLoadingSnapshot> WritePassengerPayloadAsync(string sdkRoot, AircraftLoadingSnapshot expected,
        IReadOnlyDictionary<int, decimal> targetKilograms, CancellationToken token = default);
}
