using XamppControl.Core;

namespace XamppControl.Modules;

/// <summary>Knows how to detect, start and stop one XAMPP component.</summary>
public abstract class ModuleDriver(string root, AppSettings settings)
{
    public string Root { get; } = root;

    /// <summary>Live settings; ports and service names are read on each use, so edits apply right away.</summary>
    protected AppSettings Settings { get; } = settings;

    protected ServiceSettings Services => Settings.Services;

    public abstract string DisplayName { get; }

    /// <summary>Short tag used in the log, e.g. "Apache" or "mysql".</summary>
    public virtual string Tag => DisplayName;

    /// <summary>Folder relative to the XAMPP root, e.g. "apache".</summary>
    public abstract string Folder { get; }

    public string FolderPath => Path.Combine(Root, Folder);

    public virtual bool IsInstalled => Directory.Exists(FolderPath);

    /// <summary>Text under the module name, e.g. "2.4.58" or "MariaDB 12.3.3".</summary>
    public virtual string? VersionLabel => null;

    /// <summary>Windows service name, or null if the module can't run as a service.</summary>
    public virtual string? ServiceName => null;

    public virtual (string Exe, string Args)? ServiceInstallCommand => null;
    public virtual (string Exe, string Args)? ServiceUninstallCommand => null;

    /// <summary>Ports this module needs free (from Service and Port Settings).</summary>
    public virtual IReadOnlyList<int> ConfiguredPorts => [];

    public virtual bool HasAdmin => false;
    public virtual void OpenAdmin(IModuleHost host) { }

    /// <summary>When a graceful stop fails, the panel offers "Force stop".</summary>
    public virtual bool SupportsForceStop => false;

    /// <summary>Config files (label, path relative to root). Missing files are hidden.</summary>
    public virtual IEnumerable<(string Label, string Relative)> ConfigFiles => [];

    /// <summary>Log files or folders (label, path relative to root). Missing ones are hidden.</summary>
    public virtual IEnumerable<(string Label, string Relative)> LogFiles => [];

    protected abstract string[] ProcessNames { get; }

    /// <summary>PIDs that belong to this module. Runs on a background thread.</summary>
    public virtual IReadOnlyList<int> DetectPids(IReadOnlyDictionary<int, SortedSet<int>> listening) =>
        ProcessScan.FindPids(ProcessNames, FolderPath);

    public abstract Task StartAsync(IModuleHost host);

    /// <summary>Returns false if the module is still running afterwards.</summary>
    public abstract Task<bool> StopAsync(IModuleHost host);

    public virtual async Task ForceStopAsync(IModuleHost host)
    {
        await KillAllAsync(host);
    }

    // ── helpers ─────────────────────────────────────────────

    protected Task<IReadOnlyList<int>> PidsAsync() => Task.Run(() => DetectPids(PortTable.ListeningByPid()));

    /// <summary>"(PID: 2524, 27820)", or empty when there are none.</summary>
    public static string PidText(IReadOnlyCollection<int> pids) =>
        pids.Count == 0 ? "" : $" (PID: {string.Join(", ", pids)})";

    protected async Task<bool> KillAllAsync(IModuleHost host)
    {
        var pids = await PidsAsync();
        foreach (var pid in pids)
        {
            host.Debug(Tag, $"Ending process tree of PID {pid}");
            ProcessScan.KillTree(pid);
        }
        return await WaitUntilStoppedAsync(TimeSpan.FromSeconds(10));
    }

    protected async Task<bool> WaitUntilStoppedAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if ((await PidsAsync()).Count == 0)
                return true;
            await Task.Delay(300);
        }
        return false;
    }

    protected string InRoot(string relative) => Path.Combine(Root, relative);

    protected static string LocalUrl(int port, string path = "/") =>
        port == 80 ? $"http://localhost{path}" : $"http://localhost:{port}{path}";
}
