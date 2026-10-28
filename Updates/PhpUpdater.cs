using System.Text.RegularExpressions;
using XamppControl.Core;
using XamppControl.Modules;

namespace XamppControl.Updates;

/// <summary>
/// Replaces XAMPP's php folder with a Thread Safe x64 build:
/// stop Apache → park the current php\ in versions\php → unpack the new build →
/// carry over php.ini, PEAR, extras, logs, tmp →
/// disable extensions the new build lacks → copy shared DLLs (OpenSSL, libssh2, …) into apache\bin → start Apache.
/// </summary>
public static partial class PhpUpdater
{
    public static async Task<UpdateResult> InstallAsync(UpdateContext ctx, string zipPath)
    {
        var root = ctx.Root;
        var phpDir = Path.Combine(root, "php");
        var apacheBin = Path.Combine(root, "apache", "bin");
        var staging = Path.Combine(root, "_update_php");

        if (ctx.Main.FindModule("Apache") is { IsServiceInstalled: true })
        {
            return UpdateResult.Fail("Apache is installed as a Windows service. " +
                "Remove the service (Service checkbox) before updating PHP.");
        }

        // 1. Unpack and check the new build
        ctx.Status("Unpacking PHP…");
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

        if (!File.Exists(Path.Combine(content, "php.exe")))
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail("This zip doesn't contain php.exe, so it isn't a PHP for Windows build.");
        }
        if (!File.Exists(Path.Combine(content, "php8apache2_4.dll")))
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail("This zip has no php8apache2_4.dll. " +
                "XAMPP needs a PHP 8 Thread Safe x64 build (not Non Thread Safe).");
        }

        var newVersion = XamppPaths.FileVersion(Path.Combine(content, "php.exe")) ?? "new";
        var oldVersion = XamppPaths.FileVersion(Path.Combine(phpDir, "php.exe")) ?? "unknown";

        if (VersionStore.Find(root, Component.Php, newVersion) is { } existing)
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail(existing.IsActive
                ? $"PHP {newVersion} is already the active version."
                : $"PHP {newVersion} is already installed. Use Switch to make it active.");
        }
        ctx.Log($"Installed: PHP {oldVersion}. New: PHP {newVersion}.");

        // 2. Stop Apache so php\ isn't locked
        var apacheWasRunning = ctx.Main.FindModule("Apache")?.IsRunning == true;
        if (apacheWasRunning)
        {
            ctx.Status("Stopping Apache…");
            if (!await ctx.Main.FindModule("Apache")!.StopForMaintenanceAsync())
            {
                FileTools.TryDelete(staging);
                return UpdateResult.Fail("Apache could not be stopped, so nothing was changed.");
            }
        }

        // 3. Swap folders
        var backup = VersionStore.ParkPath(root, Component.Php, oldVersion);
        ctx.Status($"Keeping PHP {oldVersion} as an installed version…");
        try
        {
            if (Directory.Exists(phpDir))
                Directory.Move(phpDir, backup);
        }
        catch (Exception ex)
        {
            FileTools.TryDelete(staging);
            await RestartAsync(ctx, apacheWasRunning);
            return UpdateResult.Fail($"Couldn't move the php folder ({ex.Message}). Close terminals, editors " +
                "or Composer runs that use PHP and try again. Nothing was changed.");
        }

        try
        {
            Directory.Move(content, phpDir);
        }
        catch (Exception ex)
        {
            if (Directory.Exists(backup))
                Directory.Move(backup, phpDir);
            FileTools.TryDelete(staging);
            await RestartAsync(ctx, apacheWasRunning);
            return UpdateResult.Fail($"Couldn't put the new PHP in place ({ex.Message}). The old PHP was restored.");
        }
        FileTools.TryDelete(staging);
        ctx.Log($"PHP {oldVersion} stays installed in \"{backup}\"; you can switch back any time.");

        // 4. Carry over XAMPP's own files (php.ini, PEAR, extras, logs, tmp, …)
        ctx.Status("Carrying over php.ini and XAMPP's PHP files…");
        if (Directory.Exists(backup))
        {
            var copied = await Task.Run(() => FileTools.CopyMissing(backup, phpDir));
            if (copied.Count > 0)
                ctx.Log("Kept from the old PHP: " + string.Join(", ", copied));

            // Folders both builds have (extras holds XAMPP's browscap.ini, which php.ini requires).
            // ext, dev and lib are skipped: their DLLs only work with the PHP version they came with.
            var merged = await Task.Run(() => FileTools.MergeMissingFiles(backup, phpDir, "ext", "dev", "lib"));
            if (merged.Count > 0)
            {
                ctx.Log($"Added {merged.Count} missing files to shared folders: " +
                        string.Join(", ", merged.Take(8)) + (merged.Count > 8 ? ", …" : ""));
            }
        }
        if (!File.Exists(Path.Combine(phpDir, "php.ini")))
        {
            var dev = Path.Combine(phpDir, "php.ini-development");
            if (File.Exists(dev))
            {
                File.Copy(dev, Path.Combine(phpDir, "php.ini"));
                ctx.Log("No php.ini found, so php.ini-development was used. Review it before relying on it.",
                    LogLevel.Warn);
            }
        }

        DisableMissingExtensions(ctx, phpDir, newVersion);
        WarnAboutMissingIniPaths(ctx, phpDir);
        DllSync.Sync(ctx, phpDir, apacheBin);

        async Task Rollback()
        {
            ctx.Status($"Rolling back to PHP {oldVersion}…");
            var apache = ctx.Main.FindModule("Apache");
            var running = apache?.IsRunning == true;
            if (running && !await apache!.StopForMaintenanceAsync())
                throw new InvalidOperationException("Apache could not be stopped.");

            var parked = VersionStore.ParkPath(root, Component.Php, newVersion);
            Directory.Move(phpDir, parked);
            Directory.Move(backup, phpDir);
            DllSync.Sync(ctx, phpDir, apacheBin);
            ctx.Log($"PHP {oldVersion} is active again. PHP {newVersion} stays installed in \"{parked}\".",
                LogLevel.Ok);

            await ctx.Main.ReloadModulesAsync();
            await RestartAsync(ctx, running || apacheWasRunning);
        }

        var rollbackLabel = $"Switch back to PHP {oldVersion}";

        // 5. Start Apache again and make sure it stays up
        if (apacheWasRunning)
        {
            ctx.Status("Starting Apache with the new PHP…");
            await ctx.Main.FindModule("Apache")!.StartAfterMaintenanceAsync();
            if (!await ctx.WaitForModuleAsync("Apache", running: true, TimeSpan.FromSeconds(10)))
            {
                await ctx.Main.ReloadModulesAsync();
                return new UpdateResult(false,
                    $"PHP {newVersion} is installed, but Apache didn't start with it. " +
                    "The log shows why; you can roll back.",
                    Rollback, rollbackLabel);
            }
        }

        await ctx.Main.ReloadModulesAsync();
        return new UpdateResult(true,
            $"PHP {newVersion} is installed and active. PHP {oldVersion} stays installed.",
            Rollback, rollbackLabel);
    }

    private static async Task RestartAsync(UpdateContext ctx, bool wasRunning)
    {
        if (wasRunning && ctx.Main.FindModule("Apache") is { IsRunning: false } apache)
            await apache.StartAfterMaintenanceAsync();
    }

    /// <summary>
    /// Comments out extension= lines whose DLL isn't in the new build (e.g. a separately installed Xdebug).
    /// </summary>
    private static void DisableMissingExtensions(UpdateContext ctx, string phpDir, string newVersion)
    {
        var ini = Path.Combine(phpDir, "php.ini");
        if (!File.Exists(ini))
            return;

        var extDir = Path.Combine(phpDir, "ext");
        var lines = File.ReadAllLines(ini);
        var disabled = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var m = ExtensionLine().Match(lines[i]);
            if (!m.Success)
                continue;

            var value = m.Groups["name"].Value;
            var file = Path.IsPathRooted(value) || value.Contains('\\') || value.Contains('/')
                ? value
                : Path.Combine(extDir, value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? value
                    : $"php_{value}.dll");

            if (File.Exists(file))
                continue;

            lines[i] = ";" + lines[i] + "    ; disabled by Modern XAMPP Control Panel: not included in the new PHP";
            disabled.Add(Path.GetFileName(file));
        }

        if (disabled.Count == 0)
            return;

        File.WriteAllLines(ini, lines);
        ctx.Log($"Disabled in php.ini (not shipped with PHP {newVersion}): {string.Join(", ", disabled)}.",
            LogLevel.Warn);
        ctx.Log($"Get builds made for PHP {VersionUtil.MajorMinor(newVersion)} TS x64, " +
            "put them in php\\ext and re-enable the lines.", LogLevel.Warn);
    }

    /// <summary>
    /// Flags php.ini settings that point at files inside php\ that don't exist (they can stop PHP from starting).
    /// </summary>
    private static void WarnAboutMissingIniPaths(UpdateContext ctx, string phpDir)
    {
        var ini = Path.Combine(phpDir, "php.ini");
        if (!File.Exists(ini))
            return;

        var phpFull = Path.GetFullPath(phpDir).TrimEnd('\\') + "\\";
        foreach (var line in File.ReadLines(ini))
        {
            if (line.TrimStart().StartsWith(';'))
                continue;
            foreach (Match m in QuotedPath().Matches(line))
            {
                var path = m.Groups["path"].Value.Replace('/', '\\');
                if (!Path.IsPathRooted(path))
                    continue;
                string full;
                try { full = Path.GetFullPath(path); } catch { continue; }
                if (full.StartsWith(phpFull, StringComparison.OrdinalIgnoreCase) &&
                    Path.HasExtension(full) && !File.Exists(full))
                {
                    ctx.Log($"php.ini refers to a missing file: {full} ({line.Trim()})", LogLevel.Warn);
                }
            }
        }
    }

    [GeneratedRegex(@"""(?<path>[A-Za-z]:[\\/][^""]+)""")]
    private static partial Regex QuotedPath();

    [GeneratedRegex(@"^\s*(?:zend_)?extension\s*=\s*""?(?<name>[^""\s;]+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex ExtensionLine();
}
