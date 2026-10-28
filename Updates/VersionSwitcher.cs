using XamppControl.Modules;

namespace XamppControl.Updates;

/// <summary>Makes a stored PHP or MariaDB build the active one, stopping and restarting the server around it.</summary>
public static class VersionSwitcher
{
    /// <summary>Swaps a parked build into the active folder, restarting its server when it was running.</summary>
    /// <param name="ctx">Reports status and log lines, and gives access to the panel's modules.</param>
    /// <param name="target">The build to activate; an active build is refused.</param>
    /// <returns>
    /// The outcome; on success, and when the switch worked but the server did not start, it carries a rollback to the
    /// previous build.
    /// </returns>
    public static Task<UpdateResult> SwitchAsync(UpdateContext ctx, InstalledVersion target) =>
        target.Component == Component.Php ? SwitchPhpAsync(ctx, target) : SwitchMariaDbAsync(ctx, target);

    /// <summary>Activates a parked PHP build with Apache stopped around the swap.</summary>
    /// <param name="ctx">Reports status and log lines, and gives access to the panel's modules.</param>
    /// <param name="target">A PHP build; it must be parked and include php8apache2_4.dll.</param>
    /// <returns>The outcome as described for <see cref="SwitchAsync"/>.</returns>
    private static async Task<UpdateResult> SwitchPhpAsync(UpdateContext ctx, InstalledVersion target)
    {
        if (PhpSwitchBlocker(ctx, target) is { } blocker)
        {
            return UpdateResult.Fail(blocker);
        }

        string activeDir = VersionStore.ActiveDir(ctx.Root, Component.Php);
        string currentVersion = VersionStore.DetectVersion(Component.Php, activeDir) ?? "unknown";
        bool apacheWasRunning = ctx.Main.FindModule("Apache")?.IsRunning == true;

        if (apacheWasRunning)
        {
            ctx.Status("Stopping Apache…");
            if (!await StopModuleAsync(ctx, "Apache"))
            {
                return UpdateResult.Fail("Apache could not be stopped, so nothing was changed.");
            }
        }

        ctx.Status($"Switching to PHP {target.Version}…");
        string? parkedPath = await SwapAsync(ctx, Component.Php, activeDir, target.Path, currentVersion);
        if (parkedPath is null)
        {
            await StartIfWantedAsync(ctx, "Apache", apacheWasRunning);
            return UpdateResult.Fail("Couldn't swap the php folders. Close terminals, editors or Composer runs " +
                "that use PHP and try again. Nothing was changed.");
        }

        DllSync.Sync(ctx, activeDir, Path.Combine(ctx.Root, "apache", "bin"));
        await ctx.Main.ReloadModulesAsync();

        var previous = new InstalledVersion(Component.Php, currentVersion, parkedPath, IsActive: false);
        Func<Task> rollback = RollbackTo(ctx, previous);
        string rollbackLabel = $"Switch back to PHP {currentVersion}";

        if (apacheWasRunning)
        {
            ctx.Status("Starting Apache…");
            await StartIfWantedAsync(ctx, "Apache", true);
            bool apacheStarted = await ctx.WaitForModuleAsync("Apache", running: true, TimeSpan.FromSeconds(10));
            if (!apacheStarted)
            {
                return new UpdateResult(false,
                    $"Switched to PHP {target.Version}, but Apache didn't start with it (see the log).",
                    rollback,
                    rollbackLabel);
            }
        }

        return new UpdateResult(true,
            $"PHP {target.Version} is now active. PHP {currentVersion} stays installed.",
            rollback,
            rollbackLabel);
    }

    /// <summary>Why a PHP build cannot be switched to right now.</summary>
    /// <param name="ctx">Gives access to the Apache module.</param>
    /// <param name="target">The PHP build to check.</param>
    /// <returns>A message for the user; null when nothing blocks the switch.</returns>
    private static string? PhpSwitchBlocker(UpdateContext ctx, InstalledVersion target)
    {
        if (target.IsActive)
        {
            return $"PHP {target.Version} is already active.";
        }
        if (!File.Exists(Path.Combine(target.Path, "php8apache2_4.dll")))
        {
            return $"PHP {target.Version} has no php8apache2_4.dll. " +
                "Only PHP 8 Thread Safe builds work with XAMPP's Apache config.";
        }
        if (ctx.Main.FindModule("Apache") is { IsServiceInstalled: true })
        {
            return "Apache is installed as a Windows service. " +
                "Remove the service (Service checkbox) before switching PHP.";
        }
        return null;
    }

    /// <summary>Activates a parked MariaDB build after a clean shutdown of the running server.</summary>
    /// <param name="ctx">Reports status and log lines, and gives access to the panel's modules and MySQL port.</param>
    /// <param name="target">A MariaDB build; it must be parked, and it keeps its own data folder.</param>
    /// <returns>The outcome as described for <see cref="SwitchAsync"/>.</returns>
    private static async Task<UpdateResult> SwitchMariaDbAsync(UpdateContext ctx, InstalledVersion target)
    {
        if (MariaDbSwitchBlocker(ctx, target) is { } blocker)
        {
            return UpdateResult.Fail(blocker);
        }

        string activeDir = VersionStore.ActiveDir(ctx.Root, Component.MariaDb);
        int port = ctx.Settings.Services.MySqlPort;
        string currentVersion = VersionStore.DetectVersion(Component.MariaDb, activeDir) ?? "unknown";
        bool mySqlWasRunning = ctx.Main.FindModule("MySQL")?.IsRunning == true;

        if (mySqlWasRunning)
        {
            ctx.Status($"Shutting down MariaDB {currentVersion} cleanly…");
            bool stoppedCleanly = await StopModuleAsync(ctx, "MySQL") && !await UpdateContext.PortOpenAsync(port);
            if (!stoppedCleanly)
            {
                return UpdateResult.Fail("MariaDB didn't shut down cleanly, so nothing was changed.");
            }
        }

        ctx.Status($"Switching to MariaDB {target.Version}…");
        string? parkedPath = await SwapAsync(ctx, Component.MariaDb, activeDir, target.Path, currentVersion);
        if (parkedPath is null)
        {
            await StartIfWantedAsync(ctx, "MySQL", mySqlWasRunning);
            return UpdateResult.Fail("Couldn't swap the mysql folders. Close programs that use them and try again. " +
                "Nothing was changed.");
        }

        await ctx.Main.ReloadModulesAsync();
        ctx.Log($"MariaDB {target.Version} uses its own databases (its data folder); " +
            $"MariaDB {currentVersion} keeps its own.");

        var previous = new InstalledVersion(Component.MariaDb, currentVersion, parkedPath, IsActive: false);
        Func<Task> rollback = RollbackTo(ctx, previous);
        string rollbackLabel = $"Switch back to MariaDB {currentVersion}";

        if (mySqlWasRunning)
        {
            ctx.Status($"Starting MariaDB {target.Version}…");
            await StartIfWantedAsync(ctx, "MySQL", true);
            if (!await UpdateContext.WaitForPortAsync(port, TimeSpan.FromSeconds(60)))
            {
                return new UpdateResult(false,
                    $"Switched to MariaDB {target.Version}, but it didn't start (see the log).",
                    rollback,
                    rollbackLabel);
            }
        }

        return new UpdateResult(true,
            $"MariaDB {target.Version} is now active. MariaDB {currentVersion} stays installed.",
            rollback,
            rollbackLabel);
    }

    /// <summary>Why a MariaDB build cannot be switched to right now.</summary>
    /// <param name="ctx">Gives access to the MySQL module.</param>
    /// <param name="target">The MariaDB build to check.</param>
    /// <returns>A message for the user; null when nothing blocks the switch.</returns>
    private static string? MariaDbSwitchBlocker(UpdateContext ctx, InstalledVersion target)
    {
        if (target.IsActive)
        {
            return $"MariaDB {target.Version} is already active.";
        }
        if (ctx.Main.FindModule("MySQL") is { IsServiceInstalled: true })
        {
            return "MySQL is installed as a Windows service. " +
                "Remove the service (Service checkbox) before switching.";
        }
        return null;
    }

    /// <summary>A rollback that switches back to the build that was active before.</summary>
    /// <param name="ctx">The context of the switch, reused when the rollback runs.</param>
    /// <param name="previous">The previously active build, now parked.</param>
    /// <returns>
    /// A function that runs the switch back and throws <see cref="InvalidOperationException"/> with its message when
    /// it fails.
    /// </returns>
    private static Func<Task> RollbackTo(UpdateContext ctx, InstalledVersion previous)
    {
        return async () =>
        {
            UpdateResult result = await SwitchAsync(ctx, previous);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Message);
            }
        };
    }

    /// <summary>
    /// Parks the active folder in the store and moves the target into place. Returns where the old one went.
    /// </summary>
    /// <param name="ctx">Receives a log line on success and an error line on failure.</param>
    /// <param name="component">The server whose store receives the active folder.</param>
    /// <param name="activeDir">The active folder (php or mysql); it may be missing, and then nothing is parked.</param>
    /// <param name="target">The parked build's folder to move into <paramref name="activeDir"/>.</param>
    /// <param name="currentVersion">The active build's version, used to name its folder in the store.</param>
    /// <returns>
    /// Where the old active folder now is, or <paramref name="target"/> when there was none; null when the swap
    /// failed (the old folder is moved back when that is possible).
    /// </returns>
    private static async Task<string?> SwapAsync(
        UpdateContext ctx,
        Component component,
        string activeDir,
        string target,
        string currentVersion)
    {
        string? parkedPath = Directory.Exists(activeDir)
            ? VersionStore.ParkPath(ctx.Root, component, currentVersion)
            : null;

        try
        {
            await Task.Run(() => MoveIntoPlace(activeDir, target, parkedPath));
            if (parkedPath is not null)
            {
                ctx.Log($"Moved the previous version to \"{parkedPath}\".");
            }
            return parkedPath ?? target;
        }
        catch (Exception ex)
        {
            // Any failure must reach the caller as null so it restarts the server it stopped;
            // the reason goes to the log.
            ctx.Log("Swap failed: " + ex.Message, LogLevel.Error);
            return null;
        }
    }

    /// <summary>Parks the active folder, then moves the target into its place; undoes the park on failure.</summary>
    /// <param name="activeDir">The active folder.</param>
    /// <param name="target">The folder to move into <paramref name="activeDir"/>.</param>
    /// <param name="parkedPath">Where to park the active folder; null when there is no active folder to park.</param>
    private static void MoveIntoPlace(string activeDir, string target, string? parkedPath)
    {
        if (parkedPath is not null)
        {
            Directory.Move(activeDir, parkedPath);
        }

        try
        {
            Directory.Move(target, activeDir);
        }
        catch
        {
            // Put the old build back so the panel keeps a working install, then let the caller log the failure.
            if (parkedPath is not null)
            {
                Directory.Move(parkedPath, activeDir);
            }
            throw;
        }
    }

    /// <summary>Stops a panel module for maintenance.</summary>
    /// <param name="ctx">Gives access to the panel's modules.</param>
    /// <param name="moduleName">The module's name in the panel, e.g. "Apache" or "MySQL".</param>
    /// <returns>True when the module is stopped afterwards; false if it still runs or is not in the panel.</returns>
    private static async Task<bool> StopModuleAsync(UpdateContext ctx, string moduleName)
    {
        return ctx.Main.FindModule(moduleName) is { } module && await module.StopForMaintenanceAsync();
    }

    /// <summary>Starts a panel module again after maintenance when it was running before and is stopped now.</summary>
    /// <param name="ctx">Gives access to the panel's modules.</param>
    /// <param name="moduleName">The module's name in the panel, e.g. "Apache" or "MySQL".</param>
    /// <param name="startWanted">True starts the module if it is stopped; false does nothing.</param>
    /// <returns>A task that completes when the start attempt has finished.</returns>
    private static async Task StartIfWantedAsync(UpdateContext ctx, string moduleName, bool startWanted)
    {
        if (startWanted && ctx.Main.FindModule(moduleName) is { IsRunning: false } module)
        {
            await module.StartAfterMaintenanceAsync();
        }
    }
}
