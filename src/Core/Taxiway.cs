using System.Text.RegularExpressions;
namespace FrontierAirportBuilder.Core;

// Parking references resolve against the current stand position, so moving a stand keeps its connection.
public sealed record TaxiwayVertex(double EastMeters,double NorthMeters,int? ParkingNumber=null)
{
    public LocalPoint Resolve(IReadOnlyList<ParkingStand> stands) => ParkingNumber is {} n
        ? stands.FirstOrDefault(s=>s!=null&&s.Number==n) is {} s ? new(s.EastMeters,s.NorthMeters) : throw new ArgumentException($"Taxiway references missing parking {n}.")
        : new(EastMeters,NorthMeters);
}
// Surface intentionally follows the airport's existing qualified material (Dirt or Asphalt).
public sealed record Taxiway(int Number,string Name,double WidthMeters,IReadOnlyList<TaxiwayVertex> Points);
public static class TaxiwayGeometry
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<Taxiway>? paths,IReadOnlyList<ParkingStand> stands,AirportSite site)
    {
        var errors=new List<string>();
        if(paths==null)return ["Taxiways cannot be null."];
        if(paths.Count>64)errors.Add("This prototype supports up to 64 taxiways.");
        var numbers=new HashSet<int>();var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var t in paths)
        {
            if(t==null){errors.Add("Taxiway is missing.");continue;}
            if(t.Number is <1 or >255 || !numbers.Add(t.Number))errors.Add("Taxiway numbers must be unique, from 1 to 255.");
            if(t.Name==null||!Regex.IsMatch(t.Name,"\\A[A-Z0-9]{1,8}\\z")||!names.Add(t.Name))errors.Add("Taxiway names must be unique, with 1–8 uppercase letters/digits.");
            if(!double.IsFinite(t.WidthMeters)||t.WidthMeters is <3 or >60)errors.Add($"Taxiway {t.Name}: width must be 3–60 m.");
            if(t.Points==null||t.Points.Count is <2 or >128){errors.Add($"Taxiway {t.Name}: use 2–128 points.");continue;}
            LocalPoint? previous=null;
            for(int i=0;i<t.Points.Count;i++)
            {
                var v=t.Points[i];
                if(v==null||!double.IsFinite(v.EastMeters)||!double.IsFinite(v.NorthMeters)){errors.Add($"Taxiway {t.Name}: points must be finite.");continue;}
                if(v.ParkingNumber!=null && i!=0 && i!=t.Points.Count-1)errors.Add($"Taxiway {t.Name}: parking connections belong at path ends.");
                try
                {
                    var p=v.Resolve(stands);
                    if(!site.Contains(p,t.WidthMeters/2))errors.Add($"Taxiway {t.Name}: {AirportSite.BoundaryMessage}");
                    if(previous is {} a && Distance(a,p)<1)errors.Add($"Taxiway {t.Name}: successive points must be at least 1 m apart.");
                    previous=p;
                }
                catch(ArgumentException e){errors.Add(e.Message);}
            }
        }
        return errors;
    }
    public static double Distance(LocalPoint a,LocalPoint b)=>Math.Sqrt(Math.Pow(a.East-b.East,2)+Math.Pow(a.North-b.North,2));
}
