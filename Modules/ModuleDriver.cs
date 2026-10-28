using XamppControl.Core;

namespace XamppControl.Modules;

/// <summary>Knows how to detect, start and stop one XAMPP component.</summary>
/// <param name="root">The XAMPP installation folder; relative paths of the module resolve against it.</param>
/// <param name="settings">The panel's live settings, kept by reference so later edits apply.</param>
public abstract class ModuleDriver(string root, AppSettings settings)
{
    /// <summary>The XAMPP installation folder.</summary>
    public string Root { get; } = root;

    /// <summary>Live settings; ports and service names are read on each use, so edits apply right away.</summary>
    protected AppSettings Settings { get; } = settings;

    /// <summary>The service names and ports from Service and Port Settings.</summary>
    protected ServiceSettings Services => Settings.Services;

    /// <summary>Module name shown in the panel, also the key for its auto-start and user files.</summary>
    public abstract string DisplayName { get; }

    /// <summary>Short tag used in the log, e.g. "Apache" or "mysql".</summary>
    public virtual string Tag => DisplayName;

    /// <summary>Folder relative to the XAMPP root, e.g. "apache".</summary>
    public abstract string Folder { get; }

    /// <summary>Full path of <see cref="Folder"/> under <see cref="Root"/>.</summary>
    public string FolderPath => Path.Combine(Root, Folder);

    /// <summary>True when the module's folder exists; uninstalled modules are shown dimmed and cannot start.</summary>
    public virtual bool IsInstalled => Directory.Exists(FolderPath);

    /// <summary>Text under the module name, e.g. "2.4.58" or "MariaDB 12.3.3"; null when unknown.</summary>
    public virtual string? VersionLabel => null;

    /// <summary>Windows service name, or null if the module can't run as a service.</summary>
    public virtual string? ServiceName => null;

    /// <summary>The program and arguments that install <see cref="ServiceName"/>; null when not available.</summary>
    public virtual (string Exe, string Args)? ServiceInstallCommand => null;

    /// <summary>The program and arguments that remove <see cref="ServiceName"/>; null when not available.</summary>
    public virtual (string Exe, string Args)? ServiceUninstallCommand => null;

    /// <summary>Ports this module needs free (from Service and Port Settings).</summary>
    public virtual IReadOnlyList<int> ConfiguredPorts => [];

    /// <summary>True when <see cref="OpenAdmin"/> opens something; the Admin button is enabled only then.</summary>
    public virtual bool HasAdmin => false;

    /// <summary>Opens the module's admin page or program; does nothing for a module without one.</summary>
    /// <param name="host">Supplies the configured browser for web admin pages.</param>
    public virtual void OpenAdmin(IModuleHost host)
    {
    }

    /// <summary>When a graceful stop fails, the panel offers "Force stop".</summary>
    public virtual bool SupportsForceStop => false;

    /// <summary>Config files (label, path relative to root). Missing files are hidden.</summary>
    public virtual IEnumerable<(string Label, string Relative)> ConfigFiles => [];

    /// <summary>Log files or folders (label, path relative to root). Missing ones are hidden.</summary>
    public virtual IEnumerable<(string Label, string Relative)> LogFiles => [];

    /// <summary>Process names without ".exe" that the module runs as.</summary>
    protected abstract string[] ProcessNames { get; }

    /// <summary>PIDs that belong to this module. Runs on a background thread.</summary>
    /// <param name="listening">Listening TCP ports per PID; the default ignores it and matches by exe folder.</param>
    /// <returns>The PIDs in ascending order; empty when the module is not running.</returns>
    public virtual IReadOnlyList<int> DetectPids(IReadOnlyDictionary<int, SortedSet<int>> listening) =>
        ProcessScan.FindPids(ProcessNames, FolderPath);

    /// <summary>Starts the module and logs what happened; servers that exit right away are reported.</summary>
    /// <param name="host">Receives the log lines.</param>
    /// <returns>A task that completes after the start attempt and any early-exit check, not when ready.</returns>
    public abstract Task StartAsync(IModuleHost host);

    /// <summary>Stops the module the normal way and logs the outcome.</summary>
    /// <param name="host">Receives the log lines.</param>
    /// <returns>False if the module is still running afterwards.</returns>
    public abstract Task<bool> StopAsync(IModuleHost host);

    /// <summary>Ends every process tree of the module without a clean shutdown.</summary>
    /// <param name="host">Receives the debug lines for each ended process.</param>
    /// <returns>A task that completes when the processes are gone or after 10 seconds of waiting.</returns>
    public virtual async Task ForceStopAsync(IModuleHost host)
    {
        await KillAllAsync(host);
    }

    // ── helpers ─────────────────────────────────────────────

    /// <summary>Runs <see cref="DetectPids"/> on a background thread with a fresh port table.</summary>
    /// <returns>The module's PIDs; empty when it is not running.</returns>
    protected Task<IReadOnlyList<int>> PidsAsync() => Task.Run(() => DetectPids(PortTable.ListeningByPid()));

    /// <summary>"(PID: 2524, 27820)" with a leading space, ready to append to a log line.</summary>
    /// <param name="pids">The PIDs to list, in the order given.</param>
    /// <returns>The text with a leading space, or empty when there are none.</returns>
    public static string PidText(IReadOnlyCollection<int> pids) =>
        pids.Count == 0 ? "" : $" (PID: {string.Join(", ", pids)})";

    /// <summary>Ends the process tree of every PID of the module and waits for them to go.</summary>
    /// <param name="host">Receives a debug line per ended process.</param>
    /// <returns>True when no process of the module is left within 10 seconds.</returns>
    protected async Task<bool> KillAllAsync(IModuleHost host)
    {
        IReadOnlyList<int> pids = await PidsAsync();
        foreach (int pid in pids)
        {
            host.Debug(Tag, $"Ending process tree of PID {pid}");
            ProcessScan.KillTree(pid);
        }

        return await WaitUntilStoppedAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>Polls the module's PIDs every 300 ms until none are left or the time runs out.</summary>
    /// <param name="timeout">How long to keep polling.</param>
    /// <returns>True when the module stopped in time; false when it is still running.</returns>
    protected async Task<bool> WaitUntilStoppedAsync(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if ((await PidsAsync()).Count == 0)
            {
                return true;
            }

            await Task.Delay(300);
        }

        return false;
    }

    /// <summary>Full path of a path given relative to the XAMPP root.</summary>
    /// <param name="relative">Path relative to <see cref="Root"/>, with backslashes as XAMPP writes them.</param>
    /// <returns>The combined path; an absolute <paramref name="relative"/> is returned as is.</returns>
    protected string InRoot(string relative) => Path.Combine(Root, relative);

    /// <summary>An http://localhost URL, leaving the port out when it is 80.</summary>
    /// <param name="port">TCP port the local server listens on.</param>
    /// <param name="path">Path after the host, starting with a slash.</param>
    /// <returns>The URL, e.g. "http://localhost/" or "http://localhost:8080/phpmyadmin/".</returns>
    protected static string LocalUrl(int port, string path = "/") =>
        port == 80 ? $"http://localhost{path}" : $"http://localhost:{port}{path}";
}
