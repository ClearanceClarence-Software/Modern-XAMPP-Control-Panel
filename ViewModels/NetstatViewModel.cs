using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
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

/// <summary>One listening socket, the process that owns it, and what the port is for.</summary>
/// <param name="listener">The socket: its address, port and PID.</param>
/// <param name="name">The process's file name, "System", "System Idle Process", or "?" when it cannot be read.</param>
/// <param name="path">Full path of the process's executable; empty when it cannot be read.</param>
/// <param name="isXampp">True when the executable lies inside the XAMPP folder.</param>
/// <param name="state">Whether the socket is new, still open, or just closed.</param>
/// <param name="service">What the port is normally used for: a module's name or a well-known protocol; empty when unknown.</param>
/// <param name="blocksModule">The stopped XAMPP module whose configured port this foreign process holds; empty when none.</param>
/// <param name="isWeb">True when a browser can open the port; false for a module's other ports, such as Mailpit's SMTP.</param>
public sealed class NetstatRow(
    Listener listener,
    string name,
    string path,
    bool isXampp,
    SocketState state,
    string service,
    string blocksModule,
    bool isWeb)
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

    /// <summary>True when the executable lies inside the XAMPP folder.</summary>
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

    /// <summary>True for a socket that appeared since the previous refresh.</summary>
    public bool IsNew => State == SocketState.New;

    /// <summary>True for a socket that closed since the previous refresh.</summary>
    public bool IsOld => State == SocketState.Old;

    /// <summary>True when a service name is known.</summary>
    public bool HasService => Service.Length > 0;

    /// <summary>True when this socket holds a port a stopped XAMPP module needs.</summary>
    public bool IsBlocking => BlocksModule.Length > 0;

    /// <summary>True when the port speaks HTTP, so the program's details offer to open it in the browser.</summary>
    public bool IsWeb { get; } = isWeb;

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

    /// <summary>True when the socket matches text typed in the search box.</summary>
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

/// <summary>One port of a program, as the small label the list and the detail pane show.</summary>
/// <param name="Text">The port number, or "+2" for the ports that did not fit.</param>
/// <param name="Service">What the port is for ("HTTPS", "MySQL"); empty when unknown.</param>
/// <param name="IsXampp">True for a port held by one of XAMPP's programs; the label takes the accent color.</param>
/// <param name="IsBlocking">True for a port a stopped XAMPP module needs; the label turns red.</param>
public sealed record PortChip(string Text, string Service, bool IsXampp, bool IsBlocking)
{
    /// <summary>True when <see cref="Service"/> has text, so the detail pane shows it next to the port.</summary>
    public bool HasService => Service.Length > 0;
}

/// <summary>Which programs the Netstat list shows.</summary>
public enum NetstatScope
{
    /// <summary>Every program that listens.</summary>
    All,

    /// <summary>Only XAMPP's own programs.</summary>
    Xampp,

    /// <summary>Only programs that hold a port a stopped XAMPP module needs.</summary>
    Blocking,
}

/// <summary>
/// One entry of the Netstat list: a program with every port it listens on. Apache on ports 80 and 443, over IPv4
/// and IPv6, is one entry with two ports, not four rows.
/// </summary>
public sealed class NetstatProgram
{
    private static readonly IBrush _xamppBrush = Brush.Parse("#FB7A24");
    private static readonly IBrush _systemBrush = Brush.Parse("#9CA3AF");

    // Calm, distinct colors for other programs' badges; a program keeps its color between refreshes.
    private static readonly IBrush[] _badgeBrushes =
    [
        Brush.Parse("#2563EB"), Brush.Parse("#7C3AED"), Brush.Parse("#0E7490"), Brush.Parse("#4B5563"),
        Brush.Parse("#BE185D"), Brush.Parse("#15803D"),
    ];

    /// <summary>Groups the sockets of one process.</summary>
    /// <param name="sockets">The process's sockets; at least one.</param>
    /// <param name="title">The program's name as people know it: "Apache", "Node.js", or the file name.</param>
    /// <param name="isSystem">True for Windows' own processes, which the list hides until asked.</param>
    public NetstatProgram(IReadOnlyList<NetstatRow> sockets, string title, bool isSystem)
    {
        Sockets = sockets;
        Title = title;
        IsSystem = isSystem;

        NetstatRow first = sockets[0];
        Pid = first.Pid;
        ExeName = first.Name;
        Path = first.Path;
        IsXampp = first.IsXampp;
        IsOld = sockets.All(socket => socket.IsOld);
        IsNew = !IsOld && sockets.All(socket => socket.IsNew);

        // A program that just closed still shows the ports it had; otherwise only the ones still open.
        List<NetstatRow> live = IsOld ? sockets.ToList() : sockets.Where(socket => !socket.IsOld).ToList();
        AllPorts = live
            .GroupBy(socket => socket.Port)
            .OrderBy(group => group.Key)
            .Select(group => new PortChip(
                group.Key.ToString(CultureInfo.InvariantCulture),
                group.First().Service,
                IsXampp,
                group.Any(socket => socket.IsBlocking)))
            .ToList();
        PortChips = AllPorts.Count <= 3
            ? AllPorts
            : [.. AllPorts.Take(2), new PortChip($"+{AllPorts.Count - 2}", "", IsXampp, false)];

        IsNetwork = live.Any(socket => !socket.Address.StartsWith("127.", StringComparison.Ordinal) && socket.Address != "::1");
        Addresses = string.Join(" · ", live.Select(socket => socket.IsIPv6 ? $"[{socket.Address}]" : socket.Address).Distinct());
        BlockedModules = live.Where(socket => socket.IsBlocking).Select(socket => socket.BlocksModule).Distinct().ToList();
        Url = live.Where(socket => socket.IsWeb).OrderBy(socket => socket.Port).FirstOrDefault()?.Url ?? "";
    }

    /// <summary>The program's sockets, open and just closed.</summary>
    public IReadOnlyList<NetstatRow> Sockets { get; }

    /// <summary>The name in the list: "Apache", "Visual Studio Code", or the file name without ".exe".</summary>
    public string Title { get; }

    /// <summary>The process ID.</summary>
    public int Pid { get; }

    /// <summary>The executable's file name, "System", or "?".</summary>
    public string ExeName { get; }

    /// <summary>Full path of the executable; empty when it cannot be read.</summary>
    public string Path { get; }

    /// <summary>True when the executable lies inside the XAMPP folder.</summary>
    public bool IsXampp { get; }

    /// <summary>True for Windows' own processes.</summary>
    public bool IsSystem { get; }

    /// <summary>True when every socket appeared since the previous refresh: the program just started listening.</summary>
    public bool IsNew { get; }

    /// <summary>True when every socket closed since the previous refresh; the entry stays one round, dimmed.</summary>
    public bool IsOld { get; }

    /// <summary>Every port, lowest first, for the detail pane.</summary>
    public IReadOnlyList<PortChip> AllPorts { get; }

    /// <summary>The ports that fit in the list: up to three, or two and a "+N".</summary>
    public IReadOnlyList<PortChip> PortChips { get; }

    /// <summary>True when a socket listens on more than this PC's own loopback address.</summary>
    public bool IsNetwork { get; }

    /// <summary>The addresses listened on, e.g. "0.0.0.0 · [::]".</summary>
    public string Addresses { get; }

    /// <summary>The stopped XAMPP modules whose ports this program holds; empty for most programs.</summary>
    public IReadOnlyList<string> BlockedModules { get; }

    /// <summary>The address a browser can open when one of the ports speaks HTTP; empty otherwise.</summary>
    public string Url { get; }

    /// <summary>The first letter of the title, for the badge.</summary>
    public string Initial => Title.Length > 0 ? Title[..1].ToUpperInvariant() : "?";

    /// <summary>The badge's color: XAMPP's orange, gray for Windows, otherwise one picked by the name.</summary>
    public IBrush BadgeBrush
    {
        get
        {
            if (IsXampp)
            {
                return _xamppBrush;
            }
            if (IsSystem)
            {
                return _systemBrush;
            }

            // string.GetHashCode differs per run; this sum is the same every time, so the color is too.
            int sum = ExeName.Sum(letter => char.ToLowerInvariant(letter));
            return _badgeBrushes[sum % _badgeBrushes.Length];
        }
    }

    /// <summary>The dimmed look of a program that just closed.</summary>
    public double RowOpacity => IsOld ? 0.5 : 1;

    /// <summary>"Network" or "This PC only".</summary>
    public string ReachText => IsNetwork ? Loc.T("Netstat.Network") : Loc.T("Netstat.ThisPc");

    /// <summary>One sentence on what the reach means.</summary>
    public string ReachExplain => IsNetwork ? Loc.T("Netstat.NetworkExplain") : Loc.T("Netstat.ThisPcExplain");

    /// <summary>The line under the title in the list: "2 ports · Network".</summary>
    public string CountText => $"{Loc.Plural("Netstat.Ports", AllPorts.Count)} · {ReachText}";

    /// <summary>The line under the title in the detail pane: "httpd.exe · part of XAMPP".</summary>
    public string ExeLine => IsXampp ? $"{ExeName} · {Loc.T("Netstat.PartOfXampp")}" : ExeName;

    /// <summary>The path as shown; on the mock machine the pretend one.</summary>
    public string PathText => MockMachine.Pretty(Path);

    /// <summary>True when the executable's folder is known.</summary>
    public bool HasPath => Path.Length > 0;

    /// <summary>True when a port speaks HTTP, so the detail pane offers to open it.</summary>
    public bool HasUrl => Url.Length > 0;

    /// <summary>
    /// True for XAMPP's database server. Its port doesn't speak HTTP, so the detail pane offers phpMyAdmin instead.
    /// </summary>
    public bool IsDatabaseServer =>
        IsXampp && (ExeName.Equals("mysqld.exe", StringComparison.OrdinalIgnoreCase)
            || ExeName.Equals("mariadbd.exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>The Open button's text: "Open http://localhost:8025".</summary>
    public string OpenLabel => Loc.T("Netstat.OpenUrl", Url.TrimEnd('/'));

    /// <summary>True when the program holds a port a stopped XAMPP module needs.</summary>
    public bool IsBlocking => BlockedModules.Count > 0;

    /// <summary>"blocks Apache", for the red label in the list.</summary>
    public string BlockingText => Loc.T("Netstat.BlocksModule", string.Join(", ", BlockedModules));

    /// <summary>The sentence in the detail pane that says what the blocking means.</summary>
    public string BlockingExplain => Loc.T("Netstat.BlocksExplain", string.Join(", ", BlockedModules));

    /// <summary>True when the process is one the user may end: not XAMPP's, not Windows itself, still listening.</summary>
    public bool CanEnd => !IsXampp && Pid > 4 && !IsOld;

    /// <summary>Everything that decides how the entry looks; two refreshes with the same text need no redraw.</summary>
    public string Signature =>
        $"{Pid}|{Title}|{IsNew}|{IsOld}|{string.Join(",", Sockets.Select(socket => $"{socket.Key}:{socket.State}:{socket.BlocksModule}:{socket.Service}"))}";

    /// <summary>The program as text, for copying.</summary>
    public string Details =>
        $"{Title} ({ExeName}, PID {Pid}){Environment.NewLine}" +
        (HasPath ? PathText + Environment.NewLine : "") +
        string.Join(Environment.NewLine, Sockets.Where(socket => !socket.IsOld).Select(socket =>
            $"{socket.Address}:{socket.Port}{(socket.HasService ? $"  {socket.Service}" : "")}"));

    /// <summary>True when the program matches text typed in the search box.</summary>
    /// <param name="filter">The text; matched against the title and every socket's columns.</param>
    /// <returns>True for an empty filter or any match.</returns>
    public bool Matches(string filter) =>
        filter.Length == 0
        || Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Sockets.Any(socket => socket.Matches(filter));
}

/// <summary>
/// Netstat: the programs that listen on a TCP port, one entry per program, with the selected one's details beside
/// the list. Refreshes every few seconds and redraws only when something changed. The list can be searched and
/// narrowed to XAMPP's programs or to those blocking a module; Windows' own processes stay folded away until asked
/// for. A foreign process can be ended from its details.
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
        [3306] = "MySQL",
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

    // Names people know for programs whose file name says little; anything else uses the file's own description.
    private static readonly Dictionary<string, string> _knownPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["httpd.exe"] = "Apache",
        ["mysqld.exe"] = "MariaDB",
        ["mariadbd.exe"] = "MariaDB",
        ["mailpit.exe"] = "Mailpit",
        ["filezilla-server.exe"] = "FileZilla Server",
        ["filezilla server.exe"] = "FileZilla Server",
        ["filezillaserver.exe"] = "FileZilla Server",
        ["mercury.exe"] = "Mercury",
        ["tomcat8.exe"] = "Tomcat",
        ["node.exe"] = "Node.js",
        ["code.exe"] = "Visual Studio Code",
        ["svchost.exe"] = "Windows service host",
        ["System"] = "Windows (System)",
        ["lsass.exe"] = "Windows sign-in service",
        ["services.exe"] = "Windows services",
        ["wininit.exe"] = "Windows startup",
        ["spoolsv.exe"] = "Print spooler",
    };

    private readonly MainViewModel _main;
    private readonly DispatcherTimer _refreshTimer;
    private Dictionary<string, Listener> _previousListeners = new();
    private Dictionary<int, (string Name, string Path)> _previousDescriptions = new();
    private List<NetstatProgram> _allPrograms = [];
    private bool _isFirstRefresh = true;
    private bool _isRefreshing;
    private bool _isSyncing;
    private string _filter = "";
    private NetstatScope _scope = NetstatScope.All;
    private bool _showSystem;
    private NetstatProgram? _selected;
    private int? _selectedPid;
    private string _summary = "";
    private string _status = "";
    private int _systemCount;
    private int? _pendingEndPid;

    /// <summary>Starts refreshing at once and then every three seconds until disposed.</summary>
    /// <param name="main">The panel; supplies the XAMPP folder, the modules' ports, the browser and the log.</param>
    public NetstatViewModel(MainViewModel main)
    {
        _main = main;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();
        Loc.Changed += OnLanguageChanged;
        _ = RefreshAsync();
    }

    /// <summary>The programs of the last refresh after the search, the scope and the Windows toggle.</summary>
    public ObservableCollection<NetstatProgram> Programs { get; } = [];

    /// <summary>The program whose details show beside the list; null while the list is empty.</summary>
    public NetstatProgram? Selected
    {
        get => _selected;
        set
        {
            // While the list is being replaced the list box reports "nothing selected"; that passes through, so the
            // entry set right after is a change the list box sees, but it is not recorded as the user's choice.
            if (SetField(ref _selected, value))
            {
                if (!_isSyncing)
                {
                    _selectedPid = value?.Pid;
                    _pendingEndPid = null;
                    Status = "";
                }
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    /// <summary>True when a program is selected, so the detail pane has something to show.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>Text typed in the search box; matched against names, ports, PIDs, paths and services.</summary>
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

    /// <summary>Which programs are listed: all, XAMPP's, or those blocking a module.</summary>
    public NetstatScope Scope
    {
        get => _scope;
        set
        {
            if (SetField(ref _scope, value))
            {
                OnPropertyChanged(nameof(IsScopeAll));
                OnPropertyChanged(nameof(IsScopeXampp));
                OnPropertyChanged(nameof(IsScopeBlocking));
                OnPropertyChanged(nameof(XamppOnly));
                ApplyView();
            }
        }
    }

    /// <summary>True while every program is listed; lights the "All" segment.</summary>
    public bool IsScopeAll => Scope == NetstatScope.All;

    /// <summary>True while only XAMPP's programs are listed; lights the "XAMPP" segment.</summary>
    public bool IsScopeXampp => Scope == NetstatScope.Xampp;

    /// <summary>True while only blocking programs are listed; lights the "Blocking" segment.</summary>
    public bool IsScopeBlocking => Scope == NetstatScope.Blocking;

    /// <summary>True shows only XAMPP's programs; the same as <see cref="Scope"/> set to XAMPP.</summary>
    public bool XamppOnly
    {
        get => Scope == NetstatScope.Xampp;
        set => Scope = value ? NetstatScope.Xampp : NetstatScope.All;
    }

    /// <summary>
    /// True also lists Windows' own processes. They are hidden by default because there are many and they are
    /// rarely the answer; one that blocks a module, or that the search finds, shows regardless.
    /// </summary>
    public bool ShowSystem
    {
        get => _showSystem;
        set
        {
            if (SetField(ref _showSystem, value))
            {
                ApplyView();
            }
        }
    }

    /// <summary>The Windows toggle's text: "Windows · 11".</summary>
    public string SystemLabel => Loc.T("Netstat.WindowsCount", _systemCount);

    /// <summary>True when the search and the scope leave nothing to list, so the empty note shows.</summary>
    public bool IsEmpty => Programs.Count == 0 && !_isFirstRefresh;

    /// <summary>The line at the bottom right: how much is listed and when it was last read.</summary>
    public string Summary
    {
        get => _summary;
        private set => SetField(ref _summary, value);
    }

    /// <summary>The result of the last action, or the "click again" confirmation before ending a process.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>Rebuilds the texts built in code after the language changed.</summary>
    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(SystemLabel));
        ApplyView(isForced: true);
    }

    /// <summary>Opens the folder of the selected program's executable in Explorer.</summary>
    /// <param name="program">The program; nothing happens without a path.</param>
    public static void OpenFolder(NetstatProgram program)
    {
        if (System.IO.Path.GetDirectoryName(program.Path) is { Length: > 0 } folder)
        {
            Shell.Open(folder);
        }
    }

    /// <summary>Opens phpMyAdmin, offering to start Apache first when it isn't running.</summary>
    /// <param name="confirmStartApache">Asks whether to start Apache; only called when it isn't running.</param>
    /// <returns>A task that completes when the browser is opened or the question is answered with no.</returns>
    public async Task OpenPhpMyAdminAsync(Func<Task<bool>> confirmStartApache)
    {
        if (!await _main.OpenPhpMyAdminAsync(null, confirmStartApache))
        {
            Status = Loc.T("Databases.ApacheDidntStart");
        }
    }

    /// <summary>Opens a program's web port in the configured browser.</summary>
    /// <param name="program">A program with a port that speaks HTTP.</param>
    public void OpenInBrowser(NetstatProgram program)
    {
        if (program.HasUrl)
        {
            Shell.OpenUrl(program.Url, _main.Settings.Browser);
        }
    }

    /// <summary>First click asks for confirmation in the status line; a second click on the same program ends it.</summary>
    /// <param name="program">The program to end; XAMPP's own and Windows's are refused.</param>
    /// <returns>A task that completes after the process is ended and the list refreshed, or at once after the first click.</returns>
    public async Task EndProcessAsync(NetstatProgram program)
    {
        if (!program.CanEnd)
        {
            return;
        }
        if (_pendingEndPid != program.Pid)
        {
            _pendingEndPid = program.Pid;
            Status = Loc.T("Netstat.ClickEndAgainTo", program.ExeName, program.Pid);
            return;
        }

        _pendingEndPid = null;
        Status = Loc.T("Netstat.EndingProcess", program.ExeName, program.Pid);
        await Task.Run(() => ProcessScan.KillTree(program.Pid));
        if (ProcessScan.NameOf(program.Pid) is not null)
        {
            // Services and programs of other users need admin rights to end.
            int exitCode = await ElevatedHelper.RunAsync("taskkill.exe", $"/PID {program.Pid} /T /F", Environment.SystemDirectory);
            if (exitCode != 0)
            {
                Status = exitCode == -2
                    ? Loc.T("Netstat.CanceledAtTheAdminPrompt", program.ExeName)
                    : Loc.T("Netstat.CouldntBeEnded", program.ExeName, exitCode);
                _main.Write(LogTag, Status, LogLevel.Warn);
                return;
            }
        }

        string ended = Loc.T("Netstat.EndedProcess", program.ExeName, program.Pid);
        _main.Write(LogTag, $"Ended {program.ExeName} (PID {program.Pid}) from the Netstat window.", LogLevel.Warn);
        await RefreshAsync();
        Status = ended;
    }

    /// <summary>Reads the listening sockets on a background thread and groups them into programs.</summary>
    /// <returns>A task that completes after the list is brought up to date; at once when a refresh is already running.</returns>
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
            Dictionary<int, (string Name, string Path)> previousDescriptions = _previousDescriptions;
            bool isFirstRefresh = _isFirstRefresh;

            // Ports a module labels as its own that no browser can open: Mailpit's SMTP, Tomcat's control ports.
            ServiceSettings services = _main.Settings.Services;
            HashSet<int> nonWebPorts = [services.MailpitSmtpPort, services.TomcatShutdownPort, services.TomcatAjpPort];
            Dictionary<int, string> modulePorts = ModulePorts();
            HashSet<int> stoppedModulePorts = StoppedModulePorts();
            (List<NetstatProgram> programs, Dictionary<string, Listener> currentListeners,
                Dictionary<int, (string Name, string Path)> descriptions) = await Task.Run(() =>
            {
                (List<NetstatRow> rows, Dictionary<string, Listener> current, Dictionary<int, (string Name, string Path)> described) =
                    BuildRows(previousListeners, previousDescriptions, isFirstRefresh, modulePorts, stoppedModulePorts, nonWebPorts);
                return (GroupByProgram(rows), current, described);
            });
            _previousDescriptions = descriptions;
            _previousListeners = currentListeners;
            _isFirstRefresh = false;
            _allPrograms = programs;
            ApplyView();
        }
        catch (Exception ex)
        {
            // Called from a timer, where an exception would end the panel; the next round tries again.
            Status = ex.Message;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>One entry per process, each with a name people know and whether it is Windows' own.</summary>
    /// <param name="rows">The sockets of this refresh.</param>
    /// <returns>The programs, in no particular order.</returns>
    private static List<NetstatProgram> GroupByProgram(List<NetstatRow> rows)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programs = new List<NetstatProgram>();
        foreach (IGrouping<int, NetstatRow> group in rows.GroupBy(row => row.Pid))
        {
            NetstatRow first = group.First();
            bool isSystem = first.Pid <= 4
                || first.Path.Length == 0
                || (windows.Length > 0 && first.Path.StartsWith(windows, StringComparison.OrdinalIgnoreCase));
            programs.Add(new NetstatProgram(group.ToList(), TitleOf(first), isSystem));
        }
        return programs;
    }

    /// <summary>The name a program is listed under.</summary>
    /// <param name="row">Any socket of the program.</param>
    /// <returns>
    /// A known name ("Apache", "Node.js"), else the description inside the executable ("Docker Desktop Backend"),
    /// else the file name without ".exe".
    /// </returns>
    private static string TitleOf(NetstatRow row)
    {
        if (_knownPrograms.TryGetValue(row.Name, out string? known))
        {
            return known;
        }

        if (row.Path.Length > 0 && !MockMachine.IsActive)
        {
            try
            {
                if (FileVersionInfo.GetVersionInfo(row.Path).FileDescription is { } description
                    && description.Trim().Length > 0)
                {
                    return description.Trim();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // The file is gone or not readable for this user; the file name is the fallback.
            }
        }

        return row.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? row.Name[..^4] : row.Name;
    }

    /// <summary>
    /// Fills <see cref="Programs"/> from the last refresh with the search, the scope and the Windows toggle
    /// applied. When what would be shown is the same as what is shown, nothing is touched, so the list does not
    /// flicker or lose its place every three seconds.
    /// </summary>
    /// <param name="isForced">True rebuilds the list even when nothing changed, after a language change.</param>
    private void ApplyView(bool isForced = false)
    {
        string filter = Filter.Trim();
        _systemCount = _allPrograms.Count(program => program.IsSystem && !program.IsOld);
        OnPropertyChanged(nameof(SystemLabel));

        List<NetstatProgram> shown = _allPrograms
            .Where(program => program.Matches(filter))
            .Where(program => Scope switch
            {
                NetstatScope.Xampp => program.IsXampp,
                NetstatScope.Blocking => program.IsBlocking,
                _ => true,
            })
            // Windows' own stay out unless asked for, searched for, or in the way of a module.
            .Where(program => !program.IsSystem || ShowSystem || program.IsBlocking || filter.Length > 0)
            .OrderBy(program => program.IsXampp ? 0 : program.IsBlocking ? 1 : program.IsSystem ? 3 : 2)
            .ThenBy(program => program.IsXampp ? program.Sockets.Min(socket => socket.Port) : 0)
            .ThenBy(program => program.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(program => program.Pid)
            .ToList();

        bool isSame = !isForced
            && shown.Count == Programs.Count
            && shown.Zip(Programs, (next, current) => next.Signature == current.Signature).All(same => same);
        if (!isSame)
        {
            _isSyncing = true;
            try
            {
                Programs.Clear();
                foreach (NetstatProgram program in shown)
                {
                    Programs.Add(program);
                }

                // The same program stays selected across refreshes; without one, the first entry is.
                Selected = shown.FirstOrDefault(program => program.Pid == _selectedPid) ?? shown.FirstOrDefault();
            }
            finally
            {
                _isSyncing = false;
            }
            if (Selected?.Pid != _selectedPid)
            {
                // The selection moved by itself (its program closed or was filtered out): a pending "click again
                // to end" was about the other program.
                _pendingEndPid = null;
                Status = "";
            }
            _selectedPid = Selected?.Pid;
        }
        OnPropertyChanged(nameof(IsEmpty));

        int sockets = shown.Sum(program => program.Sockets.Count(socket => !socket.IsOld));
        Summary = Loc.T("Netstat.SummaryPrograms", Loc.Plural("Netstat.Programs", shown.Count), Loc.Plural("Netstat.Sockets", sockets), DateTime.Now);
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
    /// <param name="previousDescriptions">Name and path per PID from the previous refresh, for processes that are gone.</param>
    /// <param name="isFirstRefresh">True marks every socket as active, since there is nothing to compare with.</param>
    /// <param name="modulePorts">Port → module name, for the service label.</param>
    /// <param name="stoppedModulePorts">Ports of stopped modules, for the "blocks" warning.</param>
    /// <param name="nonWebPorts">Module ports no browser can open.</param>
    /// <returns>
    /// The rows, current sockets plus those gone since <paramref name="previous"/>, unsorted; and the current
    /// sockets by key, for the next refresh.
    /// </returns>
    private (List<NetstatRow> Rows, Dictionary<string, Listener> Current, Dictionary<int, (string Name, string Path)> Described) BuildRows(
        Dictionary<string, Listener> previous,
        Dictionary<int, (string Name, string Path)> previousDescriptions,
        bool isFirstRefresh,
        Dictionary<int, string> modulePorts,
        HashSet<int> stoppedModulePorts,
        HashSet<int> nonWebPorts)
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
            // A process that is gone can no longer be asked for its name; the previous refresh knew it.
            (string name, string path) = state == SocketState.Old
                && previousDescriptions.TryGetValue(listener.Pid, out (string Name, string Path) before)
                    ? before
                    : DescribeProcess(listener.Pid);
            bool isXampp = IsXampp(path);
            string service = modulePorts.TryGetValue(listener.Port, out string? module)
                ? module
                : WellKnownService(listener.Port);
            bool blocks = !isXampp && state != SocketState.Old && stoppedModulePorts.Contains(listener.Port)
                && modulePorts.TryGetValue(listener.Port, out _);
            bool isWeb = !nonWebPorts.Contains(listener.Port)
                && service is "HTTP" or "HTTPS" or "HTTP alt" or "Apache" or "Tomcat" or "Mailpit";
            return new NetstatRow(listener, name, path, isXampp, state, service, blocks ? service : "", isWeb);
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
        return (rows, current, describedPids);
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

    /// <summary>What a port is normally used for when no module claims it.</summary>
    /// <param name="port">The TCP port.</param>
    /// <returns>
    /// A protocol or product name, the same in every language since some are compared (see
    /// <see cref="NetstatRow.IsWeb"/>); the two descriptive ones in the panel's language; empty when unknown.
    /// </returns>
    private static string WellKnownService(int port) => port switch
    {
        3000 => Loc.T("Netstat.NodeDev"),
        3389 => Loc.T("Netstat.RemoteDesktop"),
        _ => _wellKnownPorts.GetValueOrDefault(port, ""),
    };

    /// <summary>Whether an executable lies inside the XAMPP folder.</summary>
    /// <param name="path">Full path of the executable; empty never counts.</param>
    /// <returns>True when the path starts with the XAMPP folder, ignoring case.</returns>
    private bool IsXampp(string path) =>
        path.Length > 0 && path.StartsWith(_main.Root, StringComparison.OrdinalIgnoreCase);

    private static string KeyOf(Listener listener) => $"{listener.Address}|{listener.Port}|{listener.Pid}";

    /// <summary>Stops the refresh timer and stops following language changes.</summary>
    public void Dispose()
    {
        Loc.Changed -= OnLanguageChanged;
        _refreshTimer.Stop();
    }
}
