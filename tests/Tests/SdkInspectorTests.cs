using FrontierAirportBuilder.MsfsSdk;

namespace FrontierAirportBuilder.Tests;

public sealed class SdkInspectorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FrontierAirportBuilderTests", Guid.NewGuid().ToString("N"));

    public SdkInspectorTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Tools", "bin"));
        Directory.CreateDirectory(Path.Combine(root, "Samples", "DevmodeProjects", "Sceneries", "SimpleAirport"));
        File.WriteAllText(Path.Combine(root, "version.txt"), "1.2.4 \r\n");
        File.WriteAllText(Path.Combine(root, "Tools", "bin", "fspackagetool.exe"), "fixture only");
        File.WriteAllText(Path.Combine(root, "Samples", "DevmodeProjects", "Sceneries", "SimpleAirport", "AirportProject.xml"), "fixture only");
    }

    [Fact]
    public void ReadsVersionAndAbsolutePaths()
    {
        var result = SdkInspector.Inspect(root);
        Assert.Equal("1.2.4", result.Version);
        Assert.Equal(Path.Combine(root, "Tools", "bin", "fspackagetool.exe"), result.PackageTool);
        Assert.True(Path.IsPathFullyQualified(result.SimpleAirportProject));
    }

    [Fact]
    public void MissingToolIsReported()
    {
        var tool = Path.Combine(root, "Tools", "bin", "fspackagetool.exe");
        File.Delete(tool);
        Assert.Equal(tool, Assert.Throws<FileNotFoundException>(() => SdkInspector.Inspect(root)).FileName);
    }

    [Fact]
    public void InvalidVersionIsRejected()
    {
        File.WriteAllText(Path.Combine(root, "version.txt"), "unknown");
        Assert.Throws<InvalidDataException>(() => SdkInspector.Inspect(root));
    }

    public void Dispose() => Directory.Delete(root, true);
}
