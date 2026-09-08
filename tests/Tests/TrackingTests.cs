using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.Workflow;

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
}

public sealed class TrackingViewModelTests
{
    private static readonly AircraftTelemetry Sample = new(DateTimeOffset.UtcNow,"Cessna 208B Grand Caravan","C208B",51.12345,-1.12345,1240,273,84,true);

    private sealed class StreamingFake(AircraftTelemetry sample) : IAircraftTelemetrySource
    {
        public string? SdkRoot;
        public async Task RunAsync(string sdkRoot,IProgress<AircraftTelemetry> updates,CancellationToken token)
        {
            SdkRoot=sdkRoot;updates.Report(sample);await Task.Delay(Timeout.Infinite,token);
        }
    }
    private sealed class FailingFake(Exception error) : IAircraftTelemetrySource
    {
        public Task RunAsync(string sdkRoot,IProgress<AircraftTelemetry> updates,CancellationToken token)=>Task.FromException(error);
    }
    private sealed class SequenceFake(params AircraftTelemetry[] samples) : IAircraftTelemetrySource
    {
        public async Task RunAsync(string sdkRoot,IProgress<AircraftTelemetry> updates,CancellationToken token)
        {
            foreach(var sample in samples)updates.Report(sample);
            await Task.Delay(Timeout.Infinite,token);
        }
    }
    private sealed class DropsAfterSample : IAircraftTelemetrySource
    {
        public Task RunAsync(string sdkRoot,IProgress<AircraftTelemetry> updates,CancellationToken token)
        {
            updates.Report(Sample);return Task.FromException(new IOException("connection lost"));
        }
    }

    [Fact]
    public async Task ConnectsDisplaysSamplesAndDisconnectsCleanly()
    {
        var source=new StreamingFake(Sample);using var vm=new TrackingViewModel(source,()=>"sdk-root");
        var run=vm.ConnectAsync();await Until(()=>vm.State==TrackingState.Connected);
        Assert.Equal("sdk-root",source.SdkRoot);Assert.True(vm.HasTelemetry);
        Assert.Equal("51.12345, -1.12345",vm.Position);Assert.Equal("1,240 ft",vm.Altitude);
        Assert.Equal("273° true",vm.Heading);Assert.Equal("84.0 kt",vm.GroundSpeed);Assert.Equal("On Ground",vm.FlightState);
        vm.Disconnect();await run;
        Assert.Equal(TrackingState.Disconnected,vm.State);Assert.False(vm.HasTelemetry);Assert.Empty(vm.Error);
    }

    [Fact]
    public async Task ConnectionFailureBecomesRecoverableDisconnectedState()
    {
        using var vm=new TrackingViewModel(new FailingFake(new IOException("MSFS unavailable")),()=>"sdk");
        await vm.ConnectAsync();
        Assert.Equal(TrackingState.Error,vm.State);Assert.Contains("MSFS unavailable",vm.Error);
        Assert.True(vm.ConnectCommand.CanExecute(null));Assert.False(vm.DisconnectCommand.CanExecute(null));Assert.Null(vm.Current);
    }

    [Fact]
    public async Task LaterSamplesReplacePositionWithoutChangingConnectionState()
    {
        var next=Sample with {Latitude=51.2,Longitude=-1.3,TrueHeadingDegrees=450,GroundSpeedKnots=92};
        using var vm=new TrackingViewModel(new SequenceFake(Sample,next),()=>"sdk");
        var run=vm.ConnectAsync();await Until(()=>vm.Current?.Latitude==51.2);
        Assert.Equal(TrackingState.Connected,vm.State);Assert.Equal("51.20000, -1.30000",vm.Position);Assert.Equal("090° true",vm.Heading);
        vm.Disconnect();await run;
    }

    [Fact]
    public async Task InvalidTelemetryIsRefusedAndMarkerDataCleared()
    {
        using var vm=new TrackingViewModel(new StreamingFake(Sample with {Latitude=double.NaN}),()=>"sdk");
        await vm.ConnectAsync();
        Assert.Equal(TrackingState.Error,vm.State);Assert.Contains("latitude",vm.Error,StringComparison.OrdinalIgnoreCase);Assert.Null(vm.Current);
    }

    [Fact]
    public async Task ConnectionLossClearsPreviouslyReportedAircraft()
    {
        using var vm=new TrackingViewModel(new DropsAfterSample(),()=>"sdk");
        await vm.ConnectAsync();
        Assert.Equal(TrackingState.Error,vm.State);Assert.Null(vm.Current);Assert.False(vm.HasTelemetry);
        Assert.Contains("connection lost",vm.Error);
    }

    [Fact]
    public async Task DryRunTrackingCannotOpenLiveSource()
    {
        var source=new StreamingFake(Sample);using var vm=new TrackingViewModel(source,()=>"sdk",enabled:false);
        await vm.ConnectAsync();
        Assert.Equal(TrackingState.Disconnected,vm.State);Assert.Null(source.SdkRoot);Assert.False(vm.ConnectCommand.CanExecute(null));
        Assert.Contains("disabled",vm.Status,StringComparison.OrdinalIgnoreCase);
    }

    private static async Task Until(Func<bool> condition)
    {
        for(int i=0;i<100&&!condition();i++)await Task.Delay(10);
        Assert.True(condition());
    }
}
