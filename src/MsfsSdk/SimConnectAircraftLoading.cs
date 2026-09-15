using System.Diagnostics;
using System.Runtime.InteropServices;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.MsfsSdk;

/// <summary>SDK 1.2.4 SimConnect payload/fuel adapter. Structural station limits and arms are not exposed by SimVars.</summary>
public sealed class SimConnectAircraftLoading : IAircraftLoadingSimulator
{
    private const uint UserAircraft = 0, Once = 1, Float64 = 4, String128 = 8;
    private static readonly (string Id, string Label, string Token)[] LegacyTanks =
    [
        ("center", "Center", "CENTER"), ("center-2", "Center 2", "CENTER2"), ("center-3", "Center 3", "CENTER3"),
        ("left-main", "Left Main", "LEFT MAIN"), ("right-main", "Right Main", "RIGHT MAIN"),
        ("left-aux", "Left Auxiliary", "LEFT AUX"), ("right-aux", "Right Auxiliary", "RIGHT AUX"),
        ("left-tip", "Left Tip", "LEFT TIP"), ("right-tip", "Right Tip", "RIGHT TIP"),
        ("external-1", "External 1", "EXTERNAL1"), ("external-2", "External 2", "EXTERNAL2")
    ];

    public Task<AircraftLoadingSnapshot> ReadAsync(string sdkRoot, CancellationToken cancellationToken = default) =>
        Task.Run(() => WithSession(sdkRoot, cancellationToken, session => session.Read()), cancellationToken);
    public Task<AircraftLoadingSnapshot> WritePayloadAsync(string sdkRoot, IReadOnlyDictionary<int, decimal> targetKilograms, CancellationToken cancellationToken = default) =>
        Task.Run(() => WithSession(sdkRoot, cancellationToken, session => { session.WritePayload(targetKilograms); Thread.Sleep(250); return session.Read(); }), cancellationToken);
    public Task<AircraftLoadingSnapshot> WriteFuelAsync(string sdkRoot, IReadOnlyDictionary<string, decimal> targetGallons, CancellationToken cancellationToken = default) =>
        Task.Run(() => WithSession(sdkRoot, cancellationToken, session => { session.WriteFuel(targetGallons); Thread.Sleep(250); return session.Read(); }), cancellationToken);

    private static T WithSession<T>(string sdkRoot, CancellationToken token, Func<Session, T> action)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("Aircraft loading requires Windows x64.");
        var sdk = SdkInspector.Inspect(sdkRoot); var dll = Path.Combine(sdk.Root, "SimConnect SDK", "lib", "SimConnect.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("The configured SDK does not contain SimConnect.dll.", dll);
        var processes = Process.GetProcessesByName("FlightSimulator2024");
        try { if (processes.Length != 1) throw new IOException("Launch MSFS 2024 and load a flight before refreshing Aircraft Loading."); }
        finally { foreach (var process in processes) process.Dispose(); }
        using var session = new Session(dll, $"SDK {sdk.Version}; SimConnect payload/fuel variables"); token.ThrowIfCancellationRequested(); return action(session);
    }

    private sealed class Session : IDisposable
    {
        private readonly IntPtr library; private IntPtr connection; private readonly string details; private uint requestId = 500;
        private readonly Add add; private readonly Request request; private readonly Dispatch dispatch; private readonly SetData set;
        public Session(string dll, string details)
        {
            this.details = details; library = NativeLibrary.Load(dll);
            T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            var open = Bind<Open>("SimConnect_Open"); if (open(out connection, "Frontier Pilot aircraft loading", IntPtr.Zero, 0, IntPtr.Zero, uint.MaxValue) < 0) throw new IOException("MSFS did not accept the aircraft-loading SimConnect connection.");
            add = Bind<Add>("SimConnect_AddToDataDefinition"); request = Bind<Request>("SimConnect_RequestDataOnSimObject"); dispatch = Bind<Dispatch>("SimConnect_GetNextDispatch"); set = Bind<SetData>("SimConnect_SetDataOnSimObject");
        }
        public AircraftLoadingSnapshot Read()
        {
            const uint baseDef = 100;
            Add(baseDef, "TITLE", null, String128); Add(baseDef, "ATC MODEL", null, String128);
            foreach (var field in new[] { ("PLANE LATITUDE", "degrees"), ("PLANE LONGITUDE", "degrees"), ("PLANE ALTITUDE", "feet"), ("PLANE HEADING DEGREES TRUE", "degrees"),
                ("GROUND VELOCITY", "knots"), ("SIM ON GROUND", "bool"), ("PAYLOAD STATION COUNT", "number"), ("EMPTY WEIGHT", "pounds"), ("MAX GROSS WEIGHT", "pounds"),
                ("TOTAL WEIGHT", "pounds"), ("CG PERCENT", "percent over 100"), ("CG FWD LIMIT", "percent over 100"), ("CG AFT LIMIT", "percent over 100"),
                ("FUEL TOTAL QUANTITY WEIGHT", "pounds"), ("FUEL WEIGHT PER GALLON", "pounds"), ("NUMBER OF ENGINES", "number") }) Add(baseDef, field.Item1, field.Item2, Float64);
            var baseData = Request(baseDef, 256 + 16 * 8); var title = Fixed(baseData, 0); var type = Fixed(baseData, 128); var numbers = Doubles(baseData, 256, 16);
            var observed = DateTimeOffset.UtcNow; var identity = new FlightAircraftIdentity(title, type).EnsureValid();
            var position = new FlightPosition(observed, numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5] != 0).EnsureValid();
            int stationCount = Math.Clamp((int)Math.Round(numbers[6]), 0, 15); int engineCount = Math.Clamp((int)Math.Round(numbers[15]), 0, 4);

            const uint stationDef = 101; for (int i = 1; i <= stationCount; i++) { Add(stationDef, $"PAYLOAD STATION NAME:{i}", null, String128); Add(stationDef, $"PAYLOAD STATION WEIGHT:{i}", "pounds", Float64); }
            var stations = new List<PayloadStationReading>();
            if (stationCount > 0)
            {
                var bytes = Request(stationDef, stationCount * 136); int offset = 0;
                for (int i = 1; i <= stationCount; i++) { var name = Fixed(bytes, offset); offset += 128; var pounds = BitConverter.ToDouble(bytes, offset); offset += 8;
                    stations.Add(new(i, string.IsNullOrWhiteSpace(name) ? $"Station {i}" : name, LoadingUnits.PoundsToKilograms((decimal)pounds), null, null, true)); }
            }

            const uint systemsDef = 102;
            foreach (var tank in LegacyTanks) { Add(systemsDef, $"FUEL TANK {tank.Token} CAPACITY", "gallons", Float64); Add(systemsDef, $"FUEL TANK {tank.Token} QUANTITY", "gallons", Float64); }
            for (int i = 1; i <= Math.Max(1, engineCount); i++) Add(systemsDef, $"GENERAL ENG COMBUSTION:{i}", "bool", Float64);
            var systems = Request(systemsDef, (LegacyTanks.Length * 2 + Math.Max(1, engineCount)) * 8); var values = Doubles(systems, 0, systems.Length / 8);
            var tanks = LegacyTanks.Select((tank, i) => new FuelTankReading(tank.Id, tank.Label, (decimal)values[i * 2 + 1], (decimal)values[i * 2], true))
                .Where(x => x.CapacityGallons > 0).Select(x => x.EnsureValid()).ToArray();
            int engineOffset = LegacyTanks.Length * 2; var engines = Enumerable.Range(1, engineCount).Select(i => new AircraftEngineReading(i, values[engineOffset + i - 1] != 0)).ToArray();
            decimal? density = numbers[14] > 0 ? LoadingUnits.PoundsToKilograms((decimal)numbers[14]) : null;
            var weights = new AircraftWeightReading(Kg(numbers[7]), Kg(numbers[8]), Kg(numbers[9]), null,
                FinitePercent(numbers[10]), FinitePercent(numbers[11]), FinitePercent(numbers[12])).EnsureValid();
            return new AircraftLoadingSnapshot(observed, identity, position, stations, tanks, engines, density, weights,
                details + "; legacy named-tank compatibility view; station limits/arms unavailable").EnsureValid();
        }
        public void WritePayload(IReadOnlyDictionary<int, decimal> targets)
        {
            if (targets is null || targets.Count == 0) throw new InvalidOperationException("No payload targets were supplied."); const uint definition = 200;
            foreach (var target in targets.OrderBy(x => x.Key)) { if (target.Key is < 1 or > 15 || target.Value < 0) throw new InvalidDataException("Payload target is invalid."); Add(definition, $"PAYLOAD STATION WEIGHT:{target.Key}", "pounds", Float64); }
            Write(definition, targets.OrderBy(x => x.Key).Select(x => (double)LoadingUnits.KilogramsToPounds(x.Value)).ToArray());
        }
        public void WriteFuel(IReadOnlyDictionary<string, decimal> targets)
        {
            if (targets is null || targets.Count == 0) throw new InvalidOperationException("No fuel targets were supplied."); const uint definition = 201; var ordered = targets.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
            foreach (var target in ordered) { var tank = LegacyTanks.SingleOrDefault(x => x.Id == target.Key); if (tank == default || target.Value < 0) throw new InvalidDataException("Fuel target is invalid."); Add(definition, $"FUEL TANK {tank.Token} QUANTITY", "gallons", Float64); }
            Write(definition, ordered.Select(x => (double)x.Value).ToArray());
        }
        private void Add(uint definition, string name, string? unit, uint type) { var hr = add(connection, definition, name, unit, type, 0, uint.MaxValue); if (hr < 0) throw new IOException($"SimConnect could not define {name}: 0x{hr:X8}."); }
        private byte[] Request(uint definition, int expectedBytes)
        {
            uint id = ++requestId; var hr = request(connection, id, definition, UserAircraft, Once, 0, 0, 0, 0); if (hr < 0) throw new IOException($"SimConnect request failed: 0x{hr:X8}.");
            var timeout = Stopwatch.StartNew(); while (timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                while (dispatch(connection, out var data, out uint size) >= 0)
                {
                    if (size < 12) throw new IOException("SimConnect returned malformed aircraft-loading data."); int receive = Marshal.ReadInt32(data, 8);
                    if (receive == 1) throw new IOException($"SimConnect reported aircraft-loading exception {(size >= 16 ? Marshal.ReadInt32(data, 12) : -1)}.");
                    if (receive == 3) throw new IOException("MSFS closed the aircraft-loading connection.");
                    if (receive == 8 && size >= 40 && unchecked((uint)Marshal.ReadInt32(data, 12)) == id)
                    { int count = Marshal.ReadInt32(data, 36); if (count <= 0 || size < 40 + expectedBytes) throw new IOException("SimConnect returned incomplete aircraft-loading data."); var bytes = new byte[expectedBytes]; Marshal.Copy(data + 40, bytes, 0, bytes.Length); return bytes; }
                }
                Thread.Sleep(10);
            }
            throw new IOException("MSFS did not return aircraft-loading data within five seconds.");
        }
        private void Write(uint definition, double[] values)
        {
            var pointer = Marshal.AllocHGlobal(values.Length * sizeof(double)); try { Marshal.Copy(values, 0, pointer, values.Length); var hr = set(connection, definition, UserAircraft, 0, 0, (uint)(values.Length * sizeof(double)), pointer); if (hr < 0) throw new IOException($"SimConnect aircraft write failed: 0x{hr:X8}."); }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        private static string Fixed(byte[] bytes, int offset) { int end = Array.IndexOf(bytes, (byte)0, offset, 128); if (end < 0) end = offset + 128; return System.Text.Encoding.Default.GetString(bytes, offset, end - offset).Trim(); }
        private static double[] Doubles(byte[] bytes, int offset, int count) { var values = new double[count]; for (int i = 0; i < count; i++) values[i] = BitConverter.ToDouble(bytes, offset + i * 8); return values; }
        private static decimal? Kg(double pounds) => double.IsFinite(pounds) && pounds > 0 ? LoadingUnits.PoundsToKilograms((decimal)pounds) : null;
        private static double? FinitePercent(double fraction) => double.IsFinite(fraction) && fraction >= 0 ? fraction * 100 : null;
        public void Dispose() { if (connection != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<Close>(NativeLibrary.GetExport(library, "SimConnect_Close"))(connection); NativeLibrary.Free(library); connection = IntPtr.Zero; }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Open(out IntPtr h, string name, IntPtr window, uint message, IntPtr evt, uint config);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Close(IntPtr h);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Add(IntPtr h, uint definition, string name, string? units, uint type, float epsilon, uint datum);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Request(IntPtr h, uint request, uint definition, uint obj, uint period, uint flags, uint origin, uint interval, uint limit);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Dispatch(IntPtr h, out IntPtr data, out uint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetData(IntPtr h, uint definition, uint obj, uint flags, uint arrayCount, uint unitSize, IntPtr data);
}
