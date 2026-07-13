using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace ProcessProxyManager.Native;

public sealed class ConnectionSnapshotService
{
    private const int AddressFamilyInet = 2;
    private const int AddressFamilyInet6 = 23;
    private const int ErrorInsufficientBuffer = 122;
    private static readonly TimeSpan DefaultMaxAge = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly ConcurrentDictionary<ProcessIdentity, string> _processNames = new();
    private ConnectionSnapshot? _lastSnapshot;

    public async Task<ConnectionSnapshot> GetSnapshotAsync(
        TimeSpan? maxAge = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveMaxAge = maxAge ?? DefaultMaxAge;
        var cached = _lastSnapshot;
        if (cached is not null && DateTimeOffset.UtcNow - cached.CapturedAt <= effectiveMaxAge)
        {
            return cached;
        }

        await _captureGate.WaitAsync(cancellationToken);
        try
        {
            cached = _lastSnapshot;
            if (cached is not null && DateTimeOffset.UtcNow - cached.CapturedAt <= effectiveMaxAge)
            {
                return cached;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var snapshot = await Task.Run(() => Capture(timeout.Token), timeout.Token);
            _lastSnapshot = snapshot;
            return snapshot;
        }
        finally
        {
            _captureGate.Release();
        }
    }

    public ConnectionSnapshot Capture(CancellationToken cancellationToken = default)
    {
        var rows = new List<RawConnection>();
        ReadTcp4(rows, cancellationToken);
        ReadTcp6(rows, cancellationToken);
        ReadUdp4(rows, cancellationToken);
        ReadUdp6(rows, cancellationToken);

        var currentIdentities = new HashSet<ProcessIdentity>();
        var connections = new List<NetworkConnectionSnapshot>(rows.Count);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = GetProcessIdentity(row.ProcessId);
            currentIdentities.Add(identity);
            var processName = _processNames.GetOrAdd(identity, static value => ReadProcessName(value.ProcessId));
            connections.Add(new NetworkConnectionSnapshot(
                row.ProcessId,
                processName,
                row.Protocol,
                row.LocalAddress,
                row.LocalPort,
                row.RemoteAddress,
                row.RemotePort,
                row.State));
        }

        foreach (var cachedIdentity in _processNames.Keys)
        {
            if (!currentIdentities.Contains(cachedIdentity))
            {
                _processNames.TryRemove(cachedIdentity, out _);
            }
        }

        return new ConnectionSnapshot(DateTimeOffset.UtcNow, connections);
    }

    private static void ReadTcp4(List<RawConnection> rows, CancellationToken cancellationToken)
    {
        ReadTable(
            (IntPtr buffer, ref int length) => GetExtendedTcpTable(
                buffer,
                ref length,
                true,
                AddressFamilyInet,
                TcpTableClass.OwnerPidAll,
                0),
            pointer =>
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(pointer);
                rows.Add(new RawConnection(
                    "TCP",
                    ToIpv4(row.LocalAddress),
                    ToPort(row.LocalPort),
                    ToIpv4(row.RemoteAddress),
                    ToPort(row.RemotePort),
                    ToTcpState(row.State),
                    unchecked((int)row.OwningPid)));
            },
            Marshal.SizeOf<MibTcpRowOwnerPid>(),
            cancellationToken);
    }

    private static void ReadTcp6(List<RawConnection> rows, CancellationToken cancellationToken)
    {
        ReadTable(
            (IntPtr buffer, ref int length) => GetExtendedTcpTable(
                buffer,
                ref length,
                true,
                AddressFamilyInet6,
                TcpTableClass.OwnerPidAll,
                0),
            pointer =>
            {
                var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(pointer);
                rows.Add(new RawConnection(
                    "TCP",
                    ToIpv6(row.LocalAddress, row.LocalScopeId),
                    ToPort(row.LocalPort),
                    ToIpv6(row.RemoteAddress, row.RemoteScopeId),
                    ToPort(row.RemotePort),
                    ToTcpState(row.State),
                    unchecked((int)row.OwningPid)));
            },
            Marshal.SizeOf<MibTcp6RowOwnerPid>(),
            cancellationToken);
    }

    private static void ReadUdp4(List<RawConnection> rows, CancellationToken cancellationToken)
    {
        ReadTable(
            (IntPtr buffer, ref int length) => GetExtendedUdpTable(
                buffer,
                ref length,
                true,
                AddressFamilyInet,
                UdpTableClass.OwnerPid,
                0),
            pointer =>
            {
                var row = Marshal.PtrToStructure<MibUdpRowOwnerPid>(pointer);
                rows.Add(new RawConnection(
                    "UDP",
                    ToIpv4(row.LocalAddress),
                    ToPort(row.LocalPort),
                    string.Empty,
                    0,
                    string.Empty,
                    unchecked((int)row.OwningPid)));
            },
            Marshal.SizeOf<MibUdpRowOwnerPid>(),
            cancellationToken);
    }

    private static void ReadUdp6(List<RawConnection> rows, CancellationToken cancellationToken)
    {
        ReadTable(
            (IntPtr buffer, ref int length) => GetExtendedUdpTable(
                buffer,
                ref length,
                true,
                AddressFamilyInet6,
                UdpTableClass.OwnerPid,
                0),
            pointer =>
            {
                var row = Marshal.PtrToStructure<MibUdp6RowOwnerPid>(pointer);
                rows.Add(new RawConnection(
                    "UDP",
                    ToIpv6(row.LocalAddress, row.LocalScopeId),
                    ToPort(row.LocalPort),
                    string.Empty,
                    0,
                    string.Empty,
                    unchecked((int)row.OwningPid)));
            },
            Marshal.SizeOf<MibUdp6RowOwnerPid>(),
            cancellationToken);
    }

    private static void ReadTable(
        NativeTableReader reader,
        Action<IntPtr> addRow,
        int rowSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var length = 0;
        var result = reader(IntPtr.Zero, ref length);
        if (result != ErrorInsufficientBuffer || length <= sizeof(uint))
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            result = reader(buffer, ref length);
            if (result != 0)
            {
                return;
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPointer = IntPtr.Add(buffer, sizeof(uint));
            for (var index = 0; index < rowCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                addRow(IntPtr.Add(rowPointer, index * rowSize));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ProcessIdentity GetProcessIdentity(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new ProcessIdentity(processId, process.StartTime.ToUniversalTime().Ticks);
        }
        catch
        {
            return new ProcessIdentity(processId, 0);
        }
    }

    private static string ReadProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? process.ProcessName
                : $"{process.ProcessName}.exe";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ToIpv4(uint address)
    {
        return new IPAddress(BitConverter.GetBytes(address)).ToString();
    }

    private static string ToIpv6(byte[] address, uint scopeId)
    {
        return new IPAddress(address, scopeId).ToString();
    }

    private static int ToPort(uint port)
    {
        return unchecked((ushort)IPAddress.NetworkToHostOrder(unchecked((short)port)));
    }

    private static string ToTcpState(uint state)
    {
        return (TcpState)state switch
        {
            TcpState.Closed => "CLOSED",
            TcpState.Listen => "LISTENING",
            TcpState.SynSent => "SYN_SENT",
            TcpState.SynReceived => "SYN_RECEIVED",
            TcpState.Established => "ESTABLISHED",
            TcpState.FinWait1 => "FIN_WAIT_1",
            TcpState.FinWait2 => "FIN_WAIT_2",
            TcpState.CloseWait => "CLOSE_WAIT",
            TcpState.Closing => "CLOSING",
            TcpState.LastAck => "LAST_ACK",
            TcpState.TimeWait => "TIME_WAIT",
            TcpState.DeleteTcb => "DELETE_TCB",
            _ => string.Empty
        };
    }

    private delegate uint NativeTableReader(IntPtr buffer, ref int length);

    private readonly record struct ProcessIdentity(int ProcessId, long StartTimeUtcTicks);

    private sealed record RawConnection(
        string Protocol,
        string LocalAddress,
        int LocalPort,
        string RemoteAddress,
        int RemotePort,
        string State,
        int ProcessId);

    private enum TcpTableClass
    {
        OwnerPidAll = 5
    }

    private enum UdpTableClass
    {
        OwnerPid = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddress;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint LocalAddress;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        bool order,
        int addressFamily,
        TcpTableClass tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr udpTable,
        ref int size,
        bool order,
        int addressFamily,
        UdpTableClass tableClass,
        uint reserved);
}
