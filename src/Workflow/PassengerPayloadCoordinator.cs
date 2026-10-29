using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public interface IPassengerAircraftConfigurations
{
    PassengerAircraftConfiguration Resolve(string aircraftType, AircraftLoadingSnapshot snapshot);
    string SupportSummary { get; }
}

/// <summary>Shared physical carriage; knows nothing about careers, destinations, flight proof or money.</summary>
public sealed class PassengerPayloadCoordinator(IEconomyStateStore store, IAircraftLoadingSimulator simulator,
    IPassengerAircraftConfigurations configurations, Func<string> sdkRoot)
{
    public string SupportSummary => configurations.SupportSummary;
    public async Task<PassengerTransport> ApplyAsync(string transportId, string aircraftType, Guid airframeId,
        bool unload, Action<AircraftLoadingSnapshot> authorize, CancellationToken token = default,
        Func<CancellationToken, Task>? beforeMutation = null, Func<EconomyWorld, string?>? boardingPolicy = null)
    {
        using var lease = await AircraftPayloadOperationLease.AcquireAsync(token).ConfigureAwait(false);
        if (beforeMutation is not null) await beforeMutation(token).ConfigureAwait(false);
        {
            var transport = (await store.LoadAsync(token).ConfigureAwait(false))!.PassengerTransports.Single(x => x.Id == transportId);
            var before = (await simulator.ReadAsync(sdkRoot(), token).ConfigureAwait(false)).EnsureValid();
            authorize(before);
            var configuration = configurations.Resolve(aircraftType, before); configuration.EnsureValid();
            PassengerLoadPlanner.CheckAircraft(before, configuration);
            var terminal = unload ? PassengerCarriageState.Disembarked : PassengerCarriageState.Aboard;
            if (transport.State == terminal)
            {
                CheckIntent(transport.Payload!, before, airframeId, configuration, true);
                return transport; // durable receipt plus fresh readback; never apply an old operation twice
            }
            PassengerPayloadIntent intent;
            if (!unload && transport.State == PassengerCarriageState.Reserved)
                intent = PassengerLoadPlanner.Board(transport, configuration, before, airframeId);
            else if (unload && transport.State == PassengerCarriageState.Arrived)
                intent = PassengerLoadPlanner.Disembark(transport, before);
            else
            {
                intent = transport.Payload ?? throw new InvalidOperationException("No recoverable passenger payload intent.");
                if (intent.Action != (unload ? PassengerPayloadAction.Disembark : PassengerPayloadAction.Board))
                    throw new InvalidOperationException("Resolve the pending passenger operation before changing direction.");
            }
            CheckIntent(intent, before, airframeId, configuration, false);
            var pending = transport with { State = unload ? PassengerCarriageState.Disembarking : PassengerCarriageState.Boarding, Payload = intent with { Problem = null } };
            await SaveAsync(transport, pending, token, unload ? null : boardingPolicy).ConfigureAwait(false);
            try
            {
                var targets = intent.Targets.Where(x => Math.Abs(before.PayloadStations.Single(s => s.Index == x.Key).CurrentWeightKilograms - x.Value) > PassengerLoadPlanner.ToleranceKilograms)
                    .ToDictionary(x => x.Key, x => x.Value);
                var delta = targets.Sum(x => x.Value - before.PayloadStations.Single(s => s.Index == x.Key).CurrentWeightKilograms);
                PassengerLoadPlanner.CheckLimits(before, configuration, delta);
                if (targets.Count > 0)
                {
                    if (simulator is not IGuardedPassengerPayloadSimulator guarded)
                        throw new InvalidOperationException("Passenger writes require an adapter with an immediate identity/payload comparison guard.");
                    var immediate = await guarded.WritePassengerPayloadAsync(sdkRoot(), before, targets, token).ConfigureAwait(false);
                    Verify(intent, before, immediate, airframeId, configuration, delta);
                }
                // A separate read catches adapters/aircraft reverting an apparently successful write.
                var retained = (await simulator.ReadAsync(sdkRoot(), token).ConfigureAwait(false)).EnsureValid();
                authorize(retained); Verify(intent, before, retained, airframeId, configuration, delta);
                var verified = pending with { State = terminal, Payload = intent with { Stage = PassengerPayloadStage.Verified,
                    VerifiedUtc = retained.ObservedUtc, VerifiedPosition = retained.Position, Problem = null } };
                await SaveAsync(pending, verified, token).ConfigureAwait(false); return verified;
            }
            catch (Exception ex)
            {
                // Never guess a rollback. A thrown setter can already have applied some stations.
                await SaveAsync(pending, pending with { Payload = intent with { Problem = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message } }, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
    }

    public async Task<AircraftLoadingSnapshot> CheckAboardAsync(PassengerTransport transport, string type, Guid airframeId, CancellationToken token = default)
    {
        if (transport.State is not (PassengerCarriageState.Aboard or PassengerCarriageState.Arrived)) throw new InvalidOperationException("Resolve pending loading before continuing the flight.");
        var live = (await simulator.ReadAsync(sdkRoot(), token).ConfigureAwait(false)).EnsureValid();
        var configuration = configurations.Resolve(type, live);
        // Airborne read-only reconciliation is allowed; loading itself requires stopped ground state.
        if (live.Aircraft != configuration.Aircraft || live.PayloadStations.Count != configuration.StationCount)
            throw new InvalidOperationException("Passenger aircraft/layout changed.");
        CheckIntent(transport.Payload!, live, airframeId, configuration, true, protectOthers: false);
        return live;
    }

    private async Task SaveAsync(PassengerTransport expected, PassengerTransport updated, CancellationToken token, Func<EconomyWorld, string?>? policy = null)
    {
        updated.EnsureValid();
        await store.ApplyAsync(w =>
        {
            if (policy?.Invoke(w) is {} error) throw new InvalidOperationException(error);
            var current = w.PassengerTransports.Single(x => x.Id == expected.Id);
            if (current.State != expected.State || current.Payload?.Id != expected.Payload?.Id || current.Payload?.Stage != expected.Payload?.Stage ||
                !current.Group.People.SequenceEqual(expected.Group.People)) throw new IOException("Passenger transport changed in another session. Refresh.");
            return new((w with { Revision = w.Revision + 1, PassengerTransports = w.PassengerTransports.Select(x => x.Id == updated.Id ? updated : x).ToArray() }).EnsureValid(), true);
        }, token).ConfigureAwait(false);
    }
    private static void CheckIntent(PassengerPayloadIntent intent, AircraftLoadingSnapshot live, Guid airframeId,
        PassengerAircraftConfiguration configuration, bool requireTargets, bool protectOthers = true)
    {
        if (airframeId != intent.AirframeId || configuration.Id != intent.Configuration.Id || configuration.ConfigurationHash != intent.Configuration.ConfigurationHash || live.Aircraft != intent.Configuration.Aircraft)
            throw new InvalidOperationException("Selected airframe or verified passenger configuration changed. Payload is preserved.");
        if (configuration.AircraftTypeId != intent.Configuration.AircraftTypeId || configuration.StationCount != intent.Configuration.StationCount ||
            configuration.MaximumGrossKilograms != intent.Configuration.MaximumGrossKilograms || configuration.MaximumPayloadKilograms != intent.Configuration.MaximumPayloadKilograms ||
            !configuration.Stations.SequenceEqual(intent.Configuration.Stations))
            throw new InvalidOperationException("Saved passenger seat/limit configuration differs from the current evidence provider. Payload is preserved.");
        foreach (var owned in intent.OwnedStations)
        {
            var station = live.PayloadStations.SingleOrDefault(x => x.Index == owned.Index);
            var target = intent.Targets[owned.Index];
            if (station is null || station.Name != owned.Name || !station.IsWritable ||
                Math.Abs(station.CurrentWeightKilograms - target) > PassengerLoadPlanner.ToleranceKilograms &&
                (requireTargets || Math.Abs(station.CurrentWeightKilograms - (intent.Action == PassengerPayloadAction.Board ? owned.BaselineKilograms : owned.LoadedKilograms)) > PassengerLoadPlanner.ToleranceKilograms))
                throw new InvalidOperationException("Owned passenger payload conflicts with the saved intent. No manual change was overwritten; inspect MSFS and retry only after resolving it.");
            if (station.AuthoritativeMaximumKilograms is {} maximum && target > maximum ||
                configuration.Stations.Single(x => x.Index == owned.Index).MaximumKilograms is {} configuredMaximum && target > configuredMaximum)
                throw new InvalidOperationException("Saved passenger target exceeds a reported/configured station limit.");
        }
        if (protectOthers && intent.ProtectedStations.Any(p => live.PayloadStations.SingleOrDefault(s => s.Index == p.Index) is not {} s || s.Name != p.Name || Math.Abs(s.CurrentWeightKilograms - p.BeforeKilograms) > PassengerLoadPlanner.ToleranceKilograms))
            throw new InvalidOperationException("Unrelated payload changed during the pending operation. It was preserved; resolve the saved intent explicitly.");
    }
    private static void Verify(PassengerPayloadIntent intent, AircraftLoadingSnapshot before, AircraftLoadingSnapshot after,
        Guid airframeId, PassengerAircraftConfiguration configuration, decimal delta)
    {
        after.EnsureValid(); PassengerLoadPlanner.CheckAircraft(after, configuration);
        CheckIntent(intent, after, airframeId, configuration, true);
        if (after.ObservedUtc < before.ObservedUtc || WorldTracking.DistanceMeters(before.Position.Latitude, before.Position.Longitude, after.Position.Latitude, after.Position.Longitude) > 25)
            throw new InvalidOperationException("Aircraft moved or readback time regressed during loading.");
        if (after.Weights.CurrentGrossWeightKilograms is not {} gross || Math.Abs(gross - before.Weights.CurrentGrossWeightKilograms!.Value - delta) > 1m)
            throw new InvalidOperationException("Gross-weight readback does not confirm passenger loading. Intent retained.");
        PassengerLoadPlanner.CheckLimits(after, configuration, 0);
        if (after.Weights is { CurrentCgPercent: {} cg } &&
            (after.Weights.ForwardCgLimitPercent is {} forward && cg < forward || after.Weights.AftCgLimitPercent is {} aft && cg > aft))
            throw new InvalidOperationException("Readback CG is outside reported limits. Loading remains unresolved.");
        if (!before.FuelTanks.Select(x => (x.Id, x.QuantityGallons)).SequenceEqual(after.FuelTanks.Select(x => (x.Id, x.QuantityGallons))))
            throw new InvalidOperationException("Fuel changed during passenger loading. No fuel write was requested; intent retained for review.");
    }
}
