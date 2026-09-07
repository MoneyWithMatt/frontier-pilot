using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.MsfsSdk;

public static class LiveSurveyCapture
{
    public const string TestedSimulatorHash = "82AF1D70611D0FA43291BB5D0DE71310A3AD458ADA8883AC3BBA370CF2A01F48";
    public const string TestedSimConnectHash = "E54A505BE0B0A4CDC98DFB45F0C202BA9637F5D494C0919040BC89AC67D92ADB";
    public const string TestedHeaderHash = "3684C4B50630DB2ABEC9815105498FA5B921C653F57423B9B5E6214A60CAABEA";

    // Synchronous dispatch keeps all SDK calls on one thread. No SDK write or movement functions are bound.
    public static SurveyPoint Capture(string sdkRoot, string newDirectory, CancellationToken cancel = default)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("Windows x64 is required.");
        var dir = Path.GetFullPath(newDirectory);
        if (Directory.Exists(dir) || File.Exists(dir)) throw new IOException("Capture evidence directory must be new.");
        Directory.CreateDirectory(dir);
        var json = new JsonSerializerOptions { WriteIndented = false, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        using var log = new StreamWriter(new FileStream(Path.Combine(dir, "observations.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        void Log(string kind, object data) => log.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind, data }, json));
        void Save(string name, object data) => File.WriteAllText(Path.Combine(dir, name), JsonSerializer.Serialize(data, new JsonSerializerOptions(json) { WriteIndented = true }));
        string Hash(string p) { using var s = File.OpenRead(p); return Convert.ToHexString(SHA256.HashData(s)); }
        IntPtr library = IntPtr.Zero, connection = IntPtr.Zero;
        try
        {
            string root = Path.GetFullPath(sdkRoot), dll = Path.Combine(root, "SimConnect SDK/lib/SimConnect.dll");
            string sdkVersion = File.ReadAllText(Path.Combine(root, "version.txt")).Trim();
            var processes = Process.GetProcessesByName("FlightSimulator2024");
            string simulatorPath;
            try { if (processes.Length != 1) throw new IOException("Exactly one running MSFS 2024 instance is required."); simulatorPath = processes[0].MainModule!.FileName; }
            finally { foreach (var p in processes) p.Dispose(); }
            string sdkHash = Hash(dll), simulatorHash = Hash(simulatorPath), headerHash = Hash(Path.Combine(root, "SimConnect SDK/include/SimConnect.h"));
            var metadata = new { sdkRoot = root, sdkVersion, dll, sdkHash, headerHash, simulatorPath, simulatorHash,
                simulatorVersion = FileVersionInfo.GetVersionInfo(simulatorPath).FileVersion,
                qualification = "Sedona empirical prototype; Microsoft documents GROUND ALTITUDE as aircraft-local. Return A was 66 cm away, outside original 50 cm criterion; accepted by product owner.",
                scope = "Single avatar ground point; does not certify runway footprint flatness or airport separation." };
            Save("metadata.json", metadata);
            if (sdkVersion != "1.2.4" || sdkHash != TestedSimConnectHash || simulatorHash != TestedSimulatorHash || headerHash != TestedHeaderHash)
                throw new IOException("Simulator/SDK differs from the empirically tested build. Capture refused pending revalidation.");
            library = NativeLibrary.Load(dll);
            T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            void Check(int hr, string operation) { Log("call", new { operation, hr }); if (hr < 0) throw new IOException($"{operation} failed: 0x{hr:X8}"); }
            Check(Bind<Open>("SimConnect_Open")(out connection, "Frontier capture-survey read only", IntPtr.Zero, 0, IntPtr.Zero, uint.MaxValue), "Open");
            var add = Bind<Add>("SimConnect_AddToDataDefinition");
            var fields = new[] { ("PLANE LATITUDE", "degrees"), ("PLANE LONGITUDE", "degrees"), ("PLANE ALTITUDE", "meters"),
                ("PLANE HEADING DEGREES TRUE", "degrees"), ("GROUND ALTITUDE", "meters"), ("SIM ON GROUND", "bool"),
                ("PLANE ALT ABOVE GROUND", "meters"), ("IS CROUCH", "bool"), ("MAGVAR", "degrees") };
            foreach (var f in fields) Check(add(connection, 1, f.Item1, f.Item2, 4, 0, uint.MaxValue), "Define " + f.Item1);
            var request = Bind<Request>("SimConnect_RequestDataOnSimObject");
            var dispatch = Bind<Dispatch>("SimConnect_GetNextDispatch");
            uint[] proxies = [0, uint.MaxValue - 127, uint.MaxValue - 126];
            uint sequence = 0;
            bool handshake = false;
            var frames = new List<SurveyFrame>();
            var timer = Stopwatch.StartNew();
            string reason = "No samples received.";
            while (timer.Elapsed.TotalSeconds < 60)
            {
                cancel.ThrowIfCancellationRequested();
                uint baseId = ++sequence * 3;
                var batch = new Dictionary<uint, SurveyObject>();
                var arrivals = new List<double>();
                for (uint i = 0; i < 3; i++) Check(request(connection, baseId + i, 1, proxies[i], 1, 0, 0, 0, 0), "Request " + (baseId + i));
                var deadline = timer.Elapsed.TotalSeconds + 1;
                while (batch.Count < 3 && timer.Elapsed.TotalSeconds < deadline)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (dispatch(connection, out var data, out uint size) < 0) { Thread.Sleep(10); continue; }
                    if (size < 12) throw new IOException("Malformed SimConnect reply.");
                    int id = Marshal.ReadInt32(data, 8);
                    if (id == 1) { Log("exception", new { code = size >= 24 ? Marshal.ReadInt32(data, 12) : -1 }); throw new IOException("SimConnect reported an exception; capture refused."); }
                    if (id == 3) throw new IOException("MSFS disconnected.");
                    if (id == 2 && size >= 284) { handshake = true; Log("handshake", new { name = Marshal.PtrToStringAnsi(data + 12, 256)?.TrimEnd('\0'), version = string.Join(".", Enumerable.Range(0, 4).Select(i => Marshal.ReadInt32(data, 268 + 4 * i))) }); }
                    if (id != 8) continue;
                    if (size < 112 || Marshal.ReadInt32(data, 36) != 9 || Marshal.ReadInt32(data, 20) != 1) throw new IOException("Invalid object payload.");
                    uint requestId = unchecked((uint)Marshal.ReadInt32(data, 12));
                    if (requestId < baseId || requestId > baseId + 2) throw new IOException("Stale object response.");
                    var v = new double[9]; Marshal.Copy(data + 40, v, 0, 9);
                    var obj = new SurveyObject(unchecked((uint)Marshal.ReadInt32(data, 16)), v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8]);
                    if (!batch.TryAdd(requestId - baseId, obj)) throw new IOException("Duplicate response.");
                    arrivals.Add(timer.Elapsed.TotalSeconds);
                    Log("object", new { sequence, proxy = proxies[requestId-baseId], obj });
                }
                if (batch.Count != 3 || arrivals.Max() - arrivals.Min() > .25) throw new IOException("Missing or unsynchronised object readings.");
                var frame = new SurveyFrame(timer.Elapsed.TotalSeconds, batch[0], batch[1], batch[2]);
                frames.Add(frame); Log("frame", frame);
                while (frames.Count > 2 && frame.Seconds - frames[1].Seconds >= 5) frames.RemoveAt(0);
                reason = SurveyCapture.Validate(frames) ?? "";
                if (handshake && reason == "")
                {
                    var point = SurveyCapture.Create(frames);
                    Save("survey.json", point); Save("result.json", new { success = true, frameCount = frames.Count, durationSeconds = frames[^1].Seconds - frames[0].Seconds });
                    return point;
                }
                Log("waiting", reason);
                Thread.Sleep(250);
            }
            throw new IOException("Capture refused after 60 seconds: " + reason);
        }
        catch (Exception e) { Save("result.json", new { success = false, reason = e.Message }); throw; }
        finally
        {
            if (connection != IntPtr.Zero && library != IntPtr.Zero) Log("close", Marshal.GetDelegateForFunctionPointer<Close>(NativeLibrary.GetExport(library, "SimConnect_Close"))(connection));
            if (library != IntPtr.Zero) NativeLibrary.Free(library);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Open(out IntPtr h, string name, IntPtr window, uint message, IntPtr evt, uint config);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Add(IntPtr h, uint def, string name, string units, uint type, float epsilon, uint datum);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Request(IntPtr h, uint request, uint def, uint obj, uint period, uint flags, uint origin, uint interval, uint limit);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Dispatch(IntPtr h, out IntPtr data, out uint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Close(IntPtr h);
}
