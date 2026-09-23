using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FrontierAirportBuilder.Core;
using FrontierAirportBuilder.MsfsSdk;
using FrontierAirportBuilder.Workflow;

namespace FrontierAirportBuilder.Tests;

public sealed class SceneryMaterialsTests : TemporaryDirectoryTest
{
    [Fact]
    public void SurfaceCatalogueHasStableCompleteUniqueAndValidatedIds()
    {
        Assert.Equal(new[]
        {
            "compacted-dirt", "sandy-dirt", "gravel", "dry-grass", "green-grass",
            "weathered-asphalt", "modern-asphalt"
        }, RunwaySurfacePresets.All.Select(x => x.Id));
        Assert.Equal(7, RunwaySurfacePresets.All.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(RunwaySurfacePresets.All, x => { Assert.Same(x, x.EnsureValid()); Assert.True(x.CanGenerate); });
        Assert.Equal(SurfacePresetAvailability.TemporaryFallback, RunwaySurfacePresets.Get(RunwaySurfacePresets.DryGrassId).Availability);
        Assert.Equal(SurfacePresetAvailability.TemporaryFallback, RunwaySurfacePresets.Get(RunwaySurfacePresets.WeatheredAsphaltId).Availability);
    }

    [Theory]
    [InlineData("not a material")]
    [InlineData("{00000000-NOT-A-GUID}")]
    [InlineData("{00000000-0000-0000-0000-000000000000}")]
    [InlineData("DRY_GRASS")]
    public void InvalidMaterialReferencesAreRejected(string reference)
    {
        var preset = new RunwaySurfacePreset("test-invalid", "Invalid", reference,
            SurfacePresetAvailability.Native, RunwaySurface.Dirt, true, false, "test");
        Assert.Throws<InvalidDataException>(() => preset.EnsureValid());
    }

    [Theory]
    [InlineData(RunwaySurfacePresets.CompactedDirtId, "DIRT")]
    [InlineData(RunwaySurfacePresets.SandyDirtId, "DIRT")]
    [InlineData(RunwaySurfacePresets.GravelId, "GRAVEL")]
    [InlineData(RunwaySurfacePresets.DryGrassId, "GRASS")]
    [InlineData(RunwaySurfacePresets.GreenGrassId, "SHORT_GRASS")]
    [InlineData(RunwaySurfacePresets.WeatheredAsphaltId, "ASPHALT")]
    [InlineData(RunwaySurfacePresets.ModernAsphaltId, "ASPHALT")]
    public void GeneratedProjectsReferenceTheAuthoredNativeOrFallbackSurface(string id, string expected)
    {
        var preset = RunwaySurfacePresets.Get(id);
        var generated = ProjectGenerator.Generate(Fixtures.Strip with
        {
            Surface = preset.PhysicalSurface,
            SurfacePresetId = id,
            Taxiways = [new Taxiway(1, "T1", 12, [new(0, 0), new(25, 25)])]
        }, Path.Combine(Root, id));
        var xml = XDocument.Load(Path.Combine(generated.Directory, "PackageSources", "data", "airport.xml"));
        Assert.Equal(expected, (string?)xml.Descendants("Runway").Single().Attribute("surface"));
        Assert.Equal(expected, (string?)xml.Descendants("TaxiwayPath").Single().Attribute("surface"));
        Assert.Equal(preset.GroundMerging ? "TRUE" : null, (string?)xml.Descendants("Runway").Single().Attribute("groundMerging"));
        Assert.DoesNotContain(XDocument.Load(Path.Combine(generated.Directory, "PackageDefinitions", generated.PackageName + ".xml"))
            .Descendants("AssetGroup"), x => x.Element("Type")!.Value == "MaterialLib");
        Assert.False(Directory.Exists(Path.Combine(generated.Directory, "PackageSources", "materials")));
    }

    [Fact]
    public void MissingPresetPropertyPreservesLegacyMaterialPipeline()
    {
        var node = JsonSerializer.SerializeToNode(Fixtures.Strip with { Surface = RunwaySurface.Dirt })!.AsObject();
        node.Remove("SurfacePresetId");
        var loaded = node.Deserialize<AirstripDefinition>()!;
        Assert.Null(loaded.SurfacePresetId);
        var generated = ProjectGenerator.Generate(loaded, Path.Combine(Root, "legacy"));
        var xml = XDocument.Load(Path.Combine(generated.Directory, "PackageSources", "data", "airport.xml"));
        var material = XDocument.Load(Path.Combine(generated.Directory, "PackageSources", "materials", "frontier-dirt.material"));
        Assert.Equal((string?)material.Root!.Attribute("Guid"), (string?)xml.Descendants("Runway").Single().Attribute("surface"));
        Assert.Equal("FrontierDirt_Albedo.png", (string?)material.Descendants("Texture").Single().Attribute("FileName"));
    }

    [Fact]
    public async Task AirportPersistenceRoundTripRetainsPresetAndPrototypeLodge()
    {
        var services = BuilderServices.CreateDryRun();
        var vm = new BuilderViewModel(services, "");
        await vm.CheckAsync(); await vm.CaptureAsync();
        vm.AirportName = "Material persistence"; vm.Identifier = "FPMAT1"; vm.SiteConfirmed = true;
        vm.SelectedSurfacePreset = vm.SurfacePresets.Single(x => x.PresetId == RunwaySurfacePresets.GravelId);
        vm.Layout.AddObject(new(80, 120), 35, SceneryAssets.PrototypeBaseLodgeId);
        await vm.SaveAirportAsync();
        var id = Assert.Single(vm.Airports).Id;
        var reopened = new BuilderViewModel(services.ReopenDryRun(), "");
        await reopened.LoadAirportAsync(id);
        Assert.Equal(RunwaySurfacePresets.GravelId, reopened.SelectedSurfacePreset.PresetId);
        Assert.Equal(SceneryAssets.PrototypeBaseLodgeId, Assert.Single(reopened.Layout.Objects).AssetId);
    }

    [Fact]
    public void ConstructionCatalogueHasEveryStableCategoryAndOnlyOneExplicitPlaceholder()
    {
        Assert.Equal(Enum.GetValues<ConstructionObjectCategory>().Length, ConstructionObjectCatalogue.All.Count);
        Assert.Equal(ConstructionObjectCatalogue.All.Count, ConstructionObjectCatalogue.All.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enum.GetValues<ConstructionObjectCategory>().Order(), ConstructionObjectCatalogue.All.Select(x => x.Category).Order());
        var lodge = Assert.Single(ConstructionObjectCatalogue.All.Where(x => x.CanPlace));
        Assert.Equal(ConstructionObjectCatalogue.BaseLodgeId, lodge.Id);
        var asset = SceneryAssets.Find(lodge.SceneryAssetId);
        Assert.NotNull(asset); Assert.True(asset!.IsPrototypePlaceholder);
        Assert.Equal(new Guid("7C24C83A-0588-4208-9B89-6F01B7D9B82F"), asset.LibraryGuid);
        Assert.Equal(1, asset.Scale);
        Assert.Equal(8,asset.Footprint.WidthMeters);
        Assert.Equal(6,asset.Footprint.DepthMeters);
    }

    [Fact]
    public void UnknownOrMismatchedPresetCannotGenerateAnyFiles()
    {
        foreach (var definition in new[]
        {
            Fixtures.Strip with { SurfacePresetId = "missing-preset" },
            Fixtures.Strip with { Surface = RunwaySurface.Asphalt, SurfacePresetId = RunwaySurfacePresets.GravelId }
        })
        {
            var path = Path.Combine(Root, Guid.NewGuid().ToString("N"));
            Assert.Throws<ArgumentException>(() => ProjectGenerator.Generate(definition, path));
            Assert.False(Directory.Exists(path));
        }
    }
}
