using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public interface IEconomyStateStore
{
    string StoragePath { get; }
    Task<EconomyWorld?> LoadAsync(CancellationToken cancellationToken = default);
    Task InitializeAsync(EconomyWorld initial, CancellationToken cancellationToken = default);
    Task<EconomyTransition> ApplyAsync(Func<EconomyWorld, EconomyTransition> mutation, CancellationToken cancellationToken = default);
}

/// <summary>Versioned whole-world storage. A mutation is committed as one flushed, atomic file replacement so cargo,
/// inventory and ledger changes cannot become independently visible.</summary>
public sealed class LocalEconomyStateStore : IEconomyStateStore
{
    public const int CurrentSchemaVersion = 11;
    private sealed record Document(int SchemaVersion, EconomyWorld World);
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim instanceGate = new(1, 1);
    private readonly string directory;
    private readonly Func<CancellationToken, Task>? beforeReplace;
    public string StoragePath { get; }

    public LocalEconomyStateStore(string? storagePath = null) : this(storagePath, null) { }
    internal LocalEconomyStateStore(string? storagePath, Func<CancellationToken, Task>? beforeReplace)
    {
        StoragePath = Path.GetFullPath(storagePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrontierAirportBuilder", "desktop", "economy", "economy-state.json"));
        if (!string.Equals(Path.GetFileName(StoragePath), "economy-state.json", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Economy storage must use its dedicated managed filename.");
        directory = Path.GetDirectoryName(StoragePath) ?? throw new IOException("Economy storage path has no directory.");
        this.beforeReplace = beforeReplace;
        EnsureOwnedPath(StoragePath);
    }

    public async Task<EconomyWorld?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await instanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { using var lease = Lock(); return await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false); }
        finally { instanceGate.Release(); }
    }

    public async Task InitializeAsync(EconomyWorld initial, CancellationToken cancellationToken = default)
    {
        initial = initial.EnsureValid();
        await instanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var lease = Lock();
            if (File.Exists(StoragePath))
            {
                _ = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
                throw new IOException("Economy state already exists.");
            }
            await WriteUnlockedAsync(initial, cancellationToken).ConfigureAwait(false);
        }
        finally { instanceGate.Release(); }
    }

    public async Task<EconomyTransition> ApplyAsync(Func<EconomyWorld, EconomyTransition> mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await instanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var lease = Lock();
            var previous = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false) ?? throw new IOException("Economy state has not been initialized.");
            var result = mutation(previous) ?? throw new InvalidDataException("Economy mutation returned no result.");
            result.World.EnsureValid();
            if (!result.Changed)
            {
                if (result.World != previous) throw new InvalidDataException("An unchanged economy transaction returned different state.");
                return result;
            }
            if (result.World.Id != previous.Id || result.World.Revision != previous.Revision + 1)
                throw new InvalidDataException("Economy transaction revision is inconsistent.");
            await WriteUnlockedAsync(result.World, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally { instanceGate.Release(); }
    }

    private async Task<EconomyWorld?> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(StoragePath)) return null;
        var info = new FileInfo(StoragePath);
        if (info.Length is <= 0 or > 16_000_000) throw new IOException("Saved economy document has an invalid size. File preserved.");
        Document document;
        try
        {
            await using var stream = new FileStream(StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            document = await JsonSerializer.DeserializeAsync<Document>(stream, Json, cancellationToken).ConfigureAwait(false) ?? throw new IOException("Saved economy document is empty. File preserved.");
        }
        catch (JsonException error) { throw new IOException("Saved economy document is corrupt or unsupported. File preserved.", error); }
        if (document.SchemaVersion is < 1 or > CurrentSchemaVersion || document.World is null)
            throw new IOException("Saved economy document uses an unsupported schema. File preserved.");
        try
        {
            var world = document.World;
            // Schema 9 adds nullable employer wage policy and immutable wage receipts.
            // Missing fields retain defaults; historical ledger amounts/balances are not recalculated as wages.
            if (document.SchemaVersion < 5)
            {
                world = world with
                {
                    Shipments = world.Shipments.Select(shipment => shipment with
                    {
                        LoadedQuantity = shipment.State is ShipmentState.Loaded or ShipmentState.InTransit ? shipment.Quantity : 0,
                        DeliveredQuantity = shipment.State == ShipmentState.Delivered ? shipment.Quantity : 0,
                        ForfeitedQuantity = 0
                    }).ToArray(),
                    CargoTransfers = world.CargoTransfers ?? []
                };
            }
            if (document.SchemaVersion < 6)
                world = world with { LogisticsContracts = world.LogisticsContracts ?? [], FinancialReservations = world.FinancialReservations ?? [] };
            world = world.EnsureValid();
            if (document.SchemaVersion < CurrentSchemaVersion)
            {
                var ledger = world.InventoryLedger.ToList();
                var positions = world.Inventories.OrderBy(x => x.LocationId, StringComparer.Ordinal).ThenBy(x => x.ResourceId, StringComparer.Ordinal).ToArray();
                for (var index = 0; index < positions.Length; index++)
                {
                    var balance = positions[index]; var audited = ledger.Where(x => x.LocationId == balance.LocationId && x.ResourceId == balance.ResourceId).Sum(x => x.QuantityDelta);
                    var difference = balance.AvailableQuantity - audited;
                    if (difference < 0) throw new InvalidDataException("Legacy inventory audit exceeds its cached balance.");
                    if (difference == 0) continue;
                    ledger.Add(new($"inventory-schema-migration-opening-{index + 1:D4}", InventoryTransactionKind.LegacyOpeningBalance,
                        balance.LocationId, balance.ResourceId, difference, null,
                        new DateTimeOffset(info.LastWriteTimeUtc), $"Opening balance preserved by the schema-{document.SchemaVersion} to schema-3 migration."));
                }
                world = (world with { InventoryLedger = ledger }).EnsureValid();
                await WriteUnlockedAsync(world, cancellationToken).ConfigureAwait(false);
            }
            return world;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { throw new IOException("Saved economy document is inconsistent. File preserved.", error); }
    }

    private async Task WriteUnlockedAsync(EconomyWorld world, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".economy-state-" + Guid.NewGuid().ToString("N") + ".tmp");
        EnsureOwnedPath(temporary);
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, new Document(CurrentSchemaVersion, world), Json, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false); stream.Flush(true);
            }
            if (beforeReplace is not null) await beforeReplace(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, StoragePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private IDisposable Lock()
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(StoragePath).ToUpperInvariant())))[..24];
        var gate = new Semaphore(1, 1, "Local\\Frontier-Economy-" + name);
        if (!gate.WaitOne(TimeSpan.FromSeconds(10))) { gate.Dispose(); throw new IOException("Another Frontier economy operation is running. Retry when it finishes."); }
        return new Lease(gate);
    }
    private sealed class Lease(Semaphore gate) : IDisposable { public void Dispose() { gate.Release(); gate.Dispose(); } }
    private void EnsureOwnedPath(string path)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Economy path escapes its managed directory.");
    }
}

public sealed class EconomyService(IEconomyStateStore store)
{
    public string StoragePath => store.StoragePath;
    public Task<EconomyWorld?> LoadAsync(CancellationToken cancellationToken = default) => store.LoadAsync(cancellationToken);
    public Task InitializeAsync(EconomyWorld initial, CancellationToken cancellationToken = default) => store.InitializeAsync(initial, cancellationToken);
    public Task<EconomyTransition> AdvanceRealTimeAsync(DateTimeOffset utc, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyRealTime.Advance(world, utc), cancellationToken);
    public Task<EconomyTransition> AcceptJobAsync(string jobId, DateTimeOffset acceptedUtc, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyKernel.AcceptJob(world, jobId, acceptedUtc), cancellationToken);
    public Task<EconomyTransition> MarkShipmentInTransitAsync(string jobId, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyKernel.MarkShipmentInTransit(world, jobId), cancellationToken);
    public Task<EconomyTransition> MarkShipmentsInTransitAsync(IReadOnlyList<string> jobIds, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyKernel.MarkShipmentsInTransit(world, jobIds), cancellationToken);
    public Task<EconomyTransition> MarkShipmentLoadedAsync(string jobId, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyKernel.MarkShipmentLoaded(world, jobId), cancellationToken);
    public Task<EconomyTransition> ApplyCargoTransferAsync(ShipmentCargoTransferRequest request, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyKernel.ApplyCargoTransfer(world, request), cancellationToken);
    public Task<EconomyTransition> ApplyCargoTransfersAsync(IReadOnlyList<ShipmentCargoTransferRequest> requests, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyKernel.ApplyCargoTransfers(world, requests), cancellationToken);
    public Task<EconomyTransition> AbandonIncompletePrototypeShipmentAsync(string jobId, DateTimeOffset cancelledUtc, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyKernel.AbandonIncompletePrototypeShipment(world, jobId, cancelledUtc), cancellationToken);
    public Task<EconomyTransition> CompleteDeliveryAsync(VerifiedDeliveryEvidence evidence, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyKernel.CompleteDelivery(world, evidence), cancellationToken);
    public Task<EconomyTransition> MaintainJobBoardAsync(DateTimeOffset occurredUtc, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyJobBoard.Maintain(world, occurredUtc), cancellationToken);
    public Task<EconomyTransition> FoundProspectingCampAsync(AirportSiteCandidate candidate, VerifiedSitePresenceEvidence evidence,
        string commandId, DateTimeOffset occurredUtc, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => ProspectingCampKernel.Found(world, candidate, evidence, commandId, occurredUtc), cancellationToken);
    public Task<EconomyTransition> AdvanceOperationDayAsync(string operationId, string commandId, DateTimeOffset occurredUtc,
        CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => ProspectingCampKernel.AdvanceDay(world, operationId, commandId, occurredUtc), cancellationToken);
    public Task<EconomyTransition> EnsurePersistentGatewayStockAsync(PersistentEconomyPrototypeOptions options,
        DateTimeOffset occurredUtc, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => PersistentEconomyKernel.EnsurePrototypeGatewayStock(world, options, occurredUtc), cancellationToken);
    public Task<EconomyTransition> TransferPersistentInventoryAsync(PersistentInventoryTransferRequest request,
        CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => PersistentEconomyKernel.Transfer(world, request), cancellationToken);
    public Task<EconomyTransition> PublishOperationLogisticsAsync(PublishOperationLogisticsRequest request,
        CancellationToken cancellationToken = default) => store.ApplyAsync(world => PlayerLogisticsKernel.Publish(world, request), cancellationToken);
    public Task<EconomyTransition> EditOperationLogisticsPayoutAsync(string contractId, decimal payout, DateTimeOffset occurredUtc,
        CancellationToken cancellationToken = default) => store.ApplyAsync(world => PlayerLogisticsKernel.EditPayout(world, contractId, payout, occurredUtc), cancellationToken);
    public Task<EconomyTransition> EditOperationLogisticsTermsAsync(string contractId, decimal payout, int? fallbackMinutes, DateTimeOffset occurredUtc,
        CancellationToken cancellationToken = default) => store.ApplyAsync(world => PlayerLogisticsKernel.EditPublishedTerms(world, contractId, payout, fallbackMinutes, occurredUtc), cancellationToken);
    public Task<EconomyTransition> CancelOperationLogisticsAsync(string contractId, DateTimeOffset occurredUtc,
        CancellationToken cancellationToken = default) => store.ApplyAsync(world => PlayerLogisticsKernel.Cancel(world, contractId, occurredUtc), cancellationToken);
    public Task<EconomyTransition> ProcessRoadLogisticsAsync(DateTimeOffset occurredUtc,
        CancellationToken cancellationToken = default) => store.ApplyAsync(world => PlayerLogisticsKernel.ProcessRoad(world, occurredUtc), cancellationToken);
    public Task<EconomyTransition> UpgradeRepeatableJobsAsync(EmployedCargoPrototypeOptions options, DateTimeOffset occurredUtc,
        CancellationToken cancellationToken = default) => store.ApplyAsync(world => EmployedCargoPrototype.UpgradeForRepeatableJobs(world, options, occurredUtc), cancellationToken);
}
