using System.Text;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class SimulatorAirportLookupTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"FrontierAirportLookupTests",Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-16T12:00:00Z");
    private static SimulatorAirportScan Scan(params SimulatorAirport[] airports)=>new(Now,"Synthetic test; not live simulator evidence",airports);
    private static SimulatorAirportScan All()=>Scan(EmployerDestinationCatalogue.Research.Where(x=>x.Identifier.Length>0).DistinctBy(x=>x.Identifier).Select((x,i)=>new SimulatorAirport(x.Identifier,-22-i*.01,16)).ToArray());
    private static byte[] Packet(uint number,uint total, string id="FYNG")
    {
        var bytes=new byte[64];
        void U(int offset,uint value)=>BitConverter.GetBytes(value).CopyTo(bytes,offset);
        U(0,64);U(8,18);U(12,700);U(16,1);U(20,number);U(24,total);
        Encoding.ASCII.GetBytes(id).CopyTo(bytes,28);Encoding.ASCII.GetBytes("FY").CopyTo(bytes,37);
        BitConverter.GetBytes(-22.5).CopyTo(bytes,40);BitConverter.GetBytes(16.25).CopyTo(bytes,48);
        return bytes;
    }
    [Fact] public void WorldListRequiresEveryPacketAndUses2024RegionFieldLayout()
    {
        var reader=new AirportListPackets();Assert.False(reader.Add(Packet(1,2)));Assert.True(reader.Add(Packet(0,2,"FYWE")));
        Assert.Equal(-22.5,reader.Airports[0].Latitude);Assert.Equal(16.25,reader.Airports[0].Longitude);
        Assert.Equal("FYNG",reader.Airports[0].Identifier);
    }
    [Fact] public void MalformedDuplicateAndInconsistentPacketsCannotCompleteLookup()
    {
        Assert.Throws<IOException>(()=>new AirportListPackets().Add(Packet(0,1)[..61]));
        Assert.Throws<IOException>(()=>new AirportListPackets().Add(Packet(2,2)));
        var reader=new AirportListPackets();reader.Add(Packet(0,2));Assert.Throws<IOException>(()=>reader.Add(Packet(0,2)));
        reader=new();reader.Add(Packet(0,2));Assert.Throws<IOException>(()=>reader.Add(Packet(1,3)));
    }
    [Theory][InlineData("Missionary")][InlineData("Industrial")][InlineData("Safari")][InlineData("Offshore")]
    public async Task FoundAirportsEnableJobsWithoutManualReviews(string category)
    {
        new SimulatorAirportStore(root).Save(All());var catalogue=new DestinationReviewStore(root).Load();
        Assert.Empty(catalogue.Reviews);
        var employer=CareerCatalogue.Employers.Single(x=>x.Category==category);
        var store=new MemoryEconomyStateStore();var service=new EmployedCargoJobsService(store,utcNow:()=>Now,employer:employer,destinations:catalogue);
        var board=await service.LoadAsync();Assert.NotEmpty(board.Offers);
        var enabled=catalogue.Diagnose(category).Where(x=>x.Enabled).Select(x=>x.Resolved!.StableId).ToHashSet();
        Assert.All(board.Offers,x=>Assert.Contains(x.DestinationLocationId!,enabled));
        Assert.All(catalogue.Diagnose().Where(x=>x.Research.Identifier.Length==0),x=>Assert.False(x.Enabled));
        Assert.Equal("SimConnectAirportCatalogue",catalogue.Resolve(EmployerDestinationCatalogue.Research.First(x=>x.Identifier=="FYNG"))!.VerificationEvidence!.Method);
    }
    [Fact] public void MissingAndAmbiguousAirportsAreFlaggedAndExcluded()
    {
        new SimulatorAirportStore(root).Save(Scan(new("FYWE",-22.6,17),new("FYNG",-22,16),new("FYNG",-21,16)));
        var catalogue=new DestinationReviewStore(root).Load();
        Assert.Contains("Ambiguous",catalogue.Diagnose("Safari").Single(x=>x.Identifier=="FYNG").Recognition);
        Assert.Contains("Not found",catalogue.Diagnose("Industrial").Single(x=>x.Identifier=="FYAR").Recognition);
        Assert.Empty(catalogue.Diagnose().Where(x=>x.Enabled));
        Assert.Empty(CompanyEmployment.Create(CareerCatalogue.Employers.First(),catalogue,Now).Jobs);
    }
    [Fact] public void ChangedAndMissingAirportsRetainAcceptedRouteCoordinates()
    {
        var store=new SimulatorAirportStore(root);store.Save(All());var catalogue=new DestinationReviewStore(root).Load();
        var row=EmployerDestinationCatalogue.Research.First(x=>x.Identifier=="FYNG");var first=catalogue.Resolve(row)!;
        var changed=All();changed=changed with {Airports=changed.Airports.Select(x=>x.Identifier=="FYNG"?x with {Latitude=-20}:x).ToArray()};
        catalogue.ApplySimulatorCache(store.Save(changed));Assert.Equal(first.ApproximateCoordinates,catalogue.Resolve(row)!.ApproximateCoordinates);
        Assert.False(catalogue.Diagnose("Safari").Single(x=>x.Identifier=="FYNG").Enabled);
        catalogue.ApplySimulatorCache(store.Save(Scan()));Assert.NotNull(catalogue.Find(first.StableId));Assert.Empty(catalogue.Diagnose().Where(x=>x.Enabled));
    }
    [Fact] public void ReviewsOverrideAutomaticResolutionAndResetPreservesCache()
    {
        new SimulatorAirportStore(root).Save(All());var reviews=new DestinationReviewStore(root);
        reviews.Save(new("FYNG","Needs scenery","FYNG",null,null,"Synthetic review",Now,[]));
        new CareerStore(root).RequestReset(true);new CareerStore(root).Load();
        Assert.False(reviews.Load().Diagnose("Safari").Single(x=>x.Identifier=="FYNG").Enabled);
        Assert.NotNull(new SimulatorAirportStore(root).Load());
    }
    [Fact] public void CorruptAndFutureCacheIsPreservedInsteadOfOverwritten()
    {
        Directory.CreateDirectory(root);var path=Path.Combine(root,"simulator-airports.json");
        foreach(var value in new[]{"broken","{\"SchemaVersion\":999}"})
        {
            File.WriteAllText(path,value);Assert.ThrowsAny<Exception>(()=>new SimulatorAirportStore(root).Save(All()));Assert.Equal(value,File.ReadAllText(path));
        }
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
