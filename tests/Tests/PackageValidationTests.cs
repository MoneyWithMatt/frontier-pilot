using System.Text.Json;
using FrontierAirportBuilder.MsfsSdk;

namespace FrontierAirportBuilder.Tests;

public sealed class PackageValidationTests : TemporaryDirectoryTest
{
    public PackageValidationTests()
    {
        File.WriteAllText(Path.Combine(Root, "manifest.json"), "{\"package_order_hint\":\"CUSTOM_AIRPORT\"}");
        File.WriteAllBytes(Path.Combine(Root, "airport.bgl"), new byte[] { 1, 2, 3, 4 });
        Layout("airport.bgl", 4);
    }
    private void Layout(string path, long size) => File.WriteAllText(Path.Combine(Root, "layout.json"),
        JsonSerializer.Serialize(new { content = new[] { new { path, size } } }));
    private PackageValidation Check(int exit = 0, bool timeout = false, string errors = "") => PackageValidator.Validate(Root, exit, timeout, errors);

    [Fact] public void CompletePackagePasses() { var result = Check(); Assert.True(result.Success); Assert.Equal(1, result.LayoutEntries); }
    [Fact] public void NonzeroExitFailsEvenWithArtifacts() => Assert.False(Check(1).Success);
    [Fact] public void TimeoutFailsEvenWithArtifacts() => Assert.False(Check(timeout: true).Success);
    [Fact] public void BuilderErrorsFailDespiteExitZero() => Assert.False(Check(errors: "Package Validation failed for 'frontier-airport-fpt1'").Success);
    [Fact] public void MissingLayoutFails() { File.Delete(Path.Combine(Root, "layout.json")); Assert.False(Check().Success); }
    [Fact] public void MissingManifestFails() { File.Delete(Path.Combine(Root, "manifest.json")); Assert.False(Check().Success); }
    [Fact] public void MissingReferencedFileFails() { File.Delete(Path.Combine(Root, "airport.bgl")); Assert.False(Check().Success); }
    [Fact] public void SizeMismatchFails() { Layout("airport.bgl", 5); Assert.False(Check().Success); }
    [Fact] public void EmptyBglFails() { File.WriteAllBytes(Path.Combine(Root, "airport.bgl"), Array.Empty<byte>()); Layout("airport.bgl", 0); Assert.False(Check().Success); }
    [Fact] public void MissingOrderHintFails() { File.WriteAllText(Path.Combine(Root, "manifest.json"), "{}"); Assert.False(Check().Success); }
    [Fact] public void WrongOrderHintFails() { File.WriteAllText(Path.Combine(Root, "manifest.json"), "{\"package_order_hint\":\"\"}"); Assert.False(Check().Success); }
    [Theory] [InlineData("garbage")] [InlineData("{}")] [InlineData("{\"content\":[]}")] [InlineData("{\"content\":null}")]
    public void InvalidLayoutFails(string json) { File.WriteAllText(Path.Combine(Root, "layout.json"), json); Assert.False(Check().Success); }
    [Theory] [InlineData("../airport.bgl")] [InlineData("..\\airport.bgl")] [InlineData("C:\\airport.bgl")] [InlineData("/airport.bgl")] [InlineData("airport.bgl:stream")] [InlineData("")]
    public void UnsafePathsFail(string path) { Layout(path, 4); Assert.False(Check().Success); }
    [Fact]
    public void DuplicateEntriesFail()
    {
        File.WriteAllText(Path.Combine(Root,"layout.json"), "{\"content\":[{\"path\":\"airport.bgl\",\"size\":4},{\"path\":\"AIRPORT.BGL\",\"size\":4}]}");
        Assert.False(Check().Success);
    }
}
