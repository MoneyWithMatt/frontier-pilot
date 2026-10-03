namespace FrontierAirportBuilder.Core;

public sealed record AirportRecognitionOptions(
    double AirportMarginMeters = 250,
    double StandMarginMeters = 5,
    double ParkedMaximumGroundSpeedKnots = 1,
    double ParkedDurationSeconds = 5,
    double MaximumObservationGapSeconds = 2)
{
    public void EnsureValid()
    {
        if(new[]{AirportMarginMeters,StandMarginMeters,ParkedMaximumGroundSpeedKnots,ParkedDurationSeconds,MaximumObservationGapSeconds}
           .Any(value=>!double.IsFinite(value)||value<0) || AirportMarginMeters>5000 || StandMarginMeters>100 ||
           ParkedMaximumGroundSpeedKnots>20 || ParkedDurationSeconds>120 || MaximumObservationGapSeconds<=0)
            throw new ArgumentOutOfRangeException(nameof(AirportRecognitionOptions));
    }
}

public sealed record AircraftLocationRecognition(
    AircraftLocationContext? Context,
    string? NearestAirportIdentifier,
    double? NearestAirportDistanceMeters,
    int? NearestStandNumber,
    double? NearestStandDistanceMeters,
    string DebugReason);

/// <summary>Recognises aircraft against saved Frontier airport geometry. Stateful only for the stopped timer.</summary>
public sealed class AirportLocationRecognizer
{
    private readonly AirportRecognitionOptions options;
    private string? stoppedKey;
    private DateTimeOffset stoppedSince,lastObservation;

    public AirportLocationRecognizer(AirportRecognitionOptions? options=null)
    {
        this.options=options??new();this.options.EnsureValid();
    }

    public AircraftLocationRecognition Evaluate(AircraftTelemetry sample,IReadOnlyList<AirstripDefinition> airports)
    {
        sample=sample.EnsureValid();
        if(airports==null)throw new ArgumentNullException(nameof(airports));
        var candidates=airports.Where(a=>a!=null).Select(a=>(Airport:a,Distance:InfrastructureDistance(a,sample))).
            OrderBy(row=>row.Distance).ThenBy(row=>row.Airport.Identifier,StringComparer.Ordinal).ToArray();
        if(candidates.Length==0){Reset();return new(null,null,null,null,null,"No saved Frontier airports are available.");}
        var nearest=candidates[0];
        if(nearest.Distance>options.AirportMarginMeters)
        {
            Reset();
            return new(null,nearest.Airport.Identifier,nearest.Distance,null,null,
                $"Outside airport boundary: nearest {nearest.Airport.Identifier} infrastructure is {nearest.Distance:F1} m away (limit {options.AirportMarginMeters:F1} m).");
        }

        var point=LocalCoordinates.FromGeographic(new(nearest.Airport.Latitude,nearest.Airport.Longitude),new(sample.Latitude,sample.Longitude));
        var stands=nearest.Airport.ParkingStands.Select(stand=>(Stand:stand,Distance:TaxiwayGeometry.Distance(point,new(stand.EastMeters,stand.NorthMeters)))).
            OrderBy(row=>row.Distance).ThenBy(row=>row.Stand.Number).ToArray();
        var closest=stands.FirstOrDefault();
        if(!sample.IsOnGround)
        {
            Reset();
            return Result(sample.ObservedUtc,nearest,closest,null,AircraftParkingState.NotAtStand,"Aircraft is airborne; stand recognition requires on-ground telemetry.");
        }
        if(closest.Stand is null)
        {
            Reset();return Result(sample.ObservedUtc,nearest,closest,null,AircraftParkingState.NotAtStand,"Airport has no parking stands.");
        }
        var threshold=closest.Stand.RadiusMeters+options.StandMarginMeters;
        if(closest.Distance>threshold)
        {
            Reset();return Result(sample.ObservedUtc,nearest,closest,null,AircraftParkingState.NotAtStand,
                $"Nearest stand {closest.Stand.Number:00} is {closest.Distance:F1} m away (near-stand limit {threshold:F1} m).");
        }

        var key=nearest.Airport.Identifier+"/"+closest.Stand.Number;
        if(sample.GroundSpeedKnots>options.ParkedMaximumGroundSpeedKnots)
        {
            Reset();
            return Result(sample.ObservedUtc,nearest,closest,closest.Stand,AircraftParkingState.NearStand,
                $"Near stand {closest.Stand.Number:00}, but moving at {sample.GroundSpeedKnots:F1} kt (parked limit {options.ParkedMaximumGroundSpeedKnots:F1} kt).");
        }
        if(stoppedKey!=key || lastObservation==default || sample.ObservedUtc<lastObservation ||
           (sample.ObservedUtc-lastObservation).TotalSeconds>options.MaximumObservationGapSeconds)
        {
            stoppedKey=key;stoppedSince=sample.ObservedUtc;
        }
        lastObservation=sample.ObservedUtc;
        var stopped=(sample.ObservedUtc-stoppedSince).TotalSeconds;
        var parked=stopped>=options.ParkedDurationSeconds;
        return Result(sample.ObservedUtc,nearest,closest,closest.Stand,parked?AircraftParkingState.Parked:AircraftParkingState.NearStand,
            parked?$"Parked after {stopped:F1} s continuously grounded below {options.ParkedMaximumGroundSpeedKnots:F1} kt.":
            $"Near stand {closest.Stand.Number:00}; stopped for {stopped:F1} of {options.ParkedDurationSeconds:F1} required seconds.");
    }

    private AircraftLocationRecognition Result(DateTimeOffset observedUtc,(AirstripDefinition Airport,double Distance) airport,(ParkingStand? Stand,double Distance) nearest,
        ParkingStand? recognised,AircraftParkingState state,string reason)
    {
        var context=new AircraftLocationContext(observedUtc,airport.Airport.Identifier,airport.Airport.Name,
            recognised?.Number,recognised is null?null:$"{recognised.Name} {recognised.Number:00}",airport.Distance,
            nearest.Stand is null?null:nearest.Distance,state,reason);
        return new(context,airport.Airport.Identifier,airport.Distance,nearest.Stand?.Number,nearest.Stand is null?null:nearest.Distance,reason);
    }

    private double InfrastructureDistance(AirstripDefinition airport,AircraftTelemetry sample)
    {
        var anchorDistance=WorldTracking.DistanceMeters(airport.Latitude,airport.Longitude,sample.Latitude,sample.Longitude);
        if(anchorDistance>Geodesy.MaximumDistanceMeters+options.AirportMarginMeters)return Math.Max(0,anchorDistance-Geodesy.MaximumDistanceMeters);
        var point=LocalCoordinates.FromGeographic(new(airport.Latitude,airport.Longitude),new(sample.Latitude,sample.Longitude));
        var runway=LocalCoordinates.ToRunway(new(point.East-airport.RunwayEastMeters,point.North-airport.RunwayNorthMeters),airport.TrueHeadingDegrees);
        var dx=Math.Max(Math.Abs(runway.East)-airport.WidthMeters/2,0);
        var dy=Math.Max(Math.Abs(runway.North)-airport.LengthMeters/2,0);
        var distance=Math.Sqrt(dx*dx+dy*dy);
        foreach(var stand in airport.ParkingStands)
            distance=Math.Min(distance,Math.Max(0,TaxiwayGeometry.Distance(point,new(stand.EastMeters,stand.NorthMeters))-stand.RadiusMeters));
        foreach(var path in airport.Taxiways)
            for(var index=1;index<path.Points.Count;index++)
                distance=Math.Min(distance,Math.Max(0,DistanceToSegment(point,path.Points[index-1].Resolve(airport.ParkingStands),path.Points[index].Resolve(airport.ParkingStands))-path.WidthMeters/2));
        return distance;
    }

    private static double DistanceToSegment(LocalPoint point,LocalPoint start,LocalPoint end)
    {
        var dx=end.East-start.East;var dy=end.North-start.North;var lengthSquared=dx*dx+dy*dy;
        if(lengthSquared==0)return TaxiwayGeometry.Distance(point,start);
        var t=Math.Clamp(((point.East-start.East)*dx+(point.North-start.North)*dy)/lengthSquared,0,1);
        return TaxiwayGeometry.Distance(point,new(start.East+t*dx,start.North+t*dy));
    }
    private void Reset(){stoppedKey=null;stoppedSince=lastObservation=default;}
}
