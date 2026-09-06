using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.MsfsSdk;

public sealed record GeneratedProject(string Directory, string ProjectFile, string PackageName, RunwayGeometry Geometry);

public static class ProjectGenerator
{
    private const string TemplatePackage = "frontier-airport-fpt1";
    private const string ResourcePrefix = "FrontierAirportBuilder.MsfsSdk.Templates.SingleRunway.";

    public static GeneratedProject Generate(AirstripDefinition definition, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.EnsureValid();
        var geometry = RunwayGeometry.Calculate(definition);
        var root = Path.GetFullPath(outputDirectory);
        // Never mix generated files with existing work, SDK samples, or previous build output.
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Output directory must not already exist.");
        var packageName = "frontier-airport-" + definition.Identifier.ToLowerInvariant();
        var materialGuid = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("FrontierPilot/Asphalt/" + definition.Identifier)).AsSpan(0, 16))
            .ToString("B").ToUpperInvariant();
        var project = LoadXml("AirportProject.xml");
        project.Root!.Element("Packages")!.Element("Package")!.Value = $"PackageDefinitions\\{packageName}.xml";
        var package = LoadXml($"PackageDefinitions.{TemplatePackage}.xml");
        var settings = package.Root!.Element("ItemSettings")!;
        settings.Element("Title")!.Value = definition.Name;
        var groups = package.Root.Element("AssetGroups")!.Elements("AssetGroup").ToArray();
        groups.Single(g => g.Element("Type")!.Value == "Copy").Element("AssetDir")!.Value = $"PackageDefinitions\\{packageName}\\ContentInfo\\";
        groups.Single(g => g.Element("Type")!.Value == "Copy").Element("OutputDir")!.Value = $"ContentInfo\\{packageName}\\";
        groups.Single(g => g.Element("Type")!.Value == "MaterialLib").Element("OutputDir")!.Value = $"MaterialLibs\\{packageName}\\";
        var airportXml = LoadXml("PackageSources.data.airport.xml");
        var airport = airportXml.Root!.Element("Airport")!;
        airport.SetAttributeValue("name", definition.Name);
        airport.SetAttributeValue("ident", definition.Identifier);
        SetPosition(airport, new(definition.Latitude, Geodesy.NormalizeLongitude(definition.Longitude)), definition.AltitudeMeters);
        // Core uses east-positive declination; the MSFS airport XML uses west-positive magvar.
        airport.SetAttributeValue("magvar", Number(-definition.MagneticVariationDegreesEast));
        var runway = airport.Element("Runway")!;
        SetPosition(runway, new(definition.Latitude, Geodesy.NormalizeLongitude(definition.Longitude)), definition.AltitudeMeters);
        runway.SetAttributeValue("heading", Number(geometry.TrueHeadingDegrees));
        runway.SetAttributeValue("length", Number(definition.LengthMeters));
        runway.SetAttributeValue("width", Number(definition.WidthMeters));
        runway.SetAttributeValue("surface", materialGuid);
        runway.SetAttributeValue("number", geometry.Primary.Number.ToString("00", CultureInfo.InvariantCulture));
        foreach (var (end, value) in new[] { ("PRIMARY", geometry.Primary), ("SECONDARY", geometry.Secondary) })
        {
            var start = runway.Elements("RunwayStart").Single(e => (string?)e.Attribute("end") == end);
            SetPosition(start, value.Position, definition.AltitudeMeters);
            start.SetAttributeValue("heading", Number(value.InwardTrueHeadingDegrees));
        }
        var material = LoadXml("PackageSources.materials.frontier-asphalt.material");
        material.Root!.SetAttributeValue("Name", "frontier-asphalt");
        material.Root.SetAttributeValue("Guid", materialGuid);

        // All XML is built before creating output; there are no culture-dependent text substitutions.
        Directory.CreateDirectory(root);
        Save(project, Path.Combine(root, "AirportProject.xml"));
        Save(package, Path.Combine(root, "PackageDefinitions", packageName + ".xml"));
        Save(airportXml, Path.Combine(root, "PackageSources", "data", "airport.xml"));
        Save(material, Path.Combine(root, "PackageSources", "materials", "frontier-asphalt.material"));
        // MSBuild normalizes hyphens in resource directory segments (but not file names).
        CopyResource($"PackageDefinitions.{TemplatePackage.Replace('-', '_')}.ContentInfo.Thumbnail.jpg", Path.Combine(root, "PackageDefinitions", packageName, "ContentInfo", "Thumbnail.jpg"));
        foreach (var file in new[] { "Asphalt01_Albedo.png", "Asphalt01_Albedo.png.FLAGS", "Asphalt01_COMP.png", "Asphalt01_COMP.png.FLAGS" })
            CopyResource("PackageSources.materials.Textures." + file, Path.Combine(root, "PackageSources", "materials", "Textures", file));
        return new(root, Path.Combine(root, "AirportProject.xml"), packageName, geometry);
    }

    private static string Number(double value) => (value == 0 ? 0 : value).ToString("0.###############", CultureInfo.InvariantCulture);
    private static void SetPosition(XElement element, GeoPoint point, double altitude)
    {
        element.SetAttributeValue("lat", Number(point.Latitude));
        element.SetAttributeValue("lon", Number(point.Longitude));
        element.SetAttributeValue("alt", Number(altitude)); // Un-suffixed SDK units are metres.
    }

    private static Stream Resource(string name) => typeof(ProjectGenerator).Assembly.GetManifestResourceStream(ResourcePrefix + name)
        ?? throw new InvalidOperationException("Missing embedded template: " + name);
    private static XDocument LoadXml(string name) { using var stream = Resource(name); return XDocument.Load(stream); }
    private static void CopyResource(string name, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var resource = Resource(name);
        using var output = File.Create(path);
        resource.CopyTo(output);
    }
    private static void Save(XDocument document, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = XmlWriter.Create(path, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false), Indent = true, IndentChars = "  ", NewLineChars = "\n", NewLineHandling = NewLineHandling.Replace
        });
        document.Save(writer);
    }
}
