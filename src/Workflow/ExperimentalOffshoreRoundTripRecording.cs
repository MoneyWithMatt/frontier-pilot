using System.Text.Json;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed record OffshoreRoundTripParameters(int SchemaVersion, string Purpose, PassengerEndpoint Origin, OffshorePlatform Platform, int InspectionSeconds);
public sealed record OffshoreRoundTripReview(Guid SessionId, int Frames, bool RecordingClosed, string? Outcome, OffshoreRoundTripStatus HistoricalStatus);

/// <summary>Create-only diagnostic context and native frames; replay never resumes a flight or changes qualification.</summary>
public static class ExperimentalOffshoreRoundTripRecording
{
    public static PassengerEndpoint ResolveBase(string destinationMetadataRoot)
    {
        // Load only separate destination reviews/cache, never CareerStore, economy, fleet or default app settings.
        var catalogue = new DestinationReviewStore(destinationMetadataRoot).Load();
        var employer = CareerCatalogue.Employers.Single(e => e.Category == "Offshore");
        var location = catalogue.Base(employer);
        if (location?.ApproximateCoordinates is not {} c || !catalogue.HasTechnicalBaseReference(employer))
            throw new IOException("Resolve the Offshore base in existing destination metadata first. No airport or coordinates inferred.");
        return new(location.StableId, location.Name, c.Latitude, c.Longitude);
    }
    public static async Task<OffshoreRoundTripStatus> RecordAsync(IAircraftConditionSource source, PassengerEndpoint origin,
        OffshorePlatform platform, string newPath, CancellationToken token, Action<OffshoreRoundTripStatus>? changed = null,
        Func<DateTimeOffset>? clock = null, int inspectionSeconds = 60)
    {
        var test = new ExperimentalOffshoreRoundTrip(origin, platform, inspectionSeconds);
        if (File.Exists(newPath)) throw new IOException("Diagnostic output already exists; original preserved.");
        // Context persists before any frame. A partial/error log is preserved for explicit review, never adopted/retried.
        using (var stream = new FileStream(newPath + ".mission.json", FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            JsonSerializer.Serialize(stream, new OffshoreRoundTripParameters(1, "ExperimentalNoPay", origin, platform, inspectionSeconds), AircraftConditionFiles.Json);
            stream.Flush(true);
        }
        try
        {
            await new AircraftConditionRecorder().RecordAsync(source, newPath, null, token,
                identityGuard: i => i.Title == ExperimentalPlatformContactProfile.AircraftTitle && i.Model == "H125",
                onFrameRecorded: (_, frame) =>
                {
                    var status = test.Observe(frame, clock?.Invoke() ?? DateTimeOffset.UtcNow);
                    changed?.Invoke(status);
                });
        }
        finally
        {
            var status = test.Observe(null, clock?.Invoke() ?? DateTimeOffset.UtcNow);
            changed?.Invoke(status);
        }
        return test.Current;
    }
    public static OffshoreRoundTripReview Review(string path)
    {
        using var stream = new FileStream(path + ".mission.json", FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 65536) throw new InvalidDataException("Invalid round-trip context; original preserved.");
        var context = JsonSerializer.Deserialize<OffshoreRoundTripParameters>(stream, AircraftConditionFiles.Json);
        if (context is not { SchemaVersion: 1, Purpose: "ExperimentalNoPay", Origin: not null, Platform: not null })
            throw new InvalidDataException("Unsupported round-trip context; original preserved.");
        var test = new ExperimentalOffshoreRoundTrip(context.Origin, context.Platform, context.InspectionSeconds);
        var log = ConditionDiagnosticReplay.Read(path, frame => test.Observe(frame, frame.CompletedUtc));
        var historical = test.Observe(null, log.LastFrameUtc ?? DateTimeOffset.UtcNow);
        return new(log.SessionId, log.Frames, log.RecordingClosed, log.Outcome, historical);
    }
}
