using System.Diagnostics;
using System.Runtime.InteropServices;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.MsfsSdk;

/// <summary>Read-only telemetry for SIMCONNECT_OBJECT_ID_USER, which the SDK documents as
/// the user's aircraft. It intentionally does not query avatar or camera proxies.</summary>
public sealed class LiveAircraftTelemetry : IAircraftTelemetrySource
{
    private const uint NumericDefinition = 20;
    private const uint IdentityDefinition = 21;
    private const uint UserAircraft = 0;
    private const uint Once = 1;
    private const uint Float64 = 4;
    private const uint String128 = 8;

    public Task RunAsync(string sdkRoot, IProgress<AircraftTelemetry> updates, CancellationToken cancellationToken) =>
        Task.Run(() => Run(sdkRoot, updates, cancellationToken), cancellationToken);

    private static void Run(string sdkRoot, IProgress<AircraftTelemetry> updates, CancellationToken cancel)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("Live tracking requires Windows x64.");
        ArgumentNullException.ThrowIfNull(updates);
        var sdk = SdkInspector.Inspect(sdkRoot);
        var dll = Path.Combine(sdk.Root, "SimConnect SDK", "lib", "SimConnect.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("The configured SDK does not contain SimConnect.dll.", dll);
        var processes = Process.GetProcessesByName("FlightSimulator2024");
        try
        {
            if (processes.Length != 1) throw new IOException("Launch MSFS 2024 and load a flight, then reconnect Tracking.");
        }
        finally { foreach (var process in processes) process.Dispose(); }

        IntPtr library = IntPtr.Zero, connection = IntPtr.Zero;
        try
        {
            library = NativeLibrary.Load(dll);
            T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            void Check(int hr, string operation) { if (hr < 0) throw new IOException($"SimConnect {operation} failed: 0x{hr:X8}."); }
            Check(Bind<Open>("SimConnect_Open")(out connection, "Frontier Pilot aircraft tracking read only", IntPtr.Zero, 0, IntPtr.Zero, uint.MaxValue), "open");
            var add = Bind<Add>("SimConnect_AddToDataDefinition");
            foreach (var field in new[]
            {
                ("PLANE LATITUDE", "degrees"), ("PLANE LONGITUDE", "degrees"), ("PLANE ALTITUDE", "feet"),
                ("PLANE HEADING DEGREES TRUE", "degrees"), ("SIM ON GROUND", "bool"), ("GROUND VELOCITY", "knots")
            }) Check(add(connection, NumericDefinition, field.Item1, field.Item2, Float64, 0, uint.MaxValue), "define " + field.Item1);
            Check(add(connection, IdentityDefinition, "TITLE", null, String128, 0, uint.MaxValue), "define TITLE");
            Check(add(connection, IdentityDefinition, "ATC MODEL", null, String128, 0, uint.MaxValue), "define ATC MODEL");

            var request = Bind<Request>("SimConnect_RequestDataOnSimObject");
            var dispatch = Bind<Dispatch>("SimConnect_GetNextDispatch");
            var timer = Stopwatch.StartNew();
            var nextNumeric = TimeSpan.Zero;
            var nextIdentity = TimeSpan.Zero;
            var lastSample = TimeSpan.Zero;
            uint requestSequence = 1000, numericRequest = 0, identityRequest = 0;
            string title = "Unknown aircraft", type = "";
            bool handshake = false, requestedNumeric = false;

            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                var now = timer.Elapsed;
                if (numericRequest == 0 && now >= nextNumeric)
                {
                    numericRequest = ++requestSequence;
                    Check(request(connection, numericRequest, NumericDefinition, UserAircraft, Once, 0, 0, 0, 0), "request aircraft telemetry");
                    requestedNumeric = true;
                    nextNumeric = now + TimeSpan.FromMilliseconds(250);
                }
                if (identityRequest == 0 && now >= nextIdentity)
                {
                    identityRequest = ++requestSequence;
                    Check(request(connection, identityRequest, IdentityDefinition, UserAircraft, Once, 0, 0, 0, 0), "request aircraft identity");
                    nextIdentity = now + TimeSpan.FromSeconds(10);
                }

                while (dispatch(connection, out var data, out uint size) >= 0)
                {
                    if (size < 12) throw new IOException("SimConnect returned a malformed tracking reply.");
                    int id = Marshal.ReadInt32(data, 8);
                    if (id == 1) throw new IOException($"SimConnect reported tracking exception {ReadInt(data, size, 12, -1)}.");
                    if (id == 3) throw new IOException("MSFS closed the SimConnect tracking connection.");
                    if (id == 2) { handshake = true; continue; }
                    if (id != 8 || size < 40) continue;
                    uint reply = unchecked((uint)Marshal.ReadInt32(data, 12));
                    int count = Marshal.ReadInt32(data, 36);
                    if (reply == identityRequest)
                    {
                        if (count != 2 || size < 296) throw new IOException("SimConnect returned an invalid aircraft identity payload.");
                        title = ReadFixedAnsi(data + 40, 128);
                        type = ReadFixedAnsi(data + 168, 128);
                        identityRequest = 0;
                    }
                    else if (reply == numericRequest)
                    {
                        if (count != 6 || size < 88) throw new IOException("SimConnect returned an invalid aircraft telemetry payload.");
                        var values = new double[6];
                        Marshal.Copy(data + 40, values, 0, values.Length);
                        numericRequest = 0;
                        lastSample = timer.Elapsed;
                        updates.Report(new AircraftTelemetry(DateTimeOffset.UtcNow, title, type, values[0], values[1], values[2], values[3], values[5], values[4] != 0).EnsureValid());
                    }
                }

                now = timer.Elapsed;
                if (!handshake && now > TimeSpan.FromSeconds(10)) throw new IOException("MSFS did not complete the SimConnect tracking handshake.");
                if (requestedNumeric && lastSample == TimeSpan.Zero && now > TimeSpan.FromSeconds(5)) throw new IOException("MSFS did not return aircraft telemetry. Ensure a flight is fully loaded.");
                if (lastSample != TimeSpan.Zero && now - lastSample > TimeSpan.FromSeconds(5)) throw new IOException("Aircraft telemetry stopped; the simulator connection was lost.");
                Thread.Sleep(10);
            }
        }
        finally
        {
            if (connection != IntPtr.Zero && library != IntPtr.Zero)
                Marshal.GetDelegateForFunctionPointer<Close>(NativeLibrary.GetExport(library, "SimConnect_Close"))(connection);
            if (library != IntPtr.Zero) NativeLibrary.Free(library);
        }
    }

    private static int ReadInt(IntPtr data, uint size, int offset, int fallback) => size >= offset + 4 ? Marshal.ReadInt32(data, offset) : fallback;
    private static string ReadFixedAnsi(IntPtr address, int length)
    {
        var bytes = new byte[length];
        Marshal.Copy(address, bytes, 0, length);
        int end = Array.IndexOf(bytes, (byte)0);
        return System.Text.Encoding.Default.GetString(bytes, 0, end < 0 ? length : end).Trim();
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Open(out IntPtr h, string name, IntPtr window, uint message, IntPtr evt, uint config);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Add(IntPtr h, uint definition, string name, string? units, uint type, float epsilon, uint datum);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Request(IntPtr h, uint request, uint definition, uint obj, uint period, uint flags, uint origin, uint interval, uint limit);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Dispatch(IntPtr h, out IntPtr data, out uint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Close(IntPtr h);
}
