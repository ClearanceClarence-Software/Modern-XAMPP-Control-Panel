using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.Updates;

namespace XamppControl.ViewModels;

/// <summary>The main window: module rows, log pane, side buttons and the status polling behind them.</summary>
public sealed class MainViewModel : ObservableObject, IModuleHost, IDisposable
{
    private readonly DispatcherTimer _pollTimer;
    private readonly object _logFileLock = new();
    private bool _isRefreshing;
    private string _root = "";
    private string _subtitle = "";
    private int _logFontSize;
    private AppRelease? _appUpdate;
    private string _appUpdateNotes = "";

    /// <summary>Loads the modules and runs the startup sequence in the background, which then starts polling.</summary>
    /// <param name="settings">The loaded settings; kept and updated in place when the Config dialog saves.</param>
    public MainViewModel(AppSettings settings)
    {
        Settings = settings;
        _logFontSize = settings.LogFontSize;
        RelayCommand.UnhandledError += exception => Write("main", exception.Message, LogLevel.Error);

        ShellCommand = new RelayCommand(OpenShell);
        ExplorerCommand = new RelayCommand(() => Shell.Open(Root));
        ServicesCommand = new RelayCommand(() => Shell.Open("services.msc"));
        HelpCommand = new RelayCommand(
            () => Shell.OpenUrl("https://www.apachefriends.org/faq_windows.html", Settings.Browser));
        ClearLogCommand = new RelayCommand(() => Log.Clear());
        OpenAppUpdateCommand = new RelayCommand(
            () => Shell.OpenUrl(AppUpdate?.Url ?? AppUpdateCheck.ReleasesUrl, Settings.Browser));

        LoadModules();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _pollTimer.Tick += async (_, _) => await RefreshAsync(quiet: false);

        _ = StartupAsync();
    }

    /// <summary>The panel's settings; the same instance for the app's lifetime, updated in place on save.</summary>
    public AppSettings Settings { get; }

    /// <summary>One row per known module, in catalog order; rebuilt when the folder or the settings change.</summary>
    public ObservableCollection<ModuleViewModel> Modules { get; } = [];

    /// <summary>The log pane's entries, oldest first, trimmed to <see cref="AppSettings.MaxLogLines"/>.</summary>
    public ObservableCollection<LogEntry> Log { get; } = [];

    /// <summary>The XAMPP folder from the settings; it may hold no XAMPP (see <see cref="HasXampp"/>).</summary>
    public string Root
    {
        get => _root;
        private set => SetField(ref _root, value);
    }

    /// <summary>The header's second line: "PHP version · folder", or the folder alone when php.exe has none.</summary>
    public string Subtitle
    {
        get => _subtitle;
        private set => SetField(ref _subtitle, value);
    }

    /// <summary>The log pane's font size in points, as last saved in the Config dialog.</summary>
    public int LogFontSize
    {
        get => _logFontSize;
        private set => SetField(ref _logFontSize, value);
    }

    /// <summary>The panel's own version, shown in the header.</summary>
    public string Version => BuildInfo.Version;

    /// <summary>A newer release of the panel found at startup; null when none was found or checking is off.</summary>
    public AppRelease? AppUpdate
    {
        get => _appUpdate;
        private set
        {
            if (SetField(ref _appUpdate, value))
            {
                OnPropertyChanged(nameof(HasAppUpdate));
                OnPropertyChanged(nameof(AppUpdateText));
            }
        }
    }

    /// <summary>True when <see cref="AppUpdate"/> is set; shows the header's update link.</summary>
    public bool HasAppUpdate => AppUpdate is not null;

    /// <summary>The header's update link, e.g. "v0.2.1 is available"; empty without an update.</summary>
    public string AppUpdateText => AppUpdate is { } update ? $"{update.Version} is available" : "";

    /// <summary>The line under the What's new title, naming the running version.</summary>
    public string AppUpdateSubtitle => $"You have {BuildInfo.Version}. Here's what changed since then.";

    /// <summary>
    /// What changed since the running version: the notes of every newer release, newest first, each under its
    /// version; empty without an update.
    /// </summary>
    public string AppUpdateNotes
    {
        get => _appUpdateNotes;
        private set => SetField(ref _appUpdateNotes, value);
    }

    /// <summary>Opens the release page of <see cref="AppUpdate"/>, or the releases list when there is none.</summary>
    public RelayCommand OpenAppUpdateCommand { get; }

    /// <summary>True when the current folder contains apache or mysql.</summary>
    public bool HasXampp => XamppPaths.IsXampp(Root);

    /// <summary>Opens a command prompt in the XAMPP folder, through xampp_shell.bat when there is one.</summary>
    public RelayCommand ShellCommand { get; }

    /// <summary>Opens the XAMPP folder in Explorer.</summary>
    public RelayCommand ExplorerCommand { get; }

    /// <summary>Opens the Windows Services console.</summary>
    public RelayCommand ServicesCommand { get; }

    /// <summary>Opens the Apache Friends Windows FAQ in the configured browser.</summary>
    public RelayCommand HelpCommand { get; }

    /// <summary>Empties the log pane; the log file, when enabled, keeps its lines.</summary>
    public RelayCommand ClearLogCommand { get; }

    // ── startup / settings ───────────────────────────────────

    /// <summary>Resolves the XAMPP folder and rebuilds <see cref="Modules"/> and the header from it.</summary>
    private void LoadModules()
    {
        Root = XamppPaths.Resolve(Settings);

        Modules.Clear();
        foreach (ModuleDriver driver in ModuleCatalog.Create(Root, Settings))
        {
            Modules.Add(new ModuleViewModel(driver, this));
        }

        string? phpVersion = XamppPaths.FileVersion(Path.Combine(Root, "php", "php.exe"));
        Subtitle = phpVersion is null ? Root : $"PHP {phpVersion} · {Root}";
        OnPropertyChanged(nameof(HasXampp));
    }

    /// <summary>
    /// Logs the startup lines the original XAMPP panel writes, checks services and ports, starts polling and
    /// then starts the autostart modules.
    /// </summary>
    /// <returns>A task that completes after "Control Panel Ready" is logged.</returns>
    private async Task StartupAsync()
    {
        Write("main", "Initializing Control Panel");
        Write("main", $"Control Panel Version: {BuildInfo.Version}  [ Compiled: {BuildInfo.CompiledOn} ]");
        Debug("main", $"Settings file: \"{Path.Combine(AppSettings.Folder, "settings.json")}\"");
        LogInstallation();

        await RefreshAsync(quiet: true);

        await LogInstalledServicesAsync();
        LogAlreadyRunning();

        if (Settings.CheckPortsOnStartup)
        {
            await CheckDefaultPortsAsync();
        }

        _pollTimer.Start();

        await AutoStartModulesAsync();

        Write("main", "Control Panel Ready");

        if (Settings.CheckForAppUpdates)
        {
            await CheckForAppUpdateAsync();
        }
    }

    /// <summary>Looks for a newer release of the panel on GitHub and logs it when there is one.</summary>
    /// <returns>A task that completes when the check has answered, failed or timed out.</returns>
    private async Task CheckForAppUpdateAsync()
    {
        List<AppRelease> newerReleases;
        try
        {
            newerReleases = await AppUpdateCheck.FindNewerAsync(BuildInfo.Version);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            // Being offline is normal for a local server panel; the failure is only shown with debug output on.
            Debug("main", "Update check failed: " + ex.Message);
            return;
        }

        if (newerReleases.Count == 0)
        {
            return;
        }

        AppUpdateNotes = string.Join("\n\n", newerReleases.Select(release =>
            release.Notes.Length > 0 ? $"{release.Version}\n{release.Notes}" : release.Version));
        AppUpdate = newerReleases[0];
        Write("update",
            $"{BuildInfo.AppName} {AppUpdate.Version} is available (you have {BuildInfo.Version}). " +
            "Click the link in the header to see what's new.",
            LogLevel.Ok);
    }

    /// <summary>Logs the XAMPP folder and which modules it has, or an error when the folder holds no XAMPP.</summary>
    private void LogInstallation()
    {
        if (!XamppPaths.IsXampp(Root))
        {
            Write("main",
                $"No XAMPP installation found at \"{Root}\". Click \"Change…\" in the header or set it under Config.",
                LogLevel.Error);
            return;
        }

        Write("main", $"XAMPP Installation Directory: \"{Root}\\\"");
        Debug("main", "Checking for module existence");
        foreach (ModuleViewModel module in Modules)
        {
            Debug("main", $"{module.Name}: {(module.IsInstalled ? "found" : "not installed")}");
        }
    }

    /// <summary>Reads each module's service state and logs the modules installed as a Windows service.</summary>
    /// <returns>A task that completes once every module's service state is known.</returns>
    private async Task LogInstalledServicesAsync()
    {
        Debug("main", "Checking for Windows services");
        foreach (ModuleViewModel module in Modules)
        {
            await module.RefreshServiceStateAsync();
            if (module.IsServiceInstalled)
            {
                Write(module.Driver.Tag, $"{module.Name} Service detected");
            }
        }
    }

    /// <summary>Logs the modules that were already running when the panel started, with their PIDs.</summary>
    private void LogAlreadyRunning()
    {
        foreach (ModuleViewModel module in Modules.Where(candidate => candidate.IsRunning))
        {
            Write(module.Driver.Tag, $"{module.Name} already running{ModuleDriver.PidText(PidList(module))}");
        }
    }

    /// <summary>Starts every installed, stopped module that has autostart switched on, one after another.</summary>
    /// <returns>A task that completes when the last autostart module has been started.</returns>
    private async Task AutoStartModulesAsync()
    {
        foreach (string name in ModuleCatalog.Names.Where(Settings.IsAutoStart))
        {
            if (Find(name) is not { } module)
            {
                continue;
            }

            if (!module.IsInstalled)
            {
                Write("main", $"Autostart is on for {name}, but {name} isn't installed; skipping it.", LogLevel.Warn);
                continue;
            }

            Write("main", $"Autostart active: starting {name}");
            await module.StartIfStoppedAsync();
        }
    }

    /// <summary>Reads the PIDs back from a module's PID column.</summary>
    /// <param name="module">The module whose <see cref="ModuleViewModel.Pids"/> text is read.</param>
    /// <returns>The PIDs shown: one or two, or only the first of a longer list ("+n more" is skipped).</returns>
    private static List<int> PidList(ModuleViewModel module) =>
        module.Pids.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => int.TryParse(line, out int pid) ? pid : 0)
            .Where(pid => pid > 0)
            .ToList();

    /// <summary>Saves the settings edited in the Config dialog and reloads the modules so they take effect.</summary>
    /// <param name="edited">The dialog's edited copy; its values are copied into <see cref="Settings"/>.</param>
    /// <returns>A task that completes after the modules are reloaded and "Configuration saved" is logged.</returns>
    public async Task ApplySettingsAsync(AppSettings edited)
    {
        Settings.CopyFrom(edited);
        Settings.Save();
        AppTheme.Apply(Settings.Theme);
        LogFontSize = Settings.LogFontSize;
        TrimLog();

        // Rebuild modules so new paths, ports, service names and user-defined files take effect.
        await ReloadModulesAsync();

        Write("main", "Configuration saved", LogLevel.Ok);
    }

    /// <summary>Applies a theme right away and saves it, for the header's sun/moon button.</summary>
    /// <param name="theme">One of the <see cref="AppTheme"/> names.</param>
    public void SetTheme(string theme)
    {
        Settings.Theme = AppTheme.Normalize(theme);
        Settings.Save();
        AppTheme.Apply(Settings.Theme);
    }

    /// <summary>Switches to another XAMPP installation and saves it as the configured folder.</summary>
    /// <param name="path">The new XAMPP folder; trailing slashes and backslashes are removed before saving.</param>
    /// <returns>A task that completes after the settings are applied and the new folder is logged.</returns>
    public async Task ChangeRootAsync(string path)
    {
        AppSettings edited = Settings.Clone();
        edited.XamppRoot = path.TrimEnd('\\', '/');
        await ApplySettingsAsync(edited);
        Write("main", $"XAMPP Installation Directory: \"{Root}\\\"");
    }

    /// <summary>Re-reads modules and versions from disk, e.g. after PHP or MariaDB was updated.</summary>
    /// <returns>A task that completes once the new rows have their running and service states.</returns>
    public async Task ReloadModulesAsync()
    {
        LoadModules();
        await RefreshAsync(quiet: true);
        foreach (ModuleViewModel module in Modules)
        {
            await module.RefreshServiceStateAsync();
        }
    }

    /// <summary>Finds a module row by its display name.</summary>
    /// <param name="name">The display name, such as Apache or MySQL; compared ignoring case.</param>
    /// <returns>The row, or null when no module has that name.</returns>
    public ModuleViewModel? FindModule(string name) => Find(name);

    private ModuleViewModel? Find(string name) =>
        Modules.FirstOrDefault(module => string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase));

    // ── ports ────────────────────────────────────────────────

    /// <summary>Like XAMPP's "Check default ports on startup": warns about ports taken by other programs.</summary>
    /// <returns>A task that completes after every installed, stopped module has been checked.</returns>
    private async Task CheckDefaultPortsAsync()
    {
        Debug("main", "Checking default ports");
        List<Listener> listeners = await Task.Run(PortTable.ListAll);
        foreach (ModuleViewModel module in Modules.Where(candidate => candidate.IsInstalled && !candidate.IsRunning))
        {
            ReportBlockedPorts(module, listeners);
        }
    }

    /// <summary>Before starting a module: false (with a log explanation) if another program holds its ports.</summary>
    /// <param name="module">The module about to start; ports held by programs inside the XAMPP folder are fine.</param>
    /// <returns>True when no program outside the XAMPP folder holds any of the module's configured ports.</returns>
    public async Task<bool> PortsAreFreeAsync(ModuleViewModel module)
    {
        List<Listener> listeners = await Task.Run(PortTable.ListAll);
        return !ReportBlockedPorts(module, listeners);
    }

    /// <summary>Logs, in XAMPP's wording, each configured port that a program outside XAMPP holds.</summary>
    /// <param name="module">The module whose configured ports are checked.</param>
    /// <param name="listeners">The listening sockets to check against, read once by the caller.</param>
    /// <returns>True when at least one port is blocked, after the problem has been logged.</returns>
    private bool ReportBlockedPorts(ModuleViewModel module, IReadOnlyList<Listener> listeners)
    {
        string tag = module.Driver.Tag;
        bool isAnyPortBlocked = false;
        foreach (int port in module.Driver.ConfiguredPorts.Distinct())
        {
            if (PortTable.OwnerOf(port, listeners) is not { } pid)
            {
                continue;
            }

            string? ownerPath = ProcessScan.PathOf(pid);
            bool isOwnedByXampp = ownerPath is not null
                && ownerPath.StartsWith(Root, StringComparison.OrdinalIgnoreCase);
            if (isOwnedByXampp)
            {
                continue;
            }

            if (!isAnyPortBlocked)
            {
                Write(tag, "Problem detected!", LogLevel.Error);
            }
            isAnyPortBlocked = true;

            // PID 4 is the Windows kernel, which has no executable path or process name to show.
            string ownerName = ownerPath ?? ProcessScan.NameOf(pid) ?? (pid == 4 ? "System" : "unknown process");
            Write(tag, $"Port {port} in use by \"{ownerName}\" with PID {pid}!", LogLevel.Error);
        }

        if (isAnyPortBlocked)
        {
            Write(tag, $"{module.Name} WILL NOT start without the configured ports free!", LogLevel.Error);
            Write(tag, "You need to uninstall/disable/reconfigure the blocking application", LogLevel.Error);
            Write(tag, $"or reconfigure {module.Name} and the Control Panel to listen on a different port",
                LogLevel.Error);
        }
        return isAnyPortBlocked;
    }

    // ── status polling ───────────────────────────────────────

    /// <summary>Detects which modules run and on which ports, and updates their rows.</summary>
    /// <param name="quiet">True updates the rows without logging status changes; false logs each change.</param>
    /// <returns>A task that completes after the rows are updated; at once when a refresh is already running.</returns>
    public async Task RefreshAsync(bool quiet)
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            // A copy, because LoadModules may rebuild the collection on the UI thread while detection runs.
            var modules = Modules.ToList();
            List<(ModuleViewModel Module, IReadOnlyList<int> Pids, string Ports)> statuses =
                await Task.Run(() => DetectStatuses(modules));

            foreach ((ModuleViewModel module, IReadOnlyList<int> pids, string ports) in statuses)
            {
                module.Apply(pids, ports, quiet);
            }
        }
        catch (Exception ex)
        {
            // Detection reads the live process list and TCP tables, where anything may fail; the error is logged
            // and the next timer tick tries again, so the panel keeps running.
            Write("main", "Status check failed: " + ex.Message, LogLevel.Error);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>Detects every module's processes and listening ports; runs on a background thread.</summary>
    /// <param name="modules">The rows to check, in the order the results are returned.</param>
    /// <returns>One entry per module with its PIDs and its ports as display text.</returns>
    private static List<(ModuleViewModel Module, IReadOnlyList<int> Pids, string Ports)> DetectStatuses(
        IReadOnlyList<ModuleViewModel> modules)
    {
        Dictionary<int, SortedSet<int>> listening = PortTable.ListeningByPid();
        return modules
            .Select(module => DetectStatus(module, listening))
            .ToList();
    }

    /// <summary>Detects one module's processes and the ports they listen on.</summary>
    /// <param name="module">The row to check; a module that is not installed gets no PIDs.</param>
    /// <param name="listening">The listening ports per PID, read once for all modules.</param>
    /// <returns>
    /// The module, its PIDs, and its lowest four distinct ports joined with ", " (empty when it listens on none).
    /// </returns>
    private static (ModuleViewModel Module, IReadOnlyList<int> Pids, string Ports) DetectStatus(
        ModuleViewModel module,
        Dictionary<int, SortedSet<int>> listening)
    {
        IReadOnlyList<int> pids = module.IsInstalled ? module.Driver.DetectPids(listening) : Array.Empty<int>();
        IEnumerable<int> ports = pids
            .SelectMany(pid => listening.TryGetValue(pid, out SortedSet<int>? pidPorts)
                ? pidPorts
                : Enumerable.Empty<int>())
            .Distinct()
            .Order()
            .Take(4);
        return (module, pids, string.Join(", ", ports));
    }

    // ── IModuleHost / log ────────────────────────────────────

    string IModuleHost.Root => Root;

    /// <summary>Adds a line to the log pane and, when enabled, to the log file; safe to call from any thread.</summary>
    /// <param name="source">The tag shown in brackets, such as main or a module's tag; it also picks the color.</param>
    /// <param name="message">The text as the user reads it; most mirror the original XAMPP panel's wording.</param>
    /// <param name="level">Picks the color; <see cref="LogLevel.Debug"/> is written even with debug output off.</param>
    public void Write(string source, string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry(source, message, level);

        if (Settings.LogToFile)
        {
            AppendToFile(entry);
        }

        void AddToLog()
        {
            Log.Add(entry);
            TrimLog();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            AddToLog();
        }
        else
        {
            Dispatcher.UIThread.Post(AddToLog);
        }
    }

    /// <summary>Writes a debug line; only shown when "Show debug information" is on.</summary>
    /// <param name="source">The tag shown in brackets, such as main or a module's tag.</param>
    /// <param name="message">The text as the user reads it.</param>
    public void Debug(string source, string message)
    {
        if (Settings.ShowDebug)
        {
            Write(source, message, LogLevel.Debug);
        }
    }

    /// <summary>Copies the last lines of a module's log file into the log pane, e.g. after a failed start.</summary>
    /// <param name="source">The tag shown in brackets on each copied line.</param>
    /// <param name="path">Full path of the log file; a missing or unreadable file logs a "no entries" warning.</param>
    /// <param name="lines">How many of the file's last non-empty lines to copy, at most.</param>
    public void TailLog(string source, string path, int lines)
    {
        IReadOnlyList<string> tailLines = Shell.Tail(path, lines);
        if (tailLines.Count == 0)
        {
            Write(source, $"  (no entries in {Path.GetFileName(path)})", LogLevel.Warn);
            return;
        }

        foreach (string line in tailLines)
        {
            Write(source, "  › " + line, LogLevel.Error);
        }
    }

    /// <summary>Drops the oldest log entries until at most <see cref="AppSettings.MaxLogLines"/> remain.</summary>
    private void TrimLog()
    {
        while (Log.Count > Settings.MaxLogLines)
        {
            Log.RemoveAt(0);
        }
    }

    /// <summary>Appends one entry, with the date in front, to the log file in the settings folder.</summary>
    /// <param name="entry">The entry as it appears in the log pane.</param>
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
            // Logging to file is best effort: whatever the disk does, the line is still shown in the log pane
            // and only the file misses it.
        }
    }

    // ── side buttons ─────────────────────────────────────────

    /// <summary>Opens a console in the XAMPP folder, set up by xampp_shell.bat when the installation has one.</summary>
    private void OpenShell()
    {
        string shellBatch = Path.Combine(Root, "xampp_shell.bat");
        string arguments = File.Exists(shellBatch) ? $"/k \"{shellBatch}\"" : $"/k cd /d \"{Root}\"";
        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = Directory.Exists(Root) ? Root : Environment.CurrentDirectory,
        };

        // The console runs on its own; the panel has no use for the process handle.
        System.Diagnostics.Process.Start(startInfo)?.Dispose();
    }

    /// <summary>Stops the status polling.</summary>
    public void Dispose() => _pollTimer.Stop();
}
