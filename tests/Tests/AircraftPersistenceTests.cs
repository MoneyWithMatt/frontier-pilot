using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using System.Text.Json.Nodes;

namespace FrontierAirportBuilder.Tests;

public sealed class AircraftPersistenceTests : TemporaryDirectoryTest
{
    private string FilePath => Path.Combine(Root,"tracking","aircraft-state.json");
    private static AircraftTelemetry Sample(string title="Cessna 208B",double latitude=51.12345) =>
        new(DateTimeOffset.Parse("2026-09-08T16:42:00Z"),title,"C208",latitude,-1.12345,1240,273,84,true);

    [Fact]
    public async Task NoSavedStateReturnsEmpty()
    {
        var store=new LocalAircraftStateStore(FilePath);
        Assert.Null(await store.LoadActiveAsync());Assert.Empty(await store.LoadAllAsync());
    }

    [Fact]
    public async Task SavesAndLoadsAcrossStoreInstances()
    {
        await new LocalAircraftStateStore(FilePath).SaveActiveAsync(Sample());
        var loaded=await new LocalAircraftStateStore(FilePath).LoadActiveAsync();
        Assert.NotNull(loaded);Assert.Equal(Sample(),loaded!.Telemetry);Assert.StartsWith("prototype-",loaded.Id);
    }

    [Fact]
    public async Task SavesAndLoadsRecognisedAirportAndStandMetadata()
    {
        var sample=Sample() with {GroundSpeedKnots=0};
        var location=new AircraftLocationContext(sample.ObservedUtc,"FPRIDGE","Frontier Ridge",2,"PARKING 02",0,3.6,
            AircraftParkingState.Parked,"Parked after 5.0 s.");
        await new LocalAircraftStateStore(FilePath).SaveActiveAsync(sample,location);
        var loaded=await new LocalAircraftStateStore(FilePath).LoadActiveAsync();
        Assert.Equal(location,loaded!.Location);Assert.Equal(sample,loaded.Telemetry);
    }

    [Fact]
    public async Task LoadsTrackingV3DocumentWithoutLocationMetadata()
    {
        var store=new LocalAircraftStateStore(FilePath);await store.SaveActiveAsync(Sample());
        var document=JsonNode.Parse(await File.ReadAllTextAsync(FilePath))!.AsObject();
        document["SchemaVersion"]=1;
        document["Aircraft"]!.AsArray()[0]!.AsObject().Remove("Location");
        await File.WriteAllTextAsync(FilePath,document.ToJsonString());
        var loaded=await new LocalAircraftStateStore(FilePath).LoadActiveAsync();
        Assert.NotNull(loaded);Assert.Null(loaded!.Location);Assert.Equal(Sample(),loaded.Telemetry);
    }

    [Fact]
    public async Task DocumentCanGrowToMultipleAircraftRecords()
    {
        var store=new LocalAircraftStateStore(FilePath);
        await store.SaveActiveAsync(Sample("Cessna 208B"));
        await store.SaveActiveAsync(Sample("De Havilland Beaver",52));
        Assert.Equal(2,(await store.LoadAllAsync()).Count);
        Assert.Equal("De Havilland Beaver",(await store.LoadActiveAsync())!.Telemetry.AircraftTitle);
    }

    [Fact]
    public async Task InvalidTelemetryCannotOverwriteValidState()
    {
        var store=new LocalAircraftStateStore(FilePath);await store.SaveActiveAsync(Sample());
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveActiveAsync(Sample(latitude:double.NaN)));
        Assert.Equal(Sample(),(await store.LoadActiveAsync())!.Telemetry);
    }

    [Fact]
    public async Task CorruptDocumentIsReportedPreservedAndResettable()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);await File.WriteAllTextAsync(FilePath,"{bad json");
        var unrelated=Path.Combine(Path.GetDirectoryName(FilePath)!,"unrelated.txt");await File.WriteAllTextAsync(unrelated,"preserve");
        var store=new LocalAircraftStateStore(FilePath);
        var error=await Assert.ThrowsAsync<IOException>(()=>store.LoadActiveAsync());
        Assert.Contains("corrupt",error.Message,StringComparison.OrdinalIgnoreCase);Assert.True(File.Exists(FilePath));
        await store.ClearAsync();Assert.False(File.Exists(FilePath));Assert.True(File.Exists(unrelated));Assert.Equal("preserve",await File.ReadAllTextAsync(unrelated));Assert.Null(await store.LoadActiveAsync());
    }
}

public sealed class TrackingPersistenceTransitionTests
{
    private sealed class StreamingSource(TrackingWorldState sample) : ITrackingTelemetrySource
    {
        public async Task RunAsync(string sdkRoot,IProgress<TrackingWorldState> updates,CancellationToken token)
        { updates.Report(sample);await Task.Delay(Timeout.Infinite,token); }
    }
    private sealed class InvalidSource(TrackingWorldState sample) : ITrackingTelemetrySource
    {
        public Task RunAsync(string sdkRoot,IProgress<TrackingWorldState> updates,CancellationToken token)
        { updates.Report(sample);return Task.FromException(new IOException("ended")); }
    }
    private sealed class BurstSource(params TrackingWorldState[] samples) : ITrackingTelemetrySource
    {
        public async Task RunAsync(string sdkRoot,IProgress<TrackingWorldState> updates,CancellationToken token)
        { foreach(var sample in samples)updates.Report(sample);await Task.Delay(Timeout.Infinite,token); }
    }

    [Fact]
    public async Task RestoresLastKnownTransitionsLiveAndRetainsLatestOnDisconnect()
    {
        var saved=TrackingTestData.Aircraft with {Latitude=50,ObservedUtc=DateTimeOffset.UtcNow.AddHours(-2)};
        var live=TrackingTestData.InAircraft with {Aircraft=TrackingTestData.Aircraft with {Latitude=52,ObservedUtc=DateTimeOffset.UtcNow}};
        var store=new TestAircraftStore(saved);
        using var vm=new TrackingViewModel(new StreamingSource(live),store,()=>"sdk",persistenceInterval:TimeSpan.FromHours(1));
        var displays=new List<(AircraftTelemetry? Aircraft,bool Live)>();vm.DisplayChanged+=(aircraft,_,isLive)=>displays.Add((aircraft,isLive));
        await vm.InitializeAsync();Assert.False(vm.IsLive);Assert.Equal(50,vm.Current!.Latitude);Assert.Contains("LAST KNOWN",vm.Freshness);
        Assert.Equal((50d,false),(displays[^1].Aircraft!.Latitude,displays[^1].Live));
        var run=vm.ConnectAsync();await Until(()=>vm.IsLive);Assert.Equal(52,vm.Current!.Latitude);Assert.Equal("LIVE",vm.Freshness);
        Assert.Equal((52d,true),(displays[^1].Aircraft!.Latitude,displays[^1].Live));
        vm.Disconnect();await run;Assert.False(vm.IsLive);Assert.Equal(52,vm.Current!.Latitude);Assert.Contains("LAST KNOWN",vm.Freshness);
        Assert.Equal((52d,false),(displays[^1].Aircraft!.Latitude,displays[^1].Live));
        Assert.Equal(52,(await store.LoadActiveAsync())!.Telemetry.Latitude);
    }

    [Fact]
    public async Task InvalidLiveTelemetryDoesNotReplaceRestoredPosition()
    {
        var saved=TrackingTestData.Aircraft with {Latitude=50};
        var invalid=TrackingTestData.InAircraft with {Aircraft=TrackingTestData.Aircraft with {Latitude=double.NaN}};
        var store=new TestAircraftStore(saved);using var vm=new TrackingViewModel(new InvalidSource(invalid),store,()=>"sdk");
        await vm.InitializeAsync();await vm.ConnectAsync();
        Assert.Equal(TrackingState.Error,vm.State);Assert.Equal(50,vm.Current!.Latitude);Assert.Contains("LAST KNOWN",vm.Freshness);
        Assert.Equal(50,(await store.LoadActiveAsync())!.Telemetry.Latitude);
    }

    [Fact]
    public async Task CorruptHistoryDoesNotPreventARecoverableEmptyTrackingPage()
    {
        var store=new TestAircraftStore(loadError:new IOException("corrupt history"));
        using var vm=new TrackingViewModel(new StreamingSource(TrackingTestData.InAircraft),store,()=>"sdk");
        await vm.InitializeAsync();Assert.Null(vm.Current);Assert.Contains("corrupt history",vm.PersistenceWarning);Assert.True(vm.CanClearSavedState);
        await vm.ClearSavedStateAsync();Assert.Empty(vm.PersistenceWarning);Assert.False(vm.CanClearSavedState);
    }

    [Fact]
    public async Task RapidTelemetryIsThrottledButDisconnectFlushesLatestSample()
    {
        var now=DateTimeOffset.UtcNow;
        TrackingWorldState State(double latitude,int seconds)=>TrackingTestData.InAircraft with {Aircraft=TrackingTestData.Aircraft with {Latitude=latitude,ObservedUtc=now.AddSeconds(seconds)}};
        var store=new TestAircraftStore();using var vm=new TrackingViewModel(new BurstSource(State(50,0),State(51,1),State(52,2)),store,()=>"sdk",persistenceInterval:TimeSpan.FromSeconds(10));
        var run=vm.ConnectAsync();await Until(()=>vm.Current?.Latitude==52&&store.SaveCount>=1);
        Assert.Equal(1,store.SaveCount);
        vm.Disconnect();await run;
        Assert.Equal(52,(await store.LoadActiveAsync())!.Telemetry.Latitude);Assert.True(store.SaveCount>=2);
    }

    [Fact]
    public async Task RecognisedFrontierLocationPersistsAndRestoresAsLastKnown()
    {
        var airport=Fixtures.Strip with {Identifier="FPRIDGE",Name="Frontier Ridge",LengthMeters=200,WidthMeters=20,
            ParkingStands=[new(2,35,0,180,10)]};
        var position=LocalCoordinates.ToGeographic(new(airport.Latitude,airport.Longitude),new(35,0));
        var aircraft=TrackingTestData.Aircraft with {ObservedUtc=DateTimeOffset.UtcNow,Latitude=position.Latitude,Longitude=position.Longitude,GroundSpeedKnots=3,IsOnGround=true};
        var live=TrackingTestData.InAircraft with {Aircraft=aircraft};var store=new TestAircraftStore();
        using(var vm=new TrackingViewModel(new StreamingSource(live),store,()=>"sdk"))
        {
            vm.UpdateAirports([airport]);var run=vm.ConnectAsync();await Until(()=>vm.CurrentLocation is not null);
            Assert.Equal("Frontier Ridge (FPRIDGE)",vm.FrontierAirport);Assert.Contains("Stand 02",vm.FrontierStand);Assert.Equal("Near Stand",vm.FrontierParkingState);
            vm.Disconnect();await run;
        }
        using var restored=new TrackingViewModel(new StreamingSource(live),store,()=>"sdk");await restored.InitializeAsync();
        Assert.False(restored.IsLive);Assert.Equal("Frontier Ridge (FPRIDGE)",restored.FrontierAirport);
        Assert.Contains("Stand 02",restored.FrontierStand);Assert.Equal("Near Stand",restored.FrontierParkingState);
    }

    [Fact]
    public async Task AirportCatalogueCanEnrichAnAlreadyPersistedLiveSample()
    {
        var airport=Fixtures.Strip with {Identifier="FPRIDGE",Name="Frontier Ridge",LengthMeters=200,WidthMeters=20,
            ParkingStands=[new(2,35,0,180,10)]};
        var position=LocalCoordinates.ToGeographic(new(airport.Latitude,airport.Longitude),new(35,0));
        var aircraft=TrackingTestData.Aircraft with {ObservedUtc=DateTimeOffset.UtcNow,Latitude=position.Latitude,Longitude=position.Longitude,GroundSpeedKnots=3,IsOnGround=true};
        var live=TrackingTestData.InAircraft with {Aircraft=aircraft};var store=new TestAircraftStore();
        using var vm=new TrackingViewModel(new StreamingSource(live),store,()=>"sdk");var run=vm.ConnectAsync();
        await Until(()=>store.SaveCount>=1);Assert.Null((await store.LoadActiveAsync())!.Location);
        vm.UpdateAirports([airport]);await UntilAsync(async ()=>(await store.LoadActiveAsync())?.Location?.ParkingStandNumber==2);
        vm.Disconnect();await run;
    }

    [Fact]
    public async Task RemovedAirportClearsItsLastKnownAssociationWithoutClearingCoordinates()
    {
        var aircraft=TrackingTestData.Aircraft;
        var location=new AircraftLocationContext(aircraft.ObservedUtc,"FPRIDGE","Frontier Ridge",2,"PARKING 02",0,2,
            AircraftParkingState.Parked,"Parked after 5.0 s.");
        var store=new TestAircraftStore(aircraft,location:location);using var vm=new TrackingViewModel(new StreamingSource(TrackingTestData.InAircraft),store,()=>"sdk");
        await vm.InitializeAsync();vm.UpdateAirports([]);await UntilAsync(async ()=>(await store.LoadActiveAsync())?.Location==null);
        Assert.Equal(aircraft,vm.Current);Assert.Null(vm.CurrentLocation);Assert.Equal("No saved Frontier airport association",vm.FrontierAirport);
    }

    private static async Task Until(Func<bool> condition) { for(var i=0;i<100&&!condition();i++)await Task.Delay(10);Assert.True(condition()); }
    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        for(var i=0;i<100;i++){if(await condition())return;await Task.Delay(10);}Assert.True(await condition());
    }
}

internal sealed class TestAircraftStore : IAircraftStateStore
{
    private readonly List<PersistedAircraftState> records=[];
    private readonly object sync=new();
    private Exception? loadError;
    private int saveCount;
    public TestAircraftStore(AircraftTelemetry? seed=null,Exception? loadError=null,AircraftLocationContext? location=null)
    { this.loadError=loadError;if(seed is not null)records.Add(new("test-aircraft",seed,location)); }
    public string StoragePath=>"test-memory";
    public int SaveCount{get{lock(sync)return saveCount;}}
    public Task<PersistedAircraftState?> LoadActiveAsync(CancellationToken cancellationToken=default)
    { lock(sync){if(loadError is {} error)throw error;return Task.FromResult(records.LastOrDefault());} }
    public Task<IReadOnlyList<PersistedAircraftState>> LoadAllAsync(CancellationToken cancellationToken=default){lock(sync)return Task.FromResult<IReadOnlyList<PersistedAircraftState>>(records.ToArray());}
    public Task SaveActiveAsync(AircraftTelemetry aircraft,AircraftLocationContext? location=null,CancellationToken cancellationToken=default)
    { aircraft=aircraft.EnsureValid();lock(sync){records.RemoveAll(x=>x.Id=="test-aircraft");records.Add(new("test-aircraft",aircraft,location));saveCount++;}return Task.CompletedTask; }
    public Task ClearAsync(CancellationToken cancellationToken=default){lock(sync){records.Clear();loadError=null;}return Task.CompletedTask;}
}
