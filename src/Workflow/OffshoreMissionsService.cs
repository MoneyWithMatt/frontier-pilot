using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>Career policy/persistence adapter; normal flight validation remains in FlightAttemptTracker.</summary>
public sealed class OffshoreMissionsService(IEconomyStateStore store, CareerEmployer? employer,
    EmployerDestinationCatalogue destinations, Func<IReadOnlyList<AircraftTypeBinding>> bindings,
    Func<Guid?> selectedAirframe, Func<DateTimeOffset>? clock = null, OffshoreOperatingArea? operatingArea = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly FlightAttemptTracker.OffshoreMonitor monitor = new();
    private bool initialized;
    private EconomyWorld? world;
    public bool Enabled => employer?.Category == "Offshore";
    public decimal HourlyRate => employer?.HourlyWage ?? 0;
    public bool CanAccept => Selected is { State: OffshoreMissionState.Offered } m && m.ExpiresUtc > Now &&
        selectedAirframe() is not null && SelectedCompanyType() == "vision-jet" && Active is null && destinations.CanGenerateAtBase(employer!);
    public Func<ConditionAircraftIdentity, bool> LiveIdentityGuard { get; init; } = _ => false;
    public Func<string?> SelectedCompanyType { get; init; } = () => null;
    public Func<FlightAircraftIdentity, string?> ContinuityGuard { get; init; } = _ => null;
    public Func<CancellationToken, Task> LegacyRecoveryGuard { get; init; } = _ => Task.CompletedTask;
    public Func<FlightAircraftIdentity, OffshoreMission, string?> DispatchGuard { get; init; } = (_, _) => "Select and bind the employer aircraft in Fleet before dispatch.";
    public Action<EconomyWorld>? WorldObserved { get; init; }
    public string Reason { get; private set; } = "Refresh the Offshore board.";
    public string? SelectedId { get; set; }
    public OffshoreMission? Selected => world?.OffshoreMissions.FirstOrDefault(m => m.Id == SelectedId);
    public OffshoreMission? Active => world?.OffshoreMissions.FirstOrDefault(m => m.Active);
    public IReadOnlyList<OffshoreMission> Missions => world?.OffshoreMissions.Where(m => m.EmployerId == employer?.Id &&
        m.State != OffshoreMissionState.Expired && (m.State != OffshoreMissionState.Offered || m.ExpiresUtc > Now))
        .OrderBy(m => m.Active ? 0 : m.State == OffshoreMissionState.Offered ? 1 : 2).ThenByDescending(m => m.OfferedUtc).ToArray() ?? [];
    public decimal Balance => world?.Players.SingleOrDefault(p => p.Id == EmployedCargoPrototype.PlayerId)?.CachedBalance ?? 0;
    public double AreaDistance => Selected is {} mission && monitor.Latest is {} latest ?
        WorldTracking.DistanceMeters(latest.Position.Latitude, latest.Position.Longitude, mission.Area.Latitude, mission.Area.Longitude) / 1852 : double.NaN;
    public double AreaBearing
    {
        get
        {
            if (Selected is not {} mission || monitor.Latest is not {} latest) return double.NaN;
            try { return Geodesy.Inverse(new(latest.Position.Latitude, latest.Position.Longitude), new(mission.Area.Latitude, mission.Area.Longitude)).InitialBearingDegrees; }
            catch (ArithmeticException) { return double.NaN; }
        }
    }
    private DateTimeOffset Now => clock?.Invoke() ?? DateTimeOffset.UtcNow;
    private PassengerEndpoint? Base => destinations.Base(employer!) is { ApproximateCoordinates: {} c } b
        ? new(b.StableId, b.Name + " (" + b.AirportIdentifier.Value + ")", c.Latitude, c.Longitude) : null;
    public bool CanStart => Selected is { State: OffshoreMissionState.Accepted, Aircraft: null } m && selectedAirframe() == m.AirframeId && monitor.ReadyAtBase(m.Base, Now);
    public bool CanResume => Selected is { Active: true, Aircraft: not null, RecoveryRequired: true } m &&
        selectedAirframe() == m.AirframeId && monitor.Latest is {} latest && Matches(m, latest.Identity) &&
        (monitor.ReadyAtBase(m.Base, Now) || !m.RecoveryRequiresBase && m.LastPosition is {} last && WorldTracking.DistanceMeters(last.Latitude, last.Longitude, latest.Position.Latitude, latest.Position.Longitude) <= 1852);
    public bool CanComplete => Selected is { State: OffshoreMissionState.ReadyToComplete, RecoveryRequired: false } m &&
        selectedAirframe() == m.AirframeId && monitor.ReadyAtBase(m.Base, Now);
    private bool Matches(OffshoreMission m, ConditionAircraftIdentity identity) =>
        LiveIdentityGuard(identity) && CompanyAircraft.Resolve(new(identity.Title, identity.Model), bindings()) == "vision-jet" &&
        ContinuityGuard(new(identity.Title, identity.Model)) is null &&
        (m.Aircraft is null || m.Aircraft == new FlightAircraftIdentity(identity.Title, identity.Model));

    public async Task RefreshAsync(CancellationToken token = default)
    {
        if (!Enabled) { Reason = "This board is available to Skeleton Coast Petroleum employees."; return; }
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await new EmployedCargoJobsService(store, employer: employer, destinations: destinations, utcNow: () => Now).LoadAsync(token).ConfigureAwait(false);
            var airport = Base;
            if (airport is null) { Reason = "Resolve the employer base airport in destination diagnostics before generating missions."; return; }
            var area = operatingArea ?? OffshoreMissionDefinitions.Areas[0];
            area.Validate();
            world = (await store.ApplyAsync(w =>
            {
                var missions = w.OffshoreMissions.Select(m => m.State == OffshoreMissionState.Offered && m.ExpiresUtc <= Now
                    ? m with { State = OffshoreMissionState.Expired } : m).ToList();
                if (!initialized)
                    missions = missions.Select(m => m.Active && m.Aircraft is not null && m.State != OffshoreMissionState.ReadyToComplete
                        ? m with { RecoveryRequired = true, ObservationSeconds = m.ObservationCompletedUtc is null ? 0 : m.ObservationSeconds } : m).ToList();
                if (destinations.CanGenerateAtBase(employer!) && !missions.Any(m => m.Active || m.State == OffshoreMissionState.Offered))
                {
                    var id = OffshoreMissionKernel.OfferId(employer!.Id, w.RealTime!.NextOffersUtc.UtcTicks,CompanyOfferRefresh.Sequence(w,employer.Id));
                    if (!missions.Any(m => m.Id == id))
                        missions.Add(new(id, employer!.Id, EmployedCargoPrototype.PlayerId, "Offshore operations observation",
                            "Observe the known OL400 harbour operating area and its surroundings, then return to Walvis Bay. A gameplay patrol, not equipment or incident detection.",
                            airport, area, Now, Now.AddHours(w.RealTime.OfferLifetimeHours), employer.HourlyWage));
                }
                return missions.SequenceEqual(w.OffshoreMissions) ? new(w, false) :
                    new((w with { Revision = w.Revision + 1, OffshoreMissions = missions }).EnsureValid(), true);
            }, token).ConfigureAwait(false)).World;
            initialized = true;
            SelectedId = Active?.Id ?? SelectedId ?? Missions.FirstOrDefault()?.Id;
            Reason = monitor.Reason;
            WorldObserved?.Invoke(world);
        }
        finally { gate.Release(); }
    }
    public Task AcceptAsync(CancellationToken token = default) => Change(w =>
    {
        var m = w.OffshoreMissions.Single(m => m.Id == SelectedId);
        if (m.State != OffshoreMissionState.Offered) return new(w, false);
        if (m.ExpiresUtc <= Now || !destinations.CanGenerateAtBase(employer!) || selectedAirframe() is not {} id || SelectedCompanyType() != "vision-jet" || w.OffshoreMissions.Any(x => x.Active))
            throw new InvalidOperationException("Select the assigned Vision Jet airframe and an unexpired offer; complete the active observation mission first.");
        return OffshoreMissionKernel.Replace(w, m with { State = OffshoreMissionState.Accepted, AirframeId = id, AcceptedUtc = Now });
    }, token);
    public async Task StartOrResumeAsync(CancellationToken token = default)
    {
        using var lease = await AircraftPayloadOperationLease.AcquireAsync(token).ConfigureAwait(false);
        await LegacyRecoveryGuard(token).ConfigureAwait(false);
        await Change(w =>
    {
        var m = w.OffshoreMissions.Single(m => m.Id == SelectedId);
        var latest = monitor.Latest ?? throw new InvalidOperationException("Connect mission tracking first.");
        if (selectedAirframe() != m.AirframeId || !Matches(m, latest.Identity) || Now - latest.Position.ObservedUtc > TimeSpan.FromSeconds(2))
            throw new InvalidOperationException("Use the recorded selected employer airframe and confirmed actual Vision Jet identity with fresh tracking.");
        OffshoreMissionKernel.RequireTransportIdle(w);
        if (m.Aircraft is not null)
        {
            if (!m.RecoveryRequired || !CanResume) throw new InvalidOperationException("Resume at base or within one nautical mile of the last recorded position, in the same aircraft.");
            monitor.Reset();
            var recoveredFarFromLast = m.LastPosition is {} last && WorldTracking.DistanceMeters(last.Latitude, last.Longitude,
                latest.Position.Latitude, latest.Position.Longitude) > 1852;
            return OffshoreMissionKernel.Replace(w, m with { RecoveryRequired = false, RecoveryRequiresBase = false,
                ReturnAreaRequalificationRequired = m.ReturnAreaRequalificationRequired || m.ObservationCompletedUtc is not null && recoveredFarFromLast,
                LastPosition = latest.Position, LastSimulationSeconds = latest.SimulationSeconds });
        }
        if (!monitor.ReadyAtBase(m.Base, Now)) throw new InvalidOperationException("Stop at the assigned base for ten continuous tracking seconds before beginning.");
        var identity = new FlightAircraftIdentity(latest.Identity.Title, latest.Identity.Model);
        if (DispatchGuard(identity, m) is {} error) throw new InvalidOperationException(error);
        return OffshoreMissionKernel.Replace(w, m with { Aircraft = identity, State = OffshoreMissionState.Outbound,
            LastPosition = latest.Position, LastSimulationSeconds = latest.SimulationSeconds });
        }, token).ConfigureAwait(false);
    }
    public Task CompleteAsync(CancellationToken token = default) => Change(w =>
    {
        var m = w.OffshoreMissions.Single(m => m.Id == SelectedId);
        if (m.State == OffshoreMissionState.Completed) return new(w, false);
        if (!CanComplete || monitor.Latest is not {} latest || !Matches(m, latest.Identity))
            throw new InvalidOperationException("Qualified return, stopped live aircraft and original selected airframe are required.");
        return OffshoreMissionKernel.Complete(w, m.Id, Now);
    }, token);

    public async Task ObserveAsync(ConditionFrame? frame, CancellationToken token = default)
    {
        if (!Enabled || !initialized || Base is not {} airport) return;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            world = (await store.ApplyAsync(w =>
            {
                var active = w.OffshoreMissions.SingleOrDefault(m => m.Active);
                if (active?.Aircraft is not null && frame is not null &&
                    (selectedAirframe() != active.AirframeId || !Matches(active, frame.Aircraft)))
                {
                    monitor.Reset(); Reason = "Mission paused: selected physical airframe or actual simulator identity changed.";
                    return active.RecoveryRequired || active.ReturnReadyUtc is not null ? new(w, false) : OffshoreMissionKernel.Replace(w, active with { RecoveryRequired = true });
                }
                var result = monitor.Observe(active, airport, frame, Now);
                Reason = monitor.Reason;
                if (active is null || result == active) return new(w, false);
                return OffshoreMissionKernel.Replace(w, result!);
            }, token).ConfigureAwait(false)).World;
        }
        finally { gate.Release(); }
    }
    public async Task DisconnectAsync()
    {
        monitor.Reset();
        if (!Enabled || !initialized) return;
        await Change(w => w.OffshoreMissions.SingleOrDefault(m => m.Active && m.Aircraft is not null && m.ReturnReadyUtc is null) is {} active
            ? OffshoreMissionKernel.Replace(w, active with { RecoveryRequired = true,
                ObservationSeconds = active.ObservationCompletedUtc is null ? 0 : active.ObservationSeconds }) : new(w, false), CancellationToken.None).ConfigureAwait(false);
    }
    private async Task Change(Func<EconomyWorld, EconomyTransition> action, CancellationToken token)
    {
        if (!Enabled) throw new InvalidOperationException("No Offshore employment.");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { world = (await store.ApplyAsync(action, token).ConfigureAwait(false)).World; WorldObserved?.Invoke(world); }
        finally { gate.Release(); }
    }
}
