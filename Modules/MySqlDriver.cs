using System.Diagnostics;
using XamppControl.Core;

namespace XamppControl.Modules;

/// <summary>
/// MySQL/MariaDB. Stops with mysqladmin/mariadb-admin shutdown so InnoDB flushes its redo log;
/// a hard kill is only offered as "Force stop" when the clean shutdown fails.
/// </summary>
public sealed class MySqlDriver(string root, AppSettings settings) : ModuleDriver(root, settings)
{
    public override string DisplayName => "MySQL";
    public override string Tag => "mysql";
    public override string Folder => "mysql";
    protected override string[] ProcessNames => ["mysqld", "mariadbd"];

    private string Bin => Path.Combine(FolderPath, "bin");
    private string Ini => Path.Combine(Bin, "my.ini");

    private string? ServerExe => XamppPaths.FirstExisting(
        Path.Combine(Bin, "mysqld.exe"), Path.Combine(Bin, "mariadbd.exe"));

    private string? AdminExe => XamppPaths.FirstExisting(
        Path.Combine(Bin, "mariadb-admin.exe"), Path.Combine(Bin, "mysqladmin.exe"));

    private bool IsMariaDb
    {
        get
        {
            if (File.Exists(Path.Combine(Bin, "mariadb-admin.exe")) || File.Exists(Path.Combine(Bin, "mariadbd.exe")))
                return true;
            try
            {
                return ServerExe is not null &&
                       (FileVersionInfo.GetVersionInfo(ServerExe).ProductName ?? "")
                           .Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public override string? VersionLabel
    {
        get
        {
            var version = XamppPaths.FileVersion(ServerExe);
            if (version is null)
                return null;
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
        var exe = ServerExe;
        if (exe is null)
        {
            host.Write(Tag, @"No mysqld.exe or mariadbd.exe in mysql\bin", LogLevel.Error);
            return;
        }

        host.Write(Tag, "Attempting to start MySQL app...");
        host.Debug(Tag, $"Executing \"{exe}\" --defaults-file=\"{Ini}\" --standalone");
        using var process = Shell.StartHidden(exe, $"--defaults-file=\"{Ini}\" --standalone", Root);
        if (process is null)
        {
            host.Write(Tag, "Could not launch " + Path.GetFileName(exe), LogLevel.Error);
            return;
        }

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
        var admin = AdminExe;
        if (admin is null)
        {
            host.Write(Tag,
                @"No mariadb-admin.exe or mysqladmin.exe in mysql\bin, so a clean shutdown isn't possible.",
                LogLevel.Warn);
            return false;
        }

        var pids = await PidsAsync();
        host.Write(Tag, $"Attempting to stop MySQL{PidText(pids)} with a clean shutdown");

        var env = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(host.Settings.MySqlRootPassword))
            env["MYSQL_PWD"] = host.Settings.MySqlRootPassword!;

        var args = $"-u root -h 127.0.0.1 -P {Services.MySqlPort} --protocol=tcp shutdown";
        host.Debug(Tag, $"Executing \"{admin}\" {args}");
        var result = await Shell.RunAsync(admin, args, Root, TimeSpan.FromSeconds(20), env);
        if (result.ExitCode != 0)
        {
            var reason = FirstLine(result.Error) ?? FirstLine(result.Output) ?? "unknown error";
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

    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
