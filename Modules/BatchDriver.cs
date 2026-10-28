using XamppControl.Core;

namespace XamppControl.Modules;

/// <summary>
/// The optional XAMPP modules (FileZilla, Mercury, Tomcat) are started and stopped with the
/// batch files XAMPP ships in its root folder, falling back to the exe when a batch file is missing.
/// </summary>
/// <param name="root">The XAMPP installation folder.</param>
/// <param name="settings">The panel's live settings.</param>
/// <param name="displayName">Module name shown in the panel, e.g. "FileZilla".</param>
/// <param name="folder">The module's folder relative to <paramref name="root"/>.</param>
/// <param name="processNames">Process names without ".exe" that the module runs as.</param>
/// <param name="startBat">Start script relative to <paramref name="root"/>, e.g. "mercury_start.bat".</param>
/// <param name="stopBat">Stop script relative to <paramref name="root"/>; without it the processes are ended.</param>
/// <param name="exe">
/// The module's program relative to <paramref name="root"/>, used for the version and when the start script is
/// missing; null when there is none.
/// </param>
public class BatchDriver(
    string root,
    AppSettings settings,
    string displayName,
    string folder,
    string[] processNames,
    string startBat,
    string stopBat,
    string? exe) : ModuleDriver(root, settings)
{
    public override string DisplayName => displayName;
    public override string Folder => folder;
    protected override string[] ProcessNames => processNames;
    public override bool SupportsForceStop => true;

    /// <summary>Exe path relative to root, or an http(s) URL; null when the module has no admin.</summary>
    public string? AdminTarget { get; init; }

    /// <summary>Picks the module's ports from Service and Port Settings; null when it needs none.</summary>
    public Func<ServiceSettings, IReadOnlyList<int>>? Ports { get; init; }

    /// <summary>Config files (label, path relative to root) for the Config menu.</summary>
    public IReadOnlyList<(string Label, string Relative)> Configs { get; init; } = [];

    /// <summary>Log files or folders (label, path relative to root) for the Logs menu.</summary>
    public IReadOnlyList<(string Label, string Relative)> Logs { get; init; } = [];

    public override IEnumerable<(string Label, string Relative)> ConfigFiles => Configs;
    public override IEnumerable<(string Label, string Relative)> LogFiles => Logs;
    public override IReadOnlyList<int> ConfiguredPorts => Ports?.Invoke(Services) ?? [];

    public override string? VersionLabel => exe is null ? null : XamppPaths.FileVersion(InRoot(exe));

    public override bool HasAdmin => AdminTarget is not null;

    /// <summary>Run the start script in a visible console window instead of hidden.</summary>
    protected virtual bool ShowStartWindow => false;

    public override void OpenAdmin(IModuleHost host)
    {
        if (AdminTarget is null)
        {
            return;
        }

        if (AdminTarget.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Shell.OpenUrl(AdminTarget, host.Settings.Browser);
        }
        else
        {
            Shell.Open(InRoot(AdminTarget));
        }
    }

    public override async Task StartAsync(IModuleHost host)
    {
        host.Write(Tag, $"Attempting to start {DisplayName} app...");
        string startScript = InRoot(startBat);
        if (File.Exists(startScript))
        {
            host.Debug(Tag, $"Executing \"{startScript}\"");

            // Some start scripts keep running (e.g. Tomcat), so don't wait for them.
            if (ShowStartWindow)
            {
                Shell.StartVisibleBatch(startScript, Root);
            }
            else
            {
                Shell.StartHidden("cmd.exe", $"/c \"{startScript}\"", Root)?.Dispose();
            }
        }
        else if (exe is not null && File.Exists(InRoot(exe)))
        {
            string exePath = InRoot(exe);
            host.Debug(Tag, $"Executing \"{exePath}\"");

            // exePath is a file path combined under Root, so it always has a parent directory.
            Shell.StartHidden(exePath, "", Path.GetDirectoryName(exePath)!)?.Dispose();
        }
        else
        {
            host.Write(Tag, $"Neither {startBat} nor the {DisplayName} exe was found.", LogLevel.Error);
            return;
        }

        await Task.Delay(500);
    }

    public override async Task<bool> StopAsync(IModuleHost host)
    {
        IReadOnlyList<int> pids = await PidsAsync();
        host.Write(Tag, $"Attempting to stop {DisplayName}{PidText(pids)}");
        string stopScript = InRoot(stopBat);
        if (!File.Exists(stopScript))
        {
            return await KillAllAsync(host);
        }

        host.Debug(Tag, $"Executing \"{stopScript}\"");
        await Shell.RunAsync("cmd.exe", $"/c \"{stopScript}\"", Root, TimeSpan.FromSeconds(20));
        if (await WaitUntilStoppedAsync(TimeSpan.FromSeconds(15)))
        {
            return true;
        }

        host.Write(Tag, $"{DisplayName} is still running after {stopBat}.", LogLevel.Warn);
        return false;
    }
}

/// <summary>Tomcat runs as java.exe from wherever Java is installed, so it's matched by its ports instead.</summary>
/// <param name="root">The XAMPP installation folder.</param>
/// <param name="settings">The panel's live settings; also decides whether the start script's window shows.</param>
public sealed class TomcatDriver(string root, AppSettings settings) : BatchDriver(
    root, settings, "Tomcat", "tomcat", ["java", "javaw"], "catalina_start.bat", "catalina_stop.bat", null)
{
    protected override bool ShowStartWindow => Settings.TomcatOutputWindow;

    public override IReadOnlyList<int> DetectPids(IReadOnlyDictionary<int, SortedSet<int>> listening)
    {
        var tomcatPorts = new[] { Services.TomcatPort, Services.TomcatShutdownPort, Services.TomcatAjpPort };
        List<int> javaPids = ProcessScan.FindPids(ProcessNames, null);
        return javaPids
            .Where(pid => listening.TryGetValue(pid, out SortedSet<int>? ports) && ports.Overlaps(tomcatPorts))
            .ToList();
    }

    public override void OpenAdmin(IModuleHost host) =>
        Shell.OpenUrl(LocalUrl(Services.TomcatPort), host.Settings.Browser);
}

/// <summary>The modules the panel shows, in display order.</summary>
public static class ModuleCatalog
{
    /// <summary>Display names in panel order; the auto-start settings and user files are keyed by them.</summary>
    public static readonly string[] Names = ["Apache", "MySQL", "FileZilla", "Mercury", "Tomcat"];

    /// <summary>Builds one driver per module, in the order of <see cref="Names"/>.</summary>
    /// <param name="root">The XAMPP installation folder.</param>
    /// <param name="settings">The panel's live settings, shared by every driver.</param>
    /// <returns>A new driver for each module, whether or not it is installed.</returns>
    public static IReadOnlyList<ModuleDriver> Create(string root, AppSettings settings) =>
    [
        new ApacheDriver(root, settings),
        new MySqlDriver(root, settings),
        new BatchDriver(root, settings, "FileZilla", "FileZillaFTP", ["FileZillaServer", "FileZilla Server"],
            "filezilla_start.bat", "filezilla_stop.bat", @"FileZillaFTP\FileZillaServer.exe")
        {
            AdminTarget = @"FileZillaFTP\FileZilla Server Interface.exe",
            Ports = services => [services.FileZillaPort, services.FileZillaAdminPort],
            Configs = [("FileZilla Server.xml", @"FileZillaFTP\FileZilla Server.xml")],
            Logs = [("<Browse> FileZillaFTP\\Logs", @"FileZillaFTP\Logs")],
        },
        new BatchDriver(root, settings, "Mercury", "MercuryMail", ["mercury"],
            "mercury_start.bat", "mercury_stop.bat", @"MercuryMail\mercury.exe")
        {
            Ports = services => services.ParsedMercuryPorts,
            Configs = [("MERCURY.INI", @"MercuryMail\MERCURY.INI")],
            Logs = [("<Browse> MercuryMail\\LOGS", @"MercuryMail\LOGS")],
        },
        new TomcatDriver(root, settings)
        {
            AdminTarget = "http://localhost:8080/",
            Ports = services => [services.TomcatPort, services.TomcatShutdownPort, services.TomcatAjpPort],
            Configs =
            [
                ("server.xml", @"tomcat\conf\server.xml"),
                ("tomcat-users.xml", @"tomcat\conf\tomcat-users.xml"),
                ("web.xml", @"tomcat\conf\web.xml"),
            ],
            Logs = [("<Browse> tomcat\\logs", @"tomcat\logs")],
        },
    ];
}
