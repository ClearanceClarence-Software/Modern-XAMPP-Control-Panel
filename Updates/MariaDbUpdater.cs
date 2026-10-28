using System.Text.RegularExpressions;
using XamppControl.Core;
using XamppControl.Modules;

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

    public static async Task<UpdateResult> InstallAsync(UpdateContext ctx, string zipPath, bool makeDump)
    {
        var root = ctx.Root;
        var mysqlDir = Path.Combine(root, "mysql");
        var staging = Path.Combine(root, "_update_mysql");
        var port = ctx.Settings.Services.MySqlPort;
        var env = ctx.MySqlEnvironment();

        if (ctx.Main.FindModule(Module) is not { } mysql)
            return UpdateResult.Fail("The MySQL module isn't available.");
        if (mysql.IsServiceInstalled)
        {
            return UpdateResult.Fail("MySQL is installed as a Windows service. " +
                "Remove the service (Service checkbox) before updating.");
        }
        if (!Directory.Exists(mysqlDir))
            return UpdateResult.Fail($"No mysql folder in \"{root}\".");

        // 1. Unpack and check the new build
        ctx.Status("Unpacking MariaDB…");
        ctx.Progress(null);
        string content;
        try
        {
            content = await Task.Run(() => FileTools.ExtractToStaging(zipPath, staging));
        }
        catch (Exception ex)
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail("Couldn't unpack the zip: " + ex.Message);
        }

        var newServer = FileTools.FirstExisting(Path.Combine(content, "bin"), "mariadbd.exe", "mysqld.exe");
        if (newServer is null)
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail("This zip has no bin\\mariadbd.exe, so it isn't a MariaDB Windows x64 build.");
        }

        var oldBin = Path.Combine(mysqlDir, "bin");
        var oldServer = FileTools.FirstExisting(oldBin, "mariadbd.exe", "mysqld.exe");
        var newVersion = XamppPaths.FileVersion(newServer) ?? "new";
        var oldVersion = XamppPaths.FileVersion(oldServer) ?? "unknown";
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

        // 2. The current server has to be running for the dump and the full clean shutdown
        var wasRunning = mysql.IsRunning;
        if (!wasRunning)
        {
            ctx.Status($"Starting MariaDB {oldVersion} so it can be shut down cleanly…");
            await mysql.StartAfterMaintenanceAsync();
            if (!await UpdateContext.WaitForPortAsync(port, TimeSpan.FromSeconds(30)))
            {
                FileTools.TryDelete(staging);
                return UpdateResult.Fail($"The current MariaDB didn't start on port {port}, so it can't be shut down " +
                    "cleanly. Fix that first (the log shows why). Nothing was changed.");
            }
        }

        async Task<UpdateResult> Abort(string message)
        {
            FileTools.TryDelete(staging);
            if (!wasRunning && ctx.Main.FindModule(Module) is { IsRunning: true } m)
                await m.StopForMaintenanceAsync();
            return UpdateResult.Fail(message);
        }

        // 3. Optional SQL dump of everything
        if (makeDump)
        {
            var dumpExe = FileTools.FirstExisting(oldBin, "mariadb-dump.exe", "mysqldump.exe");
            if (dumpExe is null)
            {
                return await Abort("No mariadb-dump.exe or mysqldump.exe in the current install, " +
                    "so no dump could be made. Untick the dump option to continue without one.");
            }

            var dumpDir = Path.Combine(root, "mysql_dumps");
            Directory.CreateDirectory(dumpDir);
            var dumpFile = Path.Combine(dumpDir, $"all-databases_{FileTools.Safe(oldVersion)}_{FileTools.Stamp()}.sql");

            ctx.Status("Backing up all databases to an SQL dump…");
            var dump = await Shell.RunAsync(dumpExe,
                $"{ctx.MySqlConnectionArgs} --all-databases --routines --events --triggers --single-transaction " +
                $"--result-file=\"{dumpFile}\"",
                root, TimeSpan.FromMinutes(30), env);
            if (dump.ExitCode != 0)
            {
                ctx.LogOutput(dump, LogLevel.Error);
                return await Abort("The SQL dump failed (check the root password under " +
                    "Config › Service and Port Settings). Nothing was changed.");
            }
            ctx.Log($"SQL dump: \"{dumpFile}\" ({FileTools.FormatSize(new FileInfo(dumpFile).Length)})", LogLevel.Ok);
        }

        // 4. Full clean shutdown: flushes the redo log so a newer server can take over the data files
        ctx.Status("Shutting down MariaDB completely…");
        var client = FileTools.FirstExisting(oldBin, "mariadb.exe", "mysql.exe");
        if (client is null)
            return await Abort("No mariadb.exe or mysql.exe client in the current install. Nothing was changed.");

        var slow = await Shell.RunAsync(client, $"{ctx.MySqlConnectionArgs} -e \"SET GLOBAL innodb_fast_shutdown=0\"",
            root, TimeSpan.FromSeconds(20), env);
        if (slow.ExitCode != 0)
        {
            ctx.LogOutput(slow, LogLevel.Error);
            return await Abort("Couldn't prepare a full shutdown (check the root password under " +
                "Config › Service and Port Settings). Nothing was changed.");
        }

        // Make sure the panel sees the server as running, so the stop below really shuts it down.
        if (!await ctx.WaitForModuleAsync(Module, running: true, TimeSpan.FromSeconds(10)) ||
            !await ctx.Main.FindModule(Module)!.StopForMaintenanceAsync() ||
            await UpdateContext.PortOpenAsync(port))
        {
            return await Abort("MariaDB didn't shut down cleanly. Nothing was changed.");
        }

        // 5. Swap folders
        var backup = VersionStore.ParkPath(root, Component.MariaDb, oldVersion);
        ctx.Status($"Keeping MariaDB {oldVersion} as an installed version…");
        try
        {
            Directory.Move(mysqlDir, backup);
        }
        catch (Exception ex)
        {
            FileTools.TryDelete(staging);
            await RestartAsync(ctx, wasRunning);
            return UpdateResult.Fail($"Couldn't move the mysql folder ({ex.Message}). Close programs that use it " +
                "(HeidiSQL, a terminal in that folder, …) and try again. Nothing was changed.");
        }

        try
        {
            Directory.Move(content, mysqlDir);
        }
        catch (Exception ex)
        {
            Directory.Move(backup, mysqlDir);
            FileTools.TryDelete(staging);
            await RestartAsync(ctx, wasRunning);
            return UpdateResult.Fail(
                $"Couldn't put the new MariaDB in place ({ex.Message}). The old one was restored.");
        }
        FileTools.TryDelete(staging);
        ctx.Log($"MariaDB {oldVersion} stays installed, with its own copy of your databases, in \"{backup}\".");

        async Task Rollback()
        {
            ctx.Status($"Rolling back to MariaDB {oldVersion}…");
            if (ctx.Main.FindModule(Module) is { IsRunning: true } running)
            {
                if (!await running.StopForMaintenanceAsync())
                    await running.Driver.ForceStopAsync(ctx.Main);
            }

            var parked = VersionStore.ParkPath(root, Component.MariaDb, newVersion);
            Directory.Move(mysqlDir, parked);
            Directory.Move(backup, mysqlDir);
            ctx.Log($"MariaDB {oldVersion} is active again with its own data. " +
                $"MariaDB {newVersion} stays installed in \"{parked}\".", LogLevel.Ok);

            await ctx.Main.ReloadModulesAsync();
            await RestartAsync(ctx, wasRunning);
        }

        var rollbackLabel = $"Switch back to MariaDB {oldVersion}";
        UpdateResult FailWithRollback(string message) => new(false, message, Rollback, rollbackLabel);

        // 6. Carry over data (copied, so the backup stays a pristine copy), my.ini, scripts, bat files
        try
        {
            var newData = Path.Combine(mysqlDir, "data");
            var oldData = Path.Combine(backup, "data");
            if (Directory.Exists(oldData))
            {
                ctx.Status("Copying the data folder (this can take a while)…");
                if (Directory.Exists(newData))
                    Directory.Delete(newData, recursive: true);
                await Task.Run(() => FileTools.CopyDirectory(oldData, newData));
            }

            var oldIni = Path.Combine(backup, "bin", "my.ini");
            var newIni = Path.Combine(mysqlDir, "bin", "my.ini");
            if (File.Exists(oldIni))
                File.Copy(oldIni, newIni, overwrite: true);

            var copied = await Task.Run(() => FileTools.CopyMissing(backup, mysqlDir));
            copied.Remove("data");
            if (copied.Count > 0)
                ctx.Log("Kept from the old install: " + string.Join(", ", copied));

            FixMyIni(ctx, newIni);
            AddCompatibilityNames(ctx, Path.Combine(mysqlDir, "bin"));
        }
        catch (Exception ex)
        {
            await ctx.Main.ReloadModulesAsync();
            return FailWithRollback("Copying files into the new install failed: " + ex.Message);
        }

        await ctx.Main.ReloadModulesAsync();

        // 7. Start the new server
        ctx.Status($"Starting MariaDB {newVersion}…");
        await ctx.Main.FindModule(Module)!.StartAfterMaintenanceAsync();
        if (!await UpdateContext.WaitForPortAsync(port, TimeSpan.FromSeconds(60)))
            return FailWithRollback($"MariaDB {newVersion} didn't start. The log shows why; you can roll back.");

        // 8. Upgrade the system tables, repairing Aria tables if they report checksum errors
        var newBin = Path.Combine(mysqlDir, "bin");
        var upgradeExe = FileTools.FirstExisting(newBin, "mariadb-upgrade.exe", "mysql_upgrade.exe");
        if (upgradeExe is null)
            return FailWithRollback("The new install has no mariadb-upgrade.exe.");

        ctx.Status("Upgrading the system tables…");
        var upgrade = await Shell.RunAsync(upgradeExe, ctx.MySqlConnectionArgs, root, TimeSpan.FromMinutes(15), env);
        var output = upgrade.Output + upgrade.Error;

        if (upgrade.ExitCode != 0 && AriaProblem().IsMatch(output))
        {
            ctx.Log("System tables report Aria checksum errors; repairing them.", LogLevel.Warn);
            var check = FileTools.FirstExisting(newBin, "mariadb-check.exe", "mysqlcheck.exe");
            if (check is not null)
            {
                ctx.Status("Repairing the system tables…");
                var repair = await Shell.RunAsync(check, $"{ctx.MySqlConnectionArgs} --repair --databases mysql",
                    root, TimeSpan.FromMinutes(10), env);
                ctx.LogOutput(repair, repair.ExitCode == 0 ? LogLevel.Info : LogLevel.Error, 4);
            }
            ctx.Status("Upgrading the system tables again…");
            upgrade = await Shell.RunAsync(
                upgradeExe, $"{ctx.MySqlConnectionArgs} --force", root, TimeSpan.FromMinutes(15), env);
        }

        if (upgrade.ExitCode != 0)
        {
            ctx.LogOutput(upgrade, LogLevel.Error);
            return FailWithRollback($"MariaDB {newVersion} is running, but mariadb-upgrade reported errors " +
                "(see the log). You can run it again from a shell, or roll back.");
        }
        ctx.Log("System tables upgraded.", LogLevel.Ok);

        // 9. Leave the server as it was before the update
        if (!wasRunning && ctx.Main.FindModule(Module) is { } after)
            await after.StopForMaintenanceAsync();

        await ctx.Main.ReloadModulesAsync();
        return new UpdateResult(true,
            $"MariaDB {newVersion} is installed and active, with a copy of your databases. " +
            $"MariaDB {oldVersion} stays installed.",
            Rollback, rollbackLabel);
    }

    private static async Task RestartAsync(UpdateContext ctx, bool wasRunning)
    {
        if (wasRunning && ctx.Main.FindModule(Module) is { IsRunning: false } m)
            await m.StartAfterMaintenanceAsync();
    }

    /// <summary>Fixes my.ini settings that newer servers reject or warn about.</summary>
    private static void FixMyIni(UpdateContext ctx, string ini)
    {
        if (!File.Exists(ini))
            return;

        var lines = File.ReadAllLines(ini);
        var changes = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (KeyBuffer().IsMatch(lines[i]))
            {
                lines[i] = KeyBuffer().Replace(lines[i], "${indent}key_buffer_size${rest}");
                changes.Add("key_buffer → key_buffer_size");
            }
            else if (RemovedOption().Match(lines[i]) is { Success: true } m)
            {
                lines[i] = "# " + lines[i] + "    # removed in newer MariaDB";
                changes.Add($"commented out {m.Groups["name"].Value}");
            }
        }

        if (changes.Count == 0)
            return;
        File.WriteAllLines(ini, lines);
        ctx.Log("my.ini: " + string.Join(", ", changes));
    }

    /// <summary>Newer MariaDB only ships mariadb*.exe; keep the classic names for XAMPP scripts and tools.</summary>
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
        foreach (var (classic, modern) in names)
        {
            var target = Path.Combine(bin, classic);
            var source = Path.Combine(bin, modern);
            if (File.Exists(target) || !File.Exists(source))
                continue;
            File.Copy(source, target);
            added.Add(classic);
        }
        if (added.Count > 0)
            ctx.Log("Added classic names for XAMPP tools: " + string.Join(", ", added));
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
