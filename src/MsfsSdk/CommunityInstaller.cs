using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FrontierAirportBuilder.MsfsSdk;

public sealed record PackageFile(string Path, long Bytes, string Sha256);
public sealed record InstallationReceipt(string Identifier, string PackageName, string InstallPath, string? BackupPath,
    DateTimeOffset InstalledUtc, IReadOnlyList<PackageFile> Files);

/// <summary>The proven stage/hash/backup/install workflow, extracted from the dedicated PowerShell installers.
/// Only receipt-owned packages may be replaced. All recursive traversal rejects reparse points.</summary>
public sealed class CommunityInstaller(string packagesRoot, string stateRoot, Action assertSimulatorClosed, bool allowTestPackages = false)
{
    public string Community => System.IO.Path.Combine(packagesRoot, "Community2024");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string PackageName(string identifier)
    {
        if (!Regex.IsMatch(identifier, "\\A[A-Z][A-Z0-9]{2,7}\\z")) throw new ArgumentException("Identifier must be 3–8 uppercase letters/digits starting with a letter.");
        if (identifier is "FPT1" or "FPSC") throw new IOException("FPT1 and FPSC are preserved proof packages. Choose a new identifier.");
        return "frontier-airport-" + identifier.ToLowerInvariant();
    }
    private string Ledger(string id) => System.IO.Path.Combine(stateRoot, "ownership", PackageName(id) + ".json");
    public string OwnershipFile(string id) {var path=Ledger(id);SafePath(path);return path;}
    public InstallationReceipt InspectOwnedRemoval(string id)
    {
        assertSimulatorClosed();ValidateDestination();
        string name=PackageName(id),target=System.IO.Path.Combine(Community,name);
        Within(target,Community);SafePath(Ledger(id));
        var receipt=JsonSerializer.Deserialize<InstallationReceipt>(File.ReadAllText(Ledger(id)))??throw new IOException("Unreadable ownership receipt.");
        if(receipt.Identifier!=id||receipt.PackageName!=name||!SamePath(receipt.InstallPath,target))throw new IOException("Ownership receipt does not match the removal destination.");
        if(File.Exists(target))throw new IOException("Installed package path is a file.");
        if(Directory.Exists(target))
        {
            Compare(receipt.Files,Inventory(target));
            using var manifest=JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(target,"manifest.json")));
            bool synthetic=manifest.RootElement.TryGetProperty("frontier_test_only",out var flag)&&flag.ValueKind==JsonValueKind.True;
            if(manifest.RootElement.GetProperty("creator").GetString()!="Frontier Pilot"||synthetic!=allowTestPackages)
                throw new IOException("Installed package ownership/mode is not appropriate for this removal.");
        }
        return receipt;
    }
    // Uses the same cross-process gate as Install, including older running Frontier versions.
    public static IDisposable LockForRemoval()
    {
        var gate=new Semaphore(1,1,"Local\\FrontierAirportBuilder-CommunityInstall");
        if(!gate.WaitOne(0)){gate.Dispose();throw new IOException("Another Frontier installation/removal is running.");}
        return new RemovalGate(gate);
    }
    private sealed class RemovalGate(Semaphore gate):IDisposable {public void Dispose(){gate.Release();gate.Dispose();}}
    public static void VerifyInventory(IReadOnlyList<PackageFile> expected,IReadOnlyList<PackageFile> actual)=>Compare(expected,actual);
    public string CheckIdentifier(string id)
    {
        string name = PackageName(id);
        ValidateDestination();
        string legacy = System.IO.Path.Combine(packagesRoot, "Community", name), target = System.IO.Path.Combine(Community, name);
        SafePath(legacy); SafePath(target); SafePath(Ledger(id));
        if (Directory.Exists(legacy) || File.Exists(legacy)) throw new IOException("A matching legacy Community package exists; choose another identifier.");
        if (File.Exists(target)) throw new IOException("The package destination is a file.");
        if (Directory.Exists(target))
        {
            if (!File.Exists(Ledger(id))) throw new IOException("That package already exists and is not owned by this app. Choose another identifier.");
            var owner = JsonSerializer.Deserialize<InstallationReceipt>(File.ReadAllText(Ledger(id))) ?? throw new IOException("Unreadable ownership receipt.");
            if (owner.Identifier != id || owner.PackageName != name || !SamePath(owner.InstallPath, target)) throw new IOException("Ownership receipt does not match the destination.");
            Compare(owner.Files, Inventory(target));
            return "Replace this app's previous package; a backup will be kept.";
        }
        return "Available in your managed Community packages.";
    }
    public InstallationReceipt Install(string id, string source, IReadOnlyList<PackageFile> expected,
        CancellationToken cancellation = default, IProgress<string>? progress = null)
    {
        using var gate = new Semaphore(1, 1, "Local\\FrontierAirportBuilder-CommunityInstall");
        if (!gate.WaitOne(0)) throw new IOException("Another Frontier installation is running.");
        try
        {
            cancellation.ThrowIfCancellationRequested(); assertSimulatorClosed(); CheckIdentifier(id);
            string name = PackageName(id), target = System.IO.Path.Combine(Community, name);
            SafePath(source);
            if (Overlaps(source, packagesRoot) || IsUnder(stateRoot, source))
                throw new IOException("Source overlaps installation storage or Community.");
            source = System.IO.Path.GetFullPath(source);
            if (!string.Equals(new DirectoryInfo(source).Name, name, StringComparison.OrdinalIgnoreCase)) throw new IOException("Wrong source package name.");
            SafePath(source); Compare(expected, Inventory(source));
            var validation = PackageValidator.Validate(source, 0, false, "");
            if (!validation.Success) throw new IOException(string.Join("\n", validation.Errors));
            using (var m = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(source, "manifest.json"))))
            {
                if (m.RootElement.GetProperty("creator").GetString() != "Frontier Pilot") throw new IOException("Package creator is not Frontier Pilot.");
                bool synthetic = m.RootElement.TryGetProperty("frontier_test_only", out var flag) && flag.ValueKind == JsonValueKind.True;
                if (synthetic != allowTestPackages) throw new IOException("Test packages may only be installed by an isolated dry-run installer.");
            }
            string transaction = System.IO.Path.Combine(stateRoot, "installations", Guid.NewGuid().ToString("N"));
            string staged = System.IO.Path.Combine(transaction, name), backup = System.IO.Path.Combine(transaction, "previous");
            Within(transaction, stateRoot); Within(target, Community); Within(staged, transaction); Within(backup, transaction);
            Directory.CreateDirectory(staged);
            progress?.Report("Staging and verifying this airport's package");
            foreach (var f in expected)
            {
                cancellation.ThrowIfCancellationRequested();
                var from = System.IO.Path.Combine(source, f.Path); var to = System.IO.Path.Combine(staged, f.Path);
                Within(from, source); Within(to, staged); SafePath(from); SafePath(to);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!); File.Copy(from, to, false);
            }
            Compare(expected, Inventory(staged)); cancellation.ThrowIfCancellationRequested();
            assertSimulatorClosed(); CheckIdentifier(id);
            bool movedOld = false, movedNew = false;
            try
            {
                progress?.Report("Installing — preserving any previous version");
                // No cancellation inside the short commit/rollback section.
                if (Directory.Exists(target)) { Directory.Move(target, backup); movedOld = true; }
                Directory.Move(staged, target); movedNew = true;
                Compare(expected, Inventory(target));
                var receipt = new InstallationReceipt(id, name, target, movedOld ? backup : null, DateTimeOffset.UtcNow, expected);
                File.WriteAllText(System.IO.Path.Combine(transaction, "receipt.json"), JsonSerializer.Serialize(receipt, Json));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Ledger(id))!);
                string pendingLedger = System.IO.Path.Combine(transaction, "ownership.json");
                File.WriteAllText(pendingLedger, JsonSerializer.Serialize(receipt, Json));
                File.Move(pendingLedger, Ledger(id), true);
                return receipt;
            }
            catch
            {
                // Preserve failed content for diagnosis; never recursively delete Community content.
                if (movedNew) Directory.Move(target, System.IO.Path.Combine(transaction, "failed-install"));
                if (movedOld) Directory.Move(backup, target);
                throw;
            }
        }
        finally { gate.Release(); }
    }
    public static IReadOnlyList<PackageFile> Inventory(string root)
    {
        SafePath(root);
        var files = new List<PackageFile>();
        void Walk(string dir)
        {
            foreach (var p in Directory.EnumerateFileSystemEntries(dir))
            {
                SafePath(p);
                if (Directory.Exists(p)) Walk(p);
                else { using var stream = File.OpenRead(p); files.Add(new(System.IO.Path.GetRelativePath(root, p), stream.Length, Convert.ToHexString(SHA256.HashData(stream)))); }
            }
        }
        Walk(root); return files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static void Compare(IReadOnlyList<PackageFile> expected, IReadOnlyList<PackageFile> actual)
    {
        if (expected.Count == 0 || expected.Count != actual.Count || expected.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Count)
            throw new IOException("Package file inventory differs from the verified build/installation.");
        foreach (var f in expected)
            if (!actual.Any(a => string.Equals(a.Path, f.Path, StringComparison.OrdinalIgnoreCase) && a.Bytes == f.Bytes && a.Sha256 == f.Sha256))
                throw new IOException("Package changed since verification: " + f.Path);
    }
    private static bool SamePath(string a, string b) => string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    public static void Within(string path, string root)
    {
        if (!System.IO.Path.GetFullPath(path).StartsWith(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root)) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Path escapes its intended directory.");
        SafePath(path);
    }
    private static bool IsUnder(string child, string parent) =>
        SamePath(child,parent) || System.IO.Path.GetFullPath(child).StartsWith(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(parent)) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool Overlaps(string a, string b) => IsUnder(a,b) || IsUnder(b,a);
    public void ValidateDestination()
    {
        StrictRoot(packagesRoot); StrictRoot(stateRoot);
        SafePath(Community);
        if (Overlaps(packagesRoot, stateRoot)) throw new IOException("Community packages and app state must be separate directories.");
        if (!string.Equals(System.IO.Path.GetPathRoot(packagesRoot), System.IO.Path.GetPathRoot(stateRoot), StringComparison.OrdinalIgnoreCase))
            throw new IOException("This prototype requires Community and app storage on the same drive for safe replacement.");
        if (!Directory.Exists(Community)) throw new IOException("The configured Community2024 folder does not exist.");
        if (allowTestPackages)
        {
            string sandbox = System.IO.Path.GetDirectoryName(packagesRoot)!;
            string testParent = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FrontierAirportBuilder-DryRuns");
            Within(sandbox, testParent);
            if (!Guid.TryParseExact(new DirectoryInfo(sandbox).Name,"N",out _) ||
                !SamePath(packagesRoot,System.IO.Path.Combine(sandbox,"Packages")) ||
                !SamePath(stateRoot,System.IO.Path.Combine(sandbox,"state")))
                throw new IOException("Dry-run destination is not an isolated Frontier sandbox.");
        }
    }
    private static void StrictRoot(string path)
    {
        SafePath(path);
        if (SamePath(path,System.IO.Path.GetPathRoot(path)!)) throw new IOException("A drive root is not an installation directory.");
    }
    public static void SafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\") ||
            path.Contains('/') || path.Length > 240 || path.Skip(2).Contains(':') ||
            path.Split('\\').Skip(1).Any(p => p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ')))
            throw new IOException("Use an unambiguous absolute local path without redirection, traversal or alternate streams.");
        for (string? p = System.IO.Path.GetFullPath(path); p != null; p = System.IO.Path.GetDirectoryName(p))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(p); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Redirected files/folders are not supported for safe installation: " + p);
        }
    }
}
