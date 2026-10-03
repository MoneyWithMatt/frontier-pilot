namespace FrontierAirportBuilder.Core;

/// <summary>Local east/north metres from the surveyed anchor; heading is true north based.</summary>
public enum ParkingStandType { RAMP_GA_SMALL, RAMP_GA_MEDIUM, RAMP_GA_LARGE }
public enum ParkingStandName { PARKING, N_PARKING, S_PARKING, E_PARKING, W_PARKING }
public sealed record ParkingStand(int Number, double EastMeters, double NorthMeters, double TrueHeadingDegrees, double RadiusMeters,
    ParkingStandType Type = ParkingStandType.RAMP_GA_SMALL, ParkingStandName Name = ParkingStandName.PARKING);
public sealed record ParkingPlacement(ParkingStand Stand, GeoPoint Position, double TrueHeadingDegrees);

public static class ParkingGeometry
{
    public static ParkingPlacement Calculate(AirstripDefinition airport, ParkingStand stand)
    {
        var position = LocalCoordinates.ToGeographic(new(airport.Latitude, airport.Longitude),new(stand.EastMeters,stand.NorthMeters));
        return new(stand,position,Headings.Normalize(stand.TrueHeadingDegrees));
    }
    public static IReadOnlyList<string> Validate(IReadOnlyList<ParkingStand>? stands, double length, double width, double runwayHeading = 0, bool hasRunway = true, double runwayEast=0,double runwayNorth=0)
    {
        var errors = new List<string>();
        if (stands == null) return ["Parking stands cannot be null."];
        if (stands.Count > 8) errors.Add("This prototype supports up to eight parking stands.");
        var numbers = new HashSet<int>();
        foreach (var s in stands)
        {
            if (s == null) { errors.Add("Parking stand is missing."); continue; }
            if (s.Number is < 1 or > 99 || !numbers.Add(s.Number)) errors.Add("Parking numbers must be unique, from 1 to 99.");
            if (new[]{s.NorthMeters,s.EastMeters,s.TrueHeadingDegrees,s.RadiusMeters}.Any(v=>!double.IsFinite(v)))
            { errors.Add($"Parking {s.Number}: all values must be finite."); continue; }
            if (s.RadiusMeters is < 5 or > 30) errors.Add($"Parking {s.Number}: radius must be 5–30 metres.");
            if (!new AirportSite(length,width,runwayHeading,runwayEast,runwayNorth).Contains(new(s.EastMeters,s.NorthMeters),s.RadiusMeters))
                errors.Add($"Parking {s.Number}: {AirportSite.BoundaryMessage}");
            if(!Enum.IsDefined(s.Type)||!Enum.IsDefined(s.Name)) errors.Add($"Parking {s.Number}: unsupported type or name.");
            var local=LocalCoordinates.ToRunway(new(s.EastMeters-runwayEast,s.NorthMeters-runwayNorth),double.IsFinite(runwayHeading)?runwayHeading:0);
            double x = Math.Max(Math.Abs(local.East) - width/2,0), y = Math.Max(Math.Abs(local.North)-length/2,0);
            if (hasRunway && Math.Sqrt(x*x+y*y) < s.RadiusMeters+2)
                errors.Add($"Parking {s.Number}: leave at least 2 metres between the stand circle and the runway.");
        }
        for (int i=0;i<stands.Count;i++)
            for (int j=i+1;j<stands.Count;j++)
            {
                var a=stands[i];var b=stands[j];if(a==null||b==null)continue;
                double x=a.EastMeters-b.EastMeters,y=a.NorthMeters-b.NorthMeters;
                if(Math.Sqrt(x*x+y*y)<a.RadiusMeters+b.RadiusMeters+2)
                    errors.Add($"Parking {a.Number} and {b.Number}: stand circles need at least 2 metres separation.");
            }
        return errors;
    }
}
