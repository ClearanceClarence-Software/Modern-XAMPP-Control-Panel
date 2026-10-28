using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using XamppControl.Core;

namespace XamppControl.ViewModels;

/// <summary>How a listening socket compares with the previous Netstat refresh.</summary>
public enum SocketState
{
    /// <summary>Listening in the previous refresh too, or found by the first refresh.</summary>
    Active,

    /// <summary>Appeared since the previous refresh.</summary>
    New,

    /// <summary>Gone since the previous refresh; shown for this one round only.</summary>
    Old,
}

/// <summary>One row of the Netstat window: a listening socket and the process that owns it.</summary>
/// <param name="listener">The socket; its address, port and PID become the row's columns.</param>
/// <param name="name">The process's file name, "System", "System Idle Process", or "?" when it cannot be read.</param>
/// <param name="path">Full path of the process's executable; empty when it cannot be read.</param>
/// <param name="isXampp">True when the executable lies inside the XAMPP folder.</param>
/// <param name="state">Picks the row's colors.</param>
public sealed class NetstatRow(Listener listener, string name, string path, bool isXampp, SocketState state)
{
    /// <summary>The local IP address, IPv4 or IPv6, as text.</summary>
    public string Address { get; } = listener.Address;

    /// <summary>The local TCP port.</summary>
    public int Port { get; } = listener.Port;

    /// <summary>The owning process's ID.</summary>
    public int Pid { get; } = listener.Pid;

    /// <summary>The owning process's file name, "System", "System Idle Process", or "?".</summary>
    public string Name { get; } = name;

    /// <summary>Full path of the owning process's executable; empty when it cannot be read.</summary>
    public string Path { get; } = path;

    /// <summary>True when the executable lies inside the XAMPP folder; the row then shows a marker.</summary>
    public bool IsXampp { get; } = isXampp;

    /// <summary>How the socket compares with the previous refresh.</summary>
    public SocketState State { get; } = state;

    /// <summary>"address|port|pid", unique per socket across refreshes.</summary>
    public string Key => $"{Address}|{Port}|{Pid}";

    /// <summary>The background in XAMPP's colors: green for new sockets, dark red for ones just closed.</summary>
    public IBrush RowBrush { get; } = state switch
    {
        SocketState.New => Brush.Parse("#22C55E"),
        SocketState.Old => Brush.Parse("#7F1D1D"),
        _ => Brushes.Transparent,
    };

    /// <summary>The row's text color, readable on <see cref="RowBrush"/>.</summary>
    public IBrush TextBrush { get; } = state switch
    {
        SocketState.New => Brush.Parse("#052E16"),
        SocketState.Old => Brushes.White,
        _ => Brush.Parse("#1F2329"),
    };
}

/// <summary>
/// Netstat - TCP Listening sockets. Refreshes every few seconds; sockets that appeared since the last
/// refresh are "new" (green), sockets that disappeared stay one round as "old" (dark red).
/// </summary>
public sealed class NetstatViewModel : ObservableObject, IDisposable
{
    private readonly string _root;
    private readonly DispatcherTimer _refreshTimer;
    private Dictionary<string, Listener> _previousListeners = new();
    private bool _isFirstRefresh = true;
    private bool _isRefreshing;
    private string _summary = "";

    /// <summary>Starts refreshing at once and then every three seconds until disposed.</summary>
    /// <param name="root">The XAMPP folder; processes whose executable path starts with it count as XAMPP's.</param>
    public NetstatViewModel(string root)
    {
        _root = root;
        RefreshCommand = new RelayCommand(RefreshAsync);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    /// <summary>The sockets of the last refresh, IPv4 before IPv6, then by address and port.</summary>
    public ObservableCollection<NetstatRow> Rows { get; } = [];

    /// <summary>The Refresh button: refreshes now, without waiting for the timer.</summary>
    public RelayCommand RefreshCommand { get; }

    /// <summary>The status line: socket count, new and closed counts, and the time of the last refresh.</summary>
    public string Summary
    {
        get => _summary;
        private set => SetField(ref _summary, value);
    }

    /// <summary>Reads the listening sockets on a background thread and replaces <see cref="Rows"/> with them.</summary>
    /// <returns>A task that completes after the rows are replaced; at once when a refresh is already running.</returns>
    private async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            // Copied so the background thread never reads fields the UI thread writes.
            Dictionary<string, Listener> previousListeners = _previousListeners;
            bool isFirstRefresh = _isFirstRefresh;
            (List<NetstatRow> rows, Dictionary<string, Listener> currentListeners) =
                await Task.Run(() => BuildRows(previousListeners, isFirstRefresh));
            _previousListeners = currentListeners;
            _isFirstRefresh = false;

            Rows.Clear();
            foreach (NetstatRow row in rows)
            {
                Rows.Add(row);
            }

            int newCount = rows.Count(row => row.State == SocketState.New);
            int closedCount = rows.Count(row => row.State == SocketState.Old);
            Summary = $"{currentListeners.Count} listening sockets · {newCount} new · {closedCount} closed · "
                + $"{DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>Reads the listening sockets and compares them with the previous refresh.</summary>
    /// <param name="previous">The sockets of the previous refresh by <see cref="KeyOf"/>; only read.</param>
    /// <param name="isFirstRefresh">True marks every socket as active, since there is nothing to compare with.</param>
    /// <returns>
    /// The rows, current sockets plus those gone since <paramref name="previous"/>, in XAMPP's order; and the
    /// current sockets by key, for the next refresh.
    /// </returns>
    private (List<NetstatRow> Rows, Dictionary<string, Listener> Current) BuildRows(
        Dictionary<string, Listener> previous,
        bool isFirstRefresh)
    {
        Dictionary<string, Listener> current = ReadListeners();

        // Several sockets often share a process, so each PID is looked up once.
        var describedPids = new Dictionary<int, (string Name, string Path)>();
        (string Name, string Path) DescribeProcess(int pid)
        {
            if (describedPids.TryGetValue(pid, out (string Name, string Path) known))
            {
                return known;
            }

            string processPath = ProcessScan.PathOf(pid) ?? "";
            (string Name, string Path) described = (ProcessDisplayName(pid, processPath), processPath);
            describedPids[pid] = described;
            return described;
        }

        var rows = new List<NetstatRow>();
        foreach ((string key, Listener listener) in current)
        {
            (string name, string path) = DescribeProcess(listener.Pid);
            bool isNew = !isFirstRefresh && !previous.ContainsKey(key);
            SocketState state = isNew ? SocketState.New : SocketState.Active;
            rows.Add(new NetstatRow(listener, name, path, IsXampp(path), state));
        }

        foreach ((string key, Listener listener) in previous)
        {
            if (current.ContainsKey(key))
            {
                continue;
            }

            (string name, string path) = DescribeProcess(listener.Pid);
            rows.Add(new NetstatRow(listener, name, path, IsXampp(path), SocketState.Old));
        }

        // Same order as XAMPP: IPv4 before IPv6, then by address and port.
        var ordered = rows
            .OrderBy(row => row.Address.Contains(':'))
            .ThenBy(row => row.Address, StringComparer.Ordinal)
            .ThenBy(row => row.Port)
            .ToList();
        return (ordered, current);
    }

    /// <summary>Reads the listening TCP sockets, IPv4 and IPv6, by <see cref="KeyOf"/>.</summary>
    /// <returns>The sockets by key; when two share a key, the one read last is kept.</returns>
    private static Dictionary<string, Listener> ReadListeners()
    {
        var current = new Dictionary<string, Listener>();
        foreach (Listener listener in PortTable.ListAll())
        {
            current[KeyOf(listener)] = listener;
        }
        return current;
    }

    /// <summary>The name the Netstat window shows for a process, as XAMPP's panel names it.</summary>
    /// <param name="pid">The process ID; 0 and 4 are the Windows idle and system processes.</param>
    /// <param name="path">Full path of the executable; empty when it could not be read.</param>
    /// <returns>
    /// The executable's file name; without a path, a fixed name for PID 0 and 4, else the process name with
    /// ".exe", or "?" when that is unknown too.
    /// </returns>
    private static string ProcessDisplayName(int pid, string path)
    {
        if (path.Length > 0)
        {
            return System.IO.Path.GetFileName(path);
        }

        return pid switch
        {
            0 => "System Idle Process",
            4 => "System",
            _ => ProcessScan.NameOf(pid) is { } processName ? processName + ".exe" : "?",
        };
    }

    /// <summary>Whether an executable lies inside the XAMPP folder.</summary>
    /// <param name="path">Full path of the executable; empty never counts.</param>
    /// <returns>True when the path starts with the XAMPP folder, ignoring case.</returns>
    private bool IsXampp(string path) =>
        path.Length > 0 && path.StartsWith(_root, StringComparison.OrdinalIgnoreCase);

    private static string KeyOf(Listener listener) => $"{listener.Address}|{listener.Port}|{listener.Pid}";

    /// <summary>Stops the refresh timer.</summary>
    public void Dispose() => _refreshTimer.Stop();
}
