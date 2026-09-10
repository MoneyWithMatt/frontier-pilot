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
        await using var stream = new FileStream(StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        Document document;
        try { document = await JsonSerializer.DeserializeAsync<Document>(stream, Json, cancellationToken).ConfigureAwait(false) ?? throw new IOException("Saved economy document is empty. File preserved."); }
        catch (JsonException error) { throw new IOException("Saved economy document is corrupt or unsupported. File preserved.", error); }
        if (document.SchemaVersion != 1 || document.World is null)
            throw new IOException("Saved economy document uses an unsupported schema. File preserved.");
        try { return document.World.EnsureValid(); }
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
                await JsonSerializer.SerializeAsync(stream, new Document(1, world), Json, cancellationToken).ConfigureAwait(false);
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
        if (!gate.WaitOne(0)) { gate.Dispose(); throw new IOException("Another Frontier economy operation is running. Retry when it finishes."); }
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
    public Task<EconomyTransition> AcceptJobAsync(string jobId, DateTimeOffset acceptedUtc, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyKernel.AcceptJob(world, jobId, acceptedUtc), cancellationToken);
    public Task<EconomyTransition> MarkShipmentInTransitAsync(string jobId, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyKernel.MarkShipmentInTransit(world, jobId), cancellationToken);
    public Task<EconomyTransition> CompleteDeliveryAsync(VerifiedDeliveryEvidence evidence, CancellationToken cancellationToken = default) =>
        store.ApplyAsync(world => EconomyKernel.CompleteDelivery(world, evidence), cancellationToken);
}
