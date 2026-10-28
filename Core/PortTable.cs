using System.Net;
using System.Runtime.InteropServices;

namespace XamppControl.Core;

/// <summary>A listening TCP socket.</summary>
public sealed record Listener(string Address, int Port, int Pid, bool IsIPv6);

/// <summary>Listening TCP sockets, read from the Windows TCP tables (IPv4 + IPv6).</summary>
public static class PortTable
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidListener = 3;
    private const uint ErrorInsufficientBuffer = 122;

    // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid
    private const int Row4Size = 24, Row4Addr = 4, Row4Port = 8, Row4Pid = 20;
    // MIB_TCP6ROW_OWNER_PID: localAddr[16], scope, localPort, remoteAddr[16], scope, remotePort, state, pid
    private const int Row6Size = 56, Row6Addr = 0, Row6Port = 20, Row6Pid = 52;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr table, ref int size, bool sort, int ipVersion, int tableClass, uint reserved);

    public static List<Listener> ListAll()
    {
        var list = new List<Listener>();
        if (!OperatingSystem.IsWindows())
            return list;

        Read(AfInet, list);
        Read(AfInet6, list);
        return list;
    }

    public static Dictionary<int, SortedSet<int>> ListeningByPid()
    {
        var map = new Dictionary<int, SortedSet<int>>();
        foreach (var l in ListAll())
        {
            if (!map.TryGetValue(l.Pid, out var ports))
                map[l.Pid] = ports = new SortedSet<int>();
            ports.Add(l.Port);
        }
        return map;
    }

    /// <summary>PID listening on a port, or null if the port is free.</summary>
    public static int? OwnerOf(int port, IEnumerable<Listener> listeners) =>
        listeners.FirstOrDefault(l => l.Port == port)?.Pid;

    private static void Read(int family, List<Listener> list)
    {
        var v6 = family == AfInet6;
        var rowSize = v6 ? Row6Size : Row4Size;

        // The first call only asks for the buffer size; Windows answers ERROR_INSUFFICIENT_BUFFER.
        var size = 0;
        if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TcpTableOwnerPidListener, 0)
                != ErrorInsufficientBuffer || size <= 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, family, TcpTableOwnerPidListener, 0) != 0)
                return;

            var count = Marshal.ReadInt32(buffer);
            var row = buffer + 4;
            for (var i = 0; i < count; i++, row += rowSize)
            {
                string address;
                if (v6)
                {
                    var bytes = new byte[16];
                    Marshal.Copy(row + Row6Addr, bytes, 0, 16);
                    address = new IPAddress(bytes).ToString();
                }
                else
                {
                    // Stored in network byte order; IPAddress(long) expects exactly that layout.
                    address = new IPAddress((uint)Marshal.ReadInt32(row + Row4Addr)).ToString();
                }

                // Port is in network byte order in the low 16 bits.
                var raw = Marshal.ReadInt32(row + (v6 ? Row6Port : Row4Port));
                var port = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
                var pid = Marshal.ReadInt32(row + (v6 ? Row6Pid : Row4Pid));

                list.Add(new Listener(address, port, pid, v6));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
