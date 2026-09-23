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
        var preset = definition.SurfacePresetId is null ? null : RunwaySurfacePresets.Get(definition.SurfacePresetId).EnsureValid();
        var materialName = "frontier-" + definition.Surface.ToString().ToLowerInvariant();
        var materialGuid = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("FrontierPilot/" + definition.Surface + "/" + definition.Identifier)).AsSpan(0, 16))
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
        if (preset is not null || !definition.HasRunway)
            groups.Single(g => g.Element("Type")!.Value == "MaterialLib").Remove();
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
        runway.SetAttributeValue("surface", preset?.SdkSurfaceReference ?? materialGuid);
        runway.SetAttributeValue("groundMerging", preset?.GroundMerging == true || preset is null && definition.Surface == RunwaySurface.Dirt ? "TRUE" : null);
        runway.SetAttributeValue("number", geometry.Primary.Number.ToString("00", CultureInfo.InvariantCulture));
        foreach (var (end, value) in new[] { ("PRIMARY", geometry.Primary), ("SECONDARY", geometry.Secondary) })
        {
            var start = runway.Elements("RunwayStart").Single(e => (string?)e.Attribute("end") == end);
            SetPosition(start, value.Position, definition.AltitudeMeters);
            start.SetAttributeValue("heading", Number(value.InwardTrueHeadingDegrees));
        }
        if (preset is not null)
        {
            foreach (var layer in preset.Layers)
            {
                runway.Add(new XElement("Material", new XAttribute("guid", new Guid(layer.MaterialReference).ToString("B").ToUpperInvariant()),
                    new XAttribute("type", layer.BlendType), new XAttribute("tilingU", Number(layer.TilingU)),
                    new XAttribute("tilingV", Number(layer.TilingV)),
                    layer.FalloffMeters is { } falloff ? new XAttribute("falloff", Number(falloff)) : null,
                    new XAttribute("opacity", layer.Opacity)));
            }
        }
        // Indices 1–99 belong to parking. Reuse equal local nodes between paths, in stable order.
        var nodes=new Dictionary<LocalPoint,int>();
        var paths=new List<XElement>();
        var names=new List<XElement>();
        int Node(TaxiwayVertex v)
        {
            if(v.ParkingNumber is {} parkingNumber)return parkingNumber;
            var local=v.Resolve(definition.ParkingStands);
            if(nodes.TryGetValue(local,out int existing))return existing;
            int index=100+nodes.Count;nodes.Add(local,index);
            var geo=LocalCoordinates.ToGeographic(new(definition.Latitude,definition.Longitude),local);
            airport.Add(new XElement("TaxiwayPoint",new XAttribute("index",index),new XAttribute("type","NORMAL"),
                new XAttribute("lat",Number(geo.Latitude)),new XAttribute("lon",Number(geo.Longitude))));
            return index;
        }
        foreach(var taxiway in definition.Taxiways.OrderBy(t=>t.Number))
        {
            names.Add(new XElement("TaxiName",new XAttribute("index",taxiway.Number),new XAttribute("name",taxiway.Name)));
            for(int i=1;i<taxiway.Points.Count;i++)
            {
                var a=taxiway.Points[i-1];var b=taxiway.Points[i];
                paths.Add(new XElement("TaxiwayPath",new XAttribute("start",Node(a)),new XAttribute("end",Node(b)),
                    new XAttribute("type",a.ParkingNumber!=null||b.ParkingNumber!=null?"PARKING":"TAXI"),
                    new XAttribute("name",taxiway.Number),new XAttribute("width",Number(taxiway.WidthMeters)),
                    // SDK 1.2.4's BGL compiler requires this even though the 2024 XML reference labels it optional.
                    // Zero is the documented unlimited-aircraft value used by the installed official airport sample.
                    new XAttribute("weightLimit","0"),
                    new XAttribute("surface",preset?.SdkSurfaceReference??materialGuid),new XAttribute("drawSurface","TRUE"),new XAttribute("drawDetail","TRUE"),
                    new XAttribute("centerLine",preset?.DrawCentreLine??definition.Surface==RunwaySurface.Asphalt?"TRUE":"FALSE"),
                    new XAttribute("centerLineLighted","FALSE"),new XAttribute("leftEdge","NONE"),new XAttribute("rightEdge","NONE"),
                    new XAttribute("groundMerging",preset?.GroundMerging??definition.Surface==RunwaySurface.Dirt?"TRUE":"FALSE"),
                    new XAttribute("excludeVegetationInside","TRUE"),new XAttribute("excludeVegetationAround","TRUE")));
            }
        }
        // SDK order: all points, parking, names, then paths.
        foreach (var stand in definition.ParkingStands.OrderBy(s => s.Number))
        {
            var placement = ParkingGeometry.Calculate(definition, stand);
            airport.Add(new XElement("TaxiwayParking",
                new XAttribute("index", stand.Number), new XAttribute("type", stand.Type.ToString()),
                new XAttribute("lat", Number(placement.Position.Latitude)), new XAttribute("lon", Number(placement.Position.Longitude)),
                new XAttribute("heading", Number(placement.TrueHeadingDegrees)), new XAttribute("radius", Number(stand.RadiusMeters)),
                new XAttribute("name", stand.Name.ToString()), new XAttribute("number", stand.Number), new XAttribute("suffix", "NONE"),
                new XAttribute("numberMarking", "FALSE"), new XAttribute("has3DMesh", "FALSE")));
        }
        airport.Add(names);airport.Add(paths);
        // Library scenery objects are siblings of Airport in the scenery document. The airport
        // elevation must not be reused: these objects follow local terrain at their own positions.
        foreach (var item in definition.PlacedObjects.OrderBy(o => o.Number))
        {
            var asset = SceneryAssets.Find(item.AssetId) ?? throw new ArgumentException($"Unsupported scenery asset '{item.AssetId}'.");
            var placement = SceneryObjectGeometry.Calculate(definition, item);
            if(asset.Foundation is {} foundation)
            {
                var polygon=new XElement("Polygon",new XAttribute("displayName",$"Frontier foundation H{item.Number}"),
                    new XAttribute("altitude",Number(definition.AltitudeMeters)),
                    new XElement("Attribute",new XAttribute("name","FlattenFalloff"),new XAttribute("guid","{5548FDB5-2267-4328-8E6F-FD0A45ADEC8F}"),new XAttribute("type","FLOAT32"),new XAttribute("value",Number(foundation.FalloffMeters))),
                    new XElement("Attribute",new XAttribute("name","FlattenMode"),new XAttribute("guid","{065E9D4D-6984-4D2A-91FD-3C33C4F53B22}"),new XAttribute("type","UINT8"),new XAttribute("value","0")),
                    new XElement("Attribute",new XAttribute("name","ExcludeSecondaryHeightmap"),new XAttribute("guid","{10AC14E9-4656-4480-AA8C-3563EE6D127F}"),new XAttribute("type","UINT8"),new XAttribute("value",foundation.ExcludeSecondaryHeightmap?"1":"0")),
                    new XElement("Attribute",new XAttribute("name","ForceElevation"),new XAttribute("guid","{9460843D-964B-4E45-AC49-634856FB6E1C}"),new XAttribute("type","UINT8"),new XAttribute("value","1")));
                foreach(var corner in SceneryObjectGeometry.FootprintCorners(item,asset,foundation.MarginMeters))
                {
                    var vertex=LocalCoordinates.ToGeographic(new(definition.Latitude,definition.Longitude),corner);
                    polygon.Add(new XElement("Vertex",new XAttribute("lat",Number(vertex.Latitude)),new XAttribute("lon",Number(vertex.Longitude))));
                }
                airportXml.Root!.Add(polygon);
            }
            airportXml.Root!.Add(new XElement("SceneryObject",
                new XAttribute("lat", Number(placement.Position.Latitude)),
                new XAttribute("lon", Number(placement.Position.Longitude)),
                new XAttribute("alt", "0"),
                new XAttribute("pitch", "0"),
                new XAttribute("bank", "0"),
                new XAttribute("heading", Number(placement.TrueHeadingDegrees)),
                new XAttribute("imageComplexity", "VERY_SPARSE"),
                new XAttribute("altitudeIsAgl", "TRUE"),
                new XAttribute("snapToGround", "TRUE"),
                new XAttribute("snapToNormal", "FALSE"),
                new XElement("LibraryObject",
                    new XAttribute("name", asset.LibraryGuid.ToString("B").ToUpperInvariant()),
                    new XAttribute("scale", Number(asset.Scale)))));
        }
        // MSFS does not permit a runwayless Airport, and TaxiwayParking cannot exist without
        // an Airport. A site without airfield infrastructure is therefore compiled as scenery-only content.
        if (!definition.HasRunway) airport.Remove();
        XDocument? material = null;
        if (preset is null && definition.HasRunway)
        {
            material = LoadXml("PackageSources.materials.frontier-asphalt.material");
            material.Root!.SetAttributeValue("Name", materialName);
            material.Root.SetAttributeValue("Guid", materialGuid);
            if (definition.Surface == RunwaySurface.Dirt)
            {
                material.Root.SetAttributeValue("SurfaceType", "DIRT");
                material.Root.Element("TextureList")!.RemoveNodes();
                material.Root.Element("TextureList")!.Add(new XElement("Texture", new XAttribute("FileName", "FrontierDirt_Albedo.png"), new XAttribute("Binding", "MTL_BITMAP_DECAL0")));
                var diffuse = material.Root.Element("Attributes")!.Element("Diffuse")!;
                diffuse.SetAttributeValue("Red", "1"); diffuse.SetAttributeValue("Green", "1"); diffuse.SetAttributeValue("Blue", "1");
            }
        }

        // All XML is built before creating output; there are no culture-dependent text substitutions.
        Directory.CreateDirectory(root);
        Save(project, Path.Combine(root, "AirportProject.xml"));
        Save(package, Path.Combine(root, "PackageDefinitions", packageName + ".xml"));
        Save(airportXml, Path.Combine(root, "PackageSources", "data", "airport.xml"));
        if (material is not null) Save(material, Path.Combine(root, "PackageSources", "materials", materialName + ".material"));
        // MSBuild normalizes hyphens in resource directory segments (but not file names).
        CopyResource($"PackageDefinitions.{TemplatePackage.Replace('-', '_')}.ContentInfo.Thumbnail.jpg", Path.Combine(root, "PackageDefinitions", packageName, "ContentInfo", "Thumbnail.jpg"));
        foreach (var file in preset is null && definition.Surface == RunwaySurface.Asphalt ? new[] { "Asphalt01_Albedo.png", "Asphalt01_Albedo.png.FLAGS", "Asphalt01_COMP.png", "Asphalt01_COMP.png.FLAGS" } : Array.Empty<string>())
            CopyResource("PackageSources.materials.Textures." + file, Path.Combine(root, "PackageSources", "materials", "Textures", file));
        if (preset is null && definition.Surface == RunwaySurface.Dirt)
        {
            CopyResource("Dirt.Textures.FrontierDirt_Albedo.png", Path.Combine(root, "PackageSources/materials/Textures/FrontierDirt_Albedo.png"));
            CopyResource("PackageSources.materials.Textures.Asphalt01_Albedo.png.FLAGS", Path.Combine(root, "PackageSources/materials/Textures/FrontierDirt_Albedo.png.FLAGS"));
        }
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


