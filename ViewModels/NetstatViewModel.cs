using System.Collections.ObjectModel;
using Avalonia.Threading;
using XamppControl.Core;
using XamppControl.Modules;

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

/// <summary>One row of the Netstat window: a listening socket, the process that owns it, and what the port is for.</summary>
/// <param name="listener">The socket; its address, port and PID become the row's columns.</param>
/// <param name="name">The process's file name, "System", "System Idle Process", or "?" when it cannot be read.</param>
/// <param name="path">Full path of the process's executable; empty when it cannot be read.</param>
/// <param name="isXampp">True when the executable lies inside the XAMPP folder.</param>
/// <param name="state">Picks the row's colors.</param>
/// <param name="service">What the port is normally used for: a module's name or a well-known protocol; empty when unknown.</param>
/// <param name="blocksModule">The stopped XAMPP module whose configured port this foreign process holds; empty when none.</param>
public sealed class NetstatRow(
    Listener listener,
    string name,
    string path,
    bool isXampp,
    SocketState state,
    string service,
    string blocksModule)
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

    /// <summary>What the port is for: "Apache", "MySQL", "HTTP", "RDP"...; empty when unknown.</summary>
    public string Service { get; } = service;

    /// <summary>The stopped XAMPP module this foreign process keeps from starting; empty when none.</summary>
    public string BlocksModule { get; } = blocksModule;

    /// <summary>True when the socket is IPv6.</summary>
    public bool IsIPv6 { get; } = listener.IsIPv6;

    /// <summary>"address|port|pid", unique per socket across refreshes.</summary>
    public string Key => $"{Address}|{Port}|{Pid}";

    /// <summary>True for a socket that appeared since the previous refresh; the row shows green.</summary>
    public bool IsNew => State == SocketState.New;

    /// <summary>True for a socket that closed since the previous refresh; the row shows dark red.</summary>
    public bool IsOld => State == SocketState.Old;

    /// <summary>True when a service name is known, so the label shows.</summary>
    public bool HasService => Service.Length > 0;

    /// <summary>True when the row should carry a "blocks Apache" style warning.</summary>
    public bool IsBlocking => BlocksModule.Length > 0;

    /// <summary>"blocks Apache", for the warning pill.</summary>
    public string BlockingText => $"blocks {BlocksModule}";

    /// <summary>Who can reach the socket: "all interfaces" for the wildcard address, "this PC only" for loopback, else "".</summary>
    public string Reach => Address switch
    {
        "0.0.0.0" or "::" => "all interfaces",
        "127.0.0.1" or "::1" => "this PC only",
        _ => "",
    };

    /// <summary>True when <see cref="Reach"/> has text.</summary>
    public bool HasReach => Reach.Length > 0;

    /// <summary>True when the executable's folder is known, so "Folder" can open it.</summary>
    public bool HasPath => Path.Length > 0;

    /// <summary>True when the process is one the user may end: not XAMPP's, not Windows itself, still listening.</summary>
    public bool CanEnd => !IsXampp && Pid > 4 && !IsOld;

    /// <summary>True when the port speaks HTTP (by its service name), so "Open" shows a browser button.</summary>
    public bool IsWeb => Service is "HTTP" or "HTTPS" or "HTTP alt" or "Apache" or "Tomcat" or "Mailpit";

    /// <summary>The address a browser can open for a web port.</summary>
    public string Url
    {
        get
        {
            string scheme = Service == "HTTPS" || Port == 443 ? "https" : "http";
            bool isDefault = (scheme == "http" && Port == 80) || (scheme == "https" && Port == 443);
            return isDefault ? $"{scheme}://localhost/" : $"{scheme}://localhost:{Port}/";
        }
    }

    /// <summary>The row as one line of text, for copying.</summary>
    public string Line => $"{Address}:{Port}\tPID {Pid}\t{Name}{(HasService ? $" ({Service})" : "")}{(HasPath ? $"\t{Path}" : "")}";

    /// <summary>True when the row matches a filter typed by the user.</summary>
    /// <param name="filter">Lowercase text; matched against address, port, PID, name, path and service.</param>
    /// <returns>True for an empty filter or a match in any column.</returns>
    public bool Matches(string filter) =>
        filter.Length == 0
        || Address.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Port.ToString().Contains(filter, StringComparison.Ordinal)
        || Pid.ToString().Contains(filter, StringComparison.Ordinal)
        || Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Path.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Service.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || BlocksModule.Contains(filter, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Netstat - TCP Listening sockets. Refreshes every few seconds; sockets that appeared since the last
/// refresh are "new" (green), sockets that disappeared stay one round as "old" (dark red). Rows can be filtered,
/// sorted, limited to XAMPP, and a foreign process can be ended from its row.
/// </summary>
public sealed class NetstatViewModel : ObservableObject, IDisposable
{
    private const string LogTag = "netstat";

    private static readonly Dictionary<int, string> _wellKnownPorts = new()
    {
        [21] = "FTP",
        [22] = "SSH",
        [25] = "SMTP",
        [53] = "DNS",
        [80] = "HTTP",
        [110] = "POP3",
        [135] = "RPC",
        [139] = "NetBIOS",
        [143] = "IMAP",
        [443] = "HTTPS",
        [445] = "SMB",
        [587] = "SMTP",
        [993] = "IMAPS",
        [995] = "POP3S",
        [1433] = "SQL Server",
        [1521] = "Oracle",
        [1883] = "MQTT",
        [3000] = "Node dev",
        [3306] = "MySQL",
        [3389] = "Remote Desktop",
        [5173] = "Vite",
        [5357] = "WSD",
        [5432] = "PostgreSQL",
        [5900] = "VNC",
        [6379] = "Redis",
        [8000] = "HTTP alt",
        [8080] = "HTTP alt",
        [8443] = "HTTPS alt",
        [9003] = "Xdebug",
        [11211] = "Memcached",
        [27017] = "MongoDB",
    };

    private readonly MainViewModel _main;
    private readonly DispatcherTimer _refreshTimer;
    private Dictionary<string, Listener> _previousListeners = new();
    private List<NetstatRow> _allRows = [];
    private bool _isFirstRefresh = true;
    private bool _isRefreshing;
    private bool _isPaused;
    private string _filter = "";
    private bool _xamppOnly;
    private bool _showIPv6 = true;
    private string _sortColumn = "Port";
    private bool _sortDescending;
    private string _summary = "";
    private string _status = "";
    private int _totalCount;
    private int _xamppCount;
    private int _blockedCount;
    private int? _pendingEndPid;

    /// <summary>Starts refreshing at once and then every three seconds until disposed.</summary>
    /// <param name="main">The panel; supplies the XAMPP folder, the modules' ports, the browser and the log.</param>
    public NetstatViewModel(MainViewModel main)
    {
        _main = main;
        RefreshCommand = new RelayCommand(RefreshAsync);
        PauseCommand = new RelayCommand(() => IsPaused = !IsPaused);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    /// <summary>The sockets of the last refresh after the filter and sort.</summary>
    public ObservableCollection<NetstatRow> Rows { get; } = [];

    /// <summary>The Refresh button: refreshes now, without waiting for the timer.</summary>
    public RelayCommand RefreshCommand { get; }

    /// <summary>The Pause/Resume button: stops and starts the automatic refresh.</summary>
    public RelayCommand PauseCommand { get; }

    /// <summary>Text typed in the filter box; matched against every column.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetField(ref _filter, value))
            {
                ApplyView();
            }
        }
    }

    /// <summary>True shows only sockets owned by programs in the XAMPP folder.</summary>
    public bool XamppOnly
    {
        get => _xamppOnly;
        set
        {
            if (SetField(ref _xamppOnly, value))
            {
                ApplyView();
            }
        }
    }

    /// <summary>False hides IPv6 sockets, which usually duplicate the IPv4 ones.</summary>
    public bool ShowIPv6
    {
        get => _showIPv6;
        set
        {
            if (SetField(ref _showIPv6, value))
            {
                ApplyView();
            }
        }
    }

    /// <summary>True while the automatic refresh is stopped; the Refresh button still works.</summary>
    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetField(ref _isPaused, value))
            {
                if (value)
                {
                    _refreshTimer.Stop();
                }
                else
                {
                    _refreshTimer.Start();
                    _ = RefreshAsync();
                }
                OnPropertyChanged(nameof(PauseLabel));
            }
        }
    }

    /// <summary>"Pause" or "Resume", for the button.</summary>
    public string PauseLabel => IsPaused ? "Resume" : "Pause";

    /// <summary>The column the rows are sorted by: Port, Address, Pid or Name.</summary>
    public string SortColumn
    {
        get => _sortColumn;
        private set => SetField(ref _sortColumn, value);
    }

    /// <summary>True when the sort runs high to low.</summary>
    public bool SortDescending
    {
        get => _sortDescending;
        private set => SetField(ref _sortDescending, value);
    }

    /// <summary>Listening sockets in the last refresh.</summary>
    public int TotalCount
    {
        get => _totalCount;
        private set => SetField(ref _totalCount, value);
    }

    /// <summary>Of those, sockets owned by programs in the XAMPP folder.</summary>
    public int XamppCount
    {
        get => _xamppCount;
        private set => SetField(ref _xamppCount, value);
    }

    /// <summary>Configured XAMPP ports that another program holds while the module is stopped.</summary>
    public int BlockedCount
    {
        get => _blockedCount;
        private set
        {
            if (SetField(ref _blockedCount, value))
            {
                OnPropertyChanged(nameof(HasBlocked));
            }
        }
    }

    /// <summary>True when <see cref="BlockedCount"/> is above zero, so the tile turns red.</summary>
    public bool HasBlocked => BlockedCount > 0;

    /// <summary>True when the filter leaves no rows, so the empty note shows.</summary>
    public bool IsEmpty => Rows.Count == 0 && !_isFirstRefresh;

    /// <summary>The status line: counts after the filter and the time of the last refresh.</summary>
    public string Summary
    {
        get => _summary;
        private set => SetField(ref _summary, value);
    }

    /// <summary>The result of the last row action, or an "End" confirmation.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>The PORT header with the sort arrow when sorted by it.</summary>
    public string PortHeader => HeaderFor("Port", "PORT");

    /// <summary>The ADDRESS header with the sort arrow when sorted by it.</summary>
    public string AddressHeader => HeaderFor("Address", "ADDRESS");

    /// <summary>The PID header with the sort arrow when sorted by it.</summary>
    public string PidHeader => HeaderFor("Pid", "PID");

    /// <summary>The PROCESS header with the sort arrow when sorted by it.</summary>
    public string NameHeader => HeaderFor("Name", "PROCESS");

    private string HeaderFor(string column, string text) =>
        SortColumn == column ? $"{text} {(SortDescending ? "▼" : "▲")}" : text;

    private void RaiseHeaders()
    {
        OnPropertyChanged(nameof(PortHeader));
        OnPropertyChanged(nameof(AddressHeader));
        OnPropertyChanged(nameof(PidHeader));
        OnPropertyChanged(nameof(NameHeader));
    }

    /// <summary>Sorts by a column; clicking the current column again flips the direction.</summary>
    /// <param name="column">Port, Address, Pid or Name.</param>
    public void SortBy(string column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = false;
        }
        RaiseHeaders();
        ApplyView();
    }

    /// <summary>Opens the folder of the process's executable in Explorer.</summary>
    /// <param name="row">The row; nothing happens without a path.</param>
    public static void OpenFolder(NetstatRow row)
    {
        if (System.IO.Path.GetDirectoryName(row.Path) is { Length: > 0 } folder)
        {
            Shell.Open(folder);
        }
    }

    /// <summary>Opens a web port in the configured browser.</summary>
    /// <param name="row">A row whose port speaks HTTP.</param>
    public void OpenInBrowser(NetstatRow row) => Shell.OpenUrl(row.Url, _main.Settings.Browser);

    /// <summary>First click asks for confirmation in the status line; a second click on the same PID ends the process.</summary>
    /// <param name="row">The row whose process to end; XAMPP's own and Windows's are refused.</param>
    /// <returns>A task that completes after the process is ended and the list refreshed, or at once after the first click.</returns>
    public async Task EndProcessAsync(NetstatRow row)
    {
        if (!row.CanEnd)
        {
            return;
        }
        if (_pendingEndPid != row.Pid)
        {
            _pendingEndPid = row.Pid;
            Status = $"Click End again to end {row.Name} (PID {row.Pid}) and every port it holds. Unsaved work in it is lost.";
            return;
        }

        _pendingEndPid = null;
        Status = $"Ending {row.Name} (PID {row.Pid})…";
        await Task.Run(() => ProcessScan.KillTree(row.Pid));
        if (ProcessScan.NameOf(row.Pid) is not null)
        {
            // Services and programs of other users need admin rights to end.
            int exitCode = await ElevatedHelper.RunAsync("taskkill.exe", $"/PID {row.Pid} /T /F", Environment.SystemDirectory);
            if (exitCode != 0)
            {
                Status = exitCode == -2
                    ? $"Canceled at the admin prompt; {row.Name} keeps running."
                    : $"{row.Name} couldn't be ended (taskkill exited with {exitCode}).";
                _main.Write(LogTag, Status, LogLevel.Warn);
                return;
            }
        }

        Status = $"Ended {row.Name} (PID {row.Pid}).";
        _main.Write(LogTag, $"Ended {row.Name} (PID {row.Pid}) from the Netstat window.", LogLevel.Warn);
        await RefreshAsync();
    }

    /// <summary>Reads the listening sockets on a background thread and replaces the rows with them.</summary>
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
            Dictionary<int, string> modulePorts = ModulePorts();
            HashSet<int> stoppedModulePorts = StoppedModulePorts();
            (List<NetstatRow> rows, Dictionary<string, Listener> currentListeners) =
                await Task.Run(() => BuildRows(previousListeners, isFirstRefresh, modulePorts, stoppedModulePorts));
            _previousListeners = currentListeners;
            _isFirstRefresh = false;
            _allRows = rows;

            TotalCount = currentListeners.Count;
            XamppCount = rows.Count(row => row.IsXampp && !row.IsOld);
            BlockedCount = rows.Where(row => row.IsBlocking && !row.IsOld).Select(row => row.Port).Distinct().Count();
            ApplyView();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>Fills <see cref="Rows"/> from the last refresh with the filter, the toggles and the sort applied.</summary>
    private void ApplyView()
    {
        string filter = Filter.Trim();
        IEnumerable<NetstatRow> view = _allRows
            .Where(row => row.Matches(filter))
            .Where(row => !XamppOnly || row.IsXampp)
            .Where(row => ShowIPv6 || !row.IsIPv6);

        view = SortColumn switch
        {
            "Address" => Order(view, row => row.IsIPv6, row => row.Address, row => row.Port),
            "Pid" => Order(view, row => row.Pid, row => row.Port, row => row.IsIPv6),
            "Name" => Order(view, row => row.Name, row => row.Port, row => row.IsIPv6),
            _ => Order(view, row => row.Port, row => row.IsIPv6, row => row.Address),
        };

        List<NetstatRow> shown = view.ToList();
        Rows.Clear();
        foreach (NetstatRow row in shown)
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));

        int newCount = shown.Count(row => row.IsNew);
        int closedCount = shown.Count(row => row.IsOld);
        string hidden = shown.Count == _allRows.Count ? "" : $" of {_allRows.Count}";
        Summary = $"{shown.Count}{hidden} sockets shown · {newCount} new · {closedCount} closed · "
            + $"refreshed {DateTime.Now:HH:mm:ss}{(IsPaused ? " · paused" : "")}";
    }

    /// <summary>Sorts by up to three keys in the chosen direction; the first key flips, the others break ties.</summary>
    /// <typeparam name="T1">Type of the first key.</typeparam>
    /// <typeparam name="T2">Type of the second key.</typeparam>
    /// <typeparam name="T3">Type of the third key.</typeparam>
    /// <param name="rows">The rows to sort.</param>
    /// <param name="first">The key that follows <see cref="SortDescending"/>.</param>
    /// <param name="second">The first tie-breaker, always ascending.</param>
    /// <param name="third">The second tie-breaker, always ascending.</param>
    /// <returns>The sorted rows.</returns>
    private IOrderedEnumerable<NetstatRow> Order<T1, T2, T3>(
        IEnumerable<NetstatRow> rows,
        Func<NetstatRow, T1> first,
        Func<NetstatRow, T2> second,
        Func<NetstatRow, T3> third)
    {
        IOrderedEnumerable<NetstatRow> ordered = SortDescending ? rows.OrderByDescending(first) : rows.OrderBy(first);
        return ordered.ThenBy(second).ThenBy(third);
    }

    /// <summary>Each installed module's configured ports, by port.</summary>
    /// <returns>Port → module name, e.g. 80 → "Apache"; the first module wins when two claim a port.</returns>
    private Dictionary<int, string> ModulePorts()
    {
        var ports = new Dictionary<int, string>();
        foreach (ModuleViewModel module in _main.Modules.Where(module => module.IsInstalled))
        {
            foreach (int port in module.Driver.ConfiguredPorts)
            {
                ports.TryAdd(port, module.Name);
            }
        }
        return ports;
    }

    /// <summary>The configured ports of modules that aren't running, which a foreign holder therefore blocks.</summary>
    /// <returns>The ports; empty when every installed module runs.</returns>
    private HashSet<int> StoppedModulePorts() =>
        _main.Modules
            .Where(module => module.IsInstalled && !module.IsRunning)
            .SelectMany(module => module.Driver.ConfiguredPorts)
            .ToHashSet();

    /// <summary>Reads the listening sockets and compares them with the previous refresh.</summary>
    /// <param name="previous">The sockets of the previous refresh by <see cref="KeyOf"/>; only read.</param>
    /// <param name="isFirstRefresh">True marks every socket as active, since there is nothing to compare with.</param>
    /// <param name="modulePorts">Port → module name, for the service label.</param>
    /// <param name="stoppedModulePorts">Ports of stopped modules, for the "blocks" warning.</param>
    /// <returns>
    /// The rows, current sockets plus those gone since <paramref name="previous"/>, unsorted; and the current
    /// sockets by key, for the next refresh.
    /// </returns>
    private (List<NetstatRow> Rows, Dictionary<string, Listener> Current) BuildRows(
        Dictionary<string, Listener> previous,
        bool isFirstRefresh,
        Dictionary<int, string> modulePorts,
        HashSet<int> stoppedModulePorts)
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

        NetstatRow RowFor(Listener listener, SocketState state)
        {
            (string name, string path) = DescribeProcess(listener.Pid);
            bool isXampp = IsXampp(path);
            string service = modulePorts.TryGetValue(listener.Port, out string? module)
                ? module
                : _wellKnownPorts.GetValueOrDefault(listener.Port, "");
            bool blocks = !isXampp && state != SocketState.Old && stoppedModulePorts.Contains(listener.Port)
                && modulePorts.TryGetValue(listener.Port, out _);
            return new NetstatRow(listener, name, path, isXampp, state, service, blocks ? service : "");
        }

        var rows = new List<NetstatRow>();
        foreach ((string key, Listener listener) in current)
        {
            bool isNew = !isFirstRefresh && !previous.ContainsKey(key);
            rows.Add(RowFor(listener, isNew ? SocketState.New : SocketState.Active));
        }

        foreach ((string key, Listener listener) in previous)
        {
            if (!current.ContainsKey(key))
            {
                rows.Add(RowFor(listener, SocketState.Old));
            }
        }
        return (rows, current);
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
        path.Length > 0 && path.StartsWith(_main.Root, StringComparison.OrdinalIgnoreCase);

    private static string KeyOf(Listener listener) => $"{listener.Address}|{listener.Port}|{listener.Pid}";

    /// <summary>Stops the refresh timer.</summary>
    public void Dispose() => _refreshTimer.Stop();
}
