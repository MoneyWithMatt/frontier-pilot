using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FrontierAirportBuilder.Core;

namespace FrontierAirportBuilder.MsfsSdk;

/// <summary>Optional diagnostic connection. Binds only read/request functions; never sends events or writes SimVars.</summary>
public sealed class LiveAircraftCondition(string sdkRoot, IAircraftConditionProfile? profile = null) : IAircraftConditionSource
{
    public ConditionConnectionInfo ConnectionInfo { get; private set; } = new("unknown", "unknown", "unknown");

    public async IAsyncEnumerable<ConditionFrame> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Condition recording requires Windows x64.");
        var sdk = SdkInspector.Inspect(sdkRoot);
        var dll = Path.Combine(sdk.Root, "SimConnect SDK", "lib", "SimConnect.dll");
        var processes = Process.GetProcessesByName("FlightSimulator2024");
        try
        {
            if (processes.Length != 1) throw new IOException("Load an MSFS 2024 flight before recording condition.");
            ConnectionInfo = new(sdk.Version, processes[0].MainModule?.FileVersionInfo.FileVersion ?? "unknown",
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))));
        }
        finally { foreach (var process in processes) process.Dispose(); }
        using var session = new ConditionNativeSession(dll);
        var selected = profile ?? new StandardConditionProfile();
        ConditionAircraftIdentity? initial = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var start = DateTimeOffset.UtcNow;
            var before = await Task.Run(() => session.Identity(cancellationToken), cancellationToken);
            initial ??= before;
            if (initial != before) throw new IOException("Aircraft identity changed; start a new condition recording.");
            var fields = selected.Fields(before);
            var readings = await Task.Run(() => session.ReadFields(fields, before.ObjectId, cancellationToken), cancellationToken);
            var after = await Task.Run(() => session.Identity(cancellationToken), cancellationToken);
            if (before != after) throw new IOException("Aircraft changed during condition sampling; frame refused.");
            yield return new(start, DateTimeOffset.UtcNow, before, selected.Id, readings) { Runtime = session.Runtime.State };
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}

internal sealed record ConditionDatum(string Name, string? Unit, bool IsString = false);
internal sealed record ConditionRaw(uint ObjectId, DateTimeOffset Utc, double? Number, string? Text, string? Error);

internal sealed class ConditionReplies(int count)
{
    private readonly ConditionRaw?[] values = new ConditionRaw?[count];
    private readonly Dictionary<uint, (int Index, uint Definition, bool Text)> pending = [];
    private readonly Dictionary<uint, uint> sent = [];
    internal int Pending => pending.Count;
    internal void Add(uint request, int index, uint definition, bool text) => pending.Add(request, (index, definition, text));
    internal void Sent(uint packet, uint request) => sent[packet] = request;
    internal void Missing(int index, string reason) => values[index] = new(0, DateTimeOffset.UtcNow, null, null, reason);
    internal void Reject(uint request, string reason)
    {
        if (pending.Remove(request, out var item)) Missing(item.Index, reason);
    }
    internal void Receive(byte[] bytes)
    {
        if (bytes.Length < 12) throw new IOException("Malformed condition packet.");
        var kind = BitConverter.ToUInt32(bytes, 8);
        if (kind == 3) throw new IOException("MSFS closed the condition connection.");
        if (kind == 1)
        {
            if (bytes.Length < 24) throw new IOException("Malformed condition exception packet.");
            var code = BitConverter.ToUInt32(bytes, 12); var packet = BitConverter.ToUInt32(bytes, 16);
            if (sent.TryGetValue(packet, out var request)) Reject(request, $"SimConnect exception {code}; send ID {packet}. Availability unverified.");
            return;
        }
        if (kind != 8) return;
        if (bytes.Length < 40) throw new IOException("Malformed condition data packet.");
        if (pending.Remove(BitConverter.ToUInt32(bytes, 12), out var current))
            values[current.Index] = ConditionPacket.Decode(bytes, current.Definition, current.Text);
    }
    internal ConditionRaw[] Finish() => values.Select(x => x ?? new ConditionRaw(0, DateTimeOffset.UtcNow, null, null,
        "No reply within two seconds; unavailable, not zero.")).ToArray();
}

/// <summary>Pure bounded packet decoding shared by the native reader and malformed-packet tests.</summary>
internal static class ConditionPacket
{
    internal static ConditionRaw Decode(byte[] data, uint expectedDefinition, bool isString)
    {
        if (data.Length < 40 || BitConverter.ToUInt32(data, 8) != 8 ||
            BitConverter.ToUInt32(data, 20) != expectedDefinition || BitConverter.ToUInt32(data, 36) != 1 ||
            BitConverter.ToUInt32(data, 16) == 0 || data.Length < 40 + (isString ? 128 : 8))
            throw new IOException("Malformed aircraft condition payload.");
        var utc = DateTimeOffset.UtcNow;
        if (!isString) return new(BitConverter.ToUInt32(data, 16), utc, BitConverter.ToDouble(data, 40), null, null);
        var end = Array.IndexOf(data, (byte)0, 40, 128);
        if (end < 0) throw new IOException("Unterminated aircraft condition identity.");
        return new(BitConverter.ToUInt32(data, 16), utc, null, System.Text.Encoding.ASCII.GetString(data, 40, end - 40).Trim(), null);
    }
}

internal sealed class ConditionNativeSession : IDisposable
{
    internal ConditionRuntimeEvents Runtime { get; } = new();
    private IntPtr library, connection;
    private readonly Add add;
    private readonly Request request;
    private readonly Dispatch dispatch;
    private readonly LastPacket lastPacket;
    private readonly Dictionary<ConditionDatum, uint> definitions = [];
    private readonly Dictionary<ConditionDatum, string> refused = [];
    private uint sequence = 100;

    internal ConditionNativeSession(string dll)
    {
        try
        {
            library = NativeLibrary.Load(dll);
            T Bind<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            Check(Bind<Open>("SimConnect_Open")(out connection, "Frontier Pilot condition READ ONLY", IntPtr.Zero, 0, IntPtr.Zero, uint.MaxValue));
            add = Bind<Add>("SimConnect_AddToDataDefinition");
            request = Bind<Request>("SimConnect_RequestDataOnSimObject");
            dispatch = Bind<Dispatch>("SimConnect_GetNextDispatch");
            lastPacket = Bind<LastPacket>("SimConnect_GetLastSentPacketID");
            var subscribe = Bind<Subscribe>("SimConnect_SubscribeToSystemEvent");
            // Failed/unsupported event subscriptions leave state unknown and block exposure qualification.
            subscribe(connection, 1, "Pause_EX1");
            subscribe(connection, 2, "Sim");
            subscribe(connection, 3, "PositionChanged");
        }
        catch { Dispose(); throw; }
    }

    internal ConditionAircraftIdentity Identity(CancellationToken token)
    {
        var specs = new[] { new ConditionDatum("TITLE", null, true), new ConditionDatum("ATC MODEL", null, true), new ConditionDatum("NUMBER OF ENGINES", "number") };
        var values = Read(specs, token);
        if (values.Any(x => x.Error is not null) || values.Select(x => x.ObjectId).Distinct().Count() != 1 ||
            values[2].Number is not { } count || !double.IsFinite(count) || count != Math.Truncate(count) || count is < 0 or > 16)
            throw new IOException("Condition identity/engine count unavailable; no frame accepted.");
        var identity = new ConditionAircraftIdentity(values[0].ObjectId, values[0].Text ?? "", values[1].Text ?? "", (int)count);
        identity.Validate();
        return identity;
    }

    internal IReadOnlyList<ConditionReading> ReadFields(IReadOnlyList<ConditionField> fields, uint aircraft, CancellationToken token)
    {
        if (fields.Count is < 1 or > 512 || fields.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != fields.Count)
            throw new InvalidDataException("Condition profile fields must be unique and bounded.");
        var values = Read(fields.Select(x => new ConditionDatum(x.SimVar, x.Unit)).ToArray(), token);
        return fields.Select((field, i) => values[i].Error is { } error ? field.Missing(values[i].Utc, error) :
            values[i].ObjectId != aircraft ? field.Missing(values[i].Utc, "Aircraft object changed; reading refused.") :
            field.Observe(values[i].Number ?? double.NaN, values[i].Utc)).ToArray();
    }

    private ConditionRaw[] Read(IReadOnlyList<ConditionDatum> specs, CancellationToken token)
    {
        var replies = new ConditionReplies(specs.Count);
        for (var i = 0; i < specs.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var spec = specs[i];
            if (refused.TryGetValue(spec, out var reason)) { replies.Missing(i, reason); continue; }
            var req = ++sequence;
            if (!definitions.TryGetValue(spec, out var def))
            {
                def = ++sequence;
                if (add(connection, def, spec.Name, spec.Unit, spec.IsString ? 8u : 4u, 0, uint.MaxValue) < 0)
                { replies.Missing(i, "Definition rejected."); refused[spec] = "Definition rejected."; continue; }
                Check(lastPacket(connection, out var packet)); replies.Sent(packet, req);
                definitions[spec] = def;
            }
            replies.Add(req, i, def, spec.IsString);
            // Always explicit USER_AIRCRAFT (0), never CURRENT/AVATAR.
            if (request(connection, req, def, 0, 1, 0, 0, 0, 0) < 0)
            { replies.Reject(req, "Request rejected."); continue; }
            Check(lastPacket(connection, out var requestPacket)); replies.Sent(requestPacket, req);
        }
        var watch = Stopwatch.StartNew();
        while (replies.Pending > 0 && watch.Elapsed < TimeSpan.FromSeconds(2))
        {
            token.ThrowIfCancellationRequested();
            // Bound dispatch draining too, so a busy simulator cannot prevent cancellation.
            for (var n = 0; n < 1024 && dispatch(connection, out var pointer, out var size) >= 0; n++)
            {
                token.ThrowIfCancellationRequested();
                if (size is < 12 or > 4096) throw new IOException("Malformed condition dispatch packet.");
                var bytes = new byte[size]; Marshal.Copy(pointer, bytes, 0, bytes.Length);
                Runtime.Receive(bytes);
                replies.Receive(bytes);
            }
            if (replies.Pending > 0) Thread.Sleep(10);
        }
        var results = replies.Finish();
        // Do not repeatedly send queries rejected by this simulator. Timeout remains retryable.
        for (var i = 0; i < specs.Count; i++)
            if (results[i].Error?.StartsWith("SimConnect exception", StringComparison.Ordinal) == true)
                refused[specs[i]] = results[i].Error!;
        return results;
    }
    private static void Check(int hr) { if (hr < 0) throw new IOException($"Condition SimConnect operation failed: 0x{hr:X8}."); }
    public void Dispose()
    {
        if (connection != IntPtr.Zero && library != IntPtr.Zero)
            Marshal.GetDelegateForFunctionPointer<Close>(NativeLibrary.GetExport(library, "SimConnect_Close"))(connection);
        connection = IntPtr.Zero;
        if (library != IntPtr.Zero) NativeLibrary.Free(library);
        library = IntPtr.Zero;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Open(out IntPtr h, string name, IntPtr window, uint message, IntPtr evt, uint config);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Add(IntPtr h, uint definition, string name, string? units, uint type, float epsilon, uint datum);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Request(IntPtr h, uint request, uint definition, uint obj, uint period, uint flags, uint origin, uint interval, uint limit);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Dispatch(IntPtr h, out IntPtr data, out uint size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LastPacket(IntPtr h, out uint id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Close(IntPtr h);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)] private delegate int Subscribe(IntPtr h, uint id, string name);
}

internal sealed class ConditionRuntimeEvents
{
    internal ConditionRuntimeState State { get; private set; } = new(null, null, 0);
    internal void Receive(byte[] bytes)
    {
        if(bytes.Length < 24 || BitConverter.ToUInt32(bytes,8)!=4)return;
        var id=BitConverter.ToUInt32(bytes,16);var value=BitConverter.ToUInt32(bytes,20);
        State=id switch
        {
            1 => State with { Paused=value!=0 },
            2 => State with { Running=value==1,Discontinuity=State.Discontinuity+1 },
            3 => State with { Discontinuity=State.Discontinuity+1 },
            _ => State
        };
    }
}
