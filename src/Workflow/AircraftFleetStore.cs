using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

/// <summary>One atomic fleet snapshot includes condition, positions and immutable event receipts.</summary>
public sealed class AircraftFleetStore(string path)
{
    public string StoragePath { get; } = Path.GetFullPath(path);
    public AircraftFleet Load()
    {
        if (!File.Exists(StoragePath))
        {
            if(File.Exists(StoragePath+".previous"))throw new IOException("Fleet primary is missing but a prior snapshot exists; preserve it and recover explicitly.");
            return AircraftFleet.Empty;
        }
        if (new FileInfo(StoragePath).Length is <= 0 or > 16000000) throw new IOException("Fleet size invalid; original preserved.");
        try { return (JsonSerializer.Deserialize<AircraftFleet>(File.ReadAllText(StoragePath), AircraftConditionFiles.Json) ?? throw new InvalidDataException("Empty fleet.")).Validate(); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NullReferenceException or ArgumentException)
        { throw new IOException("Fleet corrupt or unsupported; original preserved. Restore a verified backup explicitly.", ex); }
    }
    public AircraftFleet Commit(AircraftFleet next, long expectedRevision)
    {
        next.Validate();
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StoragePath.ToUpperInvariant())))[..24];
        using var gate = new Semaphore(1, 1, "Local\\Frontier-Fleet-" + name);
        if (!gate.WaitOne(TimeSpan.FromSeconds(10))) throw new IOException("Fleet busy in another process.");
        try
        {
            var prior = Load();
            if (prior.Revision != expectedRevision || next.Revision != expectedRevision + 1) throw new IOException("Fleet revision conflict; reload before retrying.");
            foreach (var old in prior.Aircraft)
            {
                var current = next.Aircraft.SingleOrDefault(x => x.Id == old.Id) ?? throw new IOException("Physical aircraft history cannot be deleted.");
                if (!old.History.SequenceEqual(current.History.Take(old.History.Count))) throw new IOException("Existing history cannot be rewritten.");
                if(old.Components.Any(c=>!current.Components.Any(next=>next.Id==c.Id&&(!c.Removed||next.Removed))) || old.Faults.Any(f=>!current.Faults.Any(next=>next.Id==f.Id)))
                    throw new IOException("Component instances and fault history must be retained.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            var temporary = StoragePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(stream, next, AircraftConditionFiles.Json); stream.Flush(true); }
                if (File.Exists(StoragePath)) File.Replace(temporary, StoragePath, StoragePath + ".previous", true);
                else File.Move(temporary, StoragePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return next;
        }
        finally { gate.Release(); }
    }
}

/// <summary>Legacy type/title positions stay readable and unassigned. Physical positions now belong to Fleet.</summary>
public sealed class FleetTrackingProjection(IAircraftStateStore legacy, Func<PhysicalAirframe?> selected) : IAircraftStateStore
{
    public string StoragePath => legacy.StoragePath + " (legacy read-only; physical fleet is authoritative)";
    public Task<PersistedAircraftState?> LoadActiveAsync(CancellationToken cancellationToken = default) => selected()?.Latest is {} p
        ? Task.FromResult<PersistedAircraftState?>(new(selected()!.Id.ToString("N"), p.Telemetry, p.Context)) : legacy.LoadActiveAsync(cancellationToken);
    public Task<IReadOnlyList<PersistedAircraftState>> LoadAllAsync(CancellationToken cancellationToken = default) => legacy.LoadAllAsync(cancellationToken);
    public Task SaveActiveAsync(AircraftTelemetry aircraft, AircraftLocationContext? location = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ClearAsync(CancellationToken cancellationToken = default) => throw new IOException("Legacy positions are preserved. Fleet records cannot be cleared by the tracking reset action.");
}
