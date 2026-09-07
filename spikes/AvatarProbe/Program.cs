using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

if (!OperatingSystem.IsWindows() || args.Length != 3 || !int.TryParse(args[2], out int seconds) || seconds < 1 || seconds > 3600)
{
    Console.Error.WriteLine("Usage: AvatarProbe <SDK root> <new session directory> <seconds 1..3600>");
    return 2;
}
var root = Path.GetFullPath(args[0]);
var directory = Path.GetFullPath(args[1]);
if (Directory.Exists(directory)) throw new IOException("Use a new session directory.");
Directory.CreateDirectory(directory);
var phaseFile = Path.Combine(directory, "phase.txt");
File.WriteAllText(phaseFile, "unclassified");
var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
using var log = new StreamWriter(new FileStream(Path.Combine(directory, "observations.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
string phase = "unclassified";
void Record(string kind, object value) => log.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, phase, kind, value }, options));
var dll = Path.Combine(root, "SimConnect SDK", "lib", "SimConnect.dll");
var header = Path.Combine(root, "SimConnect SDK", "include", "SimConnect.h");
IntPtr library = IntPtr.Zero, connection = IntPtr.Zero;
bool quit = false;
Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit = true; };
// Constants come from the installed SDK header. These proxies remain distinct when control changes.
var targets = new[] { (Name: "aircraft", Id: 0u), (Name: "avatar", Id: uint.MaxValue - 127), (Name: "current", Id: uint.MaxValue - 126) };
var fields = new[] {
    ("PLANE LATITUDE", "degrees"), ("PLANE LONGITUDE", "degrees"),
    ("PLANE ALTITUDE", "meters"), ("PLANE HEADING DEGREES TRUE", "degrees"),
    ("PLANE PITCH DEGREES", "degrees"), ("PLANE BANK DEGREES", "degrees"),
    ("SIM ON GROUND", "bool"), ("PLANE ALT ABOVE GROUND", "meters"),
    ("GROUND ALTITUDE", "meters"), ("CAMERA STATE", "number"), ("CAMERA SUBSTATE", "number"),
    ("MAGVAR", "degrees"), ("IS CROUCH", "bool")
};
try
{
    library = NativeLibrary.Load(dll);
    Record("metadata", new {
        sdkVersion = File.ReadAllText(Path.Combine(root, "version.txt")).Trim(), root, dll,
        dllSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))),
        headerSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(header))),
        simulatorFiles = Process.GetProcessesByName("FlightSimulator2024").Select(p => {
            using (p) {
                var path = p.MainModule!.FileName;
                return new { path, fileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion,
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
            }
        }).ToArray(),
        cameraGetDeclared = File.ReadAllText(header).Contains("SimConnect_CameraGet("),
        cameraGetExported = NativeLibrary.TryGetExport(library, "SimConnect_CameraGet", out _),
        targets = targets.Select(t => new { t.Name, t.Id }),
        fields = fields.Select(f => new { name = f.Item1, units = f.Item2 }),
        note = "GROUND ALTITUDE is documented as aircraft-local even when requested on avatar. Camera state is not camera position/heading. All samples are candidates, not survey fixes."
    });
    T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    var open = Bind<Open>("SimConnect_Open");
    int hr = open(out connection, "Frontier Read-only Avatar Diagnostic", IntPtr.Zero, 0, IntPtr.Zero, uint.MaxValue);
    Record("open", new { hr });
    if (hr < 0) return 1;
    var add = Bind<Add>("SimConnect_AddToDataDefinition");
    var request = Bind<Request>("SimConnect_RequestDataOnSimObject");
    var dispatch = Bind<Dispatch>("SimConnect_GetNextDispatch");
    var packet = Bind<Packet>("SimConnect_GetLastSentPacketID");
    void Sent(string operation, int result, object details)
    {
        int packetHr = packet(connection, out uint packetId);
        Record("request", new { operation, result, packetHr, packetId, details });
    }
    for (int i = 0; i < fields.Length; i++)
        Sent("define", add(connection, (uint)i + 1, fields[i].Item1, fields[i].Item2, 4, 0, uint.MaxValue), new { field = fields[i].Item1 });
    var timer = Stopwatch.StartNew();
    long nextPoll = 0;
    while (!quit && timer.Elapsed.TotalSeconds < seconds && !File.Exists(Path.Combine(directory, "stop.txt")))
    {
        if (timer.ElapsedMilliseconds >= nextPoll)
        {
            try
            {
                string newPhase = File.ReadAllText(phaseFile).Trim();
                if (newPhase.Length > 0 && newPhase != phase) { phase = newPhase; Record("phase", phase); }
            }
            catch (IOException) { /* An external phase marker may be briefly locked. */ }
            for (int t = 0; t < targets.Length; t++)
            for (int f = 0; f < fields.Length; f++)
            {
                uint id = (uint)(100 * (t + 1) + f);
                Sent("sample", request(connection, id, (uint)f + 1, targets[t].Id, 1, 0, 0, 0, 0), new { id, target = targets[t].Name, field = fields[f].Item1 });
            }
            nextPoll = timer.ElapsedMilliseconds + 500;
        }
        // Drain a bounded batch so stop/phase controls remain responsive.
        for (int n = 0; n < 256; n++)
        {
            if (dispatch(connection, out var data, out uint size) < 0) break;
            if (size < 12) { Record("malformed", new { size }); continue; }
            int id = Marshal.ReadInt32(data, 8);
            if (id == 8 && size >= 48)
            {
                int requestId = Marshal.ReadInt32(data, 12);
                int t = requestId / 100 - 1, f = requestId % 100;
                if (t >= 0 && t < targets.Length && f >= 0 && f < fields.Length && Marshal.ReadInt32(data, 36) == 1)
                {
                    double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(data, 40));
                    Record("sample", new { requestId, target = targets[t].Name, requestedObjectId = targets[t].Id, returnedObjectId = unchecked((uint)Marshal.ReadInt32(data, 16)), field = fields[f].Item1, units = fields[f].Item2, value, finite = double.IsFinite(value) });
                }
            }
            else if (id == 1 && size >= 24)
                Record("exception", new { code = Marshal.ReadInt32(data, 12), sendId = Marshal.ReadInt32(data, 16), index = Marshal.ReadInt32(data, 20) });
            else if (id == 2 && size >= 284)
                Record("handshake", new { name = Marshal.PtrToStringAnsi(data + 12, 256)?.TrimEnd('\0'), version = string.Join(".", Enumerable.Range(0, 4).Select(i => Marshal.ReadInt32(data, 268 + 4 * i))) });
            else { Record("message", new { id, size }); if (id == 3) quit = true; }
        }
        Thread.Sleep(20);
    }
    Record("completed", new { elapsedSeconds = timer.Elapsed.TotalSeconds });
    return 0;
}
catch (Exception e) { Record("error", e.ToString()); return 1; }
finally
{
    if (library != IntPtr.Zero && connection != IntPtr.Zero)
        Record("close", Marshal.GetDelegateForFunctionPointer<Close>(NativeLibrary.GetExport(library, "SimConnect_Close"))(connection));
    if (library != IntPtr.Zero) NativeLibrary.Free(library);
}

[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
delegate int Open(out IntPtr h, string name, IntPtr window, uint message, IntPtr evt, uint config);
[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
delegate int Add(IntPtr h, uint definition, string name, string units, uint type, float epsilon, uint datum);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int Request(IntPtr h, uint request, uint definition, uint obj, uint period, uint flags, uint origin, uint interval, uint limit);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int Dispatch(IntPtr h, out IntPtr data, out uint size);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int Packet(IntPtr h, out uint id);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int Close(IntPtr h);
