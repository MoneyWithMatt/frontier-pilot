using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;

namespace FrontierAirportBuilder.Tests;

public sealed class ProjectGeneratorTests : TemporaryDirectoryTest
{
    [Fact]
    public void GeneratesOneAirportOneRunwayTwoStartsAndRequiredPackageStructure()
    {
        var generated = ProjectGenerator.Generate(Fixtures.Strip, Path.Combine(Root, "project"));
        var data = XDocument.Load(Path.Combine(generated.Directory, "PackageSources", "data", "airport.xml"));
        var airport = Assert.Single(data.Root!.Elements("Airport"));
        Assert.Equal("FPT1", (string?)airport.Attribute("ident"));
        Assert.Equal("Frontier Test Strip", (string?)airport.Attribute("name"));
        Assert.Equal("6", (string?)airport.Attribute("magvar"));
        var runway = Assert.Single(airport.Elements());
        Assert.Equal("Runway", runway.Name.LocalName);
        Assert.Equal("10", (string?)runway.Attribute("number"));
        Assert.Equal(Fixtures.Strip.AltitudeMeters, (double)runway.Attribute("alt")!, 12);
        Assert.Equal(Fixtures.Strip.LengthMeters, (double)runway.Attribute("length")!, 9);
        Assert.Equal(Fixtures.Strip.WidthMeters, (double)runway.Attribute("width")!, 9);
        var starts = runway.Elements().ToArray();
        Assert.Equal(2, starts.Length);
        Assert.All(starts, s => Assert.Equal("RunwayStart", s.Name.LocalName));
        Assert.Equal("PRIMARY", (string?)starts[0].Attribute("end"));
        Assert.Equal("SECONDARY", (string?)starts[1].Attribute("end"));
        Assert.Equal(generated.Geometry.Primary.Position.Latitude, (double)starts[0].Attribute("lat")!, 10);
        Assert.Equal(generated.Geometry.Primary.InwardTrueHeadingDegrees, (double)starts[0].Attribute("heading")!, 10);
        Assert.Equal(generated.Geometry.Secondary.Position.Longitude, (double)starts[1].Attribute("lon")!, 10);
        Assert.Equal(generated.Geometry.Secondary.InwardTrueHeadingDegrees, (double)starts[1].Attribute("heading")!, 10);
        Assert.All(starts, s => Assert.Equal(Fixtures.Strip.AltitudeMeters, (double)s.Attribute("alt")!, 12));
        var package = XDocument.Load(Path.Combine(generated.Directory, "PackageDefinitions", generated.PackageName + ".xml"));
        Assert.Equal("CUSTOM_AIRPORT", package.Root!.Element("PackageOrderHint")!.Value);
        Assert.Equal("SCENERY", package.Root.Element("ItemSettings")!.Element("ContentType")!.Value);
        Assert.Equal("Frontier Pilot", package.Root.Element("ItemSettings")!.Element("Creator")!.Value);
        Assert.Equal(new[] { "Copy", "BGL", "MaterialLib" }, package.Descendants("AssetGroup").Select(g => g.Element("Type")!.Value));
        foreach (var group in package.Descendants("AssetGroup"))
            Assert.True(Directory.Exists(Path.Combine(generated.Directory, group.Element("AssetDir")!.Value)));
        var project = XDocument.Load(generated.ProjectFile);
        Assert.Equal("2", (string?)project.Root!.Attribute("Version"));
        Assert.True(File.Exists(Path.Combine(generated.Directory, project.Root.Element("Packages")!.Element("Package")!.Value)));
        var material = XDocument.Load(Path.Combine(generated.Directory, "PackageSources", "materials", "frontier-asphalt.material"));
        Assert.Equal((string?)runway.Attribute("surface"), (string?)material.Root!.Attribute("Guid"));
        Assert.NotEqual("{B037EA38-EDF8-4AE5-B41B-2CA423ADA3EF}", (string?)runway.Attribute("surface"));
        Assert.Equal("frontier-asphalt", (string?)material.Root.Attribute("Name"));
        foreach (var texture in material.Descendants("Texture"))
            Assert.True(File.Exists(Path.Combine(generated.Directory, "PackageSources", "materials", "Textures", (string)texture.Attribute("FileName")!)));
    }

    [Fact]
    public void GenerationIsByteIdenticalAcrossDirectoriesAndCultures()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            var first = ProjectGenerator.Generate(Fixtures.Strip, Path.Combine(Root, "one"));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var second = ProjectGenerator.Generate(Fixtures.Strip, Path.Combine(Root, "path with spaces", "two"));
            Assert.Equal(Inventory(first.Directory), Inventory(second.Directory));
            foreach (var file in Directory.GetFiles(first.Directory, "*.xml", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(file);
                Assert.DoesNotContain("\r", content);
                Assert.DoesNotContain(first.Directory, content);
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void XmlEscapingIsHandledByXDocument()
    {
        var name = "A & B <strip> \"Nassau é\"";
        var generated = ProjectGenerator.Generate(Fixtures.Strip with { Name = name }, Path.Combine(Root, "escaped"));
        var file = Path.Combine(generated.Directory, "PackageSources", "data", "airport.xml");
        Assert.Equal(name, (string?)XDocument.Load(file).Root!.Element("Airport")!.Attribute("name"));
        Assert.Contains("&amp;", File.ReadAllText(file));
        Assert.Contains("&lt;strip&gt;", File.ReadAllText(file));
    }

    [Fact]
    public void EquivalentHeadingInputsProduceIdenticalOutput()
    {
        var first = ProjectGenerator.Generate(Fixtures.Strip with { TrueHeadingDegrees = -90 }, Path.Combine(Root, "a"));
        var second = ProjectGenerator.Generate(Fixtures.Strip with { TrueHeadingDegrees = 270 }, Path.Combine(Root, "b"));
        Assert.Equal(Inventory(first.Directory), Inventory(second.Directory));
    }

    [Fact]
    public void IdentifierNamespacesMaterialAndPackage()
    {
        var a = ProjectGenerator.Generate(Fixtures.Strip, Path.Combine(Root, "a"));
        var b = ProjectGenerator.Generate(Fixtures.Strip with { Identifier = "FPT2" }, Path.Combine(Root, "b"));
        Assert.NotEqual(a.PackageName, b.PackageName);
        string GuidFor(GeneratedProject p) => (string)XDocument.Load(Path.Combine(p.Directory, "PackageSources", "materials", "frontier-asphalt.material")).Root!.Attribute("Guid")!;
        Assert.NotEqual(GuidFor(a), GuidFor(b));
    }

    [Fact]
    public void NeverOverwritesAnExistingDirectory()
    {
        var sentinel = Path.Combine(Root, "keep.txt");
        File.WriteAllText(sentinel, "preserve me");
        Assert.Throws<IOException>(() => ProjectGenerator.Generate(Fixtures.Strip, Root));
        Assert.Equal("preserve me", File.ReadAllText(sentinel));
        Assert.Single(Directory.GetFiles(Root));
    }

    [Fact]
    public void InvalidInputNeverCreatesOutput()
    {
        var path = Path.Combine(Root, "invalid");
        Assert.Throws<ArgumentException>(() => ProjectGenerator.Generate(Fixtures.Strip with { Identifier = "../X" }, path));
        Assert.False(Directory.Exists(path));
    }

    private static string[] Inventory(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .OrderBy(p => p, StringComparer.Ordinal).Select(p => Path.GetRelativePath(root, p) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
}
