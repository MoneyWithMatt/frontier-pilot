using System.Text.Json;

namespace FrontierAirportBuilder.MsfsSdk;

public sealed record PackageValidation(bool Success, IReadOnlyList<string> Errors, int LayoutEntries);

public static class PackageValidator
{
    public static PackageValidation Validate(string packageDirectory, int exitCode, bool timedOut, string builderErrors)
    {
        var errors = new List<string>();
        if (exitCode != 0) errors.Add($"Package Tool exited with code {exitCode}.");
        if (timedOut) errors.Add("Package Tool timed out or was cancelled; check for a remaining simulator process.");
        if (!string.IsNullOrWhiteSpace(builderErrors)) errors.Add("The simulator wrote errors to BuilderLogError.txt during this build.");
        var entries = 0;
        try
        {
            var root = Path.GetFullPath(packageDirectory) + Path.DirectorySeparatorChar;
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
            if (manifest.RootElement.GetProperty("package_order_hint").GetString() != "CUSTOM_AIRPORT")
                errors.Add("Manifest does not declare CUSTOM_AIRPORT.");
            using var layout = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "layout.json")));
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var foundBgl = false;
            foreach (var entry in layout.RootElement.GetProperty("content").EnumerateArray())
            {
                entries++;
                var relative = entry.GetProperty("path").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') ||
                    relative.Split('/', '\\').Any(p => p is ".." or "."))
                { errors.Add("Invalid layout path: " + relative); continue; }
                var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !paths.Add(path))
                { errors.Add("Escaping or duplicate layout path: " + relative); continue; }
                var expected = entry.GetProperty("size").GetInt64();
                var file = new FileInfo(path);
                if (!file.Exists || file.Length != expected || expected < 0)
                    errors.Add("Missing file or size mismatch: " + relative);
                else if (path.EndsWith(".bgl", StringComparison.OrdinalIgnoreCase) && file.Length > 0) foundBgl = true;
            }
            if (!foundBgl) errors.Add("No non-empty BGL was verified in the layout.");
            if (entries == 0) errors.Add("Layout is empty.");
        }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { errors.Add("Package metadata is missing or invalid: " + error.Message); }
        return new(errors.Count == 0, errors, entries);
    }
}
