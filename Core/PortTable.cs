using System.Net;
using System.Runtime.InteropServices;

namespace XamppControl.Core;

/// <summary>A listening TCP socket.</summary>
/// <param name="Address">The local address as text; 0.0.0.0 or :: when it listens on every interface.</param>
/// <param name="Port">The local port in host byte order, 0 to 65535.</param>
/// <param name="Pid">The owning process ID; 4 is the Windows kernel (System).</param>
/// <param name="IsIPv6">True for a socket from the IPv6 table.</param>
public sealed record Listener(string Address, int Port, int Pid, bool IsIPv6);

/// <summary>Listening TCP sockets, read from the Windows TCP tables (IPv4 + IPv6).</summary>
public static class PortTable
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidListener = 3;
    private const uint ErrorInsufficientBuffer = 122;

    // Byte offsets in MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid
    private const int Row4Size = 24, Row4Addr = 4, Row4Port = 8, Row4Pid = 20;
    // Byte offsets in MIB_TCP6ROW_OWNER_PID: localAddr[16], scope, localPort, remoteAddr[16], scope, remotePort,
    // state, pid
    private const int Row6Size = 56, Row6Addr = 0, Row6Port = 20, Row6Pid = 52;

    /// <summary>Reads the table of listening TCP sockets with their owning PIDs.</summary>
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref int size,
        bool sort,
        int ipVersion,
        int tableClass,
        uint reserved);

    /// <summary>Every listening TCP socket, IPv4 first, then IPv6.</summary>
    /// <returns>The sockets in table order; empty off Windows or when a table cannot be read.</returns>
    public static List<Listener> ListAll()
    {
        var listeners = new List<Listener>();
        if (!OperatingSystem.IsWindows())
        {
            return listeners;
        }

        AddListeners(AfInet, listeners);
        AddListeners(AfInet6, listeners);
        return listeners;
    }

    /// <summary>The listening ports of each process.</summary>
    /// <returns>Ports by PID; a process without a listening socket has no entry.</returns>
    public static Dictionary<int, SortedSet<int>> ListeningByPid()
    {
        var portsByPid = new Dictionary<int, SortedSet<int>>();
        foreach (Listener listener in ListAll())
        {
            if (!portsByPid.TryGetValue(listener.Pid, out SortedSet<int>? ports))
            {
                ports = new SortedSet<int>();
                portsByPid[listener.Pid] = ports;
            }

            ports.Add(listener.Port);
        }

        return portsByPid;
    }

    /// <summary>PID listening on a port, or null if the port is free.</summary>
    /// <param name="port">The local TCP port.</param>
    /// <param name="listeners">The sockets to search, usually from <see cref="ListAll"/>.</param>
    /// <returns>The PID of the first socket on that port; null when none listens.</returns>
    public static int? OwnerOf(int port, IEnumerable<Listener> listeners) =>
        listeners.FirstOrDefault(listener => listener.Port == port)?.Pid;

    /// <summary>Appends the listening sockets of one address family; none when its table cannot be read.</summary>
    /// <param name="family"><see cref="AfInet"/> or <see cref="AfInet6"/>.</param>
    /// <param name="listeners">Receives the sockets in table order.</param>
    private static void AddListeners(int family, List<Listener> listeners)
    {
        bool isIPv6 = family == AfInet6;
        int rowSize = isIPv6 ? Row6Size : Row4Size;

        // The first call only asks for the buffer size; Windows answers ERROR_INSUFFICIENT_BUFFER.
        int tableSize = 0;
        uint sizeQueryResult = GetExtendedTcpTable(
            IntPtr.Zero,
            ref tableSize,
            false,
            family,
            TcpTableOwnerPidListener,
            0);
        if (sizeQueryResult != ErrorInsufficientBuffer || tableSize <= 0)
        {
            return;
        }

        IntPtr buffer = Marshal.AllocHGlobal(tableSize);
        try
        {
            if (GetExtendedTcpTable(buffer, ref tableSize, false, family, TcpTableOwnerPidListener, 0) != 0)
            {
                return;
            }

            int rowCount = Marshal.ReadInt32(buffer);
            IntPtr row = buffer + 4;
            for (int i = 0; i < rowCount; i++, row += rowSize)
            {
                listeners.Add(ReadRow(row, isIPv6));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Decodes one row of a TCP owner-PID table.</summary>
    /// <param name="row">Start of the row in unmanaged memory.</param>
    /// <param name="isIPv6">True for a MIB_TCP6ROW_OWNER_PID row; false for MIB_TCPROW_OWNER_PID.</param>
    /// <returns>The socket's local address, port and owner.</returns>
    private static Listener ReadRow(IntPtr row, bool isIPv6)
    {
        string address;
        if (isIPv6)
        {
            var addressBytes = new byte[16];
            Marshal.Copy(row + Row6Addr, addressBytes, 0, 16);
            address = new IPAddress(addressBytes).ToString();
        }
        else
        {
            // Stored in network byte order; IPAddress(long) expects exactly that layout.
            address = new IPAddress((uint)Marshal.ReadInt32(row + Row4Addr)).ToString();
        }

        // Port is in network byte order in the low 16 bits.
        int portField = Marshal.ReadInt32(row + (isIPv6 ? Row6Port : Row4Port));
        int port = ((portField & 0xFF) << 8) | ((portField >> 8) & 0xFF);
        int pid = Marshal.ReadInt32(row + (isIPv6 ? Row6Pid : Row4Pid));

        return new Listener(address, port, pid, isIPv6);
    }
}
