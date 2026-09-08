namespace FrontierAirportBuilder.Core;

/// <summary>Shared local site envelope: runway footprint expanded by 3 km, within the
/// supported 10 km geodesic range. Footprints, not just centres, must fit.</summary>
public sealed record AirportSite(double RunwayLength, double RunwayWidth, double TrueHeading)
{
    public const double ExpansionMeters = 3000;
    public const double MaximumReachMeters = Geodesy.MaximumDistanceMeters;
    public const string BoundaryMessage = "Keep infrastructure within 3 km of the runway footprint and within 10 km of the survey anchor.";
    public bool Contains(LocalPoint point, double footprintRadius = 0)
    {
        if (new[]{point.East,point.North,footprintRadius,RunwayLength,RunwayWidth,TrueHeading}.Any(v=>!double.IsFinite(v)) ||
            footprintRadius<0 || RunwayLength<=0 || RunwayWidth<=0) return false;
        var p=LocalCoordinates.ToRunway(point,TrueHeading);
        double x=Math.Max(Math.Abs(p.East)-RunwayWidth/2,0), y=Math.Max(Math.Abs(p.North)-RunwayLength/2,0);
        return Math.Sqrt(x*x+y*y)+footprintRadius<=ExpansionMeters &&
            Math.Sqrt(point.East*point.East+point.North*point.North)+footprintRadius<=MaximumReachMeters;
    }
}
