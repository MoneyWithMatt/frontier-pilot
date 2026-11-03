using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
using FrontierAirportBuilder.TestFixtures;

namespace FrontierAirportBuilder.Tests;

public sealed class LoadingIdentityReadTests
{
    [Fact]
    public async Task IncompleteTransitionReadRetriesFreshDataWithoutInventingIdentity()
    {
        var aircraft=new SyntheticPassengerAircraft();var snapshot=await aircraft.ReadAsync("");var reads=0;var waits=0;
        var result=SimConnectAircraftLoading.ReadCompleteIdentity(()=>++reads==1?snapshot with{Aircraft=new(snapshot.Aircraft.Title,"")}:snapshot,()=>waits++);
        Assert.Equal(snapshot,result);Assert.Equal(2,reads);Assert.Equal(1,waits);Assert.Equal(0,aircraft.Writes);
    }
    [Theory][InlineData("")][InlineData(" ")]
    public async Task PersistentMissingModelIsRefusedAfterThreeReads(string model)
    {
        var aircraft=new SyntheticPassengerAircraft();var snapshot=await aircraft.ReadAsync("");var reads=0;var waits=0;
        var error=Assert.Throws<InvalidDataException>(()=>SimConnectAircraftLoading.ReadCompleteIdentity(()=>{reads++;return snapshot with{Aircraft=new(snapshot.Aircraft.Title,model)};},()=>waits++));
        Assert.Contains("ATC MODEL",error.Message);Assert.Equal(3,reads);Assert.Equal(2,waits);Assert.Equal(0,aircraft.Writes);
    }
    [Fact]
    public void DisconnectAndCancellationAreNotIdentityRetries()
    {
        var reads=0;
        Assert.Throws<IOException>(()=>SimConnectAircraftLoading.ReadCompleteIdentity(()=>{reads++;throw new IOException("Disconnected");},()=>throw new Exception("Unexpected retry")));
        Assert.Equal(1,reads);
        Assert.Throws<OperationCanceledException>(()=>SimConnectAircraftLoading.ReadCompleteIdentity(()=>throw new OperationCanceledException(),()=>throw new Exception("Unexpected retry")));
    }
}
