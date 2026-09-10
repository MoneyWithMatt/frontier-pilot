using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed record CargoJobDisplay(string JobId, string Employer, string Origin, string Destination,
    string CustomerPurpose, string CargoCategory, decimal CargoMassKilograms, decimal PaymentAmount,
    string CurrencyCode, string KnownDestinationInformation, string? ShipmentId = null, ShipmentState? ShipmentState = null)
{
    public string Cargo => $"{CargoCategory} · {CargoMassKilograms:0.#} kg";
    public string Payment => $"{PaymentAmount:0.##} {CurrencyCode}";
    public string Route => $"{Origin} → {Destination}";
    public string Shipment => ShipmentState is null ? "Not accepted" : $"{ShipmentState} · {ShipmentId}";
}

public sealed record EmployedCargoJobsSnapshot(IReadOnlyList<CargoJobDisplay> Offers, CargoJobDisplay? ActiveJob,
    string StoragePath, int EconomyRevision, decimal PlayerBalance, string CurrencyCode);

/// <summary>The employee-facing boundary deliberately exposes selection and acceptance only. Delivery requires a later evidence provider.</summary>
public interface IEmployedCargoJobs
{
    string StoragePath { get; }
    Task<EmployedCargoJobsSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    Task<EmployedCargoJobsSnapshot> AcceptAsync(string jobId, CancellationToken cancellationToken = default);
}

public sealed class EmployedCargoJobsService : IEmployedCargoJobs
{
    private readonly EconomyService economy;
    private readonly NamibiaLocationCatalogueDocument catalogue;
    private readonly EmployedCargoPrototypeOptions options;
    private readonly Func<DateTimeOffset> utcNow;
    public string StoragePath => economy.StoragePath;

    public EmployedCargoJobsService(IEconomyStateStore store, NamibiaLocationCatalogueDocument? catalogue = null,
        EmployedCargoPrototypeOptions? options = null, Func<DateTimeOffset>? utcNow = null)
    {
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
            await economy.InitializeAsync(EmployedCargoPrototype.Create(catalogue, options), cancellationToken).ConfigureAwait(false);
            world = await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ?? throw new IOException("Economy initialization produced no state.");
        }
        return Present(RequirePrototype(world));
    }

    public async Task<EmployedCargoJobsSnapshot> AcceptAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var current = RequirePrototype(await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ??
            throw new IOException("Economy state has not been initialized."));
        var active = current.Jobs.SingleOrDefault(x => x.State == EconomyJobState.Accepted);
        if (active is not null && active.Id != jobId)
            throw new InvalidOperationException("Finish the active shipment before accepting another employed-cargo job.");
        _ = await economy.AcceptJobAsync(jobId, utcNow(), cancellationToken).ConfigureAwait(false);
        return Present(RequirePrototype(await economy.LoadAsync(cancellationToken).ConfigureAwait(false) ??
            throw new IOException("Accepted economy state could not be reloaded.")));
    }

    private static EconomyWorld RequirePrototype(EconomyWorld world)
    {
        world.EnsureValid();
        if (world.Id != EmployedCargoPrototype.WorldId)
            throw new IOException("Existing economy state belongs to another or unsupported economy world. File preserved.");
        if (world.Jobs.Count(x => x.State == EconomyJobState.Accepted) > 1)
            throw new IOException("Economy state contains multiple active employed shipments. File preserved.");
        return world;
    }

    private EmployedCargoJobsSnapshot Present(EconomyWorld world)
    {
        CargoJobDisplay Display(EconomyJob job)
        {
            var requirement = world.Requirements.Single(x => x.Id == job.RequirementId);
            var origin = catalogue.Get(requirement.OriginLocationId); var destination = catalogue.Get(requirement.DestinationLocationId);
            var employer = world.Businesses.Single(x => x.Id == job.EmployerBusinessId);
            var resource = world.Resources.Single(x => x.Id == requirement.ResourceId);
            var identifier = destination.AirportIdentifier.Value ?? "identifier unconfirmed";
            var coordinates = destination.ApproximateCoordinates is { } point
                ? $"published position {point.Latitude.ToString("F6", CultureInfo.InvariantCulture)}, {point.Longitude.ToString("F6", CultureInfo.InvariantCulture)}"
                : "published position unavailable";
            var timing = destination.ApproximateFlightMinutesFromErosAt120Knots is { } minutes ? $" · about {minutes} min direct at 120 kt" : "";
            var known = $"{identifier} · {coordinates}{timing} · {destination.VerificationEvidence?.Method ?? "MSFS verification unavailable"}";
            var shipment = world.Shipments.SingleOrDefault(x => x.JobId == job.Id);
            return new(job.Id, employer.Name, $"{origin.Name} ({origin.AirportIdentifier.Value})",
                $"{destination.Name} ({identifier})", destination.EconomicRole, resource.Name, requirement.Quantity,
                requirement.PaymentAmount, requirement.CurrencyCode, known, shipment?.Id, shipment?.State);
        }

        var offers = world.Jobs.Where(x => x.State == EconomyJobState.Offered)
            .Where(x => catalogue.Get(world.Requirements.Single(r => r.Id == x.RequirementId).DestinationLocationId).CanGenerateOrdinaryLiveJobs)
            .OrderBy(x => x.Id, StringComparer.Ordinal).Select(Display).ToArray();
        var currentJob = world.Jobs.SingleOrDefault(x => x.State == EconomyJobState.Accepted) ??
            world.Jobs.Where(x => x.State == EconomyJobState.Completed).OrderByDescending(x => x.CompletedUtc).FirstOrDefault();
        var player = world.Players.Single(x => x.Id == EmployedCargoPrototype.PlayerId);
        return new(offers, currentJob is null ? null : Display(currentJob), StoragePath, world.Revision, player.CachedBalance, player.CurrencyCode);
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
    private CargoJobDisplay? selectedOffer, activeJob;
    private decimal playerBalance;
    private string balanceCurrency = EmployedCargoPrototype.Currency;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ActionCommand RefreshCommand { get; }
    public ActionCommand AcceptCommand { get; }
    public ActionCommand LoadCargoCommand { get; }
    public ActionCommand UnloadCargoCommand { get; }

    public EmployedCargoJobsViewModel(IEmployedCargoJobs jobs, IFlightAttemptTracker flight)
    {
        this.jobs = jobs; this.flight = flight; context = SynchronizationContext.Current; flight.Changed += FlightChanged;
        RefreshCommand = new(() => _ = InitializeAsync(), () => !busy);
        AcceptCommand = new(() => _ = AcceptSelectedAsync(), () => !busy && selectedOffer is not null && !HasActiveJob);
        LoadCargoCommand = new(() => _ = LoadCargoAsync(), () => !busy && flight.Current.CanLoadCargo);
        UnloadCargoCommand = new(() => _ = UnloadCargoAsync(), () => !busy && flight.Current.CanUnloadCargo);
    }
    public IReadOnlyList<CargoJobDisplay> Offers { get => offers; private set { offers = value; Changed(); } }
    public CargoJobDisplay? SelectedOffer { get => selectedOffer; set { selectedOffer = value; Changed(); Changed(nameof(SelectedPurpose)); Changed(nameof(SelectedDestinationInformation)); AcceptCommand.Refresh(); } }
    public CargoJobDisplay? ActiveJob { get => activeJob; private set { activeJob = value; Changed(); Changed(nameof(ActiveRoute)); Changed(nameof(ActiveCargo)); Changed(nameof(ActiveShipment)); AcceptCommand.Refresh(); } }
    public bool HasActiveJob => ActiveJob?.ShipmentState is ShipmentState.Reserved or ShipmentState.InTransit;
    public bool IsBusy { get => busy; private set { busy = value; Changed(); RefreshCommands(); } }
    public string Status { get => status; private set { status = value; Changed(); } }
    public string Error { get => error; private set { error = value; Changed(); } }
    public string StoragePath => jobs.StoragePath;
    public string SelectedPurpose => SelectedOffer?.CustomerPurpose ?? "Select an offer to inspect its customer purpose.";
    public string SelectedDestinationInformation => SelectedOffer?.KnownDestinationInformation ?? "No offer selected.";
    public string ActiveRoute => ActiveJob?.Route ?? "No active shipment";
    public string ActiveCargo => ActiveJob is null ? "—" : $"{ActiveJob.Cargo} · payment on externally verified delivery: {ActiveJob.Payment}";
    public string ActiveShipment => ActiveJob?.Shipment ?? "—";
    public string FlightStage => flight.Current.Stage;
    public string FlightReason => flight.Current.Reason;
    public string OriginDistance => flight.Current.OriginDistanceNauticalMiles is { } value ? $"Eros distance: {value:F2} NM" : "Eros distance: —";
    public string DestinationDistance => flight.Current.DestinationDistanceNauticalMiles is { } value ? $"Destination distance: {value:F2} NM" : "Destination distance: —";
    public string Balance => $"Balance: {playerBalance:0.##} {balanceCurrency}";
    public string FlightStoragePath => flight.StoragePath;
    public bool CanResetIncompleteFlight => flight.Current.Attempt is { State: not FlightAttemptState.Delivered } && HasActiveJob;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RunAsync(() => jobs.LoadAsync(cancellationToken), "Choose one employed cargo offer.");
        if (Error == "") try { await flight.InitializeAsync(cancellationToken); } catch (Exception error) { Error = error.Message; Status = "Flight-attempt data could not be loaded. Existing files were preserved."; }
    }
    public async Task AcceptSelectedAsync(CancellationToken cancellationToken = default)
    {
        var selected = SelectedOffer; if (selected is null || HasActiveJob) return;
        await RunAsync(() => jobs.AcceptAsync(selected.JobId, cancellationToken), "Shipment reserved. Travel to Eros for simulated loading.");
        if (Error == "") try { await flight.RefreshEconomyAsync(cancellationToken); } catch (Exception error) { ReportFlightError(error); }
    }
    public async Task ObserveTelemetryAsync(AircraftTelemetry? telemetry, CancellationToken cancellationToken = default)
    { try { await flight.ObserveAsync(telemetry, cancellationToken); } catch (Exception error) { ReportFlightError(error); } }
    public async Task LoadCargoAsync(CancellationToken cancellationToken = default)
    { await RunFlightAsync(() => flight.LoadCargoAsync(cancellationToken), cancellationToken); }
    public async Task UnloadCargoAsync(CancellationToken cancellationToken = default)
    { await RunFlightAsync(() => flight.UnloadCargoAsync(cancellationToken), cancellationToken); }
    public Task<string> ExportFlightDiagnosticAsync(CancellationToken cancellationToken = default) => flight.ExportDeveloperDiagnosticAsync(cancellationToken);
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
            Status = ActiveJob?.ShipmentState == ShipmentState.Delivered ? "Delivery completed and payment recorded." :
                HasActiveJob ? "One employed shipment is active. Further acceptance is locked." : "Choose one employed cargo offer.";
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
            Status = HasActiveJob ? "One employed shipment is active. Further acceptance is locked." : success;
            Changed(nameof(HasActiveJob)); Changed(nameof(ActiveRoute)); Changed(nameof(ActiveCargo)); Changed(nameof(ActiveShipment));
        }
        catch (Exception exception) { Error = exception.Message; Status = "Jobs could not be loaded or changed. Existing economy data was preserved."; }
        finally { IsBusy = false; }
    }
    private Task ApplySnapshotAsync(EmployedCargoJobsSnapshot snapshot)
    {
        Offers = snapshot.Offers; ActiveJob = snapshot.ActiveJob; playerBalance = snapshot.PlayerBalance; balanceCurrency = snapshot.CurrencyCode; Changed(nameof(Balance));
        if (SelectedOffer is not null) SelectedOffer = Offers.SingleOrDefault(x => x.JobId == SelectedOffer.JobId);
        return Task.CompletedTask;
    }
    private void FlightChanged(FlightAttemptStatus value)
    {
        void Notify() { Changed(nameof(FlightStage)); Changed(nameof(FlightReason)); Changed(nameof(OriginDistance)); Changed(nameof(DestinationDistance)); Changed(nameof(CanResetIncompleteFlight)); RefreshCommands(); }
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
