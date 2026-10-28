using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public static class AircraftConditionFiles
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    /// <summary>Create-only V0 identity/estimate document. No repair, update or telemetry-to-estimate path.</summary>
    public static void Create(string path, AircraftConditionRecord record)
    {
        record.Validate();
        path = Path.GetFullPath(path);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, record, Json);
                stream.Flush(true);
            }
            File.Move(temp, path, overwrite: false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static AircraftConditionRecord Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 1048576) throw new InvalidDataException("Invalid condition document size; original preserved.");
        var record = JsonSerializer.Deserialize<AircraftConditionRecord>(stream, Json)
            ?? throw new InvalidDataException("Missing condition document; original preserved.");
        record.Validate();
        return record;
    }
}

/// <summary>Streaming, create-new diagnostic export. Never writes the selected aircraft document or gameplay saves.</summary>
public sealed class AircraftConditionRecorder
{
    public async Task<int> RecordAsync(IAircraftConditionSource source, string outputPath,
        AircraftConditionRecord? selectedAirframe, CancellationToken cancellationToken,
        Func<ConditionAircraftIdentity, bool>? identityGuard = null,
        Action<int, ConditionFrame>? onFrameRecorded = null)
    {
        selectedAirframe?.Validate();
        await using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream);
        var session = Guid.NewGuid(); var count = 0; var outcome = "completed";
        string? errorType = null;
        Task Write(object value) => writer.WriteLineAsync(JsonSerializer.Serialize(value, AircraftConditionFiles.Json));
        await Write(new { Kind = "session", SchemaVersion = 1, SessionId = session, StartedUtc = DateTimeOffset.UtcNow,
            AirframeId = selectedAirframe?.AirframeId, PackageVersion = selectedAirframe?.PackageVersion,
            ReadOnly = true, Compatibility = ConditionIntegrationLevel.Unverified,
            Note = "Raw diagnostic receipt is not verified compatibility, damage attribution, or delivery evidence." });
        await writer.FlushAsync();
        ConditionAircraftIdentity? identity = null;
        DateTimeOffset? last = null;
        try
        {
            await foreach (var frame in source.ReadAsync(cancellationToken).WithCancellation(cancellationToken))
            {
                ValidateFrame(frame, last);
                identity ??= frame.Aircraft;
                cancellationToken.ThrowIfCancellationRequested();
                if (frame.Aircraft != identity || selectedAirframe is not null && !selectedAirframe.Matches(frame.Aircraft) ||
                    identityGuard is not null && !identityGuard(frame.Aircraft))
                    throw new InvalidDataException("Aircraft identity does not match the selected recording; frame refused.");
                await Write(new { Kind = "frame", Sequence = ++count, SessionId = session, source.ConnectionInfo, Frame = frame });
                await writer.FlushAsync();
                onFrameRecorded?.Invoke(count, frame);
                last = frame.CompletedUtc;
            }
            if (cancellationToken.IsCancellationRequested) outcome = "cancelled";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { outcome = "cancelled"; }
        catch (Exception error) { outcome = "error"; errorType = error.GetType().Name; throw; }
        finally
        {
            await Write(new { Kind = "end", SessionId = session, CompletedUtc = DateTimeOffset.UtcNow, Frames = count, Outcome = outcome, ErrorType = errorType });
            await writer.FlushAsync();
        }
        return count;
    }

    internal static void ValidateFrame(ConditionFrame frame, DateTimeOffset? previous)
    {
        frame.Aircraft.Validate();
        if (frame.StartedUtc == default || frame.CompletedUtc < frame.StartedUtc || frame.StartedUtc <= previous ||
            frame.CompletedUtc - frame.StartedUtc > TimeSpan.FromSeconds(15) || string.IsNullOrWhiteSpace(frame.AdapterId) ||
            frame.Readings.Count is < 1 or > 512 || frame.Readings.Select(x => x.Field.Key).Distinct().Count() != frame.Readings.Count)
            throw new InvalidDataException("Invalid or discontinuous condition frame.");
        foreach (var reading in frame.Readings)
        {
            if (reading.ObservedUtc < frame.StartedUtc || reading.ObservedUtc > frame.CompletedUtc ||
                !Enum.IsDefined(reading.Availability) || !Enum.IsDefined(reading.Field.Kind) ||
                reading.Field.Kind == ConditionKind.EstimatedCondition ||
                reading.Availability != ConditionAvailability.ReceivedUnverified && reading.Value is not null ||
                reading.Availability == ConditionAvailability.ReceivedUnverified &&
                (reading.Value is not { } number || reading.Field.Observe(number, reading.ObservedUtc).Availability != ConditionAvailability.ReceivedUnverified))
                throw new InvalidDataException("Condition observation has invalid value, source or timestamp.");
        }
    }
}
