using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

// Read-only capability/connection probe. No aircraft moves, scenery writes or guessed elevations.
if (!OperatingSystem.IsWindows() || args.Length != 2)
{
    Console.Error.WriteLine("Usage (Windows x64): TerrainProbe <MSFS2024 SDK root> <new report.json>");
    return 2;
}
var reportPath = Path.GetFullPath(args[1]);
if (File.Exists(reportPath)) throw new IOException("Evidence already exists: " + reportPath);
var root = Path.GetFullPath(args[0]);
var dll = Path.Combine(root, "SimConnect SDK", "lib", "SimConnect.dll");
var header = Path.Combine(root, "SimConnect SDK", "include", "SimConnect.h");
var report = new Dictionary<string, object?>
{
    ["startedUtc"] = DateTimeOffset.UtcNow,
    ["sdkRoot"] = root,
    ["sdkVersion"] = File.ReadAllText(Path.Combine(root, "version.txt")).Trim(),
    ["dllPath"] = dll,
    ["dllSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))),
    ["headerSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(header))),
    ["headerDeclaresRequestGroundInfo"] = File.ReadAllText(header).Contains("SimConnect_RequestGroundInfo"),
    ["arbitraryCoordinateElevationProven"] = false
};
IntPtr library = IntPtr.Zero, connection = IntPtr.Zero;
try
{
    library = NativeLibrary.Load(dll);
    report["exportsRequestGroundInfo"] = NativeLibrary.TryGetExport(library, "SimConnect_RequestGroundInfo", out _);
    var open = Bind<Open>("SimConnect_Open");
    var dispatch = Bind<Dispatch>("SimConnect_GetNextDispatch");
    var requestState = Bind<RequestState>("SimConnect_RequestSystemState");
    var timer = Stopwatch.StartNew();
    int hr;
    do
    {
        hr = open(out connection, "Frontier Terrain Capability Probe", IntPtr.Zero, 0, IntPtr.Zero, uint.MaxValue);
        if (hr >= 0) break;
        Thread.Sleep(1000);
    } while (timer.Elapsed < TimeSpan.FromSeconds(60));
    report["openHresult"] = $"0x{hr:X8}";
    bool handshake = false, stateReceived = false, sampleReceived = false;
    if (hr >= 0)
    {
        report["requestSimStateHresult"] = $"0x{requestState(connection, 1, "Sim"):X8}";
        var add = Bind<AddDefinition>("SimConnect_AddToDataDefinition");
        var definitions = new[] { ("PLANE LATITUDE", "degrees"), ("PLANE LONGITUDE", "degrees"), ("GROUND ALTITUDE", "meters") };
        report["definitionHresults"] = definitions.Select(d => $"0x{add(connection, 1, d.Item1, d.Item2, 4, 0, uint.MaxValue):X8}").ToArray();
        report["requestLocalGroundHresult"] = $"0x{Bind<RequestData>("SimConnect_RequestDataOnSimObject")(connection, 2, 1, 0, 1, 0, 0, 0, 0):X8}";
        timer.Restart();
        var messages = new List<object>();
        report["messages"] = messages;
        while (timer.Elapsed < TimeSpan.FromSeconds(20) && !(handshake && stateReceived && sampleReceived))
        {
            if (dispatch(connection, out var data, out var bytes) >= 0 && bytes >= 12)
            {
                int id = Marshal.ReadInt32(data, 8);
                messages.Add(new { id, bytes });
                // SDK SIMCONNECT_RECV_OPEN: 12-byte base, 256-byte name, four DWORD version fields.
                if (id == 2 && bytes >= 284)
                {
                    handshake = true;
                    report["simulatorName"] = Marshal.PtrToStringAnsi(data + 12, 256)?.TrimEnd('\0');
                    report["simulatorVersion"] = string.Join(".", Enumerable.Range(0, 4).Select(i => Marshal.ReadInt32(data, 268 + 4 * i)));
                }
                // SDK SIMCONNECT_RECV_SYSTEM_STATE: request ID, integer, float, string after base.
                if (id == 15 && bytes >= 24 && Marshal.ReadInt32(data, 12) == 1)
                {
                    stateReceived = true;
                    report["simState"] = Marshal.ReadInt32(data, 16);
                }
                if (id == 1 && bytes >= 24)
                    report["simConnectException"] = new { code = Marshal.ReadInt32(data, 12), sendId = Marshal.ReadInt32(data, 16), index = Marshal.ReadInt32(data, 20) };
                // Seven DWORD fields after the 12-byte base put the SIMOBJECT_DATA payload at 40.
                if (id == 8 && bytes >= 64 && Marshal.ReadInt32(data, 12) == 2 && Marshal.ReadInt32(data, 36) == 3)
                {
                    var values = new double[3];
                    Marshal.Copy(data + 40, values, 0, 3);
                    sampleReceived = true;
                    report["userAircraftSample"] = new { latitude = values[0], longitude = values[1], groundAltitudeMeters = values[2], usableForArbitraryCoordinates = false };
                }
                if (id == 3) break;
            }
            else Thread.Sleep(50);
        }
    }
    report["liveHandshakeReceived"] = handshake;
    report["systemStateReceived"] = stateReceived;
    report["userAircraftSampleReceived"] = sampleReceived;
    report["limitation"] = "RequestGroundInfo is absent from this MSFS SDK. Documented GROUND ALTITUDE is user-aircraft-local, not a latitude/longitude terrain query. No coordinate elevation returned; generation must remain blocked.";

    T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
}
catch (Exception ex)
{
    report["error"] = ex.ToString();
}
finally
{
    if (connection != IntPtr.Zero && library != IntPtr.Zero)
        report["closeHresult"] = $"0x{Marshal.GetDelegateForFunctionPointer<Close>(NativeLibrary.GetExport(library, "SimConnect_Close"))(connection):X8}";
    if (library != IntPtr.Zero) NativeLibrary.Free(library);
    report["finishedUtc"] = DateTimeOffset.UtcNow;
    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    using var output = new FileStream(reportPath, FileMode.CreateNew);
    using var writer = new StreamWriter(output);
    writer.WriteLine(json);
    Console.WriteLine(json);
}
// Deliberately nonzero: connecting is not proof of arbitrary-location elevation.
return 1;

[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
delegate int Open(out IntPtr handle, string name, IntPtr window, uint message, IntPtr eventHandle, uint config);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int Dispatch(IntPtr handle, out IntPtr data, out uint bytes);
[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
delegate int RequestState(IntPtr handle, uint request, string state);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int Close(IntPtr handle);
[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
delegate int AddDefinition(IntPtr handle, uint definition, string name, string units, uint type, float epsilon, uint datumId);
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
delegate int RequestData(IntPtr handle, uint request, uint definition, uint objectId, uint period, uint flags, uint origin, uint interval, uint limit);
