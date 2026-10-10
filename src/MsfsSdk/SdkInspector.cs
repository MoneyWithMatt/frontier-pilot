using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.MsfsSdk;

public static class SdkInspector
{
    // Explicit root for the proof: automatic discovery is a later milestone.
    public static SdkInstallation Inspect(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new IOException("Microsoft Flight Simulator 2024 SDK is not configured. Open Dependency setup to download it or select an installed SDK folder.");
        root = Path.GetFullPath(root);
        var versionFile = Path.Combine(root, "version.txt");
        var tool = Path.Combine(root, "Tools", "bin", "fspackagetool.exe");
        var sample = Path.Combine(root, "Samples", "DevmodeProjects", "Sceneries", "SimpleAirport", "AirportProject.xml");
        foreach (var file in new[] { versionFile, tool, sample })
            if (!File.Exists(file)) throw new FileNotFoundException("SDK installation is incomplete.", file);
        var version = File.ReadAllText(versionFile).Trim();
        if (!Version.TryParse(version, out _)) throw new InvalidDataException("Invalid SDK version.txt.");
        return new SdkInstallation(root, version, tool, sample);
    }
}
