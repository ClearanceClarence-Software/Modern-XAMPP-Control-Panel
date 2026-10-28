using XamppControl.Core;

namespace XamppControl.Modules;

/// <summary>
/// The optional XAMPP modules (FileZilla, Mercury, Tomcat) are started and stopped with the
/// batch files XAMPP ships in its root folder, falling back to the exe when a batch file is missing.
/// </summary>
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

    /// <summary>Exe path relative to root, or an http(s) URL.</summary>
    public string? AdminTarget { get; init; }
    public Func<ServiceSettings, IReadOnlyList<int>>? Ports { get; init; }
    public IReadOnlyList<(string Label, string Relative)> Configs { get; init; } = [];
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
            return;
        if (AdminTarget.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            Shell.OpenUrl(AdminTarget, host.Settings.Browser);
        else
            Shell.Open(InRoot(AdminTarget));
    }

    public override async Task StartAsync(IModuleHost host)
    {
        host.Write(Tag, $"Attempting to start {DisplayName} app...");
        var bat = InRoot(startBat);
        if (File.Exists(bat))
        {
            host.Debug(Tag, $"Executing \"{bat}\"");
            // Some start scripts keep running (e.g. Tomcat), so don't wait for them.
            if (ShowStartWindow)
                Shell.StartVisibleBatch(bat, Root);
            else
                Shell.StartHidden("cmd.exe", $"/c \"{bat}\"", Root)?.Dispose();
        }
        else if (exe is not null && File.Exists(InRoot(exe)))
        {
            host.Debug(Tag, $"Executing \"{InRoot(exe)}\"");
            Shell.StartHidden(InRoot(exe), "", Path.GetDirectoryName(InRoot(exe))!)?.Dispose();
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
        var pids = await PidsAsync();
        host.Write(Tag, $"Attempting to stop {DisplayName}{PidText(pids)}");
        var bat = InRoot(stopBat);
        if (!File.Exists(bat))
            return await KillAllAsync(host);

        host.Debug(Tag, $"Executing \"{bat}\"");
        await Shell.RunAsync("cmd.exe", $"/c \"{bat}\"", Root, TimeSpan.FromSeconds(20));
        if (await WaitUntilStoppedAsync(TimeSpan.FromSeconds(15)))
            return true;

        host.Write(Tag, $"{DisplayName} is still running after {stopBat}.", LogLevel.Warn);
        return false;
    }
}

/// <summary>Tomcat runs as java.exe from wherever Java is installed, so it's matched by its ports instead.</summary>
public sealed class TomcatDriver(string root, AppSettings settings) : BatchDriver(
    root, settings, "Tomcat", "tomcat", ["java", "javaw"], "catalina_start.bat", "catalina_stop.bat", null)
{
    protected override bool ShowStartWindow => Settings.TomcatOutputWindow;

    public override IReadOnlyList<int> DetectPids(IReadOnlyDictionary<int, SortedSet<int>> listening)
    {
        var tomcatPorts = new[] { Services.TomcatPort, Services.TomcatShutdownPort, Services.TomcatAjpPort };
        var java = ProcessScan.FindPids(ProcessNames, null);
        return java.Where(pid => listening.TryGetValue(pid, out var ports) && ports.Overlaps(tomcatPorts)).ToList();
    }

    public override void OpenAdmin(IModuleHost host) =>
        Shell.OpenUrl(LocalUrl(Services.TomcatPort), host.Settings.Browser);
}

public static class ModuleCatalog
{
    public static readonly string[] Names = ["Apache", "MySQL", "FileZilla", "Mercury", "Tomcat"];

    public static IReadOnlyList<ModuleDriver> Create(string root, AppSettings settings) =>
    [
        new ApacheDriver(root, settings),
        new MySqlDriver(root, settings),
        new BatchDriver(root, settings, "FileZilla", "FileZillaFTP", ["FileZillaServer", "FileZilla Server"],
            "filezilla_start.bat", "filezilla_stop.bat", @"FileZillaFTP\FileZillaServer.exe")
        {
            AdminTarget = @"FileZillaFTP\FileZilla Server Interface.exe",
            Ports = s => [s.FileZillaPort, s.FileZillaAdminPort],
            Configs = [("FileZilla Server.xml", @"FileZillaFTP\FileZilla Server.xml")],
            Logs = [("<Browse> FileZillaFTP\\Logs", @"FileZillaFTP\Logs")],
        },
        new BatchDriver(root, settings, "Mercury", "MercuryMail", ["mercury"],
            "mercury_start.bat", "mercury_stop.bat", @"MercuryMail\mercury.exe")
        {
            Ports = s => s.ParsedMercuryPorts,
            Configs = [("MERCURY.INI", @"MercuryMail\MERCURY.INI")],
            Logs = [("<Browse> MercuryMail\\LOGS", @"MercuryMail\LOGS")],
        },
        new TomcatDriver(root, settings)
        {
            AdminTarget = "http://localhost:8080/",
            Ports = s => [s.TomcatPort, s.TomcatShutdownPort, s.TomcatAjpPort],
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
