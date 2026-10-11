using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class SdkInstallerCommandTests
{
    [Theory][InlineData(@"C:\MSFS 2024 SDK")][InlineData(@"C:\MSFS 2024 SDK\")]
    public void SamplesPropertyQuotesOnlyValueAndTrimsTrailingSeparator(string root)
    {
        var args=SdkInstallerCommand.BuildArguments(@"C:\downloads folder\samples.msi",@"C:\downloads folder\install.log",root);
        Assert.Equal("/i \"C:\\downloads folder\\samples.msi\" MSFS_SDK=\"C:\\MSFS 2024 SDK\" /L*v \"C:\\downloads folder\\install.log\"",args);
        Assert.DoesNotContain("\"MSFS_SDK=",args);
    }
    [Fact]public void CoreHasNoSamplesDestinationProperty()
    {
        Assert.DoesNotContain("MSFS_SDK",SdkInstallerCommand.BuildArguments(@"C:\core.msi",@"C:\core.log"));
    }
    [Theory][InlineData("relative")][InlineData("C:\\SDK\" /quiet")][InlineData("C:\\SDK\n/quiet")]
    public void UnsafePathsAreRefused(string root)
    {
        Assert.Throws<ArgumentException>(()=>SdkInstallerCommand.BuildArguments(@"C:\samples.msi",@"C:\install.log",root));
    }
}
