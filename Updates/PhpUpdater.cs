using System.Text.RegularExpressions;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.ViewModels;

namespace XamppControl.Updates;

/// <summary>
/// Replaces XAMPP's php folder with a Thread Safe x64 build:
/// stop Apache → park the current php\ in versions\php → unpack the new build →
/// carry over php.ini, PEAR, extras, logs, tmp →
/// disable extensions the new build lacks → copy shared DLLs (OpenSSL, libssh2, …) into apache\bin → start Apache.
/// </summary>
public static partial class PhpUpdater
{
    private const string ApacheModule = "Apache";

    /// <summary>Installs a PHP build from a zip as the active php folder, keeping the old one installed.</summary>
    /// <param name="ctx">The update dialog's context; status, progress and log lines go through it.</param>
    /// <param name="zipPath">Full path of a PHP for Windows zip; it must be a PHP 8 Thread Safe x64 build.</param>
    /// <returns>
    /// Success once the new build is active and Apache (when it was running) came back up; a failure without a
    /// rollback when nothing was changed, or with a rollback to the old version when the new one is in place.
    /// </returns>
    public static async Task<UpdateResult> InstallAsync(UpdateContext ctx, string zipPath)
    {
        string root = ctx.Root;
        string phpDir = Path.Combine(root, "php");
        string apacheBin = Path.Combine(root, "apache", "bin");
        string staging = Path.Combine(root, "_update_php");

        if (ctx.Main.FindModule(ApacheModule) is { IsServiceInstalled: true })
        {
            return UpdateResult.Fail("Apache is installed as a Windows service. " +
                "Remove the service (Service checkbox) before updating PHP.");
        }

        ctx.Status("Unpacking PHP…");
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

        if (BuildProblem(content) is { } buildProblem)
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail(buildProblem);
        }

        string newVersion = XamppPaths.FileVersion(Path.Combine(content, "php.exe")) ?? "new";
        string oldVersion = XamppPaths.FileVersion(Path.Combine(phpDir, "php.exe")) ?? "unknown";

        if (VersionStore.Find(root, Component.Php, newVersion) is { } existing)
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail(existing.IsActive
                ? $"PHP {newVersion} is already the active version."
                : $"PHP {newVersion} is already installed. Use Switch to make it active.");
        }
        ctx.Log($"Installed: PHP {oldVersion}. New: PHP {newVersion}.");

        // Apache loads php8apache2_4.dll, so it has to stop before php\ can be moved.
        bool apacheWasRunning = ctx.Main.FindModule(ApacheModule)?.IsRunning == true;
        if (apacheWasRunning && !await StopApacheAsync(ctx))
        {
            FileTools.TryDelete(staging);
            return UpdateResult.Fail("Apache could not be stopped, so nothing was changed.");
        }

        string backup = VersionStore.ParkPath(root, Component.Php, oldVersion);
        ctx.Status($"Keeping PHP {oldVersion} as an installed version…");
        string? swapFailure = SwapInNewBuild(phpDir, content, backup);
        FileTools.TryDelete(staging);
        if (swapFailure is not null)
        {
            await StartApacheAgainAsync(ctx, apacheWasRunning);
            return UpdateResult.Fail(swapFailure);
        }
        ctx.Log($"PHP {oldVersion} stays installed in \"{backup}\"; you can switch back any time.");

        await CarryOverXamppFilesAsync(ctx, backup, phpDir);
        DisableMissingExtensions(ctx, phpDir, newVersion);
        WarnAboutMissingIniPaths(ctx, phpDir);
        DllSync.Sync(ctx, phpDir, apacheBin);

        // A local function so the rollback keeps this install's root, folders and versions even if the
        // XAMPP folder setting changes before the user clicks it.
        async Task RollBackAsync()
        {
            ctx.Status($"Rolling back to PHP {oldVersion}…");
            bool apacheIsRunning = false;
            if (ctx.Main.FindModule(ApacheModule) is { IsRunning: true } apache)
            {
                apacheIsRunning = true;
                if (!await apache.StopForMaintenanceAsync())
                {
                    throw new InvalidOperationException("Apache could not be stopped.");
                }
            }

            string parked = VersionStore.ParkPath(root, Component.Php, newVersion);
            Directory.Move(phpDir, parked);
            Directory.Move(backup, phpDir);
            DllSync.Sync(ctx, phpDir, apacheBin);
            ctx.Log($"PHP {oldVersion} is active again. PHP {newVersion} stays installed in \"{parked}\".",
                LogLevel.Ok);

            await ctx.Main.ReloadModulesAsync();
            await StartApacheAgainAsync(ctx, apacheIsRunning || apacheWasRunning);
        }

        string rollbackLabel = $"Switch back to PHP {oldVersion}";

        if (apacheWasRunning && !await StartApacheWithNewBuildAsync(ctx))
        {
            await ctx.Main.ReloadModulesAsync();
            return new UpdateResult(false,
                $"PHP {newVersion} is installed, but Apache didn't start with it. " +
                "The log shows why; you can roll back.",
                RollBackAsync,
                rollbackLabel);
        }

        await ctx.Main.ReloadModulesAsync();
        return new UpdateResult(true,
            $"PHP {newVersion} is installed and active. PHP {oldVersion} stays installed.",
            RollBackAsync,
            rollbackLabel);
    }

    /// <summary>Checks that an unpacked zip is a build XAMPP's Apache can load.</summary>
    /// <param name="content">The folder that holds the unpacked build's files (php.exe at its top level).</param>
    /// <returns>A message for the user saying what is wrong, or null when the build is usable.</returns>
    private static string? BuildProblem(string content)
    {
        if (!File.Exists(Path.Combine(content, "php.exe")))
        {
            return "This zip doesn't contain php.exe, so it isn't a PHP for Windows build.";
        }
        if (!File.Exists(Path.Combine(content, "php8apache2_4.dll")))
        {
            return "This zip has no php8apache2_4.dll. " +
                "XAMPP needs a PHP 8 Thread Safe x64 build (not Non Thread Safe).";
        }
        return null;
    }

    /// <summary>Stops the running Apache for maintenance and logs that it does.</summary>
    /// <param name="ctx">The update dialog's context; Apache must have been seen running just before.</param>
    /// <returns>False when Apache is still running afterwards.</returns>
    private static async Task<bool> StopApacheAsync(UpdateContext ctx)
    {
        ctx.Status("Stopping Apache…");
        ModuleViewModel apache = ctx.Main.FindModule(ApacheModule)
            ?? throw new InvalidOperationException("The Apache module isn't available.");
        return await apache.StopForMaintenanceAsync();
    }

    /// <summary>Parks the current php folder at <paramref name="backup"/> and moves the new build in.</summary>
    /// <param name="phpDir">XAMPP's php folder; a missing one is not an error (nothing is parked).</param>
    /// <param name="content">The unpacked build's folder inside the staging folder.</param>
    /// <param name="backup">The free folder in the version store that receives the current php folder.</param>
    /// <returns>
    /// A message for the user when a move failed, with the old php folder back in place; null when the new build
    /// is in place.
    /// </returns>
    private static string? SwapInNewBuild(string phpDir, string content, string backup)
    {
        try
        {
            if (Directory.Exists(phpDir))
            {
                Directory.Move(phpDir, backup);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't move the php folder ({ex.Message}). Close terminals, editors " +
                "or Composer runs that use PHP and try again. Nothing was changed.";
        }

        try
        {
            Directory.Move(content, phpDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (Directory.Exists(backup))
            {
                Directory.Move(backup, phpDir);
            }
            return $"Couldn't put the new PHP in place ({ex.Message}). The old PHP was restored.";
        }

        return null;
    }

    /// <summary>
    /// Copies XAMPP's own files (php.ini, PEAR, extras, logs, tmp, …) from the parked build into the new one,
    /// never overwriting what the new build ships.
    /// </summary>
    /// <param name="ctx">The update dialog's context; what was carried over is logged.</param>
    /// <param name="backup">The parked php folder; when it does not exist, only the php.ini fallback runs.</param>
    /// <param name="phpDir">XAMPP's php folder, already holding the new build.</param>
    private static async Task CarryOverXamppFilesAsync(UpdateContext ctx, string backup, string phpDir)
    {
        ctx.Status("Carrying over php.ini and XAMPP's PHP files…");
        if (Directory.Exists(backup))
        {
            List<string> copied = await Task.Run(() => FileTools.CopyMissing(backup, phpDir));
            if (copied.Count > 0)
            {
                ctx.Log("Kept from the old PHP: " + string.Join(", ", copied));
            }

            // Folders both builds have (extras holds XAMPP's browscap.ini, which php.ini requires).
            // ext, dev and lib are skipped: their DLLs only work with the PHP version they came with.
            List<string> merged = await Task.Run(
                () => FileTools.MergeMissingFiles(backup, phpDir, "ext", "dev", "lib"));
            if (merged.Count > 0)
            {
                ctx.Log($"Added {merged.Count} missing files to shared folders: " +
                        string.Join(", ", merged.Take(8)) + (merged.Count > 8 ? ", …" : ""));
            }
        }

        UseDevelopmentIniIfMissing(ctx, phpDir);
    }

    /// <summary>Copies php.ini-development to php.ini when the folder has no php.ini at all.</summary>
    /// <param name="ctx">The update dialog's context; a warning is logged when the copy is made.</param>
    /// <param name="phpDir">XAMPP's php folder; nothing happens when it has neither file.</param>
    private static void UseDevelopmentIniIfMissing(UpdateContext ctx, string phpDir)
    {
        if (File.Exists(Path.Combine(phpDir, "php.ini")))
        {
            return;
        }

        string developmentIni = Path.Combine(phpDir, "php.ini-development");
        if (!File.Exists(developmentIni))
        {
            return;
        }

        File.Copy(developmentIni, Path.Combine(phpDir, "php.ini"));
        ctx.Log("No php.ini found, so php.ini-development was used. Review it before relying on it.",
            LogLevel.Warn);
    }

    /// <summary>Starts Apache after the swap and waits until the panel sees it running.</summary>
    /// <param name="ctx">The update dialog's context.</param>
    /// <returns>False when Apache isn't running within 10 seconds, usually because the new PHP won't load.</returns>
    private static async Task<bool> StartApacheWithNewBuildAsync(UpdateContext ctx)
    {
        ctx.Status("Starting Apache with the new PHP…");
        ModuleViewModel apache = ctx.Main.FindModule(ApacheModule)
            ?? throw new InvalidOperationException("The Apache module isn't available.");
        await apache.StartAfterMaintenanceAsync();
        return await ctx.WaitForModuleAsync(ApacheModule, running: true, TimeSpan.FromSeconds(10));
    }

    /// <summary>Starts Apache again after a failed or undone update, without waiting for it.</summary>
    /// <param name="ctx">The update dialog's context.</param>
    /// <param name="wasRunning">False leaves Apache stopped; true starts it unless it is already running.</param>
    private static async Task StartApacheAgainAsync(UpdateContext ctx, bool wasRunning)
    {
        if (wasRunning && ctx.Main.FindModule(ApacheModule) is { IsRunning: false } apache)
        {
            await apache.StartAfterMaintenanceAsync();
        }
    }

    /// <summary>
    /// Comments out extension= lines whose DLL isn't in the new build (e.g. a separately installed Xdebug).
    /// </summary>
    /// <param name="ctx">The update dialog's context; disabled extensions are logged as warnings.</param>
    /// <param name="phpDir">XAMPP's php folder; nothing happens when it has no php.ini.</param>
    /// <param name="newVersion">The new build's version, used in the log to say which builds to look for.</param>
    private static void DisableMissingExtensions(UpdateContext ctx, string phpDir, string newVersion)
    {
        string ini = Path.Combine(phpDir, "php.ini");
        if (!File.Exists(ini))
        {
            return;
        }

        string extDir = Path.Combine(phpDir, "ext");
        string[] lines = File.ReadAllLines(ini);
        var disabled = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            Match extensionMatch = ExtensionLine().Match(lines[i]);
            if (!extensionMatch.Success)
            {
                continue;
            }

            string extensionFile = ExtensionFilePath(extDir, extensionMatch.Groups["name"].Value);
            if (File.Exists(extensionFile))
            {
                continue;
            }

            lines[i] = ";" + lines[i] + "    ; disabled by Modern XAMPP Control Panel: not included in the new PHP";
            disabled.Add(Path.GetFileName(extensionFile));
        }

        if (disabled.Count == 0)
        {
            return;
        }

        File.WriteAllLines(ini, lines);
        ctx.Log($"Disabled in php.ini (not shipped with PHP {newVersion}): {string.Join(", ", disabled)}.",
            LogLevel.Warn);
        ctx.Log($"Get builds made for PHP {VersionUtil.MajorMinor(newVersion)} TS x64, " +
            "put them in php\\ext and re-enable the lines.", LogLevel.Warn);
    }

    /// <summary>The DLL an extension= value refers to, resolved the way PHP resolves it.</summary>
    /// <param name="extDir">The new build's ext folder.</param>
    /// <param name="extensionName">
    /// The value after <c>extension=</c>: a path (kept as is), a file name, or a bare name such as <c>mbstring</c>.
    /// </param>
    /// <returns>The value itself when it is a path; otherwise a path inside <paramref name="extDir"/>.</returns>
    private static string ExtensionFilePath(string extDir, string extensionName)
    {
        bool isPath = Path.IsPathRooted(extensionName) || extensionName.Contains('\\') || extensionName.Contains('/');
        if (isPath)
        {
            return extensionName;
        }

        string fileName = extensionName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? extensionName
            : $"php_{extensionName}.dll";
        return Path.Combine(extDir, fileName);
    }

    /// <summary>
    /// Flags php.ini settings that point at files inside php\ that don't exist (they can stop PHP from starting).
    /// </summary>
    /// <param name="ctx">The update dialog's context; each missing file is logged as a warning.</param>
    /// <param name="phpDir">XAMPP's php folder; nothing happens when it has no php.ini.</param>
    private static void WarnAboutMissingIniPaths(UpdateContext ctx, string phpDir)
    {
        string ini = Path.Combine(phpDir, "php.ini");
        if (!File.Exists(ini))
        {
            return;
        }

        string phpFolderPrefix = Path.GetFullPath(phpDir).TrimEnd('\\') + "\\";
        foreach (string line in File.ReadLines(ini))
        {
            if (line.TrimStart().StartsWith(';'))
            {
                continue;
            }

            foreach (Match pathMatch in QuotedPath().Matches(line))
            {
                string? missingFile = MissingFileInside(pathMatch.Groups["path"].Value, phpFolderPrefix);
                if (missingFile is not null)
                {
                    ctx.Log($"php.ini refers to a missing file: {missingFile} ({line.Trim()})", LogLevel.Warn);
                }
            }
        }
    }

    /// <summary>Resolves a quoted php.ini path and tells whether it names a missing file inside php\.</summary>
    /// <param name="iniPath">The path as written in php.ini, with forward or back slashes.</param>
    /// <param name="phpFolderPrefix">The php folder's full path ending in a backslash.</param>
    /// <returns>
    /// The full path when it is a file path (it has an extension) inside the php folder that does not exist;
    /// null for relative, unresolvable, outside or existing paths.
    /// </returns>
    private static string? MissingFileInside(string iniPath, string phpFolderPrefix)
    {
        string path = iniPath.Replace('/', '\\');
        if (!Path.IsPathRooted(path))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A path this system can't resolve can't be checked; it is skipped rather than reported.
            return null;
        }

        bool isMissingPhpFile = fullPath.StartsWith(phpFolderPrefix, StringComparison.OrdinalIgnoreCase)
            && Path.HasExtension(fullPath)
            && !File.Exists(fullPath);
        return isMissingPhpFile ? fullPath : null;
    }

    [GeneratedRegex(@"""(?<path>[A-Za-z]:[\\/][^""]+)""")]
    private static partial Regex QuotedPath();

    [GeneratedRegex(@"^\s*(?:zend_)?extension\s*=\s*""?(?<name>[^""\s;]+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex ExtensionLine();
}
