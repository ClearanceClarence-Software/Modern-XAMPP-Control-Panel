using XamppControl.Modules;

namespace XamppControl.Updates;

/// <summary>Makes a stored PHP or MariaDB build the active one, stopping and restarting the server around it.</summary>
public static class VersionSwitcher
{
    public static Task<UpdateResult> SwitchAsync(UpdateContext ctx, InstalledVersion target) =>
        target.Component == Component.Php ? SwitchPhpAsync(ctx, target) : SwitchMariaDbAsync(ctx, target);

    private static async Task<UpdateResult> SwitchPhpAsync(UpdateContext ctx, InstalledVersion target)
    {
        var root = ctx.Root;
        var active = VersionStore.ActiveDir(root, Component.Php);
        var apacheBin = Path.Combine(root, "apache", "bin");

        if (target.IsActive)
            return UpdateResult.Fail($"PHP {target.Version} is already active.");
        if (!File.Exists(Path.Combine(target.Path, "php8apache2_4.dll")))
        {
            return UpdateResult.Fail($"PHP {target.Version} has no php8apache2_4.dll. " +
                "Only PHP 8 Thread Safe builds work with XAMPP's Apache config.");
        }
        if (ctx.Main.FindModule("Apache") is { IsServiceInstalled: true })
        {
            return UpdateResult.Fail("Apache is installed as a Windows service. " +
                "Remove the service (Service checkbox) before switching PHP.");
        }

        var current = VersionStore.DetectVersion(Component.Php, active) ?? "unknown";
        var apacheWasRunning = ctx.Main.FindModule("Apache")?.IsRunning == true;

        if (apacheWasRunning)
        {
            ctx.Status("Stopping Apache…");
            if (!await ctx.Main.FindModule("Apache")!.StopForMaintenanceAsync())
                return UpdateResult.Fail("Apache could not be stopped, so nothing was changed.");
        }

        ctx.Status($"Switching to PHP {target.Version}…");
        var parked = await SwapAsync(ctx, Component.Php, active, target.Path, current);
        if (parked is null)
        {
            await StartAsync(ctx, "Apache", apacheWasRunning);
            return UpdateResult.Fail("Couldn't swap the php folders. Close terminals, editors or Composer runs " +
                "that use PHP and try again. Nothing was changed.");
        }

        DllSync.Sync(ctx, active, apacheBin);
        await ctx.Main.ReloadModulesAsync();

        var back = new InstalledVersion(Component.Php, current, parked, IsActive: false);
        Func<Task> rollback = async () =>
        {
            var result = await SwitchPhpAsync(ctx, back);
            if (!result.Success)
                throw new InvalidOperationException(result.Message);
        };

        if (apacheWasRunning)
        {
            ctx.Status("Starting Apache…");
            await StartAsync(ctx, "Apache", true);
            if (!await ctx.WaitForModuleAsync("Apache", running: true, TimeSpan.FromSeconds(10)))
            {
                return new UpdateResult(false,
                    $"Switched to PHP {target.Version}, but Apache didn't start with it (see the log).",
                    rollback, $"Switch back to PHP {current}");
            }
        }

        return new UpdateResult(true, $"PHP {target.Version} is now active. PHP {current} stays installed.",
            rollback, $"Switch back to PHP {current}");
    }

    private static async Task<UpdateResult> SwitchMariaDbAsync(UpdateContext ctx, InstalledVersion target)
    {
        var root = ctx.Root;
        var active = VersionStore.ActiveDir(root, Component.MariaDb);
        var port = ctx.Settings.Services.MySqlPort;

        if (target.IsActive)
            return UpdateResult.Fail($"MariaDB {target.Version} is already active.");
        if (ctx.Main.FindModule("MySQL") is { IsServiceInstalled: true })
        {
            return UpdateResult.Fail("MySQL is installed as a Windows service. " +
                "Remove the service (Service checkbox) before switching.");
        }

        var current = VersionStore.DetectVersion(Component.MariaDb, active) ?? "unknown";
        var wasRunning = ctx.Main.FindModule("MySQL")?.IsRunning == true;

        if (wasRunning)
        {
            ctx.Status($"Shutting down MariaDB {current} cleanly…");
            if (!await ctx.Main.FindModule("MySQL")!.StopForMaintenanceAsync() ||
                await UpdateContext.PortOpenAsync(port))
            {
                return UpdateResult.Fail("MariaDB didn't shut down cleanly, so nothing was changed.");
            }
        }

        ctx.Status($"Switching to MariaDB {target.Version}…");
        var parked = await SwapAsync(ctx, Component.MariaDb, active, target.Path, current);
        if (parked is null)
        {
            await StartAsync(ctx, "MySQL", wasRunning);
            return UpdateResult.Fail("Couldn't swap the mysql folders. Close programs that use them and try again. " +
                "Nothing was changed.");
        }

        await ctx.Main.ReloadModulesAsync();
        ctx.Log($"MariaDB {target.Version} uses its own databases (its data folder); MariaDB {current} keeps its own.");

        var back = new InstalledVersion(Component.MariaDb, current, parked, IsActive: false);
        Func<Task> rollback = async () =>
        {
            var result = await SwitchMariaDbAsync(ctx, back);
            if (!result.Success)
                throw new InvalidOperationException(result.Message);
        };

        if (wasRunning)
        {
            ctx.Status($"Starting MariaDB {target.Version}…");
            await StartAsync(ctx, "MySQL", true);
            if (!await UpdateContext.WaitForPortAsync(port, TimeSpan.FromSeconds(60)))
            {
                return new UpdateResult(false,
                    $"Switched to MariaDB {target.Version}, but it didn't start (see the log).",
                    rollback, $"Switch back to MariaDB {current}");
            }
        }

        return new UpdateResult(true, $"MariaDB {target.Version} is now active. MariaDB {current} stays installed.",
            rollback, $"Switch back to MariaDB {current}");
    }

    /// <summary>
    /// Parks the active folder in the store and moves the target into place. Returns where the old one went.
    /// </summary>
    private static async Task<string?> SwapAsync(
        UpdateContext ctx, Component c, string active, string target, string currentVersion)
    {
        var parked = Directory.Exists(active) ? VersionStore.ParkPath(ctx.Root, c, currentVersion) : null;
        try
        {
            await Task.Run(() =>
            {
                if (parked is not null)
                    Directory.Move(active, parked);
                try
                {
                    Directory.Move(target, active);
                }
                catch
                {
                    if (parked is not null)
                        Directory.Move(parked, active); // put the old one back
                    throw;
                }
            });
            if (parked is not null)
                ctx.Log($"Moved the previous version to \"{parked}\".");
            return parked ?? target;
        }
        catch (Exception ex)
        {
            ctx.Log("Swap failed: " + ex.Message, LogLevel.Error);
            return null;
        }
    }

    private static async Task StartAsync(UpdateContext ctx, string module, bool wanted)
    {
        if (wanted && ctx.Main.FindModule(module) is { IsRunning: false } m)
            await m.StartAfterMaintenanceAsync();
    }
}
