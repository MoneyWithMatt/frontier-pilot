namespace FrontierAirportBuilder.Core;

public sealed record SurveyObject(uint ObjectId, double Latitude, double Longitude, double AltitudeMeters,
    double TrueHeadingDegrees, double GroundAltitudeMeters, double OnGround, double AboveGroundMeters,
    double Crouched, double MagneticVariationDegrees);
public sealed record SurveyFrame(double Seconds, SurveyObject Aircraft, SurveyObject Avatar, SurveyObject Current);
public sealed record SurveyPoint(double Latitude, double Longitude, double AltitudeMeters, double TrueHeadingDegrees,
    double AvatarObjectAltitudeMeters, double ObjectGroundOffsetMeters, double MagneticVariationDegrees,
    string Source = "Empirical MSFS 2024 walking-avatar GROUND ALTITUDE; Sedona-calibrated prototype");

public static class SurveyCapture
{
    public static string? Validate(IReadOnlyList<SurveyFrame> frames)
    {
        if (frames.Count < 2) return "Waiting for live samples.";
        var first = frames[0];
        foreach (var (frame, index) in frames.Select((f, i) => (f, i)))
        {
            if (!double.IsFinite(frame.Seconds) || (index > 0 && (frame.Seconds <= frames[index - 1].Seconds || frame.Seconds - frames[index - 1].Seconds > 1)))
                return "Stale or out-of-order samples.";
            if (frame.Avatar.ObjectId == frame.Aircraft.ObjectId || frame.Current.ObjectId != frame.Avatar.ObjectId ||
                frame.Avatar.ObjectId != first.Avatar.ObjectId || frame.Aircraft.ObjectId != first.Aircraft.ObjectId)
                return "The controlled object must be the explicit avatar, distinct from the aircraft.";
            foreach (var obj in new[] { frame.Aircraft, frame.Avatar, frame.Current })
            {
                if (new[] { obj.Latitude, obj.Longitude, obj.AltitudeMeters, obj.TrueHeadingDegrees, obj.GroundAltitudeMeters,
                    obj.OnGround, obj.AboveGroundMeters, obj.Crouched, obj.MagneticVariationDegrees }.Any(v => !double.IsFinite(v)))
                    return "Non-finite object reading.";
                if (Math.Abs(obj.Latitude) >= 90 || Math.Abs(obj.Longitude) > 180 || obj.GroundAltitudeMeters is < -500 or > 9000 ||
                    obj.TrueHeadingDegrees is < 0 or > 360 || Math.Abs(obj.MagneticVariationDegrees) > 180)
                    return "Object reading outside supported range.";
            }
            var a = frame.Avatar;
            if (a.OnGround != 1 || frame.Current.OnGround != 1 || frame.Aircraft.OnGround != 1 || a.Crouched != 0)
                return "Stand upright on the ground with the aircraft parked.";
            double offset = a.AltitudeMeters - a.GroundAltitudeMeters;
            if (Math.Abs(offset) > .05 || Math.Abs(a.AboveGroundMeters - offset) > .02 ||
                Math.Abs(offset - (first.Avatar.AltitudeMeters - first.Avatar.GroundAltitudeMeters)) > .03)
                return "Avatar altitude and ground readings disagree.";
            if (Distance(a, frame.Current) > .02 || Math.Abs(a.GroundAltitudeMeters - frame.Current.GroundAltitudeMeters) > .02 ||
                Math.Abs(a.AltitudeMeters - frame.Current.AltitudeMeters) > .02 || HeadingDifference(a.TrueHeadingDegrees, frame.Current.TrueHeadingDegrees) > .1)
                return "Current-object readings do not agree with the avatar.";
            if (Distance(first.Avatar, a) > .05 || Math.Abs(a.GroundAltitudeMeters - first.Avatar.GroundAltitudeMeters) > .03 ||
                Math.Abs(a.AltitudeMeters - first.Avatar.AltitudeMeters) > .05 || HeadingDifference(a.TrueHeadingDegrees, first.Avatar.TrueHeadingDegrees) > 1)
                return "Avatar position, ground elevation or heading is not stable.";
            if (Distance(first.Aircraft, frame.Aircraft) > .05 || Math.Abs(first.Aircraft.GroundAltitudeMeters - frame.Aircraft.GroundAltitudeMeters) > .03)
                return "Aircraft is not stationary.";
        }
        if (frames.Count < 16 || frames[^1].Seconds - first.Seconds < 5) return "Waiting for five stable seconds.";
        return null;
    }

    public static SurveyPoint Create(IReadOnlyList<SurveyFrame> frames)
    {
        if (Validate(frames) is { } error) throw new ArgumentException(error);
        double Median(Func<SurveyObject, double> select) { var v = frames.Select(f => select(f.Avatar)).Order().ToArray(); return v[v.Length / 2]; }
        double heading = frames[0].Avatar.TrueHeadingDegrees;
        var offset = frames.Select(f => SignedAngle(f.Avatar.TrueHeadingDegrees - heading)).Order().ToArray();
        return new(Median(a => a.Latitude), Median(a => a.Longitude), Median(a => a.GroundAltitudeMeters),
            Headings.Normalize(heading + offset[offset.Length / 2]), Median(a => a.AltitudeMeters),
            Median(a => a.AltitudeMeters - a.GroundAltitudeMeters), Median(a => a.MagneticVariationDegrees));
    }
    private static double SignedAngle(double v) => (v % 360 + 540) % 360 - 180;
    private static double HeadingDifference(double a, double b) => Math.Abs(SignedAngle(a - b));
    private static double Distance(SurveyObject a, SurveyObject b)
    {
        double north = (b.Latitude - a.Latitude) * 111320;
        double east = SignedAngle(b.Longitude - a.Longitude) * 111320 * Math.Cos(a.Latitude * Math.PI / 180);
        return Math.Sqrt(north * north + east * east);
    }
}
