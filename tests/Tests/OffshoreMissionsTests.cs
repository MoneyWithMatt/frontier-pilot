using System.Text.Json.Nodes;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.TestFixtures;

namespace FrontierAirportBuilder.Tests;

public sealed class OffshoreMissionsTests
{
    [Fact]
    public async Task PlayableObservationRoundTripPays800PerAirborneHourOnce()
    {
        var f = new SyntheticOffshoreFlight(); await f.Begin(); await f.Outbound();
        Assert.Equal(OffshoreMissionState.Returning, f.Service.Selected!.State);
        Assert.Equal(10, f.Service.Selected.ObservationSeconds);
        Assert.False(f.Service.CanComplete); await f.Return();
        Assert.Equal(OffshoreMissionState.ReadyToComplete, f.Service.Selected.State);
        Assert.Equal(TimeSpan.FromSeconds(139).Ticks, f.Service.Selected.AirborneTicks);
        await f.Service.CompleteAsync(); await f.Service.CompleteAsync();
        var world = (await f.Store.LoadAsync())!; var wage = Assert.Single(world.FinancialLedger.Where(t => t.Kind == FinancialTransactionKind.OffshoreMissionWage));
        Assert.Equal(800m, wage.Wage!.HourlyRate); Assert.Equal(30.89m, wage.Amount);
        Assert.Equal(wage.Amount, world.Players.Single().CachedBalance);
        Assert.Empty(world.PassengerTransports); Assert.DoesNotContain(world.Shipments, s => s.LoadedQuantity > 0);
        var revision = world.Revision; await f.Service.CompleteAsync(); Assert.Equal(revision, (await f.Store.LoadAsync())!.Revision);
    }
    [Theory]
    [InlineData("spawn")]
    [InlineData("ground")]
    [InlineData("pause")]
    [InlineData("slew")]
    [InlineData("accelerated")]
    [InlineData("frozen")]
    public async Task UnsupportedOrUnflownAreaPresenceNeverCompletes(string problem)
    {
        var f = new SyntheticOffshoreFlight();
        if (problem == "spawn") { await f.Initialize(); await f.Service.AcceptAsync(); }
        else await f.Begin();
        for (int i = 11; i <= 45; i++)
        {
            var changes = problem switch
            {
                "slew" => new[] { ("IS SLEW ACTIVE", 1d) },
                "accelerated" => [("SIMULATION RATE", 2d)],
                "frozen" => [("IS ATTITUDE FREEZE ON", 1d)],
                _ => Array.Empty<(string, double)>()
            };
            var frame = f.Frame(i, 15.05, problem == "ground", changes);
            if (problem == "pause") frame = frame with { Runtime = new(true, true, 0) };
            await f.Service.ObserveAsync(frame);
        }
        Assert.Null(f.Service.Selected!.ObservationCompletedUtc); Assert.False(f.Service.CanComplete);
        Assert.Empty((await f.Store.LoadAsync())!.FinancialLedger);
    }
    [Fact]
    public async Task LoiterMustBeContinuousAfterGapAndDoesNotInventUnobservedWages()
    {
        var f = new SyntheticOffshoreFlight(); await f.Begin();
        for (int i = 11; i <= 65; i++) await f.Observe(i, 15 + .05 * (i - 11) / 59, false);
        var before = f.Service.Selected!.AirborneTicks;
        var near = 15 + .05 * (65 - 11) / 59; // inside the area; no synthetic position jump after the gap
        await f.Service.ObserveAsync(null); await f.Observe(66, near, false);
        Assert.Equal(before, f.Service.Selected!.AirborneTicks);
        Assert.Equal(0, f.Service.Selected.ObservationSeconds);
        for (int i = 67; i < 76; i++) await f.Observe(i, near, false);
        Assert.Null(f.Service.Selected.ObservationCompletedUtc);
        await f.Observe(76, near, false); Assert.NotNull(f.Service.Selected.ObservationCompletedUtc);
    }
    [Fact]
    public async Task ReadyProofStopsWagesEvenIfPilotFliesAgainBeforeCompleting()
    {
        var f = new SyntheticOffshoreFlight(); await f.Begin(); await f.Outbound(); await f.Return();
        var ticks = f.Service.Selected!.AirborneTicks;
        for (int i = 162; i <= 170; i++) await f.Observe(i, 15, false);
        Assert.Equal(ticks, f.Service.Selected.AirborneTicks); Assert.False(f.Service.CanComplete);
    }
    [Fact]
    public async Task SelectionIdentityTeleportAndRestartRequireExplicitRecovery()
    {
        var f = new SyntheticOffshoreFlight(); await f.Begin(); await f.Observe(11, 15, false);
        await f.Observe(12, 16, false);
        Assert.True(f.Service.Selected!.RecoveryRequired);
        Assert.False(f.Service.CanComplete);
        f.SelectedAirframe = Guid.NewGuid(); await f.Observe(13);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.StartOrResumeAsync());
        var resumed = f.CreateService(); await resumed.RefreshAsync();
        Assert.True(resumed.Active!.RecoveryRequired); Assert.Equal(f.Service.Selected.Id, resumed.Active.Id);
    }
    [Fact]
    public async Task SimClockResetAndMissingFreezeTelemetryFailClosed()
    {
        var f = new SyntheticOffshoreFlight(); await f.Begin();
        var missing = f.Frame(11, 15, false) with { Readings = f.Frame(11, 15, false).Readings.SkipLast(1).ToArray() };
        await f.Service.ObserveAsync(missing); Assert.Equal(0, f.Service.Selected!.AirborneTicks);
        await f.Observe(12, 15, false, ("SIMULATION TIME", -1));
        Assert.Equal(0, f.Service.Selected.AirborneTicks); Assert.Equal(0, f.Service.Selected.ObservationSeconds);
    }
    [Fact]
    public async Task AtomicMissionWageAuditRejectsTamperingAndDuplicateReceipts()
    {
        var f = new SyntheticOffshoreFlight(); await f.Begin(); await f.Outbound(); await f.Return(); await f.Service.CompleteAsync();
        var w = (await f.Store.LoadAsync())!; var t = w.FinancialLedger.Single();
        Assert.Throws<InvalidDataException>(() => (w with { FinancialLedger = [t with { Amount = t.Amount + 1 }] }).EnsureValid());
        Assert.Throws<InvalidDataException>(() => (w with { FinancialLedger = [t, t with { Id = "different-id" }] }).EnsureValid());
    }
    [Fact]
    public async Task CargoAndPassengerFlightCannotOverlapObservationWages()
    {
        var f = new SyntheticOffshoreFlight(); await f.Begin();
        var w = (await f.Store.LoadAsync())!;
        Assert.True(OffshoreMissionKernel.Occupied(w));
        var passengers = new PassengerTransfersService(f.Store, SyntheticOffshoreFlight.Employer, f.Destinations);
        await Assert.ThrowsAsync<InvalidOperationException>(() => passengers.RequireNoBoardedPassengersAsync(CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => OffshoreMissionKernel.RequireTransportIdle(w with {
            Shipments = [new("synthetic-shipment", "synthetic-job", "synthetic-resource", 1, "origin", "destination", ShipmentState.Loaded, null) { LoadedQuantity = 1 }] }));
    }
    [Fact]
    public async Task OlderSaveAddsEmptyMissionCollectionWithoutTouchingMoneyOrCargo()
    {
        var root = Path.Combine(Path.GetTempPath(), "OffshoreMigration", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "economy-state.json"); var store = new LocalEconomyStateStore(path);
            var f = new SyntheticOffshoreFlight(store); await f.Initialize();
            var json = JsonNode.Parse(File.ReadAllText(path))!; json["SchemaVersion"] = 13;
            json["World"]!.AsObject().Remove("OffshoreMissions"); File.WriteAllText(path, json.ToJsonString());
            var migrated = (await store.LoadAsync())!;
            Assert.Empty(migrated.OffshoreMissions); Assert.NotEmpty(migrated.Jobs); Assert.Empty(migrated.FinancialLedger);
            Assert.Equal(0, migrated.Players.Single().CachedBalance);
            var bytes = File.ReadAllBytes(path); json["SchemaVersion"] = 999; File.WriteAllText(path, json.ToJsonString());
            var invalid = File.ReadAllBytes(path);
            await Assert.ThrowsAsync<IOException>(() => store.LoadAsync()); Assert.Equal(invalid, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(PlatformOperation.InspectionVisit)]
    [InlineData(PlatformOperation.CargoDelivery)]
    [InlineData(PlatformOperation.PersonnelDelivery)]
    [InlineData(PlatformOperation.PersonnelPickup)]
    public void StaticDeckEvidenceDoesNotGenerateAnyPlatformLandingOperation(PlatformOperation operation)
    {
        var platform = OffshorePlatformRegistry.Find("ol400-h01");
        Assert.Equal(PlatformQualificationStage.StaticDeckContact, platform.Stage);
        Assert.False(platform.CanOperate(operation, "H125 Passengers", "H125", "1.9.21.0", "unknown"));
        Assert.False(CompanyAircraft.Types.Single(a => a.Id == "h125").Enabled);
        Assert.True(platform.CanOperate(PlatformOperation.Observation, "", "", "", ""));
    }
}
