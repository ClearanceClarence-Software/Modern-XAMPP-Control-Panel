using System.Text.RegularExpressions;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.ViewModels;

namespace XamppControl.Updates;

/// <summary>
/// Replaces XAMPP's mysql folder with a MariaDB Windows x64 build, the safe way:
/// optional SQL dump → full clean shutdown (innodb_fast_shutdown=0) → park mysql\ in versions\mariadb →
/// unpack → copy (not move) the data folder, my.ini, scripts and bat files → fix known my.ini issues →
/// start → mariadb-upgrade (repairing Aria system tables if needed).
/// The parked install keeps its own data folder untouched, so switching back restores the exact previous state.
/// </summary>
public static partial class MariaDbUpdater
{
    private const string Module = "MySQL";

    private const string PasswordHint = "(check the root password under Config › Service and Port Settings)";

    /// <summary>Installs a MariaDB build from a zip as the active mysql folder, keeping the old one.</summary>
    /// <param name="ctx">The update dialog's context; status, progress and log lines go through it.</param>
    /// <param name="zipPath">Full path of a MariaDB winx64 zip; it may not be older than the installed version.</param>
    /// <param name="makeDump">True writes an SQL dump of all databases to mysql_dumps before anything changes.</param>
    /// <returns>
    /// Success once the new server runs with upgraded system tables (and is stopped again if it was stopped before);
    /// a failure without a rollback when nothing was changed, or with a rollback when the new install is in place.
    /// </returns>
    public static async Task<UpdateResult> InstallAsync(UpdateContext ctx, string zipPath, bool makeDump)
    {
        string root = ctx.Root;
        string mysqlDir = Path.Combine(root, "mysql");
        string staging = Path.Combine(root, "_update_mysql");
        int port = ctx.Settings.Services.MySqlPort;
        Dictionary<string, string> environment = ctx.MySqlEnvironment();

        if (ctx.Main.FindModule(Module) is not { } mysql)
        {
            return UpdateResult.Fail("The MySQL module isn't available.");
        }
        if (mysql.IsServiceInstalled)
        {
            return UpdateResult.Fail("MySQL is installed as a Windows service. " +
                "Remove the service (Service checkbox) before updating.");
        }
        if (!Directory.Exists(mysqlDir))
        {
            return UpdateResult.Fail($"No mysql folder in \"{root}\".");
        }

        ctx.Status("Unpacking MariaDB…");
        ctx.Progress(null);
        string content;
        try
        {
            content = await Task.Run(() => FileTools.ExtractToStaging(zipPath, staging));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail("Couldn't unpack the zip: " + ex.Message);
        }

        string? newServer = FileTools.FirstExisting(Path.Combine(content, "bin"), "mariadbd.exe", "mysqld.exe");
        if (newServer is null)
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail("This zip has no bin\\mariadbd.exe, so it isn't a MariaDB Windows x64 build.");
        }

        string oldBin = Path.Combine(mysqlDir, "bin");
        string? oldServer = FileTools.FirstExisting(oldBin, "mariadbd.exe", "mysqld.exe");
        string newVersion = XamppPaths.FileVersion(newServer) ?? "new";
        string oldVersion = XamppPaths.FileVersion(oldServer) ?? "unknown";
        ctx.Log($"Installed: MariaDB {oldVersion}. New: MariaDB {newVersion}.");

        if (VersionStore.Find(root, Component.MariaDb, newVersion) is { } existing)
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail(existing.IsActive
                ? $"MariaDB {newVersion} is already the active version."
                : $"MariaDB {newVersion} is already installed. Use Switch to make it active.");
        }

        if (VersionUtil.Parse(newVersion) < VersionUtil.Parse(oldVersion))
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail($"MariaDB {newVersion} is older than the installed {oldVersion}. " +
                "Downgrades aren't supported: an older server can't open newer data files.");
        }

        // The current server has to be running for the dump and the full clean shutdown.
        bool wasRunning = mysql.IsRunning;
        if (!wasRunning && !await StartOldServerAsync(ctx, mysql, port, oldVersion))
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail($"The current MariaDB didn't start on port {port}, so it can't be shut down " +
                "cleanly. Fix that first (the log shows why). Nothing was changed.");
        }

        if (makeDump && await MakeSqlDumpAsync(ctx, root, oldBin, oldVersion, environment) is { } dumpFailure)
        {
            return await AbortAsync(ctx, staging, wasRunning, dumpFailure);
        }

        if (await ShutDownCompletelyAsync(ctx, root, oldBin, port, environment) is { } shutdownFailure)
        {
            return await AbortAsync(ctx, staging, wasRunning, shutdownFailure);
        }

        string backup = VersionStore.ParkPath(root, Component.MariaDb, oldVersion);
        ctx.Status($"Keeping MariaDB {oldVersion} as an installed version…");
        string? swapFailure = SwapInNewBuild(mysqlDir, content, backup);
        FileTools.TryDelete(staging);
        if (swapFailure is not null)
        {
            await StartAgainAsync(ctx, wasRunning);
            return UpdateResult.Fail(swapFailure);
        }
        ctx.Log($"MariaDB {oldVersion} stays installed, with its own copy of your databases, in \"{backup}\".");

        // A local function so the rollback keeps this install's root, folders and versions even if the
        // XAMPP folder setting changes before the user clicks it.
        async Task RollBackAsync()
        {
            ctx.Status($"Rolling back to MariaDB {oldVersion}…");
            if (ctx.Main.FindModule(Module) is { IsRunning: true } running)
            {
                if (!await running.StopForMaintenanceAsync())
                {
                    await running.Driver.ForceStopAsync(ctx.Main);
                }
            }

            string parked = VersionStore.ParkPath(root, Component.MariaDb, newVersion);
            Directory.Move(mysqlDir, parked);
            Directory.Move(backup, mysqlDir);
            ctx.Log($"MariaDB {oldVersion} is active again with its own data. " +
                $"MariaDB {newVersion} stays installed in \"{parked}\".", LogLevel.Ok);

            await ctx.Main.ReloadModulesAsync();
            await StartAgainAsync(ctx, wasRunning);
        }

        string rollbackLabel = $"Switch back to MariaDB {oldVersion}";
        UpdateResult FailWithRollback(string message) => new(false, message, RollBackAsync, rollbackLabel);

        try
        {
            await CarryOverDataAndSettingsAsync(ctx, backup, mysqlDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ctx.Main.ReloadModulesAsync();
            return FailWithRollback("Copying files into the new install failed: " + ex.Message);
        }

        await ctx.Main.ReloadModulesAsync();

        if (!await StartNewServerAsync(ctx, port, newVersion))
        {
            return FailWithRollback($"MariaDB {newVersion} didn't start. The log shows why; you can roll back.");
        }

        string? upgradeFailure = await UpgradeSystemTablesAsync(ctx, root, mysqlDir, newVersion, environment);
        if (upgradeFailure is not null)
        {
            return FailWithRollback(upgradeFailure);
        }

        // Leave the server as it was before the update.
        if (!wasRunning && ctx.Main.FindModule(Module) is { } server)
        {
            await server.StopForMaintenanceAsync();
        }

        await ctx.Main.ReloadModulesAsync();
        return new UpdateResult(true,
            $"MariaDB {newVersion} is installed and active, with a copy of your databases. " +
            $"MariaDB {oldVersion} stays installed.",
            RollBackAsync,
            rollbackLabel);
    }

    /// <summary>Starts the installed server so it can be dumped and shut down cleanly.</summary>
    /// <param name="ctx">The update dialog's context.</param>
    /// <param name="mysql">The panel's MySQL module, currently stopped.</param>
    /// <param name="port">The TCP port MariaDB listens on, from the panel's settings.</param>
    /// <param name="oldVersion">The installed version, shown in the status line.</param>
    /// <returns>False when the port isn't open within 30 seconds.</returns>
    private static async Task<bool> StartOldServerAsync(
        UpdateContext ctx,
        ModuleViewModel mysql,
        int port,
        string oldVersion)
    {
        ctx.Status($"Starting MariaDB {oldVersion} so it can be shut down cleanly…");
        await mysql.StartAfterMaintenanceAsync();
        return await UpdateContext.WaitForPortAsync(port, TimeSpan.FromSeconds(30));
    }

    /// <summary>Gives up before anything changed: deletes the staging folder, stops a server it started.</summary>
    /// <param name="ctx">The update dialog's context.</param>
    /// <param name="staging">The staging folder the zip was unpacked into.</param>
    /// <param name="wasRunning">False stops the server again, because the updater started it.</param>
    /// <param name="message">The reason shown to the user.</param>
    /// <returns>A failed result without a rollback, since nothing was changed.</returns>
    private static async Task<UpdateResult> AbortAsync(
        UpdateContext ctx,
        string staging,
        bool wasRunning,
        string message)
    {
        FileTools.TryDelete(staging);
        if (!wasRunning && ctx.Main.FindModule(Module) is { IsRunning: true } server)
        {
            await server.StopForMaintenanceAsync();
        }
        return UpdateResult.Fail(message);
    }

    /// <summary>Dumps all databases, routines, events and triggers of the running server to one SQL file.</summary>
    /// <param name="ctx">The update dialog's context; the dump's path and size are logged.</param>
    /// <param name="root">The XAMPP folder; the dump goes into its mysql_dumps folder, created if needed.</param>
    /// <param name="oldBin">The installed server's bin folder; it must hold mariadb-dump.exe or mysqldump.exe.</param>
    /// <param name="oldVersion">The installed version, used in the dump's file name.</param>
    /// <param name="environment">Environment for the dump tool; carries the root password as MYSQL_PWD.</param>
    /// <returns>A message for the user when no dump was made; null when the dump succeeded.</returns>
    private static async Task<string?> MakeSqlDumpAsync(
        UpdateContext ctx,
        string root,
        string oldBin,
        string oldVersion,
        Dictionary<string, string> environment)
    {
        string? dumpExe = FileTools.FirstExisting(oldBin, "mariadb-dump.exe", "mysqldump.exe");
        if (dumpExe is null)
        {
            return "No mariadb-dump.exe or mysqldump.exe in the current install, " +
                "so no dump could be made. Untick the dump option to continue without one.";
        }

        string dumpDir = Path.Combine(root, "mysql_dumps");
        Directory.CreateDirectory(dumpDir);
        string dumpFile = Path.Combine(
            dumpDir,
            $"all-databases_{FileTools.Safe(oldVersion)}_{FileTools.Stamp()}.sql");

        ctx.Status("Backing up all databases to an SQL dump…");
        Shell.RunResult dump = await Shell.RunAsync(
            dumpExe,
            $"{ctx.MySqlConnectionArgs} --all-databases --routines --events --triggers --single-transaction " +
            $"--result-file=\"{dumpFile}\"",
            root,
            TimeSpan.FromMinutes(30),
            environment);
        if (dump.ExitCode != 0)
        {
            ctx.LogOutput(dump, LogLevel.Error);
            return $"The SQL dump failed {PasswordHint}. Nothing was changed.";
        }

        ctx.Log($"SQL dump: \"{dumpFile}\" ({FileTools.FormatSize(new FileInfo(dumpFile).Length)})", LogLevel.Ok);
        return null;
    }

    /// <summary>
    /// Shuts the installed server down with innodb_fast_shutdown=0, which flushes the redo log so a newer server
    /// can take over the data files.
    /// </summary>
    /// <param name="ctx">The update dialog's context.</param>
    /// <param name="root">The XAMPP folder, used as the client's working folder.</param>
    /// <param name="oldBin">The installed server's bin folder, which must hold mariadb.exe or mysql.exe.</param>
    /// <param name="port">The TCP port MariaDB listens on; it must be closed afterwards.</param>
    /// <param name="environment">Environment for the client; carries the root password as MYSQL_PWD.</param>
    /// <returns>A message for the user when the server may not have shut down cleanly; null when it did.</returns>
    private static async Task<string?> ShutDownCompletelyAsync(
        UpdateContext ctx,
        string root,
        string oldBin,
        int port,
        Dictionary<string, string> environment)
    {
        const string notCleanMessage = "MariaDB didn't shut down cleanly. Nothing was changed.";

        ctx.Status("Shutting down MariaDB completely…");
        string? client = FileTools.FirstExisting(oldBin, "mariadb.exe", "mysql.exe");
        if (client is null)
        {
            return "No mariadb.exe or mysql.exe client in the current install. Nothing was changed.";
        }

        Shell.RunResult slowShutdown = await Shell.RunAsync(
            client,
            $"{ctx.MySqlConnectionArgs} -e \"SET GLOBAL innodb_fast_shutdown=0\"",
            root,
            TimeSpan.FromSeconds(20),
            environment);
        if (slowShutdown.ExitCode != 0)
        {
            ctx.LogOutput(slowShutdown, LogLevel.Error);
            return $"Couldn't prepare a full shutdown {PasswordHint}. Nothing was changed.";
        }

        // Make sure the panel sees the server as running, so the stop below really shuts it down.
        if (!await ctx.WaitForModuleAsync(Module, running: true, TimeSpan.FromSeconds(10)))
        {
            return notCleanMessage;
        }

        // WaitForModuleAsync just found the module running, so it exists.
        ModuleViewModel server = ctx.Main.FindModule(Module)
            ?? throw new InvalidOperationException("The MySQL module isn't available.");
        if (!await server.StopForMaintenanceAsync() || await UpdateContext.PortOpenAsync(port))
        {
            return notCleanMessage;
        }
        return null;
    }

    /// <summary>Parks the current mysql folder at <paramref name="backup"/> and moves the new build in.</summary>
    /// <param name="mysqlDir">XAMPP's mysql folder, which exists (checked before the update starts).</param>
    /// <param name="content">The unpacked build's folder inside the staging folder.</param>
    /// <param name="backup">The free folder in the version store that receives the current mysql folder.</param>
    /// <returns>
    /// A message for the user when a move failed, with the old mysql folder back in place; null when the new build
    /// is in place.
    /// </returns>
    private static string? SwapInNewBuild(string mysqlDir, string content, string backup)
    {
        try
        {
            Directory.Move(mysqlDir, backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't move the mysql folder ({ex.Message}). Close programs that use it " +
                "(HeidiSQL, a terminal in that folder, …) and try again. Nothing was changed.";
        }

        try
        {
            Directory.Move(content, mysqlDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Directory.Move(backup, mysqlDir);
            return $"Couldn't put the new MariaDB in place ({ex.Message}). The old one was restored.";
        }

        return null;
    }

    /// <summary>
    /// Copies the data folder, my.ini and the old install's other top-level files into the new install, then fixes
    /// my.ini and adds the classic tool names.
    /// </summary>
    /// <remarks>The data folder is copied, not moved, so the parked install stays a pristine copy.</remarks>
    /// <param name="ctx">The update dialog's context; what was carried over is logged.</param>
    /// <param name="backup">The parked mysql folder.</param>
    /// <param name="mysqlDir">XAMPP's mysql folder, already holding the new build; its data folder is replaced.</param>
    /// <exception cref="IOException">A file or folder could not be copied, deleted or written.</exception>
    /// <exception cref="UnauthorizedAccessException">A file or folder is read-only or not accessible.</exception>
    private static async Task CarryOverDataAndSettingsAsync(UpdateContext ctx, string backup, string mysqlDir)
    {
        string newData = Path.Combine(mysqlDir, "data");
        string oldData = Path.Combine(backup, "data");
        if (Directory.Exists(oldData))
        {
            ctx.Status("Copying the data folder (this can take a while)…");
            if (Directory.Exists(newData))
            {
                Directory.Delete(newData, recursive: true);
            }
            await Task.Run(() => FileTools.CopyDirectory(oldData, newData));
        }

        string oldIni = Path.Combine(backup, "bin", "my.ini");
        string newIni = Path.Combine(mysqlDir, "bin", "my.ini");
        if (File.Exists(oldIni))
        {
            File.Copy(oldIni, newIni, overwrite: true);
        }

        List<string> copied = await Task.Run(() => FileTools.CopyMissing(backup, mysqlDir));
        copied.Remove("data");
        if (copied.Count > 0)
        {
            ctx.Log("Kept from the old install: " + string.Join(", ", copied));
        }

        FixMyIni(ctx, newIni);
        AddCompatibilityNames(ctx, Path.Combine(mysqlDir, "bin"));
    }

    /// <summary>Starts the new server and waits for its port.</summary>
    /// <param name="ctx">The update dialog's context.</param>
    /// <param name="port">The TCP port MariaDB listens on, from the panel's settings.</param>
    /// <param name="newVersion">The new version, shown in the status line.</param>
    /// <returns>False when the port isn't open within 60 seconds.</returns>
    private static async Task<bool> StartNewServerAsync(UpdateContext ctx, int port, string newVersion)
    {
        ctx.Status($"Starting MariaDB {newVersion}…");
        ModuleViewModel server = ctx.Main.FindModule(Module)
            ?? throw new InvalidOperationException("The MySQL module isn't available.");
        await server.StartAfterMaintenanceAsync();
        return await UpdateContext.WaitForPortAsync(port, TimeSpan.FromSeconds(60));
    }

    /// <summary>Runs mariadb-upgrade on the new server, repairing and retrying on Aria errors.</summary>
    /// <param name="ctx">The update dialog's context; the tools' output is logged.</param>
    /// <param name="root">The XAMPP folder, used as the tools' working folder.</param>
    /// <param name="mysqlDir">XAMPP's mysql folder, holding the new build.</param>
    /// <param name="newVersion">The new version, used in the failure message.</param>
    /// <param name="environment">Environment for the tools; carries the root password as MYSQL_PWD.</param>
    /// <returns>A message for the user when the upgrade tool is missing or failed; null when it succeeded.</returns>
    private static async Task<string?> UpgradeSystemTablesAsync(
        UpdateContext ctx,
        string root,
        string mysqlDir,
        string newVersion,
        Dictionary<string, string> environment)
    {
        string newBin = Path.Combine(mysqlDir, "bin");
        string? upgradeExe = FileTools.FirstExisting(newBin, "mariadb-upgrade.exe", "mysql_upgrade.exe");
        if (upgradeExe is null)
        {
            return "The new install has no mariadb-upgrade.exe.";
        }

        ctx.Status("Upgrading the system tables…");
        Shell.RunResult upgrade = await Shell.RunAsync(
            upgradeExe,
            ctx.MySqlConnectionArgs,
            root,
            TimeSpan.FromMinutes(15),
            environment);
        string output = upgrade.Output + upgrade.Error;

        if (upgrade.ExitCode != 0 && AriaProblem().IsMatch(output))
        {
            ctx.Log("System tables report Aria checksum errors; repairing them.", LogLevel.Warn);
            await RepairSystemTablesAsync(ctx, root, newBin, environment);

            ctx.Status("Upgrading the system tables again…");
            upgrade = await Shell.RunAsync(
                upgradeExe,
                $"{ctx.MySqlConnectionArgs} --force",
                root,
                TimeSpan.FromMinutes(15),
                environment);
        }

        if (upgrade.ExitCode != 0)
        {
            ctx.LogOutput(upgrade, LogLevel.Error);
            return $"MariaDB {newVersion} is running, but mariadb-upgrade reported errors " +
                "(see the log). You can run it again from a shell, or roll back.";
        }

        ctx.Log("System tables upgraded.", LogLevel.Ok);
        return null;
    }

    /// <summary>Runs mariadb-check --repair on the mysql system database, when the tool exists.</summary>
    /// <param name="ctx">The update dialog's context; the last lines of the tool's output are logged.</param>
    /// <param name="root">The XAMPP folder, used as the tool's working folder.</param>
    /// <param name="newBin">The new build's bin folder.</param>
    /// <param name="environment">Environment for the tool; carries the root password as MYSQL_PWD.</param>
    private static async Task RepairSystemTablesAsync(
        UpdateContext ctx,
        string root,
        string newBin,
        Dictionary<string, string> environment)
    {
        string? checkExe = FileTools.FirstExisting(newBin, "mariadb-check.exe", "mysqlcheck.exe");
        if (checkExe is null)
        {
            return;
        }

        ctx.Status("Repairing the system tables…");
        Shell.RunResult repair = await Shell.RunAsync(
            checkExe,
            $"{ctx.MySqlConnectionArgs} --repair --databases mysql",
            root,
            TimeSpan.FromMinutes(10),
            environment);
        ctx.LogOutput(repair, repair.ExitCode == 0 ? LogLevel.Info : LogLevel.Error, 4);
    }

    /// <summary>Starts the server again after a failed or undone update, without waiting for it.</summary>
    /// <param name="ctx">The update dialog's context.</param>
    /// <param name="wasRunning">False leaves the server stopped; true starts it unless it is already running.</param>
    private static async Task StartAgainAsync(UpdateContext ctx, bool wasRunning)
    {
        if (wasRunning && ctx.Main.FindModule(Module) is { IsRunning: false } server)
        {
            await server.StartAfterMaintenanceAsync();
        }
    }

    /// <summary>Fixes my.ini settings that newer servers reject or warn about.</summary>
    /// <param name="ctx">The update dialog's context; the changes made are logged.</param>
    /// <param name="ini">Full path of my.ini; nothing happens when it does not exist.</param>
    private static void FixMyIni(UpdateContext ctx, string ini)
    {
        if (!File.Exists(ini))
        {
            return;
        }

        string[] lines = File.ReadAllLines(ini);
        var changes = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (KeyBuffer().IsMatch(lines[i]))
            {
                lines[i] = KeyBuffer().Replace(lines[i], "${indent}key_buffer_size${rest}");
                changes.Add("key_buffer → key_buffer_size");
            }
            else if (RemovedOption().Match(lines[i]) is { Success: true } removedMatch)
            {
                lines[i] = "# " + lines[i] + "    # removed in newer MariaDB";
                changes.Add($"commented out {removedMatch.Groups["name"].Value}");
            }
        }

        if (changes.Count == 0)
        {
            return;
        }

        File.WriteAllLines(ini, lines);
        ctx.Log("my.ini: " + string.Join(", ", changes));
    }

    /// <summary>Newer MariaDB only ships mariadb*.exe; keep the classic names for XAMPP scripts and tools.</summary>
    /// <param name="ctx">The update dialog's context; the names added are logged.</param>
    /// <param name="bin">The new build's bin folder; existing classic names are never overwritten.</param>
    private static void AddCompatibilityNames(UpdateContext ctx, string bin)
    {
        (string Classic, string Modern)[] names =
        [
            ("mysqld.exe", "mariadbd.exe"),
            ("mysql.exe", "mariadb.exe"),
            ("mysqladmin.exe", "mariadb-admin.exe"),
            ("mysqldump.exe", "mariadb-dump.exe"),
        ];

        var added = new List<string>();
        foreach ((string classic, string modern) in names)
        {
            string target = Path.Combine(bin, classic);
            string source = Path.Combine(bin, modern);
            if (File.Exists(target) || !File.Exists(source))
            {
                continue;
            }

            File.Copy(source, target);
            added.Add(classic);
        }

        if (added.Count > 0)
        {
            ctx.Log("Added classic names for XAMPP tools: " + string.Join(", ", added));
        }
    }

    [GeneratedRegex(@"^(?<indent>\s*)key_buffer(?<rest>\s*=.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex KeyBuffer();

    [GeneratedRegex(
        @"^\s*(?<name>innodb_additional_mem_pool_size|innodb_file_format|innodb_large_prefix|" +
        @"innodb_file_format_max|innodb_support_xa)\s*=",
        RegexOptions.IgnoreCase)]
    private static partial Regex RemovedOption();

    [GeneratedRegex(@"Aria|wrong checksum|error 176", RegexOptions.IgnoreCase)]
    private static partial Regex AriaProblem();
}
