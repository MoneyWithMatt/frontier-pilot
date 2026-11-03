using System.Text.Json;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.Workflow;

public sealed record PlatformVisitReview(Guid SessionId, int Frames, bool RecordingClosed, string? RecordingOutcome,
    PlatformVisitStatus HistoricalStatus)
{
    public string Authority => "Offline diagnostic review only. No live deck permission, flight evidence or payment.";
}

/// <summary>Reuses the condition recorder's create-only identity/sequence log. No gameplay stores or simulator writes.</summary>
public static class ExperimentalPlatformVisitRecording
{
    public static async Task<PlatformVisitStatus> RecordAsync(IAircraftConditionSource source, string newPath,
        CancellationToken token, Action<PlatformVisitStatus>? changed = null, Func<DateTimeOffset>? clock = null)
    {
        var visit = new ExperimentalPlatformVisit();
        try
        {
            await new AircraftConditionRecorder().RecordAsync(source, newPath, null, token,
                identityGuard: x => x.Title == ExperimentalPlatformContactProfile.AircraftTitle && x.Model == "H125",
                onFrameRecorded: (_, frame) =>
                {
                    var status = visit.Observe(frame, clock?.Invoke() ?? DateTimeOffset.UtcNow);
                    changed?.Invoke(status);
                });
        }
        finally
        {
            // Preserve historical milestones but never leave a current permission after recording closes.
            var status = visit.Observe(null, clock?.Invoke() ?? DateTimeOffset.UtcNow);
            changed?.Invoke(status);
        }
        return visit.Current;
    }

    public static PlatformVisitReview Review(string path)
    {
        // Streaming and bounded; corrupt/future/truncated records are never rewritten or silently repaired.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 64 * 1024 * 1024) throw new InvalidDataException("Invalid diagnostic recording size; original preserved.");
        using var reader = new StreamReader(stream);
        var visit = new ExperimentalPlatformVisit();
        Guid session = default; int count = 0; bool ended = false; string? outcome = null;
        ConditionAircraftIdentity? identity = null; DateTimeOffset? last = null;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length is 0 or > 131072 || ended) throw new InvalidDataException("Invalid/trailing diagnostic record; original preserved.");
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            var kind = root.GetProperty("Kind").GetString();
            if (session == default)
            {
                if (kind != "session" || root.GetProperty("SchemaVersion").GetInt32() != 1 ||
                    !root.GetProperty("ReadOnly").GetBoolean() || root.GetProperty("SessionId").GetGuid() == Guid.Empty)
                    throw new InvalidDataException("Unsupported diagnostic header; original preserved.");
                session = root.GetProperty("SessionId").GetGuid();
                continue;
            }
            if (root.GetProperty("SessionId").GetGuid() != session) throw new InvalidDataException("Mixed recording sessions.");
            if (kind == "end")
            {
                outcome = root.GetProperty("Outcome").GetString();
                if (root.GetProperty("Frames").GetInt32() != count || outcome is not ("completed" or "cancelled" or "error"))
                    throw new InvalidDataException("Invalid diagnostic end record.");
                ended = true;
            }
            else if (kind == "frame")
            {
                if (++count > 10000 || root.GetProperty("Sequence").GetInt32() != count)
                    throw new InvalidDataException("Invalid diagnostic sequence/count.");
                var frame = root.GetProperty("Frame").Deserialize<ConditionFrame>(AircraftConditionFiles.Json)
                    ?? throw new InvalidDataException("Missing diagnostic frame.");
                AircraftConditionRecorder.ValidateFrame(frame, last);
                identity ??= frame.Aircraft;
                if (identity != frame.Aircraft) throw new InvalidDataException("Mixed aircraft recording.");
                visit.Observe(frame, frame.CompletedUtc); // Recorded clock, explicitly offline; no wall-time credit.
                last = frame.CompletedUtc;
            }
            else throw new InvalidDataException("Unknown diagnostic record kind; original preserved.");
        }
        if (session == default) throw new InvalidDataException("Missing diagnostic header.");
        var historical = visit.Observe(null, last ?? DateTimeOffset.UtcNow);
        return new(session, count, ended, outcome, historical);
    }
}
