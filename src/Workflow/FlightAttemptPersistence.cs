using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public interface IFlightAttemptStore
{
    string StoragePath { get; }
    Task<IReadOnlyList<FlightAttempt>> LoadAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FlightDiagnosticEvent>> LoadDiagnosticTimelineAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(FlightAttempt attempt, CancellationToken cancellationToken = default);
    Task AppendDiagnosticEventAsync(FlightDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default);
    Task<FlightAttempt> ResetIncompleteProgressAsync(string attemptId, CancellationToken cancellationToken = default);
}

/// <summary>Strict whole-document persistence using flushed same-directory replacement. Invalid files are preserved.</summary>
public sealed class LocalFlightAttemptStore : IFlightAttemptStore
{
    public const int CurrentSchemaVersion = 4;
    private const int MaximumEventsPerShipment = 256;
    private sealed record Document(int SchemaVersion, IReadOnlyList<FlightAttempt> Attempts,
        IReadOnlyList<FlightDiagnosticEvent>? DiagnosticTimeline = null);
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim instanceGate = new(1, 1);
    private readonly string directory;
    public string StoragePath { get; }

    public LocalFlightAttemptStore(string? storagePath = null)
    {
        StoragePath = Path.GetFullPath(storagePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrontierAirportBuilder", "desktop", "economy", "flight-attempts.json"));
        if (!string.Equals(Path.GetFileName(StoragePath), "flight-attempts.json", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Flight-attempt storage must use its dedicated managed filename.");
        directory = Path.GetDirectoryName(StoragePath) ?? throw new IOException("Flight-attempt storage path has no directory.");
        EnsureOwnedPath(StoragePath);
    }

    public async Task<IReadOnlyList<FlightAttempt>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await instanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { using var lease = Lock(); return (await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false)).Attempts; }
        finally { instanceGate.Release(); }
    }

    public async Task<IReadOnlyList<FlightDiagnosticEvent>> LoadDiagnosticTimelineAsync(CancellationToken cancellationToken = default)
    {
        await instanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { using var lease = Lock(); return (await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false)).DiagnosticTimeline ?? []; }
        finally { instanceGate.Release(); }
    }

    public async Task SaveAsync(FlightAttempt attempt, CancellationToken cancellationToken = default)
    {
        attempt = attempt.EnsureValid();
        await instanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var lease = Lock(); var document = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false); var attempts = document.Attempts.ToList();
            var index = attempts.FindIndex(x => x.Id == attempt.Id);
            if (index < 0) attempts.Add(attempt); else { ValidateForward(attempts[index], attempt); attempts[index] = attempt; }
            await WriteUnlockedAsync(attempts.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(), document.DiagnosticTimeline ?? [], cancellationToken).ConfigureAwait(false);
        }
        finally { instanceGate.Release(); }
    }

    public async Task AppendDiagnosticEventAsync(FlightDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default)
    {
        diagnosticEvent = diagnosticEvent.EnsureValid();
        await instanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var lease = Lock(); var document = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var timeline = (document.DiagnosticTimeline ?? []).ToList();
            if (timeline.Any(x => x.Id == diagnosticEvent.Id)) return;
            timeline.Add(diagnosticEvent);
            timeline = Bound(timeline, diagnosticEvent.ShipmentId);
            await WriteUnlockedAsync(document.Attempts, timeline, cancellationToken).ConfigureAwait(false);
        }
        finally { instanceGate.Release(); }
    }

    public async Task<FlightAttempt> ResetIncompleteProgressAsync(string attemptId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(attemptId)) throw new ArgumentException("Flight-attempt ID is required.");
        await instanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var lease = Lock(); var document = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false); var attempts = document.Attempts.ToList();
            var index = attempts.FindIndex(x => x.Id == attemptId);
            if (index < 0) throw new InvalidOperationException("Flight attempt was not found.");
            var existing = attempts[index];
            if (existing.State == FlightAttemptState.Delivered || existing.DeliveredUtc is not null)
                throw new InvalidOperationException("Completed delivery evidence cannot be reset.");
            var reset = ResetProgress(existing).EnsureValid(); attempts[index] = reset;
            await WriteUnlockedAsync(attempts.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(), document.DiagnosticTimeline ?? [], cancellationToken).ConfigureAwait(false);
            return reset;
        }
        finally { instanceGate.Release(); }
    }

    private async Task<Document> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(StoragePath)) return new(CurrentSchemaVersion, [], []);
        var info = new FileInfo(StoragePath); if (info.Length is <= 0 or > 4_000_000) throw new IOException("Saved flight-attempt document has an invalid size. File preserved.");
        Document document;
        try
        {
            await using var stream = new FileStream(StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            document = await JsonSerializer.DeserializeAsync<Document>(stream, Json, cancellationToken).ConfigureAwait(false) ?? throw new IOException("Saved flight-attempt document is empty. File preserved.");
        }
        catch (JsonException error) { throw new IOException("Saved flight-attempt document is corrupt or unsupported. File preserved.", error); }
        if (document.SchemaVersion is < 1 or > CurrentSchemaVersion || document.Attempts is null) throw new IOException("Saved flight-attempt document uses an unsupported schema. File preserved.");
        try
        {
            var attempts = document.Attempts.Select(x => (document.SchemaVersion < 3
                ? x with { RequiredMaximumDistanceNauticalMiles = 10 }
                : x).EnsureValid()).ToArray();
            if (attempts.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != attempts.Length) throw new InvalidDataException("Duplicate flight-attempt ID.");
            if (attempts.Select(x => x.ShipmentId).Distinct(StringComparer.Ordinal).Count() != attempts.Length) throw new InvalidDataException("Duplicate flight-attempt shipment.");
            var timeline = (document.DiagnosticTimeline ?? []).Select(x => x.EnsureValid()).ToArray();
            if (timeline.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != timeline.Length) throw new InvalidDataException("Duplicate flight diagnostic event ID.");
            var migrated = new Document(CurrentSchemaVersion, attempts, timeline);
            if (document.SchemaVersion < CurrentSchemaVersion) await WriteUnlockedAsync(attempts, timeline, cancellationToken).ConfigureAwait(false);
            return migrated;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or NullReferenceException)
        { throw new IOException("Saved flight-attempt document is inconsistent. File preserved.", error); }
    }

    private async Task WriteUnlockedAsync(IReadOnlyList<FlightAttempt> attempts, IReadOnlyList<FlightDiagnosticEvent> timeline, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory); var temporary = Path.Combine(directory, ".flight-attempts-" + Guid.NewGuid().ToString("N") + ".tmp"); EnsureOwnedPath(temporary);
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, new Document(CurrentSchemaVersion, attempts, timeline), Json, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false); stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, StoragePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static List<FlightDiagnosticEvent> Bound(List<FlightDiagnosticEvent> events, string shipmentId)
    {
        var shipmentEvents = events.Where(x => x.ShipmentId == shipmentId).OrderBy(x => x.OccurredUtc).ThenBy(x => x.Id).ToList();
        while (shipmentEvents.Count > MaximumEventsPerShipment)
        {
            var removable = shipmentEvents.FirstOrDefault(x => x.Type is FlightDiagnosticEventType.SimConnectConnected or
                FlightDiagnosticEventType.SimConnectDisconnected or FlightDiagnosticEventType.OriginQualificationInterrupted or
                FlightDiagnosticEventType.ArrivalQualificationInterrupted);
            if (removable is null) break;
            events.Remove(removable); shipmentEvents.Remove(removable);
        }
        return events;
    }

    private static void ValidateForward(FlightAttempt old, FlightAttempt next)
    {
        if (old.JobId != next.JobId || old.ShipmentId != next.ShipmentId || old.OriginLocationId != next.OriginLocationId ||
            old.DestinationLocationId != next.DestinationLocationId || old.LoadedUtc != next.LoadedUtc || old.Aircraft != next.Aircraft ||
            old.RequiredMaximumDistanceNauticalMiles != next.RequiredMaximumDistanceNauticalMiles ||
            old.TransitBeginsAtDeparture != next.TransitBeginsAtDeparture ||
            old.OriginDepartedUtc is not null && old.OriginDepartedUtc != next.OriginDepartedUtc ||
            old.LoadedPosition != next.LoadedPosition || next.State < old.State || next.MaximumDistanceFromOriginNauticalMiles < old.MaximumDistanceFromOriginNauticalMiles ||
            old.FirstAirborneUtc is not null && old.FirstAirborneUtc != next.FirstAirborneUtc ||
            old.MovementObservedUtc is not null && old.MovementObservedUtc != next.MovementObservedUtc ||
            old.DeliveryProof is not null && old.DeliveryProof != next.DeliveryProof || old.DeliveredUtc is not null && old.DeliveredUtc != next.DeliveredUtc)
            throw new InvalidDataException("Flight-attempt progress cannot be rewritten or moved backwards.");
    }

    internal static FlightAttempt ResetProgress(FlightAttempt attempt) => attempt with
    {
        State = FlightAttemptState.CargoLoaded,
        FirstAirborneUtc = null,
        OriginDepartedUtc = null,
        MovementObservedUtc = null,
        MaximumDistanceFromOriginNauticalMiles = 0,
        MaximumDistanceObservedUtc = null,
        DeliveryProof = null,
        DeliveredUtc = null
    };

    private IDisposable Lock()
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StoragePath.ToUpperInvariant())))[..24];
        var gate = new Semaphore(1, 1, "Local\\Frontier-FlightAttempt-" + name);
        if (!gate.WaitOne(0)) { gate.Dispose(); throw new IOException("Another Frontier flight-attempt operation is running. Retry when it finishes."); }
        return new Lease(gate);
    }
    private sealed class Lease(Semaphore gate) : IDisposable { public void Dispose() { gate.Release(); gate.Dispose(); } }
    private void EnsureOwnedPath(string path)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Flight-attempt path escapes its managed directory.");
    }
}

public sealed class MemoryFlightAttemptStore : IFlightAttemptStore
{
    private readonly Dictionary<string, FlightAttempt> attempts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FlightDiagnosticEvent> timeline = new(StringComparer.Ordinal);
    public string StoragePath => "memory://flight-attempts-v0";
    public Task<IReadOnlyList<FlightAttempt>> LoadAsync(CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<FlightAttempt>>(attempts.Values.OrderBy(x => x.Id).ToArray()); }
    public Task<IReadOnlyList<FlightDiagnosticEvent>> LoadDiagnosticTimelineAsync(CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<FlightDiagnosticEvent>>(timeline.Values.OrderBy(x => x.OccurredUtc).ThenBy(x => x.Id).ToArray()); }
    public Task SaveAsync(FlightAttempt attempt, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); attempt = attempt.EnsureValid(); attempts[attempt.Id] = attempt; return Task.CompletedTask; }
    public Task AppendDiagnosticEventAsync(FlightDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); diagnosticEvent.EnsureValid(); timeline.TryAdd(diagnosticEvent.Id, diagnosticEvent);
        var bounded = LocalFlightAttemptStore.Bound(timeline.Values.ToList(), diagnosticEvent.ShipmentId);
        timeline.Clear(); foreach (var item in bounded) timeline[item.Id] = item; return Task.CompletedTask;
    }
    public Task<FlightAttempt> ResetIncompleteProgressAsync(string attemptId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!attempts.TryGetValue(attemptId, out var existing)) throw new InvalidOperationException("Flight attempt was not found.");
        if (existing.State == FlightAttemptState.Delivered || existing.DeliveredUtc is not null)
            throw new InvalidOperationException("Completed delivery evidence cannot be reset.");
        var reset = LocalFlightAttemptStore.ResetProgress(existing).EnsureValid(); attempts[attemptId] = reset; return Task.FromResult(reset);
    }
}
