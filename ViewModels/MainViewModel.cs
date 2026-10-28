using System.Collections.ObjectModel;
using Avalonia.Threading;
using XamppControl.Core;
using XamppControl.Modules;

namespace XamppControl.ViewModels;

public sealed class MainViewModel : ObservableObject, IModuleHost, IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly object _logFileLock = new();
    private bool _refreshing;
    private string _root = "";
    private string _subtitle = "";
    private int _logFontSize;

    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        _logFontSize = settings.LogFontSize;
        RelayCommand.UnhandledError += ex => Write("main", ex.Message, LogLevel.Error);

        ShellCommand = new RelayCommand(OpenShell);
        ExplorerCommand = new RelayCommand(() => Shell.Open(Root));
        ServicesCommand = new RelayCommand(() => Shell.Open("services.msc"));
        HelpCommand = new RelayCommand(
            () => Shell.OpenUrl("https://www.apachefriends.org/faq_windows.html", Settings.Browser));
        ClearLogCommand = new RelayCommand(() => Log.Clear());

        LoadModules();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _timer.Tick += async (_, _) => await RefreshAsync(quiet: false);

        _ = StartupAsync();
    }

    public AppSettings Settings { get; }

    public ObservableCollection<ModuleViewModel> Modules { get; } = [];
    public ObservableCollection<LogEntry> Log { get; } = [];

    public string Root
    {
        get => _root;
        private set => SetField(ref _root, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => SetField(ref _subtitle, value);
    }

    public int LogFontSize
    {
        get => _logFontSize;
        private set => SetField(ref _logFontSize, value);
    }

    public string Version => BuildInfo.Version;

    /// <summary>True when the current folder contains apache or mysql.</summary>
    public bool HasXampp => XamppPaths.IsXampp(Root);

    public RelayCommand ShellCommand { get; }
    public RelayCommand ExplorerCommand { get; }
    public RelayCommand ServicesCommand { get; }
    public RelayCommand HelpCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    // ── startup / settings ───────────────────────────────────

    private void LoadModules()
    {
        Root = XamppPaths.Resolve(Settings);

        Modules.Clear();
        foreach (var driver in ModuleCatalog.Create(Root, Settings))
            Modules.Add(new ModuleViewModel(driver, this));

        var php = XamppPaths.FileVersion(Path.Combine(Root, "php", "php.exe"));
        Subtitle = php is null ? Root : $"PHP {php} · {Root}";
        OnPropertyChanged(nameof(HasXampp));
    }

    private async Task StartupAsync()
    {
        Write("main", "Initializing Control Panel");
        Write("main", $"Control Panel Version: {BuildInfo.Version}  [ Compiled: {BuildInfo.CompiledOn} ]");
        Debug("main", $"Settings file: \"{Path.Combine(AppSettings.Folder, "settings.json")}\"");

        if (!XamppPaths.IsXampp(Root))
        {
            Write("main",
                $"No XAMPP installation found at \"{Root}\". Click \"Change…\" in the header or set it under Config.",
                LogLevel.Error);
        }
        else
        {
            Write("main", $"XAMPP Installation Directory: \"{Root}\\\"");
            Debug("main", "Checking for module existence");
            foreach (var module in Modules)
                Debug("main", $"{module.Name}: {(module.IsInstalled ? "found" : "not installed")}");
        }

        await RefreshAsync(quiet: true);

        Debug("main", "Checking for Windows services");
        foreach (var module in Modules)
        {
            await module.RefreshServiceStateAsync();
            if (module.IsServiceInstalled)
                Write(module.Driver.Tag, $"{module.Name} Service detected");
        }

        foreach (var module in Modules.Where(m => m.IsRunning))
            Write(module.Driver.Tag, $"{module.Name} already running{ModuleDriver.PidText(PidList(module))}");

        if (Settings.CheckPortsOnStartup)
            await CheckDefaultPortsAsync();

        _timer.Start();

        foreach (var name in ModuleCatalog.Names.Where(Settings.IsAutoStart))
        {
            if (Find(name) is { } module)
            {
                Write("main", $"Autostart active: starting {name}");
                await module.StartIfStoppedAsync();
            }
        }

        Write("main", "Control Panel Ready");
    }

    private static List<int> PidList(ModuleViewModel module) =>
        module.Pids.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p, out var n) ? n : 0)
            .Where(n => n > 0)
            .ToList();

    /// <summary>Called after the Config dialog is saved with its edited copy of the settings.</summary>
    public async Task ApplySettingsAsync(AppSettings edited)
    {
        Settings.CopyFrom(edited);
        Settings.Save();
        LogFontSize = Settings.LogFontSize;
        TrimLog();

        // Rebuild modules so new paths, ports, service names and user-defined files take effect.
        LoadModules();
        await RefreshAsync(quiet: true);
        foreach (var module in Modules)
            await module.RefreshServiceStateAsync();

        Write("main", "Configuration saved", LogLevel.Ok);
    }

    /// <summary>Switches to another XAMPP installation and saves it as the configured folder.</summary>
    public async Task ChangeRootAsync(string path)
    {
        var edited = Settings.Clone();
        edited.XamppRoot = path.TrimEnd('\\', '/');
        await ApplySettingsAsync(edited);
        Write("main", $"XAMPP Installation Directory: \"{Root}\\\"");
    }

    /// <summary>Re-reads modules and versions from disk, e.g. after PHP or MariaDB was updated.</summary>
    public async Task ReloadModulesAsync()
    {
        LoadModules();
        await RefreshAsync(quiet: true);
        foreach (var module in Modules)
            await module.RefreshServiceStateAsync();
    }

    public ModuleViewModel? FindModule(string name) => Find(name);

    private ModuleViewModel? Find(string name) =>
        Modules.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    // ── ports ────────────────────────────────────────────────

    /// <summary>Like XAMPP's "Check default ports on startup": warns about ports taken by other programs.</summary>
    private async Task CheckDefaultPortsAsync()
    {
        Debug("main", "Checking default ports");
        var listeners = await Task.Run(PortTable.ListAll);
        foreach (var module in Modules.Where(m => m.IsInstalled && !m.IsRunning))
            ReportBlockedPorts(module, listeners);
    }

    /// <summary>Before starting a module: false (with a log explanation) if another program holds its ports.</summary>
    public async Task<bool> PortsAreFreeAsync(ModuleViewModel module)
    {
        var listeners = await Task.Run(PortTable.ListAll);
        return !ReportBlockedPorts(module, listeners);
    }

    private bool ReportBlockedPorts(ModuleViewModel module, IReadOnlyList<Listener> listeners)
    {
        var blocked = false;
        foreach (var port in module.Driver.ConfiguredPorts.Distinct())
        {
            if (PortTable.OwnerOf(port, listeners) is not { } pid)
                continue;

            var path = ProcessScan.PathOf(pid);
            var ownedByXampp = path is not null && path.StartsWith(Root, StringComparison.OrdinalIgnoreCase);
            if (ownedByXampp)
                continue;

            if (!blocked)
                Write(module.Driver.Tag, "Problem detected!", LogLevel.Error);
            blocked = true;

            var owner = path ?? ProcessScan.NameOf(pid) ?? (pid == 4 ? "System" : "unknown process");
            Write(module.Driver.Tag, $"Port {port} in use by \"{owner}\" with PID {pid}!", LogLevel.Error);
        }

        if (blocked)
        {
            var tag = module.Driver.Tag;
            Write(tag, $"{module.Name} WILL NOT start without the configured ports free!", LogLevel.Error);
            Write(tag, "You need to uninstall/disable/reconfigure the blocking application", LogLevel.Error);
            Write(tag, $"or reconfigure {module.Name} and the Control Panel to listen on a different port",
                LogLevel.Error);
        }
        return blocked;
    }

    // ── status polling ───────────────────────────────────────

    public async Task RefreshAsync(bool quiet)
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            var modules = Modules.ToList();
            var results = await Task.Run(() =>
            {
                var listening = PortTable.ListeningByPid();
                return modules
                    .Select(m =>
                    {
                        var pids = m.IsInstalled ? m.Driver.DetectPids(listening) : Array.Empty<int>();
                        var ports = pids
                            .SelectMany(pid => listening.TryGetValue(pid, out var p) ? p : Enumerable.Empty<int>())
                            .Distinct()
                            .Order()
                            .Take(4);
                        return (Module: m, Pids: pids, Ports: string.Join(", ", ports));
                    })
                    .ToList();
            });

            foreach (var (module, pids, ports) in results)
                module.Apply(pids, ports, quiet);
        }
        catch (Exception ex)
        {
            Write("main", "Status check failed: " + ex.Message, LogLevel.Error);
        }
        finally
        {
            _refreshing = false;
        }
    }

    // ── IModuleHost / log ────────────────────────────────────

    string IModuleHost.Root => Root;

    public void Write(string source, string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry(source, message, level);

        if (Settings.LogToFile)
            AppendToFile(entry);

        void Add()
        {
            Log.Add(entry);
            TrimLog();
        }

        if (Dispatcher.UIThread.CheckAccess())
            Add();
        else
            Dispatcher.UIThread.Post(Add);
    }

    public void Debug(string source, string message)
    {
        if (Settings.ShowDebug)
            Write(source, message, LogLevel.Debug);
    }

    public void TailLog(string source, string path, int lines)
    {
        var tail = Shell.Tail(path, lines);
        if (tail.Count == 0)
        {
            Write(source, $"  (no entries in {Path.GetFileName(path)})", LogLevel.Warn);
            return;
        }
        foreach (var line in tail)
            Write(source, "  › " + line, LogLevel.Error);
    }

    private void TrimLog()
    {
        while (Log.Count > Settings.MaxLogLines)
            Log.RemoveAt(0);
    }

    private void AppendToFile(LogEntry entry)
    {
        try
        {
            lock (_logFileLock)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                File.AppendAllText(AppSettings.LogFilePath,
                    $"{DateTime.Now:yyyy-MM-dd} {entry}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging to file is best effort.
        }
    }

    // ── side buttons ─────────────────────────────────────────

    private void OpenShell()
    {
        var shellBat = Path.Combine(Root, "xampp_shell.bat");
        var args = File.Exists(shellBat) ? $"/k \"{shellBat}\"" : $"/k cd /d \"{Root}\"";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", args)
        {
            UseShellExecute = true,
            WorkingDirectory = Directory.Exists(Root) ? Root : Environment.CurrentDirectory,
        })?.Dispose();
    }

    public void Dispose() => _timer.Stop();
}
