using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public interface IAircraftLoadingStore
{
    string StoragePath { get; }
    Task<IReadOnlyList<CargoLoadManifest>> LoadManifestsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersistentAircraftFuelState>> LoadFuelStatesAsync(CancellationToken cancellationToken = default);
    Task SaveManifestAsync(CargoLoadManifest manifest, CancellationToken cancellationToken = default);
    Task SaveFuelStateAsync(PersistentAircraftFuelState state, CancellationToken cancellationToken = default);
}

public sealed class LocalAircraftLoadingStore : IAircraftLoadingStore
{
    public const int CurrentSchemaVersion = 1;
    private sealed record Document(int SchemaVersion, IReadOnlyList<CargoLoadManifest> Manifests,
        IReadOnlyList<PersistentAircraftFuelState> FuelStates);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter() } };
    private readonly string directory; private readonly SemaphoreSlim instanceGate = new(1, 1);
    public string StoragePath { get; }

    public LocalAircraftLoadingStore(string? storagePath = null)
    {
        StoragePath = Path.GetFullPath(storagePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrontierAirportBuilder", "desktop", "economy", "aircraft-loading.json"));
        if (!string.Equals(Path.GetFileName(StoragePath), "aircraft-loading.json", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Aircraft-loading storage must use its dedicated managed filename.");
        directory = Path.GetDirectoryName(StoragePath) ?? throw new IOException("Aircraft-loading path has no directory."); EnsureOwned(StoragePath);
    }

    public async Task<IReadOnlyList<CargoLoadManifest>> LoadManifestsAsync(CancellationToken cancellationToken = default)
    { await instanceGate.WaitAsync(cancellationToken); try { using var lease = Lock(); return (await ReadAsync(cancellationToken)).Manifests; } finally { instanceGate.Release(); } }
    public async Task<IReadOnlyList<PersistentAircraftFuelState>> LoadFuelStatesAsync(CancellationToken cancellationToken = default)
    { await instanceGate.WaitAsync(cancellationToken); try { using var lease = Lock(); return (await ReadAsync(cancellationToken)).FuelStates; } finally { instanceGate.Release(); } }
    public Task SaveManifestAsync(CargoLoadManifest manifest, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            manifest = manifest.EnsureValid(); var values = document.Manifests.ToList(); var index = values.FindIndex(x => x.Id == manifest.Id);
            if (index < 0) values.Add(manifest); else { ValidateForward(values[index], manifest); if (values[index] == manifest) return document; values[index] = manifest; }
            return document with { Manifests = values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray() };
        }, cancellationToken);
    public Task SaveFuelStateAsync(PersistentAircraftFuelState state, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            state = state.EnsureValid(); var values = document.FuelStates.ToList(); var index = values.FindIndex(x => x.Id == state.Id);
            if (index < 0) values.Add(state); else if (values[index] != state) values[index] = state;
            return document with { FuelStates = values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray() };
        }, cancellationToken);

    private async Task MutateAsync(Func<Document, Document> mutation, CancellationToken token)
    {
        await instanceGate.WaitAsync(token); try { using var lease = Lock(); var old = await ReadAsync(token); var next = mutation(old); if (ReferenceEquals(old, next)) return; await WriteAsync(next, token); }
        finally { instanceGate.Release(); }
    }
    private async Task<Document> ReadAsync(CancellationToken token)
    {
        if (!File.Exists(StoragePath)) return new(CurrentSchemaVersion, [], []);
        var info = new FileInfo(StoragePath); if (info.Length is <= 0 or > 4_000_000) throw new IOException("Aircraft-loading document has an invalid size. File preserved.");
        Document document;
        try { await using var stream = new FileStream(StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            document = await JsonSerializer.DeserializeAsync<Document>(stream, Json, token) ?? throw new IOException("Aircraft-loading document is empty. File preserved."); }
        catch (JsonException error) { throw new IOException("Aircraft-loading document is corrupt or unsupported. File preserved.", error); }
        if (document.SchemaVersion != CurrentSchemaVersion || document.Manifests is null || document.FuelStates is null)
            throw new IOException("Aircraft-loading document uses an unsupported schema. File preserved.");
        try
        {
            var manifests = document.Manifests.Select(x => x.EnsureValid()).ToArray(); var fuels = document.FuelStates.Select(x => x.EnsureValid()).ToArray();
            if (manifests.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != manifests.Length || fuels.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != fuels.Length)
                throw new InvalidDataException("Aircraft-loading document contains duplicate records.");
            return new(CurrentSchemaVersion, manifests, fuels);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or NullReferenceException)
        { throw new IOException("Aircraft-loading document is inconsistent. File preserved.", error); }
    }
    private async Task WriteAsync(Document document, CancellationToken token)
    {
        Directory.CreateDirectory(directory); var temporary = Path.Combine(directory, ".aircraft-loading-" + Guid.NewGuid().ToString("N") + ".tmp"); EnsureOwned(temporary);
        try { await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            { await JsonSerializer.SerializeAsync(stream, document, Json, token); await stream.FlushAsync(token); stream.Flush(true); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, StoragePath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void ValidateForward(CargoLoadManifest old, CargoLoadManifest next)
    {
        if (old.State == LoadManifestState.Completed && old != next) throw new InvalidDataException("Completed cargo manifests are immutable.");
        bool allocationChanged = old.Stations.Count != next.Stations.Count || old.Stations.Zip(next.Stations).Any(pair =>
            pair.First.StationIndex != pair.Second.StationIndex || pair.First.BaselineKilograms != pair.Second.BaselineKilograms ||
            pair.First.FrontierCargoKilograms != pair.Second.FrontierCargoKilograms || pair.First.TargetKilograms != pair.Second.TargetKilograms ||
            pair.First.UnloadTargetKilograms is not null && pair.First.UnloadTargetKilograms != pair.Second.UnloadTargetKilograms);
        if (old.JobId != next.JobId || old.ShipmentId != next.ShipmentId || old.FlightAttemptId != next.FlightAttemptId || old.Aircraft != next.Aircraft ||
            old.Mode != next.Mode || allocationChanged ||
            old.CargoKilograms != next.CargoKilograms || old.CreatedUtc != next.CreatedUtc || next.State < old.State)
            throw new InvalidDataException("Cargo manifest identity, allocation or progress cannot be rewritten.");
    }
    private IDisposable Lock()
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StoragePath.ToUpperInvariant())))[..24]; var gate = new Semaphore(1, 1, "Local\\Frontier-AircraftLoading-" + name);
        if (!gate.WaitOne(0)) { gate.Dispose(); throw new IOException("Another Frontier aircraft-loading operation is running."); } return new Lease(gate);
    }
    private void EnsureOwned(string path) { var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Aircraft-loading path escapes its managed directory."); }
    private sealed class Lease(Semaphore gate) : IDisposable { public void Dispose() { gate.Release(); gate.Dispose(); } }
}

public sealed class MemoryAircraftLoadingStore : IAircraftLoadingStore
{
    private readonly Dictionary<string, CargoLoadManifest> manifests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PersistentAircraftFuelState> fuels = new(StringComparer.Ordinal);
    public string StoragePath => "memory://aircraft-loading";
    public int ManifestWrites { get; private set; } public int FuelWrites { get; private set; }
    public Task<IReadOnlyList<CargoLoadManifest>> LoadManifestsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CargoLoadManifest>>(manifests.Values.OrderBy(x => x.Id).ToArray());
    public Task<IReadOnlyList<PersistentAircraftFuelState>> LoadFuelStatesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PersistentAircraftFuelState>>(fuels.Values.OrderBy(x => x.Id).ToArray());
    public Task SaveManifestAsync(CargoLoadManifest manifest, CancellationToken cancellationToken = default) { manifests[manifest.EnsureValid().Id] = manifest; ManifestWrites++; return Task.CompletedTask; }
    public Task SaveFuelStateAsync(PersistentAircraftFuelState state, CancellationToken cancellationToken = default) { fuels[state.EnsureValid().Id] = state; FuelWrites++; return Task.CompletedTask; }
}
