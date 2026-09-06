namespace FrontierAirportBuilder.Core;

public readonly record struct GeoPoint(double Latitude, double Longitude);
public readonly record struct GeodesicDestination(GeoPoint Position, double ForwardBearingDegrees);

public static class Geodesy
{
    public const double SemiMajorAxisMeters = 6378137;
    public const double Flattening = 1 / 298.257223563;
    private const double Radians = Math.PI / 180;

    // Vincenty's direct solution on WGS84, including the forward bearing at the destination.
    // Scope: local runway geometry (<= 10 km), not a general antipodal inverse solver.
    public static GeodesicDestination Direct(GeoPoint origin, double bearingDegrees, double distanceMeters)
    {
        if (!double.IsFinite(origin.Latitude) || Math.Abs(origin.Latitude) >= 90 ||
            !double.IsFinite(origin.Longitude) || Math.Abs(origin.Longitude) > 180)
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (!double.IsFinite(distanceMeters) || distanceMeters is < 0 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(distanceMeters));
        var bearing = Headings.Normalize(bearingDegrees);
        if (distanceMeters == 0)
            return new(new(origin.Latitude, NormalizeLongitude(origin.Longitude)), bearing);
        var b = SemiMajorAxisMeters * (1 - Flattening);
        var alpha1 = bearing * Radians;
        var tanU1 = (1 - Flattening) * Math.Tan(origin.Latitude * Radians);
        var cosU1 = 1 / Math.Sqrt(1 + tanU1 * tanU1);
        var sinU1 = tanU1 * cosU1;
        var sinAlpha1 = Math.Sin(alpha1);
        var cosAlpha1 = Math.Cos(alpha1);
        var sigma1 = Math.Atan2(tanU1, cosAlpha1);
        var sinAlpha = cosU1 * sinAlpha1;
        var cosSqAlpha = 1 - sinAlpha * sinAlpha;
        var uSq = cosSqAlpha * (SemiMajorAxisMeters * SemiMajorAxisMeters - b * b) / (b * b);
        var aCoefficient = 1 + uSq / 16384 * (4096 + uSq * (-768 + uSq * (320 - 175 * uSq)));
        var bCoefficient = uSq / 1024 * (256 + uSq * (-128 + uSq * (74 - 47 * uSq)));
        var baseSigma = distanceMeters / (b * aCoefficient);
        var sigma = baseSigma;
        var converged = false;
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var cos2SigmaM = Math.Cos(2 * sigma1 + sigma);
            var sinSigma = Math.Sin(sigma);
            var cosSigma = Math.Cos(sigma);
            var delta = bCoefficient * sinSigma * (cos2SigmaM + bCoefficient / 4 *
                (cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM) - bCoefficient / 6 * cos2SigmaM *
                (-3 + 4 * sinSigma * sinSigma) * (-3 + 4 * cos2SigmaM * cos2SigmaM)));
            var next = baseSigma + delta;
            if (Math.Abs(next - sigma) < 1e-13) { sigma = next; converged = true; break; }
            sigma = next;
        }
        if (!converged) throw new ArithmeticException("WGS84 direct solution did not converge.");
        var sinS = Math.Sin(sigma);
        var cosS = Math.Cos(sigma);
        var cos2SM = Math.Cos(2 * sigma1 + sigma);
        var temporary = sinU1 * sinS - cosU1 * cosS * cosAlpha1;
        var latitude = Math.Atan2(sinU1 * cosS + cosU1 * sinS * cosAlpha1,
            (1 - Flattening) * Math.Sqrt(sinAlpha * sinAlpha + temporary * temporary));
        var lambda = Math.Atan2(sinS * sinAlpha1, cosU1 * cosS - sinU1 * sinS * cosAlpha1);
        var c = Flattening / 16 * cosSqAlpha * (4 + Flattening * (4 - 3 * cosSqAlpha));
        var longitudeCorrection = lambda - (1 - c) * Flattening * sinAlpha *
            (sigma + c * sinS * (cos2SM + c * cosS * (-1 + 2 * cos2SM * cos2SM)));
        var finalBearing = Math.Atan2(sinAlpha, -temporary) / Radians;
        return new(new(latitude / Radians, NormalizeLongitude(origin.Longitude + longitudeCorrection / Radians)),
            Headings.Normalize(finalBearing));
    }

    public static double NormalizeLongitude(double longitude)
    {
        if (!double.IsFinite(longitude)) throw new ArgumentOutOfRangeException(nameof(longitude));
        var value = longitude % 360;
        if (value < -180) value += 360;
        if (value >= 180) value -= 360;
        return value == 0 ? 0 : value;
    }
}
