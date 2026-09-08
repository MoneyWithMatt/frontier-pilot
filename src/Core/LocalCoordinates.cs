namespace FrontierAirportBuilder.Core;

public readonly record struct LocalPoint(double East, double North);
public static class LocalCoordinates
{
    public static GeoPoint ToGeographic(GeoPoint anchor, LocalPoint offset)
    {
        if(!double.IsFinite(offset.East)||!double.IsFinite(offset.North)) throw new ArgumentException("Local coordinates must be finite.");
        double distance=Math.Sqrt(offset.East*offset.East+offset.North*offset.North);
        return Geodesy.Direct(anchor,Math.Atan2(offset.East,offset.North)*180/Math.PI,distance).Position;
    }
    /// <summary>WGS84 local tangent-plane offset for airport-scale inverse calculations.</summary>
    public static LocalPoint FromGeographic(GeoPoint anchor, GeoPoint position)
    {
        if(new[]{anchor.Latitude,anchor.Longitude,position.Latitude,position.Longitude}.Any(v=>!double.IsFinite(v)) ||
           Math.Abs(anchor.Latitude)>=90 || Math.Abs(position.Latitude)>=90 ||
           Math.Abs(anchor.Longitude)>180 || Math.Abs(position.Longitude)>180)
            throw new ArgumentOutOfRangeException(nameof(position));
        var inverse=Geodesy.Inverse(anchor,position);var bearing=inverse.InitialBearingDegrees*Math.PI/180;
        return new(inverse.DistanceMeters*Math.Sin(bearing),inverse.DistanceMeters*Math.Cos(bearing));
    }
    // Return East=right, North=forward in a runway-aligned frame.
    public static LocalPoint ToRunway(LocalPoint p,double heading)
    {
        double h=Headings.Normalize(heading)*Math.PI/180;
        return new(p.East*Math.Cos(h)-p.North*Math.Sin(h),p.East*Math.Sin(h)+p.North*Math.Cos(h));
    }
    public static LocalPoint FromRunway(double right,double forward,double heading)
    {
        double h=Headings.Normalize(heading)*Math.PI/180;
        return new(right*Math.Cos(h)+forward*Math.Sin(h),-right*Math.Sin(h)+forward*Math.Cos(h));
    }
}
