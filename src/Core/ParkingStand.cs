namespace FrontierAirportBuilder.Core;

/// <summary>Local placement relative to the captured runway centre and true heading.</summary>
public sealed record ParkingStand(int Number, double ForwardMeters, double RightMeters, double FacingOffsetDegrees, double RadiusMeters);
public sealed record ParkingPlacement(ParkingStand Stand, GeoPoint Position, double TrueHeadingDegrees);

public static class ParkingGeometry
{
    public static ParkingPlacement Calculate(AirstripDefinition airport, ParkingStand stand)
    {
        double distance = Math.Sqrt(stand.ForwardMeters * stand.ForwardMeters + stand.RightMeters * stand.RightMeters);
        double bearing = airport.TrueHeadingDegrees + Math.Atan2(stand.RightMeters, stand.ForwardMeters) * 180 / Math.PI;
        var position = Geodesy.Direct(new(airport.Latitude,airport.Longitude),bearing,distance).Position;
        return new(stand, position, Headings.Normalize(airport.TrueHeadingDegrees + stand.FacingOffsetDegrees));
    }
    public static IReadOnlyList<string> Validate(IReadOnlyList<ParkingStand>? stands, double length, double width)
    {
        var errors = new List<string>();
        if (stands == null) return ["Parking stands cannot be null."];
        if (stands.Count > 8) errors.Add("This prototype supports up to eight parking stands.");
        var numbers = new HashSet<int>();
        foreach (var s in stands)
        {
            if (s == null) { errors.Add("Parking stand is missing."); continue; }
            if (s.Number is < 1 or > 99 || !numbers.Add(s.Number)) errors.Add("Parking numbers must be unique, from 1 to 99.");
            if (new[]{s.ForwardMeters,s.RightMeters,s.FacingOffsetDegrees,s.RadiusMeters}.Any(v=>!double.IsFinite(v)))
            { errors.Add($"Parking {s.Number}: all values must be finite."); continue; }
            if (s.RadiusMeters is < 5 or > 30) errors.Add($"Parking {s.Number}: radius must be 5–30 metres.");
            if (Math.Sqrt(s.ForwardMeters*s.ForwardMeters+s.RightMeters*s.RightMeters)+s.RadiusMeters > 500)
                errors.Add($"Parking {s.Number}: keep the complete stand within 500 metres of the runway centre.");
            double x = Math.Max(Math.Abs(s.RightMeters) - width/2,0), y = Math.Max(Math.Abs(s.ForwardMeters)-length/2,0);
            if (Math.Sqrt(x*x+y*y) < s.RadiusMeters+2)
                errors.Add($"Parking {s.Number}: leave at least 2 metres between the stand circle and the runway.");
        }
        for (int i=0;i<stands.Count;i++)
            for (int j=i+1;j<stands.Count;j++)
            {
                var a=stands[i];var b=stands[j];if(a==null||b==null)continue;
                double x=a.RightMeters-b.RightMeters,y=a.ForwardMeters-b.ForwardMeters;
                if(Math.Sqrt(x*x+y*y)<a.RadiusMeters+b.RadiusMeters+2)
                    errors.Add($"Parking {a.Number} and {b.Number}: stand circles need at least 2 metres separation.");
            }
        return errors;
    }
}
