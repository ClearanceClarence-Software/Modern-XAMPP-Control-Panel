using Microsoft.Win32;
using XamppControl.Core;

namespace XamppControl.Modules;

/// <summary>
/// Finds FileZilla Server 1.x, the current FileZilla Server. Unlike XAMPP's bundled 0.9.41 it installs outside
/// the XAMPP folder and always runs as the Windows service "filezilla-server".
/// </summary>
public static class FileZillaServer
{
    /// <summary>The Windows service FileZilla Server 1.x installs.</summary>
    public const string ServiceName = "filezilla-server";

    /// <summary>The official download page for FileZilla Server.</summary>
    public const string DownloadPage = "https://filezilla-project.org/download.php?type=server";

    /// <summary>The server's executable in the install folder.</summary>
    public const string ServerExe = "filezilla-server.exe";

    /// <summary>The administration program in the install folder.</summary>
    public const string AdminExe = "filezilla-server-gui.exe";

    /// <summary>Where FileZilla Server 1.x keeps its settings and users by default.</summary>
    public static string ConfigFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "filezilla-server");

    /// <summary>The folder FileZilla Server 1.x is installed in, read from its Windows service.</summary>
    /// <returns>The folder holding filezilla-server.exe; null when 1.x isn't installed or off Windows.</returns>
    public static string? FindInstallFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string? servicePath = null;
        try
        {
            using RegistryKey? service =
                Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
            servicePath = service?.GetValue("ImagePath") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException
                                       or UnauthorizedAccessException
                                       or IOException)
        {
            // Without read access to the service's key, the default install folder below is tried instead.
        }

        string? folder = ExeFolder(servicePath);
        if (folder is not null && File.Exists(Path.Combine(folder, ServerExe)))
        {
            return folder;
        }

        string defaultFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "FileZilla Server");
        return File.Exists(Path.Combine(defaultFolder, ServerExe)) ? defaultFolder : null;
    }

    /// <summary>The installed version of FileZilla Server 1.x.</summary>
    /// <param name="installFolder">The folder from <see cref="FindInstallFolder"/>.</param>
    /// <returns>The version, such as "1.12.6"; null when it can't be read.</returns>
    public static string? VersionOf(string installFolder) =>
        XamppPaths.FileVersion(Path.Combine(installFolder, ServerExe));

    /// <summary>The folder of the executable in a service's command line.</summary>
    /// <param name="commandLine">
    /// The service's ImagePath: a quoted or plain exe path, maybe followed by arguments.
    /// </param>
    /// <returns>The folder; null when the command line is empty or names no .exe.</returns>
    private static string? ExeFolder(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        string trimmed = commandLine.Trim();
        string exePath;
        if (trimmed.StartsWith('"'))
        {
            int closingQuote = trimmed.IndexOf('"', 1);
            exePath = closingQuote > 0 ? trimmed[1..closingQuote] : trimmed[1..];
        }
        else
        {
            int exeEnd = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            exePath = exeEnd > 0 ? trimmed[..(exeEnd + 4)] : trimmed;
        }

        return Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(exePath));
    }
}

/// <summary>
/// The FileZilla row when FileZilla Server 1.x is installed: it runs as a Windows service, so starting and stopping
/// go through the service (one UAC prompt each), and Admin opens its administration program.
/// </summary>
/// <param name="root">The XAMPP installation folder.</param>
/// <param name="settings">The panel's live settings.</param>
/// <param name="installFolder">The folder FileZilla Server 1.x is installed in.</param>
public sealed class FileZillaServiceDriver(string root, AppSettings settings, string installFolder)
    : ModuleDriver(root, settings)
{
    public override string DisplayName => "FileZilla";

    public override string Folder => installFolder;

    protected override string[] ProcessNames => ["filezilla-server"];

    public override bool IsInstalled => File.Exists(Path.Combine(installFolder, FileZillaServer.ServerExe));

    public override string? VersionLabel =>
        FileZillaServer.VersionOf(installFolder) is { } version ? $"FileZilla Server {version}" : "FileZilla Server";

    public override string? ServiceName => FileZillaServer.ServiceName;

    public override bool IsAlwaysService => true;

    public override IReadOnlyList<int> ConfiguredPorts => [Services.FileZillaPort];

    public override bool HasAdmin => File.Exists(Path.Combine(installFolder, FileZillaServer.AdminExe));

    public override void OpenAdmin(IModuleHost host) =>
        Shell.Open(Path.Combine(installFolder, FileZillaServer.AdminExe));

    public override IEnumerable<(string Label, string Relative)> ConfigFiles =>
    [
        ("<Browse> FileZilla Server settings", FileZillaServer.ConfigFolder),
        ("Old XAMPP settings (FileZilla Server.xml)", @"FileZillaFTP\FileZilla Server.xml"),
    ];

    public override IEnumerable<(string Label, string Relative)> LogFiles =>
    [
        ("<Browse> FileZilla Server logs", Path.Combine(FileZillaServer.ConfigFolder, "logs")),
    ];

    public override IReadOnlyList<int> DetectPids(IReadOnlyDictionary<int, SortedSet<int>> listening) =>
        ProcessScan.FindPids(ProcessNames, installFolder);

    public override async Task StartAsync(IModuleHost host)
    {
        host.Write(Tag, $"Starting the {FileZillaServer.ServiceName} service…");
        ReportServiceCommand(host, await WindowsServices.StartAsync(FileZillaServer.ServiceName), "start");
    }

    public override async Task<bool> StopAsync(IModuleHost host)
    {
        host.Write(Tag, $"Stopping the {FileZillaServer.ServiceName} service…");
        ReportServiceCommand(host, await WindowsServices.StopAsync(FileZillaServer.ServiceName), "stop");
        return await WaitUntilStoppedAsync(TimeSpan.FromSeconds(15));
    }

    /// <summary>Logs a failed net start or net stop.</summary>
    /// <param name="host">Receives the log line.</param>
    /// <param name="exitCode">net.exe's exit code; -2 when the UAC prompt was declined.</param>
    /// <param name="verb">"start" or "stop", for the message.</param>
    private void ReportServiceCommand(IModuleHost host, int exitCode, string verb)
    {
        if (exitCode == -2)
        {
            host.Write(Tag, "Canceled at the admin prompt.", LogLevel.Warn);
        }
        else if (exitCode != 0)
        {
            host.Write(Tag, $"net {verb} {FileZillaServer.ServiceName} failed (exit code {exitCode}).", LogLevel.Error);
        }
    }
}
