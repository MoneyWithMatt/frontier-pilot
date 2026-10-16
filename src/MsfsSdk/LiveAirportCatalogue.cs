using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FrontierAirportBuilder.MsfsSdk;

public sealed record SimulatorAirport(string Identifier, double Latitude, double Longitude);
public sealed record SimulatorAirportScan(DateTimeOffset ObservedUtc, string Source, SimulatorAirport[] Airports);

/// <summary>Read-only worldwide airport list, never a scenery or runway inspection.</summary>
public static class LiveAirportCatalogue
{
    public static Task<SimulatorAirportScan> ReadAsync(string sdkRoot, CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(sdkRoot, cancellationToken), cancellationToken);

    private static SimulatorAirportScan Read(string sdkRoot, CancellationToken cancel)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("Airport lookup requires Windows x64.");
        var sdk = SdkInspector.Inspect(sdkRoot);
        var processes = Process.GetProcessesByName("FlightSimulator2024");
        string version;
        try
        {
            if (processes.Length != 1) throw new IOException("Load a flight anywhere in MSFS 2024, then check airports again.");
            version = processes[0].MainModule?.FileVersionInfo.FileVersion ?? "unknown";
        }
        finally { foreach (var process in processes) process.Dispose(); }
        IntPtr library = IntPtr.Zero, connection = IntPtr.Zero;
        try
        {
            library = NativeLibrary.Load(Path.Combine(sdk.Root, "SimConnect SDK", "lib", "SimConnect.dll"));
            T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            void Check(int hr) { if (hr < 0) throw new IOException($"Airport lookup failed: 0x{hr:X8}."); }
            Check(Bind<Open>("SimConnect_Open")(out connection, "Frontier Pilot airport lookup", IntPtr.Zero, 0, IntPtr.Zero, uint.MaxValue));
            // Non-EX1 is intentional: EX1 only returns the reality bubble, not worldwide airports.
            Check(Bind<Request>("SimConnect_RequestFacilitiesList")(connection, 0, 700));
            var dispatch = Bind<Dispatch>("SimConnect_GetNextDispatch");
            var packets = new AirportListPackets();
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(45))
            {
                cancel.ThrowIfCancellationRequested();
                if (dispatch(connection, out var data, out var size) >= 0)
                {
                    if (size < 12 || size > 16_000_000) throw new IOException("Malformed airport response.");
                    var id = Marshal.ReadInt32(data, 8);
                    if (id is 1 or 3) throw new IOException("MSFS interrupted airport lookup. No missing-airport results were recorded.");
                    if (id != 18) continue;
                    var bytes = new byte[size]; Marshal.Copy(data, bytes, 0, bytes.Length);
                    if (packets.Add(bytes))
                    {
                        if(packets.Airports.Count==0)throw new IOException("MSFS returned an empty worldwide list; previous results preserved.");
                        return new(DateTimeOffset.UtcNow, $"SDK {sdk.Version}; simulator {version}; SimConnect worldwide airport list", packets.Airports.ToArray());
                    }
                }
                else cancel.WaitHandle.WaitOne(20);
            }
            throw new IOException("Airport lookup timed out before the complete list arrived. Previous results preserved.");
        }
        finally
        {
            if (connection != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<Close>(NativeLibrary.GetExport(library, "SimConnect_Close"))(connection);
            if (library != IntPtr.Zero) NativeLibrary.Free(library);
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Open(out IntPtr handle, string name, IntPtr window, uint message, IntPtr evt, uint config);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Request(IntPtr handle, uint type, uint request);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Dispatch(IntPtr handle, out IntPtr data, out uint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Close(IntPtr handle);
}

internal sealed class AirportListPackets
{
    private uint? total;
    private readonly HashSet<uint> received = [];
    public List<SimulatorAirport> Airports { get; } = [];
    public bool Add(byte[] bytes)
    {
        // MSFS 2024 SDK packed layout: 28-byte header; Ident[9], Region[3], three doubles.
        if (bytes.Length < 28) throw new IOException("Truncated airport list.");
        uint U(int offset) => BitConverter.ToUInt32(bytes, offset);
        if (U(12) != 700) return false;
        var count = U(16); var entry = U(20); var batches = U(24);
        if (batches is 0 or > 100_000 || entry >= batches || count > 100_000 || Airports.Count+count>200_000 || bytes.Length != 28L + count * 36L ||
            total is {} expected && expected != batches || !received.Add(entry)) throw new IOException("Invalid airport list packet sequence.");
        total = batches;
        for (var i = 0; i < count; i++)
        {
            var offset = 28 + i * 36;
            var identifier = Encoding.ASCII.GetString(bytes, offset, 9).Split('\0')[0].Trim().ToUpperInvariant();
            var lat = BitConverter.ToDouble(bytes, offset + 12); var lon = BitConverter.ToDouble(bytes, offset + 20);
            if (identifier.Length == 0 || !double.IsFinite(lat) || !double.IsFinite(lon) || lat is < -90 or > 90 || lon is < -180 or > 180)
                throw new IOException("Invalid airport coordinates in simulator reply.");
            Airports.Add(new(identifier, lat, lon));
        }
        return received.Count == total;
    }
}
