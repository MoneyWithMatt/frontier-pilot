using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.TestFixtures;

namespace FrontierAirportBuilder.Tests;

public sealed class PassengerPayloadTests
{
    private readonly MemoryEconomyStateStore store = new();
    private readonly SyntheticPassengerAircraft aircraft = new();
    private readonly Guid airframe = Guid.NewGuid();
    private PassengerPayloadCoordinator Engine => new(store, aircraft, aircraft, () => "synthetic", aircraft.WaitForRetentionAsync);
    private async Task<string> Reserve(params decimal[] weights)
    {
        var transport = PassengerTransportKernel.WithWeights(PassengerTransportKernel.Plan("independent-charter", weights.Length, true), weights);
        await store.InitializeAsync(EmployedCargoPrototype.Create(NamibiaLocationCatalogue.Load()) with { PassengerTransports = [transport] });
        return transport.Id;
    }
    private Task<PassengerTransport> Apply(string id, bool unload = false, Guid? selected = null) => Engine.ApplyAsync(id, "fixture-type", selected ?? airframe, unload, _ => { });
    private async Task Arrive(string id) => await store.ApplyAsync(w => new((w with { Revision = w.Revision + 1,
        PassengerTransports = w.PassengerTransports.Select(x => x.Id == id ? x with { State = PassengerCarriageState.Arrived, Arrival = FlightPosition.From(aircraft.Sample) } : x).ToArray() }).EnsureValid(), true));

    [Fact] public async Task SharedTransportCarriesMultiplePeopleWithoutMissionOrPayAndPreservesOtherLoad()
    {
        var id = await Reserve(75, 92); var boarded = await Apply(id);
        Assert.Equal(2, boarded.Group.Count); Assert.Equal(167, boarded.Group.WeightKilograms);
        Assert.Equal(new decimal[] { 80, 30, 75, 92, 0, 33 }, aircraft.Weights);
        await Apply(id); Assert.Equal(1, aircraft.Writes);
        await Arrive(id); await Apply(id, true); await Apply(id, true);
        Assert.Equal(new decimal[] { 80, 30, 0, 0, 0, 33 }, aircraft.Weights); Assert.Equal(2, aircraft.Writes); Assert.Equal(0, aircraft.FuelWrites);
        Assert.Equal(2, aircraft.RetentionChecks);
        var world = (await store.LoadAsync())!; Assert.Empty(world.PassengerTransfers); Assert.Empty(world.FinancialLedger);
    }

    [Theory][InlineData(0)][InlineData(-10)] public async Task ManifestRejectsNonpositiveWeights(int invalid)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Reserve(invalid)); Assert.Equal(0, aircraft.Writes);
    }

    [Fact] public async Task CapacityUsesVerifiedSeatsAndPreservesExistingOccupants()
    {
        var id = await Reserve(60, 70, 80); aircraft.Weights[2] = 40;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(id)); Assert.Equal(0, aircraft.Writes); Assert.Equal(40, aircraft.Weights[2]);
    }

    [Fact] public async Task TooManyPeopleAndOversizedStationWeightAreBlocked()
    {
        var id = await Reserve(60, 70, 80, 90); await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(id));
        var transport = PassengerTransportKernel.WithWeights(PassengerTransportKernel.Plan("heavy-person", 1, true), [151]);
        var live = await aircraft.ReadAsync("");
        Assert.Throws<InvalidOperationException>(() => PassengerLoadPlanner.Board(transport, aircraft.Resolve("fixture", live), live, airframe));
        Assert.Equal(0, aircraft.Writes);
    }

    [Fact] public async Task IdentityLayoutAndStationRestrictionsRemainHardWhileOperationalWeightsArePilotResponsibility()
    {
        var transport = PassengerTransportKernel.WithWeights(PassengerTransportKernel.Plan("limits", 1, true), [80]);
        var live = await aircraft.ReadAsync(""); var config = aircraft.Resolve("fixture", live);
        foreach (var invalid in new[] {
            live with { Aircraft = new("Other", "Other") },
            live with { PayloadStations = live.PayloadStations.Take(5).ToArray() },
            live with { PayloadStations = live.PayloadStations.Select(x => x.Index >= 3 ? x with { IsWritable = false } : x).ToArray() } })
            Assert.Throws<InvalidOperationException>(() => PassengerLoadPlanner.Board(transport, config, invalid, airframe));
        foreach (var advisory in new[] {
            live with { Weights = live.Weights with { CurrentGrossWeightKilograms = 4990, CurrentCgPercent = 90 } },
            live with { Weights = live.Weights with { MaximumPayloadKilograms = 200 } },
            live with { Weights = live.Weights with { CurrentGrossWeightKilograms = null } } })
            Assert.Equal(80, PassengerLoadPlanner.Board(transport, config, advisory, airframe).OwnedStations.Sum(x=>x.PassengerKilograms));
    }

    [Fact] public async Task PartialBoardingIsDurableAndRestartAppliesOnlyRemainingAbsoluteTargets()
    {
        var id = await Reserve(75, 92); aircraft.FailAfterStation = 1;
        await Assert.ThrowsAsync<IOException>(() => Apply(id));
        var pending = (await store.LoadAsync())!.PassengerTransports.Single();
        Assert.Equal(PassengerCarriageState.Boarding, pending.State); Assert.Contains("partial", pending.Payload!.Problem);
        Assert.Equal(75, aircraft.Weights[2]); Assert.Equal(0, aircraft.Weights[3]);
        var recovered = await Apply(id); Assert.Equal(PassengerCarriageState.Aboard, recovered.State);
        Assert.Equal(75, aircraft.Weights[2]); Assert.Equal(92, aircraft.Weights[3]); Assert.Equal(2, aircraft.Writes);
    }

    [Fact] public async Task SetterThrowAfterAllTargetsDoesNotDoubleApplyOnRetry()
    {
        var id = await Reserve(75); aircraft.FailAfterStation = 1;
        await Assert.ThrowsAsync<IOException>(() => Apply(id)); await Apply(id);
        Assert.Equal(75, aircraft.Weights[2]); Assert.Equal(1, aircraft.Writes);
    }

    [Fact] public async Task ManualChangesAfterFailureAreNeverOverwritten()
    {
        var id = await Reserve(75, 92); aircraft.FailAfterStation = 1; await Assert.ThrowsAsync<IOException>(() => Apply(id));
        aircraft.Weights[2] = 20; await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(id));
        Assert.Equal(20, aircraft.Weights[2]); Assert.Equal(1, aircraft.Writes);
    }

    [Fact] public async Task RevertedReadbackAndUnrelatedPayloadChangesDoNotConfirmBoarding()
    {
        var id = await Reserve(75); aircraft.RevertReadback = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(id));
        Assert.Equal(PassengerCarriageState.Boarding, (await store.LoadAsync())!.PassengerTransports.Single().State);
        aircraft.RevertReadback = false; aircraft.AlterProtectedStation = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(id));
        Assert.Equal(34, aircraft.Weights[5]); Assert.Equal(0, aircraft.FuelWrites);
    }

    [Fact] public async Task UnloadRefusesMissingOrEditedPassengerMassAndWrongAirframe()
    {
        var id = await Reserve(75); await Apply(id); await Arrive(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(id, true, Guid.NewGuid()));
        aircraft.Weights[2] = 40; await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(id, true));
        Assert.Equal(40, aircraft.Weights[2]); Assert.Equal(1, aircraft.Writes);
    }

    [Fact] public async Task PartialUnloadRecoversAcrossRestartAndRemovesOwnedWeightOnce()
    {
        var id = await Reserve(75, 92); await Apply(id); await Arrive(id); aircraft.FailAfterStation = 1;
        await Assert.ThrowsAsync<IOException>(() => Apply(id, true));
        Assert.Equal(PassengerCarriageState.Disembarking, (await store.LoadAsync())!.PassengerTransports.Single().State);
        await Apply(id, true); await Apply(id, true);
        Assert.Equal(new decimal[] { 80, 30, 0, 0, 0, 33 }, aircraft.Weights); Assert.Equal(3, aircraft.Writes);
    }

    [Fact] public async Task DuplicateConcurrentOperationsAreSerializedAcrossCoordinators()
    {
        var id = await Reserve(75, 92); await Task.WhenAll(Apply(id), Apply(id));
        Assert.Equal(1, aircraft.Writes); await Arrive(id);
        await Task.WhenAll(Apply(id, true), Apply(id, true)); Assert.Equal(2, aircraft.Writes);
    }

    [Fact] public async Task ConfigurationEvidenceAndStationCountAreNotSeatCapacity()
    {
        var live = await aircraft.ReadAsync("");
        var provider = new PassengerAircraftConfigurations(() => null);
        Assert.Throws<InvalidOperationException>(() => provider.Resolve("vision-jet", live));
        Assert.Throws<InvalidOperationException>(() => provider.Resolve("c152", live));
        Assert.Throws<InvalidOperationException>(() => provider.Resolve("c208", live));
        Assert.Equal(6, live.PayloadStations.Count); Assert.Equal(3, aircraft.Resolve("fixture", live).Capacity);
    }

    [Fact] public async Task LastMomentManualPayloadChangePreventsTheNativeWrite()
    {
        var id = await Reserve(75); aircraft.ChangeBeforeWrite = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(id));
        Assert.Equal(0, aircraft.Writes); Assert.Equal(25, aircraft.Weights[2]);
        Assert.Equal(PassengerCarriageState.Boarding, (await store.LoadAsync())!.PassengerTransports.Single().State);
    }

    [Fact] public async Task SavedSeatConfigurationCannotClaimMoreSeatsWithAnUnchangedFingerprint()
    {
        var id = await Reserve(75); await Apply(id);
        await store.ApplyAsync(w => new((w with { Revision = w.Revision + 1, PassengerTransports = w.PassengerTransports.Select(x => x with {
            Payload = x.Payload! with { Configuration = x.Payload.Configuration with { Stations = x.Payload.Configuration.Stations.Select(s => s with { Seats = 2 }).ToArray() } }
        }).ToArray() }).EnsureValid(), true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(id)); Assert.Equal(1, aircraft.Writes); Assert.Equal(75, aircraft.Weights[2]);
    }
}
