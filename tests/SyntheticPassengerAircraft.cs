using System.IO;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.TestFixtures;

/// <summary>Explicitly synthetic. No station/seat values here qualify a production aircraft.</summary>
public sealed class SyntheticPassengerAircraft : IGuardedPassengerPayloadSimulator, IPassengerAircraftConfigurations
{
    public AircraftTelemetry Sample { get; set; } = new(DateTimeOffset.Parse("2026-10-29T12:00:00Z"), "Synthetic Vision Jet", "SF50", -23, 15, 500, 0, 0, true);
    public decimal[] Weights { get; } = [80, 30, 0, 0, 0, 33];
    public int Writes { get; private set; }
    public int FailAfterStation { get; set; } = -1;
    public bool RevertReadback { get; set; }
    public bool AlterProtectedStation { get; set; }
    public bool ChangeBeforeWrite { get; set; }
    public int FuelWrites { get; private set; }
    public decimal StationMaximum { get; set; } = 500;
    public decimal MaximumGross { get; set; } = 5000;
    public decimal MaximumPayload { get; set; } = 1000;
    public double CurrentCg { get; set; } = 25;
    public string StationPrefix { get; set; } = "Synthetic station ";
    public bool SuppressGrossChange { get; set; }
    public int RetentionChecks { get; private set; }
    public Task WaitForRetentionAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); RetentionChecks++; return Task.CompletedTask; }
    public string SupportSummary => "SYNTHETIC passenger fixture; physical loading/visuals are not live accepted.";
    public PassengerAircraftConfiguration Resolve(string type, AircraftLoadingSnapshot snapshot) => new("synthetic-cabin", type, snapshot.Aircraft,
        6, [new(3, 1, 150), new(4, 1, 150), new(5, 1, 150)], 5000, 1000, new string('A', 64), "Synthetic individual seats, not production aircraft evidence", "Unsupported");
    public Task<AircraftLoadingSnapshot> ReadAsync(string sdkRoot, CancellationToken token = default) => Task.FromResult(Snapshot());
    public Task<AircraftLoadingSnapshot> WritePassengerPayloadAsync(string sdkRoot, AircraftLoadingSnapshot expected, IReadOnlyDictionary<int, decimal> targets, CancellationToken token = default)
    {
        if (ChangeBeforeWrite) Weights[2] = 25;
        PassengerLoadPlanner.CheckUnchangedBeforeWrite(expected, Snapshot());
        return WritePayloadAsync(sdkRoot, targets, token);
    }
    private AircraftLoadingSnapshot Snapshot() => new(Sample.ObservedUtc, FlightAircraftIdentity.From(Sample), FlightPosition.From(Sample),
        Weights.Select((w, i) => new PayloadStationReading(i + 1, StationPrefix + (i + 1), w, AuthoritativeMaximumKilograms: StationMaximum)).ToArray(),
        [new("main", "Synthetic fuel", 10, 100)], [new(1, false)], 3, new(1000, MaximumGross, 1030 + (SuppressGrossChange ? 143 : Weights.Sum()), MaximumPayload, CurrentCg, 10, 40), "Synthetic payload adapter");
    public Task<AircraftLoadingSnapshot> WritePayloadAsync(string sdkRoot, IReadOnlyDictionary<int, decimal> targets, CancellationToken token = default)
    {
        Writes++; var applied = 0;
        foreach (var t in targets)
        {
            Weights[t.Key - 1] = t.Value;
            if (++applied == FailAfterStation) { FailAfterStation = -1; throw new IOException("Injected partial payload write"); }
        }
        if (AlterProtectedStation) Weights[5] += 1;
        var snapshot = Snapshot();
        if (RevertReadback) foreach (var t in targets) Weights[t.Key - 1] = 0;
        return Task.FromResult(snapshot);
    }
    public Task<AircraftLoadingSnapshot> WriteFuelAsync(string sdkRoot, IReadOnlyDictionary<string, decimal> targets, CancellationToken token = default)
    { FuelWrites++; throw new InvalidOperationException("Passenger transport must never write fuel."); }
}
