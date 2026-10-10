using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FrontierAirportBuilder.MsfsSdk;

namespace FrontierAirportBuilder.Workflow;

public sealed record DependencyStatus(string SdkRoot, bool SdkReady, string SdkMessage, string CommunityPath, string CommunityMessage);
public sealed record DependencyDownload(string Id, string Name, Uri Url, string Size);

/// <summary>Read-only discovery and explicit downloads; never invokes Package Tool or changes Community content.</summary>
public static class DependencySetup
{
    public const string RequiredVersion = "1.2.4";
    public static readonly DependencyDownload Core = Package("Core", "264 MB");
    public static readonly DependencyDownload Samples = Package("Samples", "3.2 GB");
    private static DependencyDownload Package(string id, string size) => new(id, $"SDK {RequiredVersion} {id}",
        new Uri($"https://sdk.flightsimulator.com/msfs2024/files/installers/{RequiredVersion}/MSFS2024_SDK_{id}_Installer_{RequiredVersion}.zip"), size);

    public static DependencyStatus Detect(IEnumerable<string> candidates, string configPath)
    {
        string selected = "", sdkMessage = "SDK missing. Install Core and Samples, then recheck. Existing SDK installations can be selected below.";
        bool ready = false;
        void RecordFailure(string candidate,string message)
        { if(selected.Length==0){selected=candidate;sdkMessage=message;} }
        foreach (var candidate in candidates.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(candidate)) continue;
            // Do not treat an arbitrary existing folder as an SDK installation.
            if (!File.Exists(Path.Combine(candidate, "version.txt"))) continue;
            try
            {
                var version = File.ReadAllText(Path.Combine(candidate, "version.txt")).Trim();
                if (version != RequiredVersion) throw new IOException($"SDK {version} found. Avatar survey requires SDK {RequiredVersion}; select the qualified version.");
                var sdk = SdkInspector.Inspect(candidate);
                VerifyHash(Path.Combine(sdk.Root, "SimConnect SDK", "lib", "SimConnect.dll"), LiveSurveyCapture.TestedSimConnectHash);
                VerifyHash(Path.Combine(sdk.Root, "SimConnect SDK", "include", "SimConnect.h"), LiveSurveyCapture.TestedHeaderHash);
                ready = true;
                selected = sdk.Root;
                sdkMessage = $"SDK {RequiredVersion} components verified. Live survey still requires the qualified simulator build and a loaded flight.";
                break;
            }
            catch (FileNotFoundException ex)
            {
                var sample = Path.Combine(candidate,"Samples","DevmodeProjects","Sceneries","SimpleAirport","AirportProject.xml");
                RecordFailure(candidate,string.Equals(ex.FileName,sample,StringComparison.OrdinalIgnoreCase)
                    ? $"SDK Core {RequiredVersion} found. Samples is missing: install SDK Samples into this same folder, then recheck."
                    : "SDK components are missing. Install Core and Samples to the same SDK folder, then recheck.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { RecordFailure(candidate,ex.Message); }
            // Retain a discovered incomplete installation for display; a later valid installation still wins.
        }
        string community = "", communityMessage;
        try
        {
            var matches = Regex.Matches(File.ReadAllText(configPath), "(?m)^\\s*InstalledPackagesPath\\s+\"([^\"]+)\"\\s*$");
            if (matches.Count != 1) throw new IOException("MSFS configuration must contain exactly one InstalledPackagesPath.");
            var packages = matches[0].Groups[1].Value;
            if (!Path.IsPathFullyQualified(packages)) throw new IOException("MSFS packages path must be absolute.");
            community = Path.Combine(packages, "Community2024");
            communityMessage = Directory.Exists(community) ? "Community folder detected." : "Configured Community folder is missing. Launch MSFS to finish its setup, then recheck.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { communityMessage = "Community folder unavailable: " + ex.Message + " Launch MSFS 2024 once, then recheck."; }
        return new(selected, ready, sdkMessage, community, communityMessage);
    }

    private static void VerifyHash(string path, string expected)
    {
        using var file = File.OpenRead(path);
        if (Convert.ToHexString(SHA256.HashData(file)) != expected)
            throw new IOException("SDK files do not match the qualified build. Survey remains disabled; select the original SDK 1.2.4 installation.");
    }

    public static async Task<string> DownloadAsync(DependencyDownload package, string downloadsRoot, IProgress<string> progress,
        CancellationToken token, HttpClient? client = null)
    {
        if (package != Core && package != Samples) throw new ArgumentException("Only the pinned Microsoft SDK packages can be downloaded.");
        var destination = Path.Combine(Path.GetFullPath(downloadsRoot), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        var archive = Path.Combine(destination, "download.zip.part");
        var partialInstaller = Path.Combine(destination, "installer.msi.part");
        var partialCabinets = new List<string>();
        bool ownsClient = client is null;
        client ??= new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            using var response = await client.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0;
            const long limit = 8L * 1024 * 1024 * 1024;
            if (total > limit) throw new IOException("SDK download exceeds the supported size.");
            if (total > 0 && new DriveInfo(Path.GetPathRoot(destination)!).AvailableFreeSpace < total * 2 + 1024L * 1024 * 1024)
                throw new IOException("Not enough free space to download and extract this SDK installer.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long received = 0; var lastReport = DateTime.UtcNow;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    received += read;
                    if (received > limit) throw new IOException("SDK download exceeds the supported size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 250)
                    {
                        progress.Report(total > 0 ? $"Downloading {package.Name}: {received * 100 / total}% ({received / 1_000_000} MB)" : $"Downloading {package.Name}: {received / 1_000_000} MB");
                        lastReport = DateTime.UtcNow;
                    }
                }
                if (total > 0 && received != total) throw new IOException("SDK download was incomplete. Retry the download.");
            }
            progress.Report("Extracting the Microsoft installer…");
            using var zip = ZipFile.OpenRead(archive);
            var installers = zip.Entries.Where(x => x.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (installers.Length != 1 || installers[0].Length > limit) throw new IOException("Unexpected SDK archive contents. Installer was not accepted.");
            var parent = installers[0].FullName[..^installers[0].Name.Length];
            var cabinets = zip.Entries.Where(x => x.Name.EndsWith(".cab", StringComparison.OrdinalIgnoreCase) && x.FullName == parent + x.Name).ToArray();
            if (cabinets.Any(x => x.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) ||
                cabinets.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != cabinets.Length ||
                cabinets.Sum(x => x.Length) > limit - installers[0].Length)
                throw new IOException("Unexpected SDK cabinet payloads. Installer was not accepted.");
            // Microsoft's installer references sibling cabinets; extracting only the MSI would leave it unusable.
            foreach (var cabinet in cabinets)
            {
                var partial = Path.Combine(destination, cabinet.Name + ".part");partialCabinets.Add(partial);
                await using (var input = cabinet.Open())
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    await input.CopyToAsync(output, token);
                File.Move(partial, Path.Combine(destination, cabinet.Name));
            }
            var installer = Path.Combine(destination, "MicrosoftSdkInstaller.msi");
            await using (var input = installers[0].Open())
            await using (var output = new FileStream(partialInstaller, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output, token);
            token.ThrowIfCancellationRequested();
            File.Move(partialInstaller, installer);
            return installer;
        }
        finally
        {
            if (ownsClient) client.Dispose();
            // Only remove this invocation's partial archive; downloaded installers remain for audit/retry.
            if (File.Exists(archive)) File.Delete(archive);
            if (File.Exists(partialInstaller)) File.Delete(partialInstaller);
            foreach (var partial in partialCabinets) if (File.Exists(partial)) File.Delete(partial);
        }
    }
}
