using System.Text.Json;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>
/// One explicit on-pad 5 kg ballast cycle, not a job, passenger profile or normal cargo authority.
/// Uses the existing loading adapter. Interrupted/ambiguous writes remain unresolved, never guessed back.
/// </summary>
public sealed class ExperimentalPlatformPayloadCycle(IAircraftLoadingSimulator simulator,
    Func<CancellationToken, Task<ConditionFrame>> nextFrame,
    Func<DateTimeOffset>? clock = null, Func<CancellationToken, Task>? retain = null)
{
    public const decimal TestMassKilograms = 5m;
    public const decimal ReviewedBaselineKilograms = 99.7903214m;
    private const decimal Tolerance = .05m;
    private static readonly string[] Names =
        ["Pilot", "Passenger 1", "Passenger 2", "Passenger 3", "Passenger 4", "Cargo 1", "Cargo 2", "Cargo 3", "Co-Pilot"];

    public async Task RunAsync(string sdkRoot, string newJournalPath, CancellationToken token)
    {
        // CreateNew before simulator access. A repeated path cannot apply a second load.
        await using var stream = new FileStream(newJournalPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream);
        async Task Journal(string stage, object? detail = null)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { Stage = stage, Utc = Now(),
                DiagnosticOnly = true, MassKilograms = TestMassKilograms, Station = 6, Name = "Cargo 1", Detail = detail }));
            await writer.FlushAsync(); stream.Flush(true);
        }
        var detector = new ExperimentalPlatformPresence();
        ConditionAircraftIdentity? aircraft = null;
        async Task<PlatformPresenceStatus> RequireDeck()
        {
            for (var i = 0; i < 80; i++)
            {
                token.ThrowIfCancellationRequested();
                var frame = await nextFrame(token);
                aircraft ??= frame.Aircraft;
                if (frame.Aircraft != aircraft) throw new InvalidOperationException("Test aircraft identity changed; no payload operation authorised.");
                var status = detector.Observe(frame, Now());
                await Journal("DeckObservation", new { Frame = frame, Status = status });
                if (status.DiagnosticDeckPresence && frame.Runtime is { Running: true, Paused: false }) return status;
            }
            throw new InvalidOperationException("No fresh sustained OL400 deck presence; no payload operation authorised.");
        }
        void Authorize(AircraftLoadingSnapshot snapshot, PlatformPresenceStatus presence)
        {
            ValidateSnapshot(snapshot);
            if (aircraft is null || snapshot.Aircraft.Title != aircraft.Title || snapshot.Aircraft.Type != aircraft.Model ||
                presence.ObservedUtc is not { } observed || Now() - observed > TimeSpan.FromSeconds(2) ||
                Now() < observed || Now() - snapshot.ObservedUtc > TimeSpan.FromSeconds(2) || Now() < snapshot.ObservedUtc)
                throw new InvalidOperationException("Fresh matching aircraft/pad observations are required.");
        }
        try
        {
            await Journal("Started", "Explicit diagnostic only; no flight, inventory, wages or career-store access.");
            var presence = await RequireDeck();
            var before = (await simulator.ReadAsync(sdkRoot, token)).EnsureValid();
            Authorize(before, presence);
            var baseline = before.PayloadStations.Single(s => s.Index == 6).CurrentWeightKilograms;
            if (Math.Abs(baseline - ReviewedBaselineKilograms) > Tolerance)
                throw new InvalidOperationException("Cargo 1 differs from the reviewed 99.7903214 kg baseline; refuse adoption or a duplicate load.");
            var target = baseline + TestMassKilograms;
            if (before.PayloadStations.Single(s => s.Index == 6).AuthoritativeMaximumKilograms is { } max && target > max)
                throw new InvalidOperationException("Simulator-reported station limit would be exceeded.");
            var warnings = AircraftLoadPlan.EvaluateWarnings(before.Weights,
                before.Weights.CurrentGrossWeightKilograms + TestMassKilograms, null);
            await Journal("LoadIntent", new { BaselineKilograms = baseline, TargetKilograms = target, Before = before, Warnings = warnings });
            var fresh = (await simulator.ReadAsync(sdkRoot, token)).EnsureValid();
            Authorize(fresh, presence); AssertUnchanged(before, fresh);
            var immediate = await simulator.WritePayloadAsync(sdkRoot, new Dictionary<int, decimal> { [6] = target }, token);
            Verify(fresh, immediate, target, TestMassKilograms);
            await (retain?.Invoke(token) ?? Task.Delay(TimeSpan.FromSeconds(3), token));
            var loaded = await simulator.ReadAsync(sdkRoot, token);
            Verify(fresh, loaded, target, TestMassKilograms);
            await Journal("LoadVerified", loaded);

            // Re-establish current presence after retention; previous maximum dwell is never authority.
            detector.Reset("Requalify after payload change.");
            presence = await RequireDeck();
            var beforeUnload = (await simulator.ReadAsync(sdkRoot, token)).EnsureValid();
            Authorize(beforeUnload, presence);
            if (Math.Abs(beforeUnload.PayloadStations.Single(s => s.Index == 6).CurrentWeightKilograms - target) > Tolerance)
                throw new InvalidOperationException("Owned test mass is missing or changed; no guessed removal.");
            await Journal("UnloadIntent", new { BaselineKilograms = baseline, TargetKilograms = baseline, Before = beforeUnload });
            fresh = (await simulator.ReadAsync(sdkRoot, token)).EnsureValid();
            Authorize(fresh, presence); AssertUnchanged(beforeUnload, fresh);
            immediate = await simulator.WritePayloadAsync(sdkRoot, new Dictionary<int, decimal> { [6] = baseline }, token);
            Verify(fresh, immediate, baseline, -TestMassKilograms);
            await (retain?.Invoke(token) ?? Task.Delay(TimeSpan.FromSeconds(3), token));
            var unloaded = await simulator.ReadAsync(sdkRoot, token);
            Verify(fresh, unloaded, baseline, -TestMassKilograms);
            await Journal("Completed", unloaded);
        }
        catch (Exception error)
        {
            await Journal("Unresolved", new { Error = error.Message,
                Instruction = "Stop. Inspect journal and actual aircraft. No automatic retry, adoption, rollback or inferred completion." });
            throw;
        }
        finally { detector.Reset("Diagnostic ended."); }
    }

    private DateTimeOffset Now() => clock?.Invoke() ?? DateTimeOffset.UtcNow;

    public static void ValidateSnapshot(AircraftLoadingSnapshot snapshot)
    {
        snapshot.EnsureValid();
        if (snapshot.Aircraft != new FlightAircraftIdentity("H125 Passengers", "H125") ||
            snapshot.PayloadStations.Count != 9 || !snapshot.PayloadStations.OrderBy(x => x.Index).Select(x => x.Name).SequenceEqual(Names) ||
            !snapshot.PayloadStations.Select(x => x.Index).Order().SequenceEqual(Enumerable.Range(1, 9)) ||
            !snapshot.PayloadStations.Single(x => x.Index == 6).IsWritable)
            throw new InvalidOperationException("Actual aircraft/layout differs from inspected nine-station H125; test blocked.");
        var position = snapshot.Position;
        if (!position.IsOnGround || position.GroundSpeedKnots > .5 ||
            WorldTracking.DistanceMeters(position.Latitude, position.Longitude,
                ExperimentalPlatformContactProfile.Latitude, ExperimentalPlatformContactProfile.Longitude) > 8 ||
            position.AltitudeFeet * .3048 - ExperimentalPlatformContactProfile.DeckMeters is < .3 or > 1.5)
            throw new InvalidOperationException("Payload test requires the stationary H125 on OL400's actual deck.");
        if (snapshot.Weights.CurrentGrossWeightKilograms is null || snapshot.FuelWeightKilogramsPerGallon is null ||
            snapshot.FuelTanks.Count != 1 || snapshot.FuelTanks[0].Id != "center")
            throw new InvalidOperationException("Inspected H125 gross/fuel diagnostics unavailable; test blocked.");
    }

    public static void AssertUnchanged(AircraftLoadingSnapshot before, AircraftLoadingSnapshot fresh)
    {
        ValidateSnapshot(before); ValidateSnapshot(fresh);
        foreach (var station in before.PayloadStations)
            if (Math.Abs(fresh.PayloadStations.Single(x => x.Index == station.Index).CurrentWeightKilograms - station.CurrentWeightKilograms) > Tolerance)
                throw new InvalidOperationException($"Station {station.Index} changed before write; refuse manual-state overwrite.");
    }

    public static void Verify(AircraftLoadingSnapshot before, AircraftLoadingSnapshot after, decimal target, decimal delta)
    {
        ValidateSnapshot(before); ValidateSnapshot(after);
        if (after.ObservedUtc < before.ObservedUtc) throw new InvalidOperationException("Out-of-order payload readback.");
        foreach (var station in before.PayloadStations)
        {
            var expected = station.Index == 6 ? target : station.CurrentWeightKilograms;
            if (Math.Abs(after.PayloadStations.Single(x => x.Index == station.Index).CurrentWeightKilograms - expected) > Tolerance)
                throw new InvalidOperationException($"Station {station.Index} refused/clamped/changed; payload is unresolved.");
        }
        var fuelBefore = before.FuelTanks[0]; var fuelAfter = after.FuelTanks[0];
        var fuelGallons = fuelAfter.QuantityGallons - fuelBefore.QuantityGallons;
        if (fuelBefore.CapacityGallons != fuelAfter.CapacityGallons ||
            before.FuelWeightKilogramsPerGallon != after.FuelWeightKilogramsPerGallon ||
            fuelGallons > .01m || fuelGallons < -.25m)
            throw new InvalidOperationException("Unexpected fuel-state change; no fuel restoration attempted.");
        var fuelChange = fuelGallons * before.FuelWeightKilogramsPerGallon!.Value;
        var actual = after.Weights.CurrentGrossWeightKilograms!.Value - before.Weights.CurrentGrossWeightKilograms!.Value;
        if (Math.Abs(actual - delta - fuelChange) > .5m)
            throw new InvalidOperationException("Total weight did not match test mass plus observed fuel burn.");
    }
}
