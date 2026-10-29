using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed record CargoJobDisplay(string JobId, string Employer, string Origin, string Destination,
    string CustomerPurpose, string CargoCategory, decimal CargoMassKilograms, decimal PaymentAmount,
    string CurrencyCode, string KnownDestinationInformation, string? ShipmentId = null, ShipmentState? ShipmentState = null,
    string? OriginLocationId = null, string? DestinationLocationId = null, double? OriginLatitude = null,
    double? OriginLongitude = null, double? DestinationLatitude = null, double? DestinationLongitude = null,
    decimal LoadedCargoKilograms = 0, decimal DeliveredCargoKilograms = 0)
{
    public string? CompanyAircraftTypeId {get;init;}
    public decimal? HourlyWage {get;init;}
    public string? WageHistory {get;init;}
    public DateTimeOffset? ExpiresUtc { get; init; }
    public string Availability => ExpiresUtc is {} time ? $"Expires {time.ToLocalTime():dd MMM HH:mm}" : "No automatic expiry";
    public string Cargo => $"{CargoCategory} · {CargoMassKilograms:0.#} kg"+(CompanyAircraftTypeId is {} id?$" · Company: {id}":"");
    public string Payment => WageHistory??(HourlyWage is {} rate?DollarDisplay.Format(rate)+" / flight hour":DollarDisplay.Format(PaymentAmount));
    public string Route => $"{Origin} → {Destination}";
    public string Shipment => ShipmentState is null ? "Not accepted" : $"{ShipmentState} · {ShipmentId}";
}

public sealed record EmployedCargoJobsSnapshot(IReadOnlyList<CargoJobDisplay> Offers, CargoJobDisplay? ActiveJob,
    IReadOnlyList<CargoJobDisplay> ReservedJobs, IReadOnlyList<CargoJobDisplay> RecentCompletedJobs,
    string StoragePath, int EconomyRevision, decimal PlayerBalance, string CurrencyCode)
{ public EconomyRealTimeState? RealTime { get; init; } }
public sealed record FlightDiagnosticEventDisplay(string Utc, string Event, string Details);

/// <summary>The employee-facing boundary deliberately exposes selection and acceptance only. Delivery requires a later evidence provider.</summary>
public interface IEmployedCargoJobs
{
    string StoragePath { get; }
    Task<EmployedCargoJobsSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    Task<EmployedCargoJobsSnapshot> AcceptAsync(string jobId, CancellationToken cancellationToken = default);
}

public sealed class EmployedCargoJobsService : IEmployedCargoJobs
{
    private readonly IEconomyStateStore store;
    public CareerEmployer? Employer {get;}
    private readonly EmployerDestinationCatalogue? destinations;
    public string? SelectedAircraftTypeId {get;set;}
    public IReadOnlyList<CompanyAircraftType> AircraftChoices(CargoJobDisplay? job)
    {
        if(Employer is null||job is null||job.Employer!=Employer.Name)return [];
        var destination=destinations!.Diagnose(Employer.Category).SingleOrDefault(x=>x.Resolved?.StableId==job.DestinationLocationId&&x.Enabled);
        return destination is null?[]:CompanyAircraft.Choices(Employer,destination,job.CargoMassKilograms);
    }
    public string EmploymentStatus=>Employer is null?"Legacy / freelance board":$"{Employer.Name} · {Employer.WageDisplay} · paid for verified airborne time on successful delivery. {destinations!.Diagnose(Employer.Category).Count(x=>x.Enabled)} eligible destinations. "+
        (Employer.Category=="Offshore"?"Cargo board; specialist transfers are on Passenger transfers.":"Cargo only; passenger transfers deferred.");
    private readonly EconomyService economy;
    private readonly NamibiaLocationCatalogueDocument catalogue;
    private readonly EmployedCargoPrototypeOptions options;
    private readonly Func<DateTimeOffset> utcNow;
    public string StoragePath => economy.StoragePath;

    public EmployedCargoJobsService(IEconomyStateStore store, NamibiaLocationCatalogueDocument? catalogue = null,
        EmployedCargoPrototypeOptions? options = null, Func<DateTimeOffset>? utcNow = null,
        CareerEmployer? employer = null, EmployerDestinationCatalogue? destinations = null)
    {
        this.store=store;Employer=employer;this.destinations=destinations??(employer is null?null:new());
        economy = new(store); this.catalogue = (catalogue ?? NamibiaLocationCatalogue.Load()).EnsureValid();
        this.options = (options ?? new()).EnsureValid(); this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public static EmployedCargoJobsService CreateLocal() => new(new LocalEconomyStateStore());
    public static EmployedCargoJobsService CreateMemory() => new(new MemoryEconomyStateStore());

    public async Task<EmployedCargoJobsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var world = await economy.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (world is null)
        {
            await economy.InitializeAsync(Employer is null?EmployedCargoPrototype.Create(catalogue, options):CompanyEmployment.Create(Employer,destinations!,utcNow()), cancellationToken).ConfigureAwait(false);
            world = await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ?? throw new IOException("Economy initialization produced no state.");
        }
        if(Employer is not null)
        {
            await store.ApplyAsync(w=>CompanyEmployment.Configure(w,Employer,destinations!,utcNow()),cancellationToken).ConfigureAwait(false);
        }
        if (Employer is null && world.RecurringDemand.Count == 0)
        {
            _ = await economy.UpgradeRepeatableJobsAsync(options, utcNow(), cancellationToken).ConfigureAwait(false);
            world = await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ?? throw new IOException("Economy upgrade produced no state.");
        }
        _ = await economy.AdvanceRealTimeAsync(utcNow(), cancellationToken).ConfigureAwait(false);
        world = await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ?? throw new IOException("Economy board maintenance produced no state.");
        return Present(RequirePrototype(world));
    }

    public async Task<EmployedCargoJobsSnapshot> AcceptAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var current = RequirePrototype(await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ??
            throw new IOException("Economy state has not been initialized."));
        var selectedType=SelectedAircraftTypeId;
        if(Employer is null) _ = await economy.AcceptJobAsync(jobId, utcNow(), cancellationToken).ConfigureAwait(false);
        else await store.ApplyAsync(w=>
        {
            var job=w.Jobs.Single(x=>x.Id==jobId);
            if(job.State==EconomyJobState.Accepted)return new(w,false);
            if(job.EmployerBusinessId==Employer.Id)
            {
                var display=Present(w).Offers.SingleOrDefault(x=>x.JobId==jobId)??throw new InvalidOperationException("Destination is not enabled for this employer.");
                if(!AircraftChoices(display).Any(x=>x.Id==selectedType))throw new InvalidOperationException("Choose an appropriate company aircraft before accepting this job.");
            }
            var result=EconomyKernel.AcceptJob(w,jobId,utcNow());
            return result with {World=(result.World with {Jobs=result.World.Jobs.Select(x=>x.Id==jobId&&job.EmployerBusinessId==Employer.Id?x with {CompanyAircraftTypeId=selectedType}:x).ToArray()}).EnsureValid()};
        },cancellationToken).ConfigureAwait(false);
        return Present(RequirePrototype(await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ??
            throw new IOException("Accepted economy state could not be reloaded.")));
    }

    private static EconomyWorld RequirePrototype(EconomyWorld world)
    {
        world.EnsureValid();
        if (world.Id != EmployedCargoPrototype.WorldId)
            throw new IOException("Existing economy state belongs to another or unsupported economy world. File preserved.");
        return world;
    }

    private EmployedCargoJobsSnapshot Present(EconomyWorld world)
    {
        CargoJobDisplay Display(EconomyJob job)
        {
            var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
            var origin = Place(requirement.OriginLocationId); var destination = Place(requirement.DestinationLocationId);
            var employer = world.Businesses.Single(x => x.Id == job.EmployerBusinessId);
            var resource = world.Resources.Single(x => x.Id == requirement.ResourceId);
            var shipment = world.Shipments.SingleOrDefault(x => x.JobId == job.Id);
            var minutes = Math.Max(1, (int)Math.Round(Geodesy.Inverse(new(origin.Latitude, origin.Longitude),
                new(destination.Latitude, destination.Longitude)).DistanceMeters / 1852 / 120 * 60));
            var knownDestination = destination.Known + $" · about {minutes} min direct from {origin.Display} at 120 kt (planning estimate)";
            return new(job.Id, employer.Name, origin.Display, destination.Display,
                requirement.Purpose ?? destination.Purpose, resource.Name, requirement.Quantity,
                requirement.PaymentAmount, requirement.CurrencyCode, knownDestination, shipment?.Id, shipment?.State,
                requirement.OriginLocationId, requirement.DestinationLocationId, origin.Latitude, origin.Longitude,
                destination.Latitude, destination.Longitude, shipment?.LoadedQuantity ?? 0, shipment?.DeliveredQuantity ?? 0) {
                    ExpiresUtc = job.ExpiresUtc,CompanyAircraftTypeId=job.CompanyAircraftTypeId,
                    HourlyWage=job.State==EconomyJobState.Completed||world.LogisticsContracts.Any(x=>x.JobId==job.Id)?null:employer.PilotHourlyRate,
                    WageHistory=job.CompletedWage is {} wage?WageHistory(world,wage):null };
        }

        var operationNeedIds = world.Operations.SelectMany(x => new[] { ProspectingCampKernel.InboundNeed(x.Id), ProspectingCampKernel.OutboundNeed(x.Id) }).ToHashSet(StringComparer.Ordinal);
        var offers = world.Jobs.Where(x => x.State == EconomyJobState.Offered)
            .Where(x => { var requirement = world.Requirements.Single(r => r.Id == x.RequirementId);
                if (world.LogisticsContracts.Any(c => c.JobId == x.Id && c.State == OperationLogisticsContractState.Published)) return true;
                if (operationNeedIds.Contains(requirement.NeedId)) return true;
                if(Employer is not null)return x.EmployerBusinessId==Employer.Id&&world.RecurringDemand.Any(d=>d.NeedId==requirement.NeedId&&d.IsEnabled)&&destinations!.Diagnose(Employer.Category).Any(d=>d.Enabled&&d.Resolved?.StableId==requirement.DestinationLocationId);
                return catalogue.Locations.SingleOrDefault(c => c.StableId == requirement.DestinationLocationId)?.CanGenerateOrdinaryLiveJobs == true; })
            .OrderBy(x => x.Id, StringComparer.Ordinal).Select(Display).ToArray();
        var reserved = world.Jobs.Where(x => x.State == EconomyJobState.Accepted).Select(Display)
            .OrderByDescending(x => x.ShipmentState is ShipmentState.Loaded or ShipmentState.InTransit)
            .ThenBy(x => x.JobId, StringComparer.Ordinal).ToArray();
        var currentJob = reserved.FirstOrDefault(x => x.ShipmentState is ShipmentState.Loaded or ShipmentState.InTransit);
        var completed = world.Jobs.Where(x => x.State == EconomyJobState.Completed).OrderByDescending(x => x.CompletedUtc)
            .ThenBy(x => x.Id, StringComparer.Ordinal).Take(12).Select(Display).ToArray();
        var player = world.Players.Single(x => x.Id == EmployedCargoPrototype.PlayerId);
        return new(offers, currentJob, reserved, completed, StoragePath, world.Revision, player.CachedBalance, player.CurrencyCode) { RealTime = world.RealTime };

        (string Display, string Purpose, string Known, double Latitude, double Longitude) Place(string id)
        {
            var authored = destinations?.Find(id) ?? catalogue.Locations.SingleOrDefault(x => x.StableId == id);
            if (authored is not null)
            {
                var identifier = authored.AirportIdentifier.Value ?? "identifier unconfirmed";
                var coordinates = authored.ApproximateCoordinates is { } point
                    ? $"published position {point.Latitude.ToString("F6", CultureInfo.InvariantCulture)}, {point.Longitude.ToString("F6", CultureInfo.InvariantCulture)}"
                    : "published position unavailable";
                var cataloguePoint = authored.ApproximateCoordinates ?? throw new InvalidDataException("Job location has no configured coordinates.");
                return ($"{authored.Name} ({identifier})", authored.EconomicRole,
                    $"{identifier} · {coordinates} · {authored.VerificationEvidence?.Method ?? "MSFS verification unavailable"}", cataloguePoint.Latitude, cataloguePoint.Longitude);
            }
            var site = world.Sites.SingleOrDefault(x => x.LocationId == id) ?? throw new InvalidDataException("Job refers to an unknown authored or player-created location.");
            return ($"{site.Name} ({site.AirportIdentifier})", "Player-owned economic operation",
                string.Create(CultureInfo.InvariantCulture, $"{site.AirportIdentifier} · player-created airport anchor {site.Latitude:F6}, {site.Longitude:F6}"), site.Latitude, site.Longitude);
        }
    }
    private static string WageHistory(EconomyWorld world,PilotWage wage)
    {
        var payment=world.FinancialLedger.Single(x=>x.Kind==FinancialTransactionKind.PilotWage&&x.Wage?.FlightId==wage.FlightId);
        return $"{wage.Summary}\nPaid once per flight · {payment.OccurredUtc:u}\nFlight: {wage.FlightId}\nPayment job: {payment.JobId}";
    }
}

public sealed class MemoryEconomyStateStore : IEconomyStateStore
{
    private EconomyWorld? world;
    public string StoragePath => "memory://employed-cargo-v0";
    public Task<EconomyWorld?> LoadAsync(CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(world); }
    public Task InitializeAsync(EconomyWorld initial, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); if (world is not null) throw new IOException("Economy state already exists.");
        world = initial.EnsureValid(); return Task.CompletedTask;
    }
    public Task<EconomyTransition> ApplyAsync(Func<EconomyWorld, EconomyTransition> mutation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); if (world is null) throw new IOException("Economy state has not been initialized.");
        var previous = world; var result = mutation(previous); result.World.EnsureValid();
        if (!result.Changed && result.World != previous) throw new InvalidDataException("An unchanged economy transaction returned different state.");
        if (result.Changed && (result.World.Id != previous.Id || result.World.Revision != previous.Revision + 1))
            throw new InvalidDataException("Economy transaction revision is inconsistent.");
        if (result.Changed) world = result.World; return Task.FromResult(result);
    }
}

public sealed class EmployedCargoJobsViewModel : INotifyPropertyChanged
{
    private readonly IEmployedCargoJobs jobs;
    private readonly IFlightAttemptTracker flight;
    private readonly SynchronizationContext? context;
    private bool busy;
    private string status = "Loading employed cargo jobs…", error = "";
    private IReadOnlyList<CargoJobDisplay> offers = [];
    private IReadOnlyList<CargoJobDisplay> reservedJobs = [], completedJobs = [], loadingJobs = [];
    private CargoJobDisplay? selectedOffer, activeJob;
    private decimal playerBalance;
    private string balanceCurrency = EmployedCargoPrototype.Currency;
    private EconomyRealTimeState? realTime;
    public string BoardTiming => realTime is {} clock ? $"SYSTEM offers: every {clock.OfferIntervalHours}h · expire after {clock.OfferLifetimeHours}h · next update {clock.NextOffersUtc.ToLocalTime():dd MMM HH:mm}. Accepted jobs stay reserved." : "Loading real-time job schedule…";
    public event PropertyChangedEventHandler? PropertyChanged;
    public ActionCommand RefreshCommand { get; }
    public ActionCommand AcceptCommand { get; }
    public ActionCommand LoadCargoCommand { get; }
    public ActionCommand UnloadCargoCommand { get; }

    public EmployedCargoJobsViewModel(IEmployedCargoJobs jobs, IFlightAttemptTracker flight)
    {
        this.jobs = jobs; this.flight = flight; context = SynchronizationContext.Current; flight.Changed += FlightChanged;
        RefreshCommand = new(() => _ = InitializeAsync(), () => !busy);
        AcceptCommand = new(() => _ = AcceptSelectedAsync(), () => !busy && selectedOffer is not null);
        LoadCargoCommand = new(() => _ = LoadCargoAsync(), () => !busy && flight.Current.CanLoadCargo);
        UnloadCargoCommand = new(() => _ = UnloadCargoAsync(), () => !busy && flight.Current.CanUnloadCargo);
    }
    public IReadOnlyList<CargoJobDisplay> Offers { get => offers; private set { offers = value; Changed(); } }
    public string EmploymentStatus=>(jobs as EmployedCargoJobsService)?.EmploymentStatus??"";
    public bool ShowOfferCompensation=>Offers.Any(x=>x.HourlyWage is null);
    public bool ShowReservedCompensation=>ReservedJobs.Any(x=>x.HourlyWage is null);
    public IReadOnlyList<CompanyAircraftType> CompanyAircraftChoices=>(jobs as EmployedCargoJobsService)?.AircraftChoices(SelectedOffer)??[];
    public CompanyAircraftType? SelectedCompanyAircraft
    {
        get=>CompanyAircraftChoices.FirstOrDefault(x=>x.Id==(jobs as EmployedCargoJobsService)?.SelectedAircraftTypeId);
        set {if(jobs is EmployedCargoJobsService service)service.SelectedAircraftTypeId=value?.Id;Changed();}
    }
    public IReadOnlyList<CargoJobDisplay> ReservedJobs { get => reservedJobs; private set { reservedJobs = value; Changed(); Changed(nameof(ReservedSummary)); } }
    public IReadOnlyList<CargoJobDisplay> LoadingJobs { get => loadingJobs; private set { loadingJobs = value; Changed(); Changed(nameof(LoadingSelectionSummary)); } }
    public IReadOnlyList<CargoJobDisplay> CompletedJobs { get => completedJobs; private set { completedJobs = value; Changed(); } }
    public CargoJobDisplay? SelectedOffer { get => selectedOffer; set { selectedOffer = value; Changed(); Changed(nameof(SelectedPurpose)); Changed(nameof(SelectedDestinationInformation)); Changed(nameof(CompanyAircraftChoices));Changed(nameof(SelectedCompanyAircraft)); AcceptCommand.Refresh(); } }
    public CargoJobDisplay? ActiveJob { get => activeJob; private set { activeJob = value; Changed(); Changed(nameof(ActiveRoute)); Changed(nameof(ActiveCargo)); Changed(nameof(ActiveShipment)); AcceptCommand.Refresh(); } }
    public bool HasActiveJob => ActiveJob?.ShipmentState is ShipmentState.Reserved or ShipmentState.Loaded or ShipmentState.InTransit;
    public string ReservedSummary => ReservedJobs.Count == 0 ? "No accepted jobs are reserved." : $"{ReservedJobs.Count} accepted job{(ReservedJobs.Count == 1 ? "" : "s")} reserved.";
    public string LoadingSelectionSummary => LoadingJobs.Count == 0 ? "Select one or more reserved jobs with the same route." :
        $"Preparing {LoadingJobs.Count} job{(LoadingJobs.Count == 1 ? "" : "s")} · {LoadingJobs.Sum(x => x.CargoMassKilograms - x.DeliveredCargoKilograms):0.##} kg · {LoadingJobs[0].Route}";
    public bool IsBusy { get => busy; private set { busy = value; Changed(); RefreshCommands(); } }
    public string Status { get => status; private set { status = value; Changed(); } }
    public string Error { get => error; private set { error = value; Changed(); } }
    public string StoragePath => jobs.StoragePath;
    public string SelectedPurpose => SelectedOffer?.CustomerPurpose ?? "Select an offer to inspect its customer purpose.";
    public string SelectedDestinationInformation => SelectedOffer?.KnownDestinationInformation ?? "No offer selected.";
    public string ActiveRoute => ActiveJob?.Route ?? "No active shipment";
    public string ActiveCargo => ActiveJob is null ? "—" : $"{ActiveJob.Cargo} · payment on externally verified delivery: {ActiveJob.Payment}";
    public string ActiveShipment => ActiveJob is { } job && flight.Current.Attempt is { } attempt && attempt.JobId == job.JobId
        ? $"{(attempt.TransitBeginsAtDeparture && attempt.OriginDepartedUtc is null ? "Loaded" : "InTransit")} · {job.ShipmentId}"
        : ActiveJob?.Shipment ?? "—";
    public string FlightStage => flight.Current.Stage;
    public string FlightReason => flight.Current.Reason;
    public string VerifiedAirborneTime => "Verified airborne time: "+TimeSpan.FromTicks(flight.Current.Attempt?.VerifiedAirborneTicks??0).ToString(@"hh\:mm\:ss");
    public string OriginDistance => flight.Current.OriginDistanceNauticalMiles is { } value ? $"Origin distance: {value:F2} NM" : "Origin distance: —";
    public string DestinationDistance => flight.Current.DestinationDistanceNauticalMiles is { } value ? $"Destination distance: {value:F2} NM" : "Destination distance: —";
    public string Balance => $"Balance: {DollarDisplay.Format(playerBalance)}";
    public string FlightStoragePath => flight.StoragePath;
    private FlightAttemptDiagnosticSnapshot FlightDiagnostic => flight.Current.Diagnostic;
    public string DiagnosticConnection => FlightDiagnostic.ConnectionState;
    public string DiagnosticFreshness => FlightDiagnostic.TelemetryFreshness;
    public string DiagnosticLastSample => FlightDiagnostic.LastValidSampleUtc?.ToString("O") ?? "—";
    public string DiagnosticRoute => $"{FlightDiagnostic.Origin} → {FlightDiagnostic.Destination}";
    public string DiagnosticIds => $"Job {FlightDiagnostic.JobId} · Shipment {FlightDiagnostic.ShipmentId}";
    public string DiagnosticRecordedAircraft => FlightDiagnostic.RecordedAircraftIdentity;
    public string DiagnosticObservedAircraft => FlightDiagnostic.CurrentAircraftIdentity;
    public string DiagnosticLiveValues => $"On ground: {FlightDiagnostic.OnGround} · Ground speed: {FlightDiagnostic.GroundSpeed}";
    public string DiagnosticDistances => $"Origin: {FlightDiagnostic.OriginDistance} · Destination: {FlightDiagnostic.DestinationDistance}";
    public string DiagnosticPersistedEvidence => $"Airborne: {FlightDiagnostic.AirborneEvidenceRecorded} · Movement: {FlightDiagnostic.MovementEvidenceRecorded} · Maximum from origin: {FlightDiagnostic.MaximumOriginDistance}";
    public string DiagnosticQualification => $"Current: {FlightDiagnostic.QualificationDuration} · Required: {FlightDiagnostic.RequiredQualificationDuration}";
    public string DiagnosticUnavailableReason => FlightDiagnostic.UnavailableReason;
    public IReadOnlyList<FlightDiagnosticEventDisplay> DiagnosticTimeline => FlightDiagnostic.Timeline.Select(x => new FlightDiagnosticEventDisplay(
        x.OccurredUtc.ToString("O"), x.Type.ToString(), string.Join(" · ", x.Details.Select(pair => pair.Key + "=" + pair.Value)))).ToArray();
    public bool CanResetIncompleteFlight => flight.Current.Attempt is { State: not FlightAttemptState.Delivered } && HasActiveJob;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RunAsync(() => jobs.LoadAsync(cancellationToken), "Choose one employed cargo offer.");
        if (Error == "") try { await flight.InitializeAsync(cancellationToken); } catch (Exception error) { Error = error.Message; Status = "Flight-attempt data could not be loaded. Existing files were preserved."; }
    }
    public Task RefreshLiveAsync(CancellationToken cancellationToken = default) => RunAsync(() => jobs.LoadAsync(cancellationToken), "SYSTEM board follows real time: new offers every 3 hours; unaccepted offers expire after 6 hours.");
    public async Task AcceptSelectedAsync(CancellationToken cancellationToken = default)
    {
        var selected = SelectedOffer; if (selected is null) return;
        await RunAsync(() => jobs.AcceptAsync(selected.JobId, cancellationToken), $"Shipment reserved. It is now available on Reserved jobs.");
        if (Error == "") try { await flight.RefreshEconomyAsync(cancellationToken); } catch (Exception error) { ReportFlightError(error); }
    }
    public async Task PrepareReservedJobsAsync(IReadOnlyList<CargoJobDisplay> selected, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        Error = "";
        try
        {
            var jobs = selected.DistinctBy(x => x.JobId).ToArray();
            if (jobs.Length == 0) throw new InvalidOperationException("Select at least one reserved job to prepare.");
            if (jobs.Any(x => x.ShipmentState is not (ShipmentState.Reserved or ShipmentState.Loaded)))
                throw new InvalidOperationException("Only reserved or pre-departure loaded jobs can be prepared.");
            if (jobs.Select(x => (x.OriginLocationId, x.DestinationLocationId)).Distinct().Count() != 1)
                throw new InvalidOperationException("Jobs loaded on the same flight must have the same origin and destination.");
            LoadingJobs = jobs;
            await flight.SelectJobsAsync(jobs.Select(x => x.JobId).ToArray(), cancellationToken);
            Status = LoadingSelectionSummary;
        }
        catch (Exception error) { ReportFlightError(error); }
    }
    public async Task RecoverManifestJobsAsync(CargoLoadManifest manifest, CancellationToken cancellationToken = default)
    {
        var snapshot = await jobs.LoadAsync(cancellationToken);
        var identities = manifest.Shipments.Count > 0
            ? manifest.Shipments.Select(x => (x.JobId, x.ShipmentId)).ToArray()
            : [(manifest.JobId, manifest.ShipmentId)];
        var restored = identities.Select(id => snapshot.ReservedJobs.SingleOrDefault(x =>
            x.JobId == id.JobId && x.ShipmentId == id.ShipmentId)
            ?? throw new InvalidOperationException("A shipment in the pending cargo manifest is no longer accepted. The saved intent was preserved; no simulator write was made.")).ToArray();
        await flight.RefreshEconomyAsync(cancellationToken);
        await flight.SelectJobsAsync(restored.Select(x => x.JobId).ToArray(), cancellationToken);
        await ApplySnapshotAsync(snapshot);
        LoadingJobs = restored;
    }
    public async Task ObserveTelemetryAsync(AircraftTelemetry? telemetry, CancellationToken cancellationToken = default)
    { try { await flight.ObserveAsync(telemetry, cancellationToken); } catch (Exception error) { ReportFlightError(error); } }
    public async Task LoadCargoAsync(CancellationToken cancellationToken = default)
    { await RunFlightAsync(() => flight.LoadCargoAsync(cancellationToken), cancellationToken); }
    public async Task UnloadCargoAsync(CancellationToken cancellationToken = default)
    { await RunFlightAsync(() => flight.UnloadCargoAsync(cancellationToken), cancellationToken); }
    public Task<string> ExportFlightDiagnosticAsync(CancellationToken cancellationToken = default) => flight.ExportDeveloperDiagnosticAsync(cancellationToken);
    public Task<string> CopyFlightDiagnosticSummaryAsync(CancellationToken cancellationToken = default) => flight.CreateDiagnosticSummaryAsync(cancellationToken);
    public async Task ResetIncompleteFlightAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || !CanResetIncompleteFlight) return;
        IsBusy = true; Error = "";
        try { await flight.ResetIncompletePrototypeProgressAsync(cancellationToken); Status = "Incomplete flight observations reset. Cargo remains loaded and InTransit."; }
        catch (Exception error) { ReportFlightError(error); }
        finally { IsBusy = false; Changed(nameof(CanResetIncompleteFlight)); }
    }
    private async Task RunFlightAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        if (IsBusy) return; IsBusy = true; Error = "";
        try
        {
            await action(); await ApplySnapshotAsync(await jobs.LoadAsync(cancellationToken));
            Status = flight.Current.Stage == "Delivered and paid" ? "Delivery completed and payment recorded. A replacement offer is available." :
                ReservedJobs.Count > 0 ? "Accepted jobs are reserved. Choose them on Reserved jobs when you are ready to load." : "Choose an employed cargo offer.";
        }
        catch (Exception error) { ReportFlightError(error); }
        finally { IsBusy = false; }
    }
    private async Task RunAsync(Func<Task<EmployedCargoJobsSnapshot>> action, string success)
    {
        if (IsBusy) return; IsBusy = true; Error = "";
        try
        {
            var snapshot = await action(); await ApplySnapshotAsync(snapshot);
            Status = ReservedJobs.Count > 0 ? "Accepted jobs are reserved. You may accept more or prepare compatible jobs for loading." : success;
            Changed(nameof(HasActiveJob)); Changed(nameof(ActiveRoute)); Changed(nameof(ActiveCargo)); Changed(nameof(ActiveShipment));
        }
        catch (Exception exception) { Error = exception.Message; Status = "Jobs could not be loaded or changed. Existing economy data was preserved."; }
        finally { IsBusy = false; }
    }
    private Task ApplySnapshotAsync(EmployedCargoJobsSnapshot snapshot)
    {
        realTime = snapshot.RealTime; Changed(nameof(BoardTiming));
        Offers = snapshot.Offers; ActiveJob = snapshot.ActiveJob; ReservedJobs = snapshot.ReservedJobs; CompletedJobs = snapshot.RecentCompletedJobs;
        Changed(nameof(ShowOfferCompensation));Changed(nameof(ShowReservedCompensation));Changed(nameof(EmploymentStatus));
        if (LoadingJobs.Count > 0) LoadingJobs = LoadingJobs.Select(x => ReservedJobs.SingleOrDefault(r => r.JobId == x.JobId)).Where(x => x is not null).Cast<CargoJobDisplay>().ToArray();
        if (LoadingJobs.Count == 0)
        {
            var aboard = ReservedJobs.Where(x => x.ShipmentState is ShipmentState.Loaded or ShipmentState.InTransit).ToArray();
            if (aboard.Length > 0)
                LoadingJobs = aboard.Where(x => x.OriginLocationId == aboard[0].OriginLocationId && x.DestinationLocationId == aboard[0].DestinationLocationId).ToArray();
        }
        playerBalance = snapshot.PlayerBalance; balanceCurrency = snapshot.CurrencyCode; Changed(nameof(Balance));
        if (SelectedOffer is not null) SelectedOffer = Offers.SingleOrDefault(x => x.JobId == SelectedOffer.JobId);
        return Task.CompletedTask;
    }
    private void FlightChanged(FlightAttemptStatus value)
    {
        void Notify()
        {
            Changed(nameof(ActiveShipment));
            Changed(nameof(FlightStage)); Changed(nameof(FlightReason)); Changed(nameof(VerifiedAirborneTime)); Changed(nameof(OriginDistance)); Changed(nameof(DestinationDistance)); Changed(nameof(CanResetIncompleteFlight));
            Changed(nameof(DiagnosticConnection)); Changed(nameof(DiagnosticFreshness)); Changed(nameof(DiagnosticLastSample)); Changed(nameof(DiagnosticRoute)); Changed(nameof(DiagnosticIds));
            Changed(nameof(DiagnosticRecordedAircraft)); Changed(nameof(DiagnosticObservedAircraft)); Changed(nameof(DiagnosticLiveValues)); Changed(nameof(DiagnosticDistances));
            Changed(nameof(DiagnosticPersistedEvidence)); Changed(nameof(DiagnosticQualification)); Changed(nameof(DiagnosticUnavailableReason)); Changed(nameof(DiagnosticTimeline)); RefreshCommands();
        }
        if (context is not null && !ReferenceEquals(context, SynchronizationContext.Current)) context.Post(_ => Notify(), null); else Notify();
    }
    private void ReportFlightError(Exception error)
    {
        void Report() { Error = error.Message; Status = "Flight action was refused; persisted cargo and payment state were unchanged."; }
        if (context is not null && !ReferenceEquals(context, SynchronizationContext.Current)) context.Post(_ => Report(), null); else Report();
    }
    private void RefreshCommands() { RefreshCommand.Refresh(); AcceptCommand.Refresh(); LoadCargoCommand.Refresh(); UnloadCargoCommand.Refresh(); }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
