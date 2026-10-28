using System.Diagnostics;
using XamppControl.Core;

namespace XamppControl.Modules;

/// <summary>
/// MySQL/MariaDB. Stops with mysqladmin/mariadb-admin shutdown so InnoDB flushes its redo log;
/// a hard kill is only offered as "Force stop" when the clean shutdown fails.
/// </summary>
/// <param name="root">The XAMPP installation folder.</param>
/// <param name="settings">The panel's live settings.</param>
public sealed class MySqlDriver(string root, AppSettings settings) : ModuleDriver(root, settings)
{
    public override string DisplayName => "MySQL";
    public override string Tag => "mysql";
    public override string Folder => "mysql";
    protected override string[] ProcessNames => ["mysqld", "mariadbd"];

    private string Bin => Path.Combine(FolderPath, "bin");
    private string Ini => Path.Combine(Bin, "my.ini");

    private string? ServerExe => XamppPaths.FirstExisting(
        Path.Combine(Bin, "mysqld.exe"),
        Path.Combine(Bin, "mariadbd.exe"));

    private string? AdminExe => XamppPaths.FirstExisting(
        Path.Combine(Bin, "mariadb-admin.exe"),
        Path.Combine(Bin, "mysqladmin.exe"));

    /// <summary>True when the server is MariaDB: by MariaDB-named binaries, else by the product name.</summary>
    private bool IsMariaDb
    {
        get
        {
            bool hasMariaDbBinaries = File.Exists(Path.Combine(Bin, "mariadb-admin.exe"))
                || File.Exists(Path.Combine(Bin, "mariadbd.exe"));
            if (hasMariaDbBinaries)
            {
                return true;
            }

            try
            {
                string? serverExe = ServerExe;
                if (serverExe is null)
                {
                    return false;
                }

                string productName = FileVersionInfo.GetVersionInfo(serverExe).ProductName ?? "";
                return productName.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // Reading the version resource is best effort and anything may fail on a broken install;
                // the only consequence is that the version label says MySQL.
                return false;
            }
        }
    }

    public override string? VersionLabel
    {
        get
        {
            string? version = XamppPaths.FileVersion(ServerExe);
            if (version is null)
            {
                return null;
            }

            return IsMariaDb ? $"MariaDB {version}" : $"MySQL {version}";
        }
    }

    public override string? ServiceName => Services.MySqlServiceName;

    public override (string Exe, string Args)? ServiceInstallCommand =>
        ServerExe is null ? null : (ServerExe, $"--install \"{ServiceName}\" --defaults-file=\"{Ini}\"");

    public override (string Exe, string Args)? ServiceUninstallCommand =>
        ServerExe is null ? null : (ServerExe, $"--remove \"{ServiceName}\"");

    public override IReadOnlyList<int> ConfiguredPorts => [Services.MySqlPort];

    public override bool HasAdmin => true;

    public override void OpenAdmin(IModuleHost host) =>
        Shell.OpenUrl(LocalUrl(Services.ApachePort, "/phpmyadmin/"), host.Settings.Browser);

    public override bool SupportsForceStop => true;

    public override IEnumerable<(string Label, string Relative)> ConfigFiles =>
    [
        ("my.ini", @"mysql\bin\my.ini"),
        ("<Browse> mysql", @"mysql"),
    ];

    public override IEnumerable<(string Label, string Relative)> LogFiles =>
    [
        ("mysql_error.log", @"mysql\data\mysql_error.log"),
        ("<Browse> mysql\\data", @"mysql\data"),
    ];

    public override async Task StartAsync(IModuleHost host)
    {
        string? serverExe = ServerExe;
        if (serverExe is null)
        {
            host.Write(Tag, @"No mysqld.exe or mariadbd.exe in mysql\bin", LogLevel.Error);
            return;
        }

        host.Write(Tag, "Attempting to start MySQL app...");
        host.Debug(Tag, $"Executing \"{serverExe}\" --defaults-file=\"{Ini}\" --standalone");
        using Process? process = Shell.StartHidden(serverExe, $"--defaults-file=\"{Ini}\" --standalone", Root);
        if (process is null)
        {
            host.Write(Tag, "Could not launch " + Path.GetFileName(serverExe), LogLevel.Error);
            return;
        }

        // A bad config or a locked data folder makes the server exit early; a healthy server keeps running.
        if (await Shell.ExitsWithinAsync(process, TimeSpan.FromSeconds(4)))
        {
            host.Write(Tag,
                $"MySQL shut down unexpectedly (exit code {process.ExitCode}). Last lines of mysql_error.log:",
                LogLevel.Error);
            host.TailLog(Tag, InRoot(@"mysql\data\mysql_error.log"), 8);
        }
    }

    public override async Task<bool> StopAsync(IModuleHost host)
    {
        string? adminExe = AdminExe;
        if (adminExe is null)
        {
            host.Write(Tag,
                @"No mariadb-admin.exe or mysqladmin.exe in mysql\bin, so a clean shutdown isn't possible.",
                LogLevel.Warn);
            return false;
        }

        IReadOnlyList<int> pids = await PidsAsync();
        host.Write(Tag, $"Attempting to stop MySQL{PidText(pids)} with a clean shutdown");

        Shell.RunResult result = await RunAdminShutdownAsync(host, adminExe);
        if (result.ExitCode != 0)
        {
            string reason = FirstLine(result.Error) ?? FirstLine(result.Output) ?? "unknown error";
            host.Write(Tag, "Clean shutdown failed: " + reason, LogLevel.Error);
            host.Write(Tag,
                "Set the root password under Config, or use Force stop (InnoDB will recover on next start).",
                LogLevel.Warn);
            return false;
        }

        if (await WaitUntilStoppedAsync(TimeSpan.FromSeconds(30)))
        {
            host.Write(Tag, "Server shut down cleanly.", LogLevel.Ok);
            return true;
        }

        host.Write(Tag, "Server is still running after 30 s.", LogLevel.Warn);
        return false;
    }

    public override async Task ForceStopAsync(IModuleHost host)
    {
        host.Write(Tag, "Force-stopping MySQL. InnoDB will run crash recovery on next start.", LogLevel.Warn);
        await KillAllAsync(host);
    }

    /// <summary>Asks the server to shut down as root over TCP on the configured port, with a 20-second limit.</summary>
    /// <param name="host">Supplies the root password and receives the debug line with the command.</param>
    /// <param name="adminExe">Full path of mariadb-admin.exe or mysqladmin.exe.</param>
    /// <returns>The admin tool's exit code and output; exit code -1 when it could not start or timed out.</returns>
    private async Task<Shell.RunResult> RunAdminShutdownAsync(IModuleHost host, string adminExe)
    {
        // The password goes in the environment rather than on the command line, where other processes could read it.
        var environment = new Dictionary<string, string>();
        if (host.Settings.MySqlRootPassword is { Length: > 0 } rootPassword)
        {
            environment["MYSQL_PWD"] = rootPassword;
        }

        string arguments = $"-u root -h 127.0.0.1 -P {Services.MySqlPort} --protocol=tcp shutdown";
        host.Debug(Tag, $"Executing \"{adminExe}\" {arguments}");
        return await Shell.RunAsync(adminExe, arguments, Root, TimeSpan.FromSeconds(20), environment);
    }

    /// <summary>The first non-blank line of a tool's output, trimmed.</summary>
    /// <param name="text">Captured standard output or error; may be empty.</param>
    /// <returns>The line, or null when the text has no non-blank line.</returns>
    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
