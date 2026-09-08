using System.Diagnostics;
using System.Runtime.InteropServices;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.MsfsSdk;

/// <summary>Read-only tracking through the explicit aircraft, avatar and current-user proxies.</summary>
public sealed class LiveAircraftTelemetry : ITrackingTelemetrySource
{
    private const uint NumericDefinition = 20, IdentityDefinition = 21, UserAircraft = 0;
    private const uint UserAvatar = uint.MaxValue - 127, UserCurrent = uint.MaxValue - 126;
    private const uint Once = 1, Float64 = 4, String128 = 8;

    public Task RunAsync(string sdkRoot, IProgress<TrackingWorldState> updates, CancellationToken cancellationToken) =>
        Task.Run(() => Run(sdkRoot, updates, cancellationToken), cancellationToken);

    private static void Run(string sdkRoot, IProgress<TrackingWorldState> updates, CancellationToken cancel)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("Live tracking requires Windows x64.");
        ArgumentNullException.ThrowIfNull(updates);
        var sdk = SdkInspector.Inspect(sdkRoot);
        var dll = Path.Combine(sdk.Root, "SimConnect SDK", "lib", "SimConnect.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("The configured SDK does not contain SimConnect.dll.", dll);
        var processes = Process.GetProcessesByName("FlightSimulator2024");
        string simulatorVersion;
        try
        {
            if (processes.Length != 1) throw new IOException("Launch MSFS 2024 and load a flight, then reconnect Tracking.");
            simulatorVersion = processes[0].MainModule?.FileVersionInfo.FileVersion ?? "unknown";
        }
        finally { foreach (var process in processes) process.Dispose(); }
        var details = $"SDK {sdk.Version}; simulator {simulatorVersion}; USER_AIRCRAFT/USER_AVATAR/USER_CURRENT proxies";

        IntPtr library = IntPtr.Zero, connection = IntPtr.Zero;
        try
        {
            library = NativeLibrary.Load(dll);
            T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            void Check(int hr, string operation) { if (hr < 0) throw new IOException($"SimConnect {operation} failed: 0x{hr:X8}."); }
            Check(Bind<Open>("SimConnect_Open")(out connection, "Frontier Pilot aircraft and avatar tracking read only", IntPtr.Zero, 0, IntPtr.Zero, uint.MaxValue), "open");
            var add = Bind<Add>("SimConnect_AddToDataDefinition");
            foreach (var field in new[] { ("PLANE LATITUDE", "degrees"), ("PLANE LONGITUDE", "degrees"), ("PLANE ALTITUDE", "feet"),
                ("PLANE HEADING DEGREES TRUE", "degrees"), ("SIM ON GROUND", "bool"), ("GROUND VELOCITY", "knots") })
                Check(add(connection, NumericDefinition, field.Item1, field.Item2, Float64, 0, uint.MaxValue), "define " + field.Item1);
            Check(add(connection, IdentityDefinition, "TITLE", null, String128, 0, uint.MaxValue), "define TITLE");
            Check(add(connection, IdentityDefinition, "ATC MODEL", null, String128, 0, uint.MaxValue), "define ATC MODEL");

            var request = Bind<Request>("SimConnect_RequestDataOnSimObject");
            var dispatch = Bind<Dispatch>("SimConnect_GetNextDispatch");
            var timer = Stopwatch.StartNew();
            var nextNumeric = TimeSpan.Zero; var nextIdentity = TimeSpan.Zero; var lastSample = TimeSpan.Zero;
            uint sequence = 1000, identitySequence = 100, batchBase = 0, identityRequest = 0;
            string title = "Unknown aircraft", type = "";
            bool handshake = false, requestedNumeric = false;
            var batch = new Dictionary<uint, ObjectReading>();

            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                var now = timer.Elapsed;
                if (batchBase == 0 && now >= nextNumeric)
                {
                    batch.Clear(); batchBase = sequence += 3;
                    Check(request(connection, batchBase, NumericDefinition, UserAircraft, Once, 0, 0, 0, 0), "request aircraft telemetry");
                    Check(request(connection, batchBase + 1, NumericDefinition, UserAvatar, Once, 0, 0, 0, 0), "request avatar telemetry");
                    Check(request(connection, batchBase + 2, NumericDefinition, UserCurrent, Once, 0, 0, 0, 0), "request current-user telemetry");
                    requestedNumeric = true; nextNumeric = now + TimeSpan.FromMilliseconds(250);
                }
                if (identityRequest == 0 && now >= nextIdentity)
                {
                    identityRequest = ++identitySequence;
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
                        title = ReadFixedAnsi(data + 40, 128); type = ReadFixedAnsi(data + 168, 128); identityRequest = 0;
                    }
                    else if (batchBase != 0 && reply >= batchBase && reply <= batchBase + 2)
                    {
                        if (count != 6 || size < 88) throw new IOException("SimConnect returned an invalid world-position payload.");
                        var values = new double[6]; Marshal.Copy(data + 40, values, 0, values.Length);
                        if (!batch.TryAdd(reply - batchBase, new(unchecked((uint)Marshal.ReadInt32(data, 16)), values)))
                            throw new IOException("SimConnect returned a duplicate world-position payload.");
                        if (batch.Count == 3)
                        {
                            updates.Report(Classify(title, type, batch[0], batch[1], batch[2], details));
                            batchBase = 0; lastSample = timer.Elapsed;
                        }
                    }
                }

                now = timer.Elapsed;
                if (!handshake && now > TimeSpan.FromSeconds(10)) throw new IOException("MSFS did not complete the SimConnect tracking handshake.");
                if (requestedNumeric && lastSample == TimeSpan.Zero && now > TimeSpan.FromSeconds(5)) throw new IOException("MSFS did not return world telemetry. Ensure a flight is fully loaded.");
                if (lastSample != TimeSpan.Zero && now - lastSample > TimeSpan.FromSeconds(5)) throw new IOException("World telemetry stopped; the simulator connection was lost.");
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

    internal static TrackingWorldState Classify(string title, string type, ObjectReading aircraft, ObjectReading avatar, ObjectReading current, string details)
    {
        var now = DateTimeOffset.UtcNow; var a = aircraft.Values;
        var plane = new AircraftTelemetry(now, title, type, a[0], a[1], a[2], a[3], a[5], a[4] != 0).EnsureValid();
        if (current.ObjectId == aircraft.ObjectId)
        {
            if (ReadingsAgree(a, current.Values))
                return new TrackingWorldState(plane, aircraft.ObjectId, current.ObjectId, avatar.ObjectId == 0 ? null : avatar.ObjectId,
                    PlayerControlMode.InAircraft, null, "Current controlled object is the aircraft.", details).EnsureValid();
            return new TrackingWorldState(plane, aircraft.ObjectId, current.ObjectId, avatar.ObjectId == 0 ? null : avatar.ObjectId,
                PlayerControlMode.Unknown, null, "Aircraft identity matched, but current-object readings disagreed; player state refused.", details).EnsureValid();
        }

        if (avatar.ObjectId != 0 && avatar.ObjectId != aircraft.ObjectId && current.ObjectId == avatar.ObjectId)
        {
            var v = avatar.Values; var c = current.Values;
            try
            {
                var player = new PlayerTelemetry(now, avatar.ObjectId, v[0], v[1], v[2], v[3], v[4] != 0).EnsureValid();
                if (ReadingsAgree(v, c) && player.IsOnGround && c[4] != 0)
                    return new TrackingWorldState(plane, aircraft.ObjectId, current.ObjectId, avatar.ObjectId, PlayerControlMode.OnFoot, player,
                        "Explicit avatar and current-controlled-object readings agree.", details).EnsureValid();
            }
            catch (InvalidDataException) { }
            return new TrackingWorldState(plane, aircraft.ObjectId, current.ObjectId, avatar.ObjectId, PlayerControlMode.Unknown, null,
                "Avatar identity matched, but its readings were inconsistent or not on ground; player position refused.", details).EnsureValid();
        }

        return new TrackingWorldState(plane, aircraft.ObjectId, current.ObjectId, avatar.ObjectId == 0 ? null : avatar.ObjectId,
            PlayerControlMode.Unknown, null, "Current object matched neither a distinct avatar nor the aircraft; player position refused.", details).EnsureValid();
    }

    private static bool ReadingsAgree(double[] first, double[] second) => first.Length >= 5 && second.Length >= 5 &&
        first.Take(5).Concat(second.Take(5)).All(double.IsFinite) &&
        WorldTracking.DistanceMeters(first[0], first[1], second[0], second[1]) <= 2 &&
        Math.Abs(first[2] - second[2]) <= 10 && WorldTracking.HeadingDifference(first[3], second[3]) <= 10;

    internal sealed record ObjectReading(uint ObjectId, double[] Values);
    private static int ReadInt(IntPtr data, uint size, int offset, int fallback) => size >= offset + 4 ? Marshal.ReadInt32(data, offset) : fallback;
    private static string ReadFixedAnsi(IntPtr address, int length) { var bytes = new byte[length]; Marshal.Copy(address, bytes, 0, length); int end = Array.IndexOf(bytes, (byte)0); return System.Text.Encoding.Default.GetString(bytes, 0, end < 0 ? length : end).Trim(); }
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Open(out IntPtr h, string name, IntPtr window, uint message, IntPtr evt, uint config);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Add(IntPtr h, uint definition, string name, string? units, uint type, float epsilon, uint datum);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Request(IntPtr h, uint request, uint definition, uint obj, uint period, uint flags, uint origin, uint interval, uint limit);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Dispatch(IntPtr h, out IntPtr data, out uint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Close(IntPtr h);
}
