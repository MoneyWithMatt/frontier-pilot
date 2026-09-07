using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.MsfsSdk;

public sealed record BuildResult(int ExitCode, bool TimedOut, PackageValidation Validation, string PackageDirectory, string EvidenceDirectory);

public static class PackageBuilder
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<BuildResult> BuildAsync(GeneratedProject project, SdkInstallation sdk, TimeSpan timeout, CancellationToken cancellation = default, IProgress<string>? progress = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("MSFS Package Tool requires Windows.");
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var gate = new Semaphore(1, 1, "Local\\FrontierAirportBuilder-PackageBuild");
        if (!gate.WaitOne(0)) throw new IOException("Another Frontier build is running.");
        try
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report("Checking simulator is closed");
            // The simulator error log is shared. Don't conflate errors with an existing editor/build.
            foreach (var name in new[] { "fspackagetool", "FlightSimulator2024" })
            {
                var processes = Process.GetProcessesByName(name);
                var running = processes.Length > 0;
                foreach (var existingProcess in processes) existingProcess.Dispose();
                if (running) throw new IOException($"{name} is already running. Finish that session before this isolated CLI build.");
            }
            var evidence = Path.Combine(project.Directory, ".build");
            if (Directory.Exists(evidence) || Directory.Exists(Path.Combine(project.Directory, "Packages")))
                throw new IOException("Build output already exists; generate a fresh isolated project.");
            Directory.CreateDirectory(evidence);
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator 2024", "BuilderLogError.txt");
            var beforeLog = ReadSharedLog(logPath);
            var start = new ProcessStartInfo(sdk.PackageTool)
            {
                WorkingDirectory = project.Directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
            };
            start.ArgumentList.Add(project.ProjectFile);
            start.ArgumentList.Add("-nopause");
            start.Environment["MSFS2024_SDK"] = Path.TrimEndingDirectorySeparator(sdk.Root) + Path.DirectorySeparatorChar;
            await WriteJson(Path.Combine(evidence, "invocation.json"), new
            {
                Executable = start.FileName, start.WorkingDirectory, Arguments = start.ArgumentList.ToArray(),
                SdkRoot = sdk.Root, SdkVersion = sdk.Version, ToolSHA256 = Hash(sdk.PackageTool),
                MSFS2024_SDK = start.Environment["MSFS2024_SDK"], StartedUtc = DateTimeOffset.UtcNow, TimeoutSeconds = timeout.TotalSeconds
            });
            static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
            await File.WriteAllTextAsync(Path.Combine(evidence, "command.ps1"),
                $"Set-Location -LiteralPath {Quote(start.WorkingDirectory)}\n$env:MSFS2024_SDK = {Quote(start.Environment["MSFS2024_SDK"]!)}\n& {Quote(start.FileName)} {Quote(project.ProjectFile)} '-nopause'\n");
            await WriteInventory(project.Directory, Path.Combine(evidence, "inputs.json"));
            using var process = new Process { StartInfo = start };
            using var streamsCancellation = new CancellationTokenSource();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(timeout);
            var timedOut = false;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report("Compiling with MSFS Package Tool — this may take a few minutes");
                if (!process.Start()) throw new IOException("Could not start Package Tool.");
                process.StandardInput.Close();
                var stdout = Pump(process.StandardOutput, Path.Combine(evidence, "stdout.log"), streamsCancellation.Token);
                var stderr = Pump(process.StandardError, Path.Combine(evidence, "stderr.log"), streamsCancellation.Token);
                try { await process.WaitForExitAsync(deadline.Token); }
                catch (OperationCanceledException)
                {
                    timedOut = true;
                    // Never kill Steam and its unrelated games as an inherited child tree.
                    if (!process.HasExited) process.Kill();
                    await process.WaitForExitAsync();
                }
                streamsCancellation.CancelAfter(TimeSpan.FromSeconds(5));
                await Task.WhenAll(stdout, stderr);
            }
            catch (Exception error)
            {
                await File.WriteAllTextAsync(Path.Combine(evidence, "launch-error.log"), error.ToString());
                throw;
            }
            var afterLog = ReadSharedLog(logPath);
            progress?.Report("Checking package manifest, layout and builder diagnostics");
            var delta = afterLog.StartsWith(beforeLog, StringComparison.Ordinal) ? afterLog[beforeLog.Length..] : afterLog;
            await File.WriteAllTextAsync(Path.Combine(evidence, "builder-errors-this-run.log"), delta);
            var packageDirectory = Path.Combine(project.Directory, "Packages", project.PackageName);
            var result = new BuildResult(process.ExitCode, timedOut,
                PackageValidator.Validate(packageDirectory, process.ExitCode, timedOut, delta), packageDirectory, evidence);
            await WriteJson(Path.Combine(evidence, "result.json"), result);
            await WriteInventory(project.Directory, Path.Combine(evidence, "output-tree.json"));
            return result;
        }
        finally { gate.Release(); }
    }

    private static async Task Pump(StreamReader reader, string path, CancellationToken cancellation)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
        var buffer = new char[4096];
        try
        {
            while (await reader.ReadAsync(buffer.AsMemory(), cancellation) is var count && count > 0)
                await writer.WriteAsync(buffer.AsMemory(0, count));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private static string ReadSharedLog(string path)
    {
        if (!File.Exists(path)) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static Task WriteJson<T>(string path, T value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json));
    private static Task WriteInventory(string root, string destination) => WriteJson(destination,
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(root, p).StartsWith(".build" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal).Select(p => new { Path = Path.GetRelativePath(root, p), Bytes = new FileInfo(p).Length, SHA256 = Hash(p) }).ToArray());
}
