using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>Versioned, atomic local storage for last-known aircraft positions.</summary>
public sealed class LocalAircraftStateStore : IAircraftStateStore
{
    private sealed record Document(int SchemaVersion, string ActiveAircraftId, IReadOnlyList<PersistedAircraftState> Aircraft);
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string directory;
    public string StoragePath { get; }

    public LocalAircraftStateStore(string? storagePath = null)
    {
        StoragePath = Path.GetFullPath(storagePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrontierAirportBuilder", "desktop", "tracking", "aircraft-state.json"));
        if (!string.Equals(Path.GetFileName(StoragePath), "aircraft-state.json", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Aircraft-state storage must use its dedicated managed filename.");
        directory = Path.GetDirectoryName(StoragePath) ?? throw new IOException("Aircraft-state storage path has no directory.");
        EnsureOwnedPath(StoragePath);
    }

    public async Task<PersistedAircraftState?> LoadActiveAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            if (document is null) return null;
            return document.Aircraft.SingleOrDefault(a => a.Id == document.ActiveAircraftId);
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<PersistedAircraftState>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return (await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false))?.Aircraft ?? []; }
        finally { gate.Release(); }
    }

    public async Task SaveActiveAsync(AircraftTelemetry aircraft, AircraftLocationContext? location = null, CancellationToken cancellationToken = default)
    {
        aircraft = aircraft.EnsureValid();
        location=location?.EnsureValid();
        if(location is not null && location.ObservedUtc!=aircraft.ObservedUtc)
            throw new InvalidDataException("Aircraft telemetry and Frontier location timestamps do not agree.");
        var id = Identity(aircraft);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var records = previous?.Aircraft.ToDictionary(a => a.Id, StringComparer.Ordinal) ?? new Dictionary<string, PersistedAircraftState>(StringComparer.Ordinal);
            records[id] = new PersistedAircraftState(id, aircraft, location).EnsureValid();
            var document = new Document(2, id, records.Values.OrderBy(a => a.Id, StringComparer.Ordinal).ToArray());
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, ".aircraft-state-" + Guid.NewGuid().ToString("N") + ".tmp");
            EnsureOwnedPath(temporary);
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(stream, document, Json, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(true);
                }
                File.Move(temporary, StoragePath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { if (File.Exists(StoragePath)) File.Delete(StoragePath); }
        finally { gate.Release(); }
    }

    private async Task<Document?> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(StoragePath)) return null;
        var info = new FileInfo(StoragePath);
        if (info.Length is <= 0 or > 1_000_000) throw new IOException("Saved aircraft-state document has an invalid size. File preserved.");
        await using var stream = new FileStream(StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        Document document;
        try { document = await JsonSerializer.DeserializeAsync<Document>(stream, Json, cancellationToken).ConfigureAwait(false) ?? throw new IOException("Saved aircraft-state document is empty."); }
        catch (JsonException error) { throw new IOException("Saved aircraft-state document is corrupt or unsupported. File preserved.", error); }
        if (document.SchemaVersion is not (1 or 2) || string.IsNullOrWhiteSpace(document.ActiveAircraftId) || document.Aircraft is null || document.Aircraft.Count is < 1 or > 1000)
            throw new IOException("Saved aircraft-state document is unsupported. File preserved.");
        var validated = document.Aircraft.Select(a => a.EnsureValid()).ToArray();
        if (validated.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != validated.Length || validated.All(a => a.Id != document.ActiveAircraftId))
            throw new IOException("Saved aircraft-state identities are inconsistent. File preserved.");
        return document with { Aircraft = validated };
    }

    private static string Identity(AircraftTelemetry aircraft)
    {
        var source = (aircraft.AircraftType + "\n" + aircraft.AircraftTitle).Trim().ToUpperInvariant();
        return "prototype-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..24].ToLowerInvariant();
    }

    private void EnsureOwnedPath(string path)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path);
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Aircraft-state path escapes its managed directory.");
    }
}

internal sealed class MemoryAircraftStateStore : IAircraftStateStore
{
    private PersistedAircraftState? state;
    public string StoragePath => "Dry-run memory only";
    public Task<PersistedAircraftState?> LoadActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(state);
    public Task<IReadOnlyList<PersistedAircraftState>> LoadAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PersistedAircraftState>>(state is null ? [] : [state]);
    public Task SaveActiveAsync(AircraftTelemetry aircraft, AircraftLocationContext? location = null, CancellationToken cancellationToken = default) { state = new PersistedAircraftState("dry-run-aircraft", aircraft.EnsureValid(),location).EnsureValid(); return Task.CompletedTask; }
    public Task ClearAsync(CancellationToken cancellationToken = default) { state = null; return Task.CompletedTask; }
}
