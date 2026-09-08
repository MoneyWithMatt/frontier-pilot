using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;
using FrontierAirportBuilder.MsfsSdk;

namespace FrontierAirportBuilder.Tests;

public sealed class AircraftTelemetryTests
{
    [Fact]
    public void ValidatesAndNormalizesAircraftObservation()
    {
        var value = new AircraftTelemetry(DateTimeOffset.UtcNow, "  Cessna 208B  ", " C208 ", 51.12345, -1.12345, 1240, 361, 84, false).EnsureValid();
        Assert.Equal("Cessna 208B", value.AircraftTitle);
        Assert.Equal("C208", value.AircraftType);
        Assert.Equal(1, value.TrueHeadingDegrees);
    }

    [Theory]
    [InlineData(double.NaN, 0, 100, 0, 0)]
    [InlineData(91, 0, 100, 0, 0)]
    [InlineData(0, 181, 100, 0, 0)]
    [InlineData(0, 0, 300000, 0, 0)]
    [InlineData(0, 0, 100, double.NaN, 0)]
    [InlineData(0, 0, 100, 0, -1)]
    public void RejectsInvalidAircraftObservation(double latitude,double longitude,double altitude,double heading,double speed)
    {
        var value = new AircraftTelemetry(DateTimeOffset.UtcNow,"Aircraft","Type",latitude,longitude,altitude,heading,speed,true);
        Assert.Throws<InvalidDataException>(()=>value.EnsureValid());
    }

    [Fact]
    public void KeepsAircraftAndWalkingPlayerAsIndependentWorldPositions()
    {
        var player = new PlayerTelemetry(DateTimeOffset.UtcNow, 32768, 51.12400, -1.12300, 300, 42, true);
        var state = new TrackingWorldState(TrackingTestData.Aircraft, 524288, 32768, 32768, PlayerControlMode.OnFoot, player, "verified").EnsureValid();
        Assert.Equal(TrackingTestData.Aircraft.Latitude, state.Aircraft.Latitude);
        Assert.Equal(player.Latitude, state.Player!.Latitude);
        Assert.True(state.DistanceFromAircraftMeters > 50);
    }

    [Fact]
    public void RefusesAvatarWhenCurrentObjectIdentityDoesNotMatch()
    {
        var player = new PlayerTelemetry(DateTimeOffset.UtcNow, 32768, 51.124, -1.123, 300, 42, true);
        var state = new TrackingWorldState(TrackingTestData.Aircraft, 524288, 999, 32768, PlayerControlMode.OnFoot, player, "bad");
        Assert.Throws<InvalidDataException>(() => state.EnsureValid());
    }

    [Fact]
    public void UnknownStateCannotLeakUnverifiedPlayerPosition()
    {
        var player = new PlayerTelemetry(DateTimeOffset.UtcNow, 32768, 51.124, -1.123, 300, 42, true);
        var state = new TrackingWorldState(TrackingTestData.Aircraft, 524288, 999, 32768, PlayerControlMode.Unknown, player, "bad");
        Assert.Throws<InvalidDataException>(() => state.EnsureValid());
    }
}

public sealed class TrackingViewModelTests
{
    private sealed class StreamingFake(TrackingWorldState sample) : ITrackingTelemetrySource
    {
        public string? SdkRoot;
        public async Task RunAsync(string sdkRoot,IProgress<TrackingWorldState> updates,CancellationToken token)
        { SdkRoot=sdkRoot;updates.Report(sample);await Task.Delay(Timeout.Infinite,token); }
    }
    private sealed class FailingFake(Exception error) : ITrackingTelemetrySource
    { public Task RunAsync(string sdkRoot,IProgress<TrackingWorldState> updates,CancellationToken token)=>Task.FromException(error); }
    private sealed class SequenceFake(params TrackingWorldState[] samples) : ITrackingTelemetrySource
    {
        public async Task RunAsync(string sdkRoot,IProgress<TrackingWorldState> updates,CancellationToken token)
        { foreach(var sample in samples)updates.Report(sample);await Task.Delay(Timeout.Infinite,token); }
    }
    private sealed class DropsAfterSample : ITrackingTelemetrySource
    {
        public Task RunAsync(string sdkRoot,IProgress<TrackingWorldState> updates,CancellationToken token)
        { updates.Report(TrackingTestData.InAircraft);return Task.FromException(new IOException("connection lost")); }
    }

    [Fact]
    public async Task ConnectsDisplaysAircraftAndDisconnectsCleanly()
    {
        var source=new StreamingFake(TrackingTestData.InAircraft);using var vm=new TrackingViewModel(source,new TestAircraftStore(),()=>"sdk-root");
        var run=vm.ConnectAsync();await Until(()=>vm.State==TrackingState.Connected);
        Assert.Equal("sdk-root",source.SdkRoot);Assert.True(vm.HasTelemetry);Assert.False(vm.HasPlayer);
        Assert.Equal("51.12345, -1.12345",vm.Position);Assert.Equal("1,240 ft",vm.Altitude);
        Assert.Equal("273° true",vm.Heading);Assert.Equal("84.0 kt",vm.GroundSpeed);Assert.Equal("On Ground",vm.FlightState);
        Assert.Equal("In Aircraft",vm.PlayerState);
        vm.Disconnect();await run;
        Assert.Equal(TrackingState.Disconnected,vm.State);Assert.True(vm.HasTelemetry);Assert.False(vm.IsLive);Assert.Contains("LAST KNOWN",vm.Freshness);Assert.Empty(vm.Error);
    }

    [Fact]
    public async Task WalkingAvatarMovesIndependentlyAndReturningHidesPlayerMarkerData()
    {
        var player = new PlayerTelemetry(DateTimeOffset.UtcNow,32768,51.124,-1.123,1241,42,true);
        var onFoot = new TrackingWorldState(TrackingTestData.Aircraft,524288,32768,32768,PlayerControlMode.OnFoot,player,"agree","SDK test");
        using var vm=new TrackingViewModel(new SequenceFake(onFoot,TrackingTestData.InAircraft),new TestAircraftStore(),()=>"sdk");
        var observed = new List<TrackingWorldState?>(); vm.TelemetryChanged += observed.Add;
        var run=vm.ConnectAsync();await Until(()=>observed.Any(x=>x?.PlayerMode==PlayerControlMode.OnFoot)&&vm.PlayerState=="In Aircraft");
        var walking=observed.First(x=>x?.PlayerMode==PlayerControlMode.OnFoot)!;
        Assert.Equal(TrackingTestData.Aircraft.Latitude,walking.Aircraft.Latitude);
        Assert.Equal(player.Latitude,walking.Player!.Latitude);Assert.True(walking.DistanceFromAircraftMeters>50);
        Assert.False(vm.HasPlayer);Assert.Equal("—",vm.PlayerPosition);
        vm.Disconnect();await run;
    }

    [Fact]
    public async Task UnknownModeExposesReasonWithoutFabricatingPlayer()
    {
        var unknown=TrackingTestData.InAircraft with {CurrentObjectId=999,PlayerMode=PlayerControlMode.Unknown,PlayerTrackingStatus="identity mismatch"};
        using var vm=new TrackingViewModel(new StreamingFake(unknown),new TestAircraftStore(),()=>"sdk");
        var run=vm.ConnectAsync();await Until(()=>vm.State==TrackingState.Connected);
        Assert.Equal("Unknown · position refused",vm.PlayerState);Assert.False(vm.HasPlayer);Assert.Equal("—",vm.PlayerPosition);
        Assert.Contains("identity mismatch",vm.PlayerTrackingStatus); vm.Disconnect();await run;
    }

    [Fact]
    public async Task ConnectionFailureBecomesRecoverableDisconnectedState()
    {
        using var vm=new TrackingViewModel(new FailingFake(new IOException("MSFS unavailable")),new TestAircraftStore(),()=>"sdk");
        await vm.ConnectAsync();Assert.Equal(TrackingState.Error,vm.State);Assert.Contains("MSFS unavailable",vm.Error);
        Assert.True(vm.ConnectCommand.CanExecute(null));Assert.Null(vm.Current);
    }

    [Fact]
    public async Task LaterAircraftSamplesReplacePosition()
    {
        var next=TrackingTestData.InAircraft with {Aircraft=TrackingTestData.Aircraft with {Latitude=51.2,Longitude=-1.3,TrueHeadingDegrees=450,GroundSpeedKnots=92}};
        using var vm=new TrackingViewModel(new SequenceFake(TrackingTestData.InAircraft,next),new TestAircraftStore(),()=>"sdk");
        var run=vm.ConnectAsync();await Until(()=>vm.Current?.Latitude==51.2);
        Assert.Equal("51.20000, -1.30000",vm.Position);Assert.Equal("090° true",vm.Heading);vm.Disconnect();await run;
    }

    [Fact]
    public async Task InvalidTelemetryIsRefusedAndMapStateCleared()
    {
        var invalid=TrackingTestData.InAircraft with {Aircraft=TrackingTestData.Aircraft with {Latitude=double.NaN}};
        using var vm=new TrackingViewModel(new StreamingFake(invalid),new TestAircraftStore(),()=>"sdk");await vm.ConnectAsync();
        Assert.Equal(TrackingState.Error,vm.State);Assert.Contains("latitude",vm.Error,StringComparison.OrdinalIgnoreCase);Assert.Null(vm.World);
    }

    [Fact]
    public async Task ConnectionLossClearsAllWorldState()
    {
        using var vm=new TrackingViewModel(new DropsAfterSample(),new TestAircraftStore(),()=>"sdk");await vm.ConnectAsync();
        Assert.Equal(TrackingState.Error,vm.State);Assert.Null(vm.World);Assert.Contains("connection lost",vm.Error);
    }

    [Fact]
    public async Task DryRunTrackingCannotOpenLiveSource()
    {
        var source=new StreamingFake(TrackingTestData.InAircraft);using var vm=new TrackingViewModel(source,new TestAircraftStore(),()=>"sdk",enabled:false);
        await vm.ConnectAsync();Assert.Null(source.SdkRoot);Assert.False(vm.ConnectCommand.CanExecute(null));Assert.Contains("disabled",vm.Status,StringComparison.OrdinalIgnoreCase);
    }

    private static async Task Until(Func<bool> condition) { for(int i=0;i<100&&!condition();i++)await Task.Delay(10);Assert.True(condition()); }
}

public sealed class LiveTrackingClassificationTests
{
    private static LiveAircraftTelemetry.ObjectReading Reading(uint id,double lat=51.12345,double lon=-1.12345,double altitude=1240,double heading=273,double onGround=1,double speed=0) =>
        new(id,[lat,lon,altitude,heading,onGround,speed]);

    [Fact]
    public void CurrentAircraftIdentityProducesInAircraftState()
    {
        var state=LiveAircraftTelemetry.Classify("Plane","Type",Reading(524288),Reading(32768,0,0,0,0),Reading(524288),"SDK test");
        Assert.Equal(PlayerControlMode.InAircraft,state.PlayerMode);Assert.Null(state.Player);Assert.Equal(524288u,state.AircraftObjectId);
    }

    [Fact]
    public void DistinctMatchingAvatarProducesIndependentOnFootState()
    {
        var avatar=Reading(32768,51.124,-1.123,1241,42);
        var state=LiveAircraftTelemetry.Classify("Plane","Type",Reading(524288),avatar,avatar,"SDK test");
        Assert.Equal(PlayerControlMode.OnFoot,state.PlayerMode);Assert.Equal(32768u,state.Player!.ObjectId);
        Assert.Equal(51.12345,state.Aircraft.Latitude);Assert.Equal(51.124,state.Player.Latitude);
    }

    [Theory]
    [InlineData(true,true)]
    [InlineData(false,false)]
    public void InvalidOrAirborneAvatarIsRefusedWithoutLosingAircraft(bool invalidPosition,bool onGround)
    {
        var latitude=invalidPosition?double.NaN:51.124;
        var avatar=Reading(32768,latitude,-1.123,1241,42,onGround?1:0);
        var state=LiveAircraftTelemetry.Classify("Plane","Type",Reading(524288),avatar,avatar,"SDK test");
        Assert.Equal(PlayerControlMode.Unknown,state.PlayerMode);Assert.Null(state.Player);Assert.Equal(51.12345,state.Aircraft.Latitude);
    }

    [Fact]
    public void AliasedOrUnrecognizedCurrentObjectIsRefused()
    {
        var state=LiveAircraftTelemetry.Classify("Plane","Type",Reading(524288),Reading(524288),Reading(999),"SDK test");
        Assert.Equal(PlayerControlMode.Unknown,state.PlayerMode);Assert.Null(state.Player);
    }
}

internal static class TrackingTestData
{
    internal static readonly AircraftTelemetry Aircraft = new(DateTimeOffset.UtcNow,"Cessna 208B Grand Caravan","C208B",51.12345,-1.12345,1240,273,84,true);
    internal static readonly TrackingWorldState InAircraft = new(Aircraft,524288,524288,32768,PlayerControlMode.InAircraft,null,"aircraft","SDK test");
}
