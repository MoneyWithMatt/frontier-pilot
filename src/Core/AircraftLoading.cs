namespace FrontierAirportBuilder.Core;

public static class LoadingUnits
{
    public const decimal PoundsPerKilogram = 2.2046226218487757m;
    public const decimal LitresPerUsGallon = 3.785411784m;
    public static decimal KilogramsToPounds(decimal value) => value * PoundsPerKilogram;
    public static decimal PoundsToKilograms(decimal value) => value / PoundsPerKilogram;
    public static decimal GallonsToLitres(decimal value) => value * LitresPerUsGallon;
    public static decimal LitresToGallons(decimal value) => value / LitresPerUsGallon;
}

public enum AircraftLoadingMode { MsfsPayloadIntegration, SimulatedFrontierLoading }
public enum LoadManifestState { PendingApplication, SimulatorApplied, CommittedInTransit, PendingUnload, SimulatorUnloaded, Completed, PendingAbandonment, Abandoned }
public enum SimulatorReconciliationState { NotChecked, MatchesManifest, CargoAppearsMissing, Indeterminate, RestoreAvailable, RestoreBlocked }
public enum FuelReconciliationState { ContinuousLive, FrozenAfterDisconnect, AwaitingDecision, RestoredVerified, KeptSimulatorFuel, Blocked }

public sealed record AircraftStationPosition(double LongitudinalFeet, double LateralFeet, double VerticalFeet);
public sealed record PayloadStationReading(int Index, string Name, decimal CurrentWeightKilograms,
    AircraftStationPosition? Position = null, decimal? AuthoritativeMaximumKilograms = null, bool IsWritable = true)
{
    public PayloadStationReading EnsureValid()
    {
        if (Index is < 1 or > 15 || string.IsNullOrWhiteSpace(Name) || Name.Length > 128 || CurrentWeightKilograms < 0 ||
            AuthoritativeMaximumKilograms is { } maximum && (maximum <= 0 || CurrentWeightKilograms > maximum + .01m))
            throw new InvalidDataException("Payload-station data is invalid.");
        if (Position is { } p && new[] { p.LongitudinalFeet, p.LateralFeet, p.VerticalFeet }.Any(x => !double.IsFinite(x)))
            throw new InvalidDataException("Payload-station position is invalid.");
        return this;
    }
}

public sealed record FuelTankReading(string Id, string Name, decimal QuantityGallons, decimal CapacityGallons, bool IsWritable = true)
{
    public FuelTankReading EnsureValid()
    {
        EconomyValidation.Id(Id, "fuel tank ID"); EconomyValidation.Text(Name, "fuel tank name", 128);
        if (QuantityGallons < 0 || CapacityGallons <= 0 || QuantityGallons > CapacityGallons + .001m)
            throw new InvalidDataException("Fuel-tank data is invalid.");
        return this;
    }
}

public sealed record AircraftEngineReading(int Index, bool? IsRunning)
{
    public AircraftEngineReading EnsureValid()
    { if (Index is < 1 or > 4) throw new InvalidDataException("Engine index is invalid."); return this; }
}

public sealed record AircraftWeightReading(decimal? EmptyWeightKilograms, decimal? MaximumGrossWeightKilograms,
    decimal? CurrentGrossWeightKilograms, decimal? MaximumPayloadKilograms, double? CurrentCgPercent,
    double? ForwardCgLimitPercent, double? AftCgLimitPercent)
{
    public AircraftWeightReading EnsureValid()
    {
        foreach (var value in new[] { EmptyWeightKilograms, MaximumGrossWeightKilograms, CurrentGrossWeightKilograms, MaximumPayloadKilograms })
            if (value is { } mass && mass <= 0) throw new InvalidDataException("Aircraft weight data is invalid.");
        foreach (var value in new[] { CurrentCgPercent, ForwardCgLimitPercent, AftCgLimitPercent })
            if (value is { } number && !double.IsFinite(number)) throw new InvalidDataException("Aircraft CG data is invalid.");
        return this;
    }
}

public sealed record AircraftLoadingSnapshot(DateTimeOffset ObservedUtc, FlightAircraftIdentity Aircraft,
    FlightPosition Position, IReadOnlyList<PayloadStationReading> PayloadStations,
    IReadOnlyList<FuelTankReading> FuelTanks, IReadOnlyList<AircraftEngineReading> Engines,
    decimal? FuelWeightKilogramsPerGallon, AircraftWeightReading Weights, string IntegrationDetails)
{
    public AircraftLoadingSnapshot EnsureValid()
    {
        if (ObservedUtc == default || Position.ObservedUtc != ObservedUtc) throw new InvalidDataException("Aircraft-loading observation time is invalid.");
        Aircraft.EnsureValid(); Position.EnsureValid(); Weights.EnsureValid();
        if (PayloadStations is null || FuelTanks is null || Engines is null) throw new InvalidDataException("Aircraft-loading collections are missing.");
        foreach (var item in PayloadStations) item.EnsureValid(); foreach (var item in FuelTanks) item.EnsureValid(); foreach (var item in Engines) item.EnsureValid();
        EconomyValidation.Unique(PayloadStations.Select(x => x.Index.ToString()), "payload station");
        EconomyValidation.Unique(FuelTanks.Select(x => x.Id), "fuel tank");
        if (FuelWeightKilogramsPerGallon is { } density && density <= 0) throw new InvalidDataException("Fuel density is invalid.");
        EconomyValidation.Text(IntegrationDetails, "loading integration details", 512); return this;
    }
}

public sealed record CargoStationAllocation(int StationIndex, decimal CargoKilograms)
{
    public CargoStationAllocation EnsureValid()
    { if (StationIndex is < 1 or > 15 || CargoKilograms < 0) throw new InvalidDataException("Cargo allocation is invalid."); return this; }
}

public sealed record PlannedPayloadStation(int StationIndex, decimal BaselineKilograms, decimal FrontierCargoKilograms,
    decimal TargetKilograms, decimal? ReadBackKilograms = null, decimal? UnloadTargetKilograms = null,
    decimal? UnloadReadBackKilograms = null);

public sealed record AircraftLoadPlan(decimal CargoKilograms, IReadOnlyList<PlannedPayloadStation> Stations,
    decimal PlannedPayloadKilograms, decimal? PlannedGrossWeightKilograms, double? PlannedCgPercent,
    IReadOnlyList<string> Warnings)
{
    public static AircraftLoadPlan Create(AircraftLoadingSnapshot supplied, decimal cargoKilograms,
        IReadOnlyList<CargoStationAllocation> suppliedAllocations, double? plannedCgPercent = null)
    {
        var snapshot = supplied.EnsureValid(); if (cargoKilograms <= 0) throw new InvalidOperationException("Shipment cargo mass must be positive.");
        if (suppliedAllocations is null) throw new InvalidDataException("Cargo allocation is missing.");
        var allocations = suppliedAllocations.Select(x => x.EnsureValid()).ToArray();
        if (allocations.Select(x => x.StationIndex).Distinct().Count() != allocations.Length) throw new InvalidDataException("Cargo allocation repeats a station.");
        if (Math.Abs(allocations.Sum(x => x.CargoKilograms) - cargoKilograms) > .001m)
            throw new InvalidOperationException("Assigned Frontier cargo must equal the shipment mass exactly.");
        var planned = new List<PlannedPayloadStation>();
        foreach (var station in snapshot.PayloadStations)
        {
            var assigned = allocations.SingleOrDefault(x => x.StationIndex == station.Index)?.CargoKilograms ?? 0;
            if (assigned > 0 && !station.IsWritable) throw new InvalidOperationException($"Payload station {station.Index} is not writable through the simulator adapter.");
            var target = station.CurrentWeightKilograms + assigned;
            if (station.AuthoritativeMaximumKilograms is { } maximum && target > maximum + .001m)
                throw new InvalidOperationException($"Payload station {station.Index} exceeds its authoritative structural limit.");
            planned.Add(new(station.Index, station.CurrentWeightKilograms, assigned, target));
        }
        if (allocations.Any(x => snapshot.PayloadStations.All(s => s.Index != x.StationIndex))) throw new InvalidOperationException("Cargo was assigned to an unavailable station.");
        var existingPayload = snapshot.PayloadStations.Sum(x => x.CurrentWeightKilograms); var totalPayload = existingPayload + cargoKilograms;
        if (snapshot.Weights.MaximumPayloadKilograms is { } maximumPayload && totalPayload > maximumPayload + .001m)
            throw new InvalidOperationException("Planned payload exceeds the aircraft's authoritative maximum-payload value.");
        decimal? gross = snapshot.Weights.CurrentGrossWeightKilograms + cargoKilograms;
        var warnings = EvaluateWarnings(snapshot.Weights, gross, plannedCgPercent);
        if (snapshot.PayloadStations.Any(x => x.AuthoritativeMaximumKilograms is null)) warnings.Add("One or more structural station limits are unavailable from the aircraft.");
        if (snapshot.Weights.MaximumPayloadKilograms is null) warnings.Add("Authoritative overall maximum payload is unavailable from the aircraft.");
        if (plannedCgPercent is null) warnings.Add("Planned CG cannot be calculated because authoritative station arms and empty-aircraft moment are unavailable through SimConnect.");
        return new(cargoKilograms, planned, totalPayload, gross, plannedCgPercent, warnings);
    }

    public static AircraftLoadPlan CreateDesiredState(AircraftLoadingSnapshot supplied,
        IReadOnlyList<CargoStationAllocation> suppliedAllocations, CargoLoadManifest? currentManifest,
        decimal maximumAvailableCargoKilograms, double? plannedCgPercent = null, decimal toleranceKilograms = .25m)
    {
        var snapshot = supplied.EnsureValid();
        if (suppliedAllocations is null || maximumAvailableCargoKilograms < 0 || toleranceKilograms <= 0)
            throw new InvalidDataException("Desired cargo plan is invalid.");
        var allocations = suppliedAllocations.Select(x => x.EnsureValid()).ToArray();
        if (allocations.Select(x => x.StationIndex).Distinct().Count() != allocations.Length)
            throw new InvalidDataException("Cargo allocation repeats a station.");
        if (allocations.Any(x => snapshot.PayloadStations.All(s => s.Index != x.StationIndex)))
            throw new InvalidOperationException("Cargo was assigned to an unavailable station.");
        var desiredTotal = allocations.Sum(x => x.CargoKilograms);
        if (desiredTotal > maximumAvailableCargoKilograms + .001m)
            throw new InvalidOperationException($"Requested Frontier cargo is {desiredTotal:0.###} kg, but only {maximumAvailableCargoKilograms:0.###} kg exists for this aircraft and location.");
        var oldStations = currentManifest?.Stations.ToDictionary(x => x.StationIndex) ?? [];
        var planned = new List<PlannedPayloadStation>();
        decimal currentManagedTotal = 0;
        foreach (var station in snapshot.PayloadStations)
        {
            var desired = allocations.SingleOrDefault(x => x.StationIndex == station.Index)?.CargoKilograms ?? 0;
            var priorManaged = oldStations.TryGetValue(station.Index, out var old) ? old.FrontierCargoKilograms : 0;
            var physicalManaged = currentManifest?.Reconciliation == SimulatorReconciliationState.CargoAppearsMissing ? 0 : priorManaged;
            var baseline = station.CurrentWeightKilograms - physicalManaged;
            if (baseline < -toleranceKilograms)
                throw new InvalidOperationException($"Station {station.Index} ({station.Name}) mismatch: live {station.CurrentWeightKilograms:0.###} kg, persisted Frontier cargo {priorManaged:0.###} kg, inferred non-Frontier baseline {baseline:0.###} kg. Reconcile the manifest before applying changes.");
            baseline = Math.Max(0, baseline); currentManagedTotal += physicalManaged;
            if (desired != priorManaged && !station.IsWritable)
                throw new InvalidOperationException($"Payload station {station.Index} is not writable through the simulator adapter.");
            var target = baseline + desired;
            if (station.AuthoritativeMaximumKilograms is { } maximum && target > maximum + .001m)
                throw new InvalidOperationException($"Payload station {station.Index} exceeds its authoritative structural limit.");
            planned.Add(new(station.Index, baseline, desired, target));
        }
        var totalPayload = planned.Sum(x => x.TargetKilograms);
        if (snapshot.Weights.MaximumPayloadKilograms is { } maximumPayload && totalPayload > maximumPayload + .001m)
            throw new InvalidOperationException("Planned payload exceeds the aircraft's authoritative maximum-payload value.");
        var cargoDelta = desiredTotal - currentManagedTotal;
        decimal? gross = snapshot.Weights.CurrentGrossWeightKilograms + cargoDelta;
        if (gross is { } plannedGross && snapshot.Weights.MaximumGrossWeightKilograms is { } maximumGross && plannedGross > maximumGross + .001m)
            throw new InvalidOperationException("Planned gross weight exceeds the aircraft's authoritative maximum-gross-weight value.");
        var warnings = EvaluateWarnings(snapshot.Weights, gross, plannedCgPercent);
        if (snapshot.PayloadStations.Any(x => x.AuthoritativeMaximumKilograms is null)) warnings.Add("One or more structural station limits are unavailable from the aircraft.");
        if (snapshot.Weights.MaximumPayloadKilograms is null) warnings.Add("Authoritative overall maximum payload is unavailable from the aircraft.");
        if (plannedCgPercent is null) warnings.Add("Planned CG cannot be calculated because authoritative station arms and empty-aircraft moment are unavailable through SimConnect.");
        return new(desiredTotal, planned, totalPayload, gross, plannedCgPercent, warnings);
    }

    public static List<string> EvaluateWarnings(AircraftWeightReading weights, decimal? gross, double? cg)
    {
        var warnings = new List<string>();
        if (gross is { } planned && weights.MaximumGrossWeightKilograms is { } max && planned > max)
            warnings.Add("Planned weight is above the reported maximum gross weight.");
        if (cg is { } value && weights.ForwardCgLimitPercent is { } forward && value < forward)
            warnings.Add("Planned CG is forward of the reported limit.");
        if (cg is { } value2 && weights.AftCgLimitPercent is { } aft && value2 > aft)
            warnings.Add("Planned CG is aft of the reported limit.");
        return warnings;
    }
}

public sealed record CargoLoadManifest(string Id, string JobId, string ShipmentId, string FlightAttemptId,
    FlightAircraftIdentity Aircraft, AircraftLoadingMode Mode, IReadOnlyList<PlannedPayloadStation> Stations,
    decimal CargoKilograms, LoadManifestState State, DateTimeOffset CreatedUtc,
    DateTimeOffset? SimulatorAppliedUtc = null, DateTimeOffset? CommittedUtc = null,
    DateTimeOffset? UnloadStartedUtc = null, DateTimeOffset? SimulatorUnloadedUtc = null,
    DateTimeOffset? CompletedUtc = null, decimal VerificationToleranceKilograms = .25m,
    string VerificationResult = "Pending", FlightPosition? LastValidPosition = null,
    DateTimeOffset? LastReconciledUtc = null, SimulatorReconciliationState Reconciliation = SimulatorReconciliationState.NotChecked)
{
    public decimal ShipmentQuantityKilograms { get; init; }
    public decimal DeliveredCargoKilograms { get; init; }
    public decimal PriorCargoKilograms { get; init; }
    public string? LastTransferCommandId { get; init; }
    public bool IsDesiredStateManifest { get; init; }
    public CargoLoadManifest EnsureValid()
    {
        EconomyValidation.Id(Id, nameof(Id)); EconomyValidation.Id(JobId, nameof(JobId)); EconomyValidation.Id(ShipmentId, nameof(ShipmentId)); EconomyValidation.Id(FlightAttemptId, nameof(FlightAttemptId));
        Aircraft.EnsureValid(); if (Stations is null || CargoKilograms < 0 || PriorCargoKilograms < 0 || CreatedUtc == default || VerificationToleranceKilograms <= 0)
            throw new InvalidDataException("Cargo manifest is invalid.");
        EconomyValidation.Unique(Stations.Select(x => x.StationIndex.ToString()), "manifest station");
        if (Mode == AircraftLoadingMode.MsfsPayloadIntegration && Math.Abs(Stations.Sum(x => x.FrontierCargoKilograms) - CargoKilograms) > .001m) throw new InvalidDataException("Manifest cargo allocation does not equal shipment mass.");
        if (IsDesiredStateManifest && (ShipmentQuantityKilograms <= 0 || CargoKilograms + DeliveredCargoKilograms > ShipmentQuantityKilograms + .001m || DeliveredCargoKilograms < 0))
            throw new InvalidDataException("Desired-state manifest quantities are invalid.");
        foreach (var station in Stations) if (station.StationIndex is < 1 or > 15 || station.BaselineKilograms < 0 || station.FrontierCargoKilograms < 0 || station.TargetKilograms != station.BaselineKilograms + station.FrontierCargoKilograms || station.ReadBackKilograms < 0 || station.UnloadTargetKilograms < 0 || station.UnloadReadBackKilograms < 0)
            throw new InvalidDataException("Manifest station data is invalid.");
        if (Mode == AircraftLoadingMode.SimulatedFrontierLoading && State is LoadManifestState.SimulatorApplied or LoadManifestState.SimulatorUnloaded)
            throw new InvalidDataException("Simulated loading cannot claim a simulator write state.");
        EconomyValidation.Text(VerificationResult, "manifest verification result", 512); LastValidPosition?.EnsureValid(); return this;
    }
}

public sealed record PersistedFuelTank(string Id, string Name, decimal QuantityGallons, decimal CapacityGallons);
public sealed record PersistentAircraftFuelState(string Id, FlightAircraftIdentity Aircraft, IReadOnlyList<PersistedFuelTank> Tanks,
    FlightPosition LastValidPosition, DateTimeOffset ObservedUtc, DateTimeOffset PersistedUtc,
    FuelReconciliationState Reconciliation, DateTimeOffset? LastReconciledUtc = null, string Result = "Live snapshot")
{
    public PersistentAircraftFuelState EnsureValid()
    {
        EconomyValidation.Id(Id, nameof(Id)); Aircraft.EnsureValid(); LastValidPosition.EnsureValid();
        if (Tanks is null || ObservedUtc == default || PersistedUtc < ObservedUtc) throw new InvalidDataException("Persistent fuel state is invalid.");
        EconomyValidation.Unique(Tanks.Select(x => x.Id), "persisted fuel tank");
        foreach (var tank in Tanks) new FuelTankReading(tank.Id, tank.Name, tank.QuantityGallons, tank.CapacityGallons).EnsureValid();
        EconomyValidation.Text(Result, "fuel persistence result", 512); return this;
    }
}

public interface IAircraftLoadingSimulator
{
    Task<AircraftLoadingSnapshot> ReadAsync(string sdkRoot, CancellationToken cancellationToken = default);
    Task<AircraftLoadingSnapshot> WritePayloadAsync(string sdkRoot, IReadOnlyDictionary<int, decimal> targetKilograms, CancellationToken cancellationToken = default);
    Task<AircraftLoadingSnapshot> WriteFuelAsync(string sdkRoot, IReadOnlyDictionary<string, decimal> targetGallons, CancellationToken cancellationToken = default);
}
