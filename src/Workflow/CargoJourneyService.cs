using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>Explicit consignment handling over shared, guarded simulator station mass. Intent precedes every write.</summary>
public sealed class CargoJourneyService(IEconomyStateStore store, IAircraftLoadingSimulator simulator, Func<string> sdkRoot,
    Func<Guid?> selectedAirframe, Func<EconomyWorld, FlightPosition, PassengerEndpoint?> location,
    Func<DateTimeOffset>? clock = null, Func<CancellationToken, Task>? retention = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly FlightAttemptTracker.TransportMonitor monitor = new();
    private DateTimeOffset Now => clock?.Invoke() ?? DateTimeOffset.UtcNow;
    public Func<CancellationToken, Task>? LegacyRecoveryGuard { get; init; }
    public Func<FlightAircraftIdentity, IReadOnlyList<string>, string?>? DispatchGuard { get; init; }
    public Action<AircraftLoadingSnapshot, bool>? AuthorizeHandling { get; init; }
    public Action<EconomyWorld>? WorldObserved { get; init; }
    public string Reason { get; private set; } = "Read aircraft and select explicit consignments. Operational weight and balance remain the pilot's responsibility.";
    public EconomyWorld? World { get; private set; }
    public AircraftLoadingSnapshot? Live { get; private set; }
    public CargoAirframeManifest? Manifest => World?.CargoAirframes.SingleOrDefault(x => x.AirframeId == selectedAirframe());
    public PassengerEndpoint? Here => World is {} w && Live is {} l ? location(w, l.Position) : null;
    public event Action? Changed;

    public async Task RefreshAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { World = await store.LoadAsync(token).ConfigureAwait(false); Live = (await simulator.ReadAsync(sdkRoot(), token).ConfigureAwait(false)).EnsureValid(); Changed?.Invoke(); }
        finally { gate.Release(); }
    }
    private static void NoPassengers(EconomyWorld w)
    {
        if (w.PassengerTransfers.Any(x => x.State == PassengerTransferState.Boarded) || w.PassengerTransports.Any(x => x.State is PassengerCarriageState.Boarding or PassengerCarriageState.Aboard or PassengerCarriageState.Arrived or PassengerCarriageState.Disembarking))
            throw new InvalidOperationException("Mixed cargo/passenger payload accounting is pending qualification. Disembark passengers before cargo handling; their mass is preserved.");
    }
    private void Ground(AircraftLoadingSnapshot live, CargoAirframeManifest? manifest)
    {
        if (selectedAirframe() is not {} id || id == Guid.Empty) throw new InvalidOperationException("Select a registered physical airframe first.");
        if (!live.Position.IsOnGround || live.Position.GroundSpeedKnots > 5 || Now < live.ObservedUtc || Now - live.ObservedUtc > TimeSpan.FromSeconds(3))
            throw new InvalidOperationException("Cargo handling requires a fresh, stopped ground observation.");
        if (manifest is not null && (manifest.AirframeId != selectedAirframe() || manifest.Aircraft != live.Aircraft))
            throw new InvalidOperationException("Actual aircraft or selected airframe differs from saved cargo ownership. Nothing was written.");
    }
    private static void Claims(CargoAirframeManifest m, AircraftLoadingSnapshot live)
    {
        if (m.Aircraft != live.Aircraft) throw new InvalidOperationException("Actual aircraft does not match cargo ownership.");
        if (m.StationLayout.Length != live.PayloadStations.Count || m.StationLayout.Any(b => live.PayloadStations.SingleOrDefault(s => s.Index == b.Index)?.Name != b.Name))
            throw new InvalidOperationException("Cargo station layout changed; saved claims are preserved.");
        foreach (var s in m.Stations.Where(x => x.FrontierCargoKilograms > 0))
            if (live.PayloadStations.SingleOrDefault(x => x.Index == s.StationIndex) is not {} actual || Math.Abs(actual.CurrentWeightKilograms - s.TargetKilograms) > .25m)
                throw new InvalidOperationException("Owned cargo mass conflicts with MSFS. Manual changes are preserved; reconcile the recorded station weight before continuing.");
    }

    public async Task<CargoOperation> PrepareAsync(IReadOnlyDictionary<string, decimal> desired,
        IReadOnlyList<CargoStationAllocation> allocations, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        var stage="checking existing loading records";
        try
        {
            using var lease = await AircraftPayloadOperationLease.AcquireAsync(token).ConfigureAwait(false);
            if (LegacyRecoveryGuard is {} guard) await guard(token).ConfigureAwait(false);
            var w = await store.LoadAsync(token).ConfigureAwait(false) ?? throw new InvalidOperationException("Accept a cargo job first.");
            NoPassengers(w);
            stage="reading aircraft identity and payload from MSFS";
            var live = (await simulator.ReadAsync(sdkRoot(), token).ConfigureAwait(false)).EnsureValid();
            var id = selectedAirframe() ?? throw new InvalidOperationException("Select a physical airframe.");
            var m = w.CargoAirframes.SingleOrDefault(x => x.AirframeId == id); Ground(live, m);
            stage="checking walking-avatar access and live tracking identity";
            AuthorizeHandling?.Invoke(live, true);
            stage="checking cargo, aircraft eligibility and station distribution";
            var here = location(w, live.Position) ?? throw new InvalidOperationException("Stop at one unambiguous known cargo location.");
            if (m?.Pending is not null) throw new InvalidOperationException("Resolve the persisted cargo preview/operation before preparing another.");
            if (m?.Legs.Any(x => x.SettledUtc is null) == true) throw new InvalidOperationException("Complete the current armed leg before changing cargo.");
            if (w.Shipments.Any(s => s.LoadedQuantity > 0 && !w.CargoAirframes.Any(a => a.ManagedJobs.Contains(s.JobId))))
                throw new InvalidOperationException("Legacy loaded cargo requires its existing Aircraft recovery flow before multi-stop handling. No ownership was inferred.");
            if (w.CargoAirframes.Any(a => a.AirframeId != id && (a.Cargo.Length > 0 || a.Pending is not null || a.Legs.Any(l => l.SettledUtc is null))))
                throw new InvalidOperationException("Finish the other airframe's cargo journey before starting another active flight.");
            if (m is not null) Claims(m, live);
            var changes = desired.Select(x => new CargoChange(x.Key, w.Shipments.Single(s => s.JobId == x.Key).LoadedQuantity, x.Value)).Where(x => x.Before != x.After).ToArray();
            if (changes.Length == 0) throw new InvalidOperationException("Select at least one consignment quantity change.");
            foreach (var c in changes)
            {
                var job = w.Jobs.Single(x => x.Id == c.JobId); var shipment = w.Shipments.Single(x => x.JobId == c.JobId);
                if (w.Resources.Single(r => r.Id == shipment.ResourceId).UnitName != "kg")
                    throw new InvalidOperationException("This resource has no configured quantity-to-kilogram conversion. Its inventory is preserved; only kilogram consignments can be physically loaded by this adapter.");
                if (job.State != EconomyJobState.Accepted || c.After < 0 || c.After > shipment.Quantity - shipment.DeliveredQuantity ||
                    c.Before > 0 && m?.Cargo.Any(x => x.JobId == c.JobId) != true || w.CargoAirframes.Any(a => a.AirframeId != id && a.ManagedJobs.Contains(c.JobId)))
                    throw new InvalidOperationException("Consignment quantity, acceptance or aircraft ownership is incompatible.");
                if (c.After > c.Before && shipment.OriginLocationId != here.Id) throw new InvalidOperationException("Additional reserved cargo is only available at its origin.");
                if (c.After < c.Before && shipment.DestinationLocationId != here.Id && (shipment.OriginLocationId != here.Id || shipment.State == ShipmentState.InTransit))
                    throw new InvalidOperationException("Deliver at this consignment's destination, or return unflown cargo at its origin. Off-route disposal is not part of this flow.");
                if (c.After < c.Before && here.Id == shipment.DestinationLocationId)
                {
                    var leg = m is null ? throw new InvalidOperationException("No flight leg.") : CargoJourneyKernel.DeliveryLeg(w, m.Id, c.JobId);
                    var carried = leg.Carried.Single(x => x.JobId == c.JobId);
                    if (shipment.DeliveredQuantity - carried.DeliveredBefore + c.Before - c.After > carried.Kilograms)
                        throw new InvalidOperationException("Delivery exceeds the cargo carried on the qualified arrival leg.");
                }
            }
            var pickups = changes.Where(x => x.After > x.Before).Select(x => x.JobId).ToArray();
            var dispatch = DispatchGuard?.Invoke(live.Aircraft, pickups);
            if (dispatch is not null) throw new InvalidOperationException(dispatch);
            var baselines = live.PayloadStations.ToDictionary(s => s.Index, s => s.CurrentWeightKilograms - (m?.Stations.SingleOrDefault(p => p.StationIndex == s.Index)?.FrontierCargoKilograms ?? 0));
            var total = (m?.Cargo.Sum(x => x.Kilograms) ?? 0) + changes.Sum(x => x.After - x.Before);
            foreach (var allocation in allocations) allocation.EnsureValid();
            if (Math.Abs(allocations.Sum(x => x.CargoKilograms) - total) > .001m) throw new InvalidOperationException("Station cargo distribution must equal explicit desired consignment mass.");
            var baselineSnapshot = live with { PayloadStations = live.PayloadStations.Select(s => s with { CurrentWeightKilograms = baselines[s.Index] }).ToArray(),
                Weights=live.Weights with { CurrentGrossWeightKilograms=live.Weights.CurrentGrossWeightKilograms-(m?.Cargo.Sum(c=>c.Kilograms)??0) } };
            // Existing planner enforces only actual station limits. MTOW/CG are advisory.
            var plan = total > 0 ? AircraftLoadPlan.Create(baselineSnapshot, total, allocations) :
                new AircraftLoadPlan(0, live.PayloadStations.Select(s => new PlannedPayloadStation(s.Index, baselines[s.Index], 0, baselines[s.Index])).ToArray(), baselines.Values.Sum(), null, null, []);
            var operation = new CargoOperation("cargo-op-" + Guid.NewGuid().ToString("N"), here.Id, Now, changes,
                live.PayloadStations.ToArray(), plan.Stations.ToArray(), live.Position) { FuelBefore = live.FuelTanks.ToArray(), AuthorizedUtc = Now };
            m ??= new CargoAirframeManifest(CargoJourneyKernel.ManifestId(id), id, live.Aircraft, w.Jobs.Single(x => x.Id == changes[0].JobId).PlayerId, 1, here.Id, [], [], [], null, [], []) { StationLayout = live.PayloadStations.ToArray() };
            var expected = m.Revision;
            var updated = m with { LocationId = here.Id, Revision = expected + 1, Pending = operation, ManagedJobs = m.ManagedJobs.Concat(changes.Select(x => x.JobId)).Distinct().ToArray() };
            stage="saving the loading plan";
            World = (await store.ApplyAsync(current =>
            {
                NoPassengers(current);
                if (current.Revision != w.Revision) throw new IOException("Cargo/economy changed during preview. Refresh and prepare again.");
                return CargoJourneyKernel.Replace(current, updated);
            }, token).ConfigureAwait(false)).World;
            Live = live; Reason = "PROPOSED: outside authorization saved. Review consignments and station targets, board the same aircraft, then Apply within five minutes. Cargo is unchanged until readback and commit.";
            Changed?.Invoke(); return operation;
        }
        catch(Exception error) when(error is not OperationCanceledException)
        {throw new InvalidOperationException($"Loading plan stopped while {stage}. {error.Message} This preview did not write payload to MSFS.",error);}
        finally { gate.Release(); }
    }

    private void CheckOperation(CargoAirframeManifest m, CargoOperation p, AircraftLoadingSnapshot live, bool complete)
    {
        Ground(live, m);
        if (location(World!, live.Position)?.Id != p.LocationId || WorldTracking.DistanceMeters(live.Position.Latitude, live.Position.Longitude, p.Position.Latitude, p.Position.Longitude) > 25)
            throw new InvalidOperationException("Aircraft moved from the persisted cargo handling position.");
        if (live.PayloadStations.Count != p.BeforeStations.Length) throw new InvalidOperationException("Payload station layout changed.");
        foreach (var before in p.BeforeStations)
        {
            var actual = live.PayloadStations.SingleOrDefault(x => x.Index == before.Index);
            var target = p.Targets.Single(x => x.StationIndex == before.Index).TargetKilograms;
            if (actual is null || actual.Name != before.Name || (!actual.IsWritable && Math.Abs(target - before.CurrentWeightKilograms) > .001m) ||
                actual.AuthoritativeMaximumKilograms is {} max && target > max ||
                Math.Abs(actual.CurrentWeightKilograms - target) > .25m && (complete || Math.Abs(actual.CurrentWeightKilograms - before.CurrentWeightKilograms) > .25m))
                throw new InvalidOperationException("Station readback conflicts with saved before/target mass or MSFS refused/clamped a write. Intent retained; no manual payload was overwritten.");
        }
        if (p.FuelBefore.Length != live.FuelTanks.Count || p.FuelBefore.Any(b => live.FuelTanks.SingleOrDefault(x => x.Id == b.Id) is not {} f || Math.Abs(f.QuantityGallons - b.QuantityGallons) > .05m))
            throw new InvalidOperationException("Fuel changed during cargo handling. Cargo intent retained; fuel was never written.");
    }
    public async Task ApplyAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var lease = await AircraftPayloadOperationLease.AcquireAsync(token).ConfigureAwait(false);
            World = await store.LoadAsync(token).ConfigureAwait(false) ?? throw new InvalidOperationException("No economy.");
            NoPassengers(World);
            var m = Manifest ?? throw new InvalidOperationException("Select the recorded airframe.");
            var p = m.Pending ?? throw new InvalidOperationException("Prepare an explicit loading preview first.");
            try
            {
                var before = (await simulator.ReadAsync(sdkRoot(), token).ConfigureAwait(false)).EnsureValid(); CheckOperation(m, p, before, false);
                if (p.AuthorizedUtc is not {} authorized || Now < authorized || Now - authorized > TimeSpan.FromMinutes(5)) throw new InvalidOperationException("Outside authorization expired. Exit near the same aircraft and renew the pending operation's authorization.");
                AuthorizeHandling?.Invoke(before, false);
                var targets = p.Targets.Where(x => Math.Abs(before.PayloadStations.Single(s => s.Index == x.StationIndex).CurrentWeightKilograms - x.TargetKilograms) > .25m).ToDictionary(x => x.StationIndex, x => x.TargetKilograms);
                if (!p.WriteStarted)
                {
                    p = p with { WriteStarted=true, ExpectedGrossKilograms=before.Weights.CurrentGrossWeightKilograms + targets.Sum(t=>t.Value-before.PayloadStations.Single(s=>s.Index==t.Key).CurrentWeightKilograms) };
                    World = (await store.ApplyAsync(w=>
                    {
                        NoPassengers(w);var current=w.CargoAirframes.Single(x=>x.Id==m.Id);
                        if(current.Revision!=m.Revision || current.Pending?.Id!=p.Id)throw new IOException("Cargo intent changed before physical application.");
                        return CargoJourneyKernel.Replace(w,current with {Revision=current.Revision+1,Pending=p});
                    },token).ConfigureAwait(false)).World;
                    m=World.CargoAirframes.Single(x=>x.Id==m.Id);
                    Ground(before,m);AuthorizeHandling?.Invoke(before,false);
                }
                if (targets.Count > 0)
                {
                    if (simulator is not IGuardedAircraftPayloadSimulator guarded) throw new InvalidOperationException("Cargo requires the shared immediate identity/payload comparison adapter.");
                    var immediate = await guarded.WriteGuardedPayloadAsync(sdkRoot(), before, targets, token).ConfigureAwait(false);
                    CheckOperation(m, p, immediate.EnsureValid(), true);
                }
                await (retention?.Invoke(token) ?? Task.Delay(TimeSpan.FromSeconds(3), token)).ConfigureAwait(false);
                Live = (await simulator.ReadAsync(sdkRoot(), token).ConfigureAwait(false)).EnsureValid(); CheckOperation(m, p, Live, true); AuthorizeHandling?.Invoke(Live, false);
                if (p.ExpectedGrossKilograms is {} expectedGross && (Live.Weights.CurrentGrossWeightKilograms is not {} actualGross || Math.Abs(actualGross-expectedGross)>1m))
                    throw new InvalidOperationException("Retained total aircraft weight does not match the persisted physical loading intent. Retrying identical station echoes cannot qualify it.");
                if (before.Weights.CurrentGrossWeightKilograms is {} grossBefore && Live.Weights.CurrentGrossWeightKilograms is {} grossAfter &&
                    Math.Abs(grossAfter - grossBefore - targets.Sum(t => t.Value - before.PayloadStations.Single(s => s.Index == t.Key).CurrentWeightKilograms)) > 1m)
                    throw new InvalidOperationException("Gross-weight change does not confirm the physical cargo delta. Intent retained; operational maximum gross remains advisory.");
                World = (await store.ApplyAsync(w =>
                {
                    NoPassengers(w);
                    var current = w.CargoAirframes.Single(x => x.Id == m.Id);
                    if (current.Pending?.Id != p.Id || current.Revision != m.Revision) throw new IOException("Cargo intent changed. Refresh.");
                    return CargoJourneyKernel.Replace(w, current with { Pending = p with { Verified = true, Problem = null, VerifiedUtc = Live.ObservedUtc,
                        Targets = p.Targets.Select(s=>s with { ReadBackKilograms=Live.PayloadStations.Single(a=>a.Index==s.StationIndex).CurrentWeightKilograms }).ToArray() }, Revision = current.Revision + 1 });
                }, token).ConfigureAwait(false)).World;
                World = (await store.ApplyAsync(w => CargoJourneyKernel.CommitOperation(w, m.Id, p.Id,
                    (world, job) => FlightAttemptTracker.CargoDeliveryEvidence(world, m.Id, job, Now)), token).ConfigureAwait(false)).World;
                Reason = "CONFIRMED: physical cargo readback retained; consignments, inventories and completion committed atomically.";
            }
            catch (Exception ex)
            {
                Reason = ex.Message;
                World = (await store.ApplyAsync(w =>
                {
                    var current = w.CargoAirframes.Single(x => x.Id == m.Id);
                    if (current.Pending?.Id != p.Id) return new(w, false);
                    return CargoJourneyKernel.Replace(w, current with { Revision = current.Revision + 1, Pending = current.Pending with { Problem = ex.Message[..Math.Min(500, ex.Message.Length)] } });
                }, CancellationToken.None).ConfigureAwait(false)).World;
                throw;
            }
            finally { Changed?.Invoke(); }
        }
        finally { gate.Release(); }
    }

    public async Task RenewAuthorizationAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            World = await store.LoadAsync(token).ConfigureAwait(false) ?? throw new InvalidOperationException("No economy.");
            var m = Manifest ?? throw new InvalidOperationException("Select the cargo airframe.");
            var p = m.Pending ?? throw new InvalidOperationException("No pending cargo operation.");
            Live = (await simulator.ReadAsync(sdkRoot(), token).ConfigureAwait(false)).EnsureValid(); CheckOperation(m, p, Live, false); AuthorizeHandling?.Invoke(Live, true);
            World = (await store.ApplyAsync(w =>
            {
                if (w.Revision != World.Revision) throw new IOException("Manifest changed. Refresh.");
                return CargoJourneyKernel.Replace(w, m with { Revision = m.Revision + 1, Pending = p with { AuthorizedUtc = Now } });
            }, token).ConfigureAwait(false)).World;
            Reason = "Outside authorization renewed for the same immutable targets. Board the same aircraft and retry Apply."; Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
    public async Task CancelUnappliedPlanAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var lease = await AircraftPayloadOperationLease.AcquireAsync(token).ConfigureAwait(false);
            World = await store.LoadAsync(token).ConfigureAwait(false) ?? throw new InvalidOperationException("No economy.");
            var m = Manifest ?? throw new InvalidOperationException("Select the cargo airframe.");
            Live = (await simulator.ReadAsync(sdkRoot(), token).ConfigureAwait(false)).EnsureValid(); Ground(Live, m);
            if (m.Pending is {} p && p.BeforeStations.Any(b => Live.PayloadStations.SingleOrDefault(s => s.Index == b.Index) is not {} actual || actual.Name != b.Name || Math.Abs(actual.CurrentWeightKilograms-b.CurrentWeightKilograms)>.25m))
                throw new InvalidOperationException("This operation may already have changed payload. Resolve it with Apply/retry; it cannot be discarded.");
            if (m.Legs.Any(l => l.SettledUtc is null && l.Flight is not null)) throw new InvalidOperationException("An armed leg cannot be discarded; its recorded progress is retained.");
            World = (await store.ApplyAsync(w =>
            {
                if (w.Revision != World.Revision) throw new IOException("Manifest changed. Refresh.");
                return CargoJourneyKernel.Replace(w, m with { Revision=m.Revision+1, Pending=null, Legs=m.Legs.Where(l=>l.SettledUtc is not null).ToArray() });
            }, token).ConfigureAwait(false)).World;
            monitor.Reset(); Reason="Unapplied preview / unarmed next-stop plan discarded. Committed cargo and history are unchanged."; Changed?.Invoke();
        }
        finally { gate.Release(); }
    }

    private static FlightAttemptTracker.TransportFlightContext Context(CargoAirframeManifest m, CargoFlightLeg leg) =>
        new(leg.Id, leg.Origin, leg.Destination, leg.Flight);

    public async Task PlanLegAsync(PassengerEndpoint destination, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            World = await store.LoadAsync(token).ConfigureAwait(false) ?? throw new InvalidOperationException("No economy."); NoPassengers(World);
            var m = Manifest ?? throw new InvalidOperationException("Load cargo in the recorded airframe first.");
            Live = (await simulator.ReadAsync(sdkRoot(), token).ConfigureAwait(false)).EnsureValid(); Ground(Live, m); Claims(m, Live);
            var here = location(World, Live.Position) ?? throw new InvalidOperationException("Stop at a known location.");
            if (!destination.CanGenerate || destination.Id == here.Id || !World.Locations.Any(x => x.Id == destination.Id) || m.Pending is not null || m.Cargo.Length == 0 || m.Legs.Any(x => x.SettledUtc is null))
                throw new InvalidOperationException("Select another known qualified location with cargo aboard, no pending operation and no active leg.");
            var employers = m.Cargo.Select(c => World.Jobs.Single(j => j.Id == c.JobId)).Where(j => !World.LogisticsContracts.Any(c => c.JobId == j.Id))
                .Select(j => World.Businesses.Single(b => b.Id == j.EmployerBusinessId)).Where(b => b.PilotHourlyRate is not null).DistinctBy(b => b.Id).ToArray();
            if (employers.Length > 1) throw new InvalidOperationException("One employed flight leg must have one employer; finish the current employer's cargo before another employer's flight.");
            var leg = new CargoFlightLeg("cargo-leg-" + Guid.NewGuid().ToString("N"), here, destination, Now, m.Cargo.ToArray(), EmployerId: employers.SingleOrDefault()?.Id, HourlyRate: employers.SingleOrDefault()?.PilotHourlyRate);
            World = (await store.ApplyAsync(w =>
            {
                if (w.Revision != World.Revision) throw new IOException("Manifest changed. Refresh.");
                return CargoJourneyKernel.Replace(w, m with { Revision = m.Revision + 1, LocationId = here.Id, Legs = m.Legs.Append(leg).ToArray() });
            }, token).ConfigureAwait(false)).World;
            monitor.Reset(); Reason = "Next stop planned. Remain stopped for ten continuous tracking seconds, then arm this leg before departure."; Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
    public async Task ArmLegAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            World = await store.LoadAsync(token).ConfigureAwait(false) ?? throw new InvalidOperationException("No economy."); NoPassengers(World);
            var m = Manifest ?? throw new InvalidOperationException("No cargo manifest."); var leg = m.Legs.Single(x => x.SettledUtc is null);
            if (leg.Flight is not null) return;
            Live = (await simulator.ReadAsync(sdkRoot(), token).ConfigureAwait(false)).EnsureValid(); Ground(Live, m); Claims(m, Live);
            var flight = monitor.Board(Context(m, leg), Now);
            World = (await store.ApplyAsync(w =>
            {
                if (w.CargoAirframes.Single(x => x.Id == m.Id).Revision != m.Revision) throw new IOException("Manifest changed while arming. Refresh.");
                return CargoJourneyKernel.Replace(w, m with { Revision = m.Revision + 1,
                    Legs = m.Legs.Select(l => l.Id == leg.Id ? l with { Flight = flight } : l).ToArray() });
            }, token).ConfigureAwait(false)).World;
            Reason = "Leg armed with this immutable cargo snapshot. Fly to the planned next stop."; Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
    public async Task ObserveAsync(AircraftTelemetry? sample, CancellationToken token = default)
    {
        if (!await gate.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            World = await store.LoadAsync(token).ConfigureAwait(false);
            if(World is {} observed)WorldObserved?.Invoke(observed);
            if (Manifest is not {} m || m.Legs.SingleOrDefault(x => x.SettledUtc is null) is not {} leg) return;
            if (sample is null) { monitor.Reset(); Reason = "Tracking disconnected. Cargo and proven progress retained; no gap time accrues."; Changed?.Invoke(); return; }
            NoPassengers(World!);
            Live = (await simulator.ReadAsync(sdkRoot(), token).ConfigureAwait(false)).EnsureValid(); Claims(m, Live);
            if (Live.Aircraft != FlightAircraftIdentity.From(sample) || Math.Abs((Live.ObservedUtc - sample.ObservedUtc).TotalSeconds) > 3)
                throw new InvalidOperationException("Payload and tracking observations disagree; qualification paused.");
            var flight = monitor.Observe(Context(m, leg), sample);
            Reason = flight?.DeliveryProof is not null ? "Qualified arrival persisted. Successful consignment delivery releases eligible leg wages once; through-cargo stays aboard." :
                monitor.Reason.Replace("Passengers", "Cargo").Replace("passengers", "cargo").Replace("before boarding", "before arming the leg");
            if (flight is not null && flight != leg.Flight)
            {
                World = (await store.ApplyAsync(w =>
                {
                    var current = w.CargoAirframes.Single(x => x.Id == m.Id);
                    if (current.Revision != m.Revision) throw new IOException("Manifest changed during observation.");
                    var changed = current with { Revision = current.Revision + 1, Legs = current.Legs.Select(l => l.Id == leg.Id ? l with { Flight = flight } : l).ToArray() };
                    var updated = w with { CargoAirframes = w.CargoAirframes.Select(x => x.Id == m.Id ? changed : x).ToArray() };
                    if (flight.FirstAirborneUtc is not null)
                        updated = EconomyKernel.MarkShipmentsInTransit(updated, leg.Carried.Select(x => x.JobId).ToArray()).World;
                    return flight.DeliveryProof is null ? new((updated with { Revision = w.Revision + 1 }).EnsureValid(), true) :
                        CargoJourneyKernel.SettleLeg(updated with { Revision = w.Revision }, changed, changed.Legs.Single(x => x.Id == leg.Id), sample.ObservedUtc);
                }, token).ConfigureAwait(false)).World;
            }
            if(World is {} completed)WorldObserved?.Invoke(completed);
            Changed?.Invoke();
        }
        catch (Exception ex) { monitor.Reset(); Reason = ex.Message; Changed?.Invoke(); }
        finally { gate.Release(); }
    }
}

public sealed partial class FlightAttemptTracker
{
    internal static VerifiedDeliveryEvidence? CargoDeliveryEvidence(EconomyWorld w, string manifest, string jobId, DateTimeOffset utc)
    {
        var s = w.Shipments.Single(x => x.JobId == jobId);
        var m = w.CargoAirframes.Single(x => x.Id == manifest);
        if (m.Pending?.LocationId != s.DestinationLocationId) return null;
        var leg = CargoJourneyKernel.DeliveryLeg(w, manifest, jobId);
        var carried = leg.Carried.Single(x => x.JobId == jobId);
        var change = m.Pending.Changes.Single(x => x.JobId == jobId);
        if (leg.Flight?.DeliveryProof is not {} proof || leg.Flight.LoadedUtc < w.Jobs.Single(x => x.Id == jobId).AcceptedUtc ||
            s.DeliveredQuantity - carried.DeliveredBefore + change.Before - change.After > carried.Kilograms || utc < proof.ReadyUtc)
            throw new InvalidOperationException("Consignment lacks eligible post-loading arrival evidence.");
        return new(FlightAttemptIds.DeliveryEvidence(s.Id), "frontier-cargo-flight-leg-v1", jobId, s.Id,
            s.OriginLocationId, s.DestinationLocationId, utc, "Qualified flight leg and explicit, readback-confirmed consignment delivery.") { CargoLegId = leg.Id };
    }
}
