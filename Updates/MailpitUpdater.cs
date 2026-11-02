using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.Php;
using XamppControl.ViewModels;

namespace XamppControl.Updates;

/// <summary>A Mailpit release on GitHub.</summary>
/// <param name="Version">The version without the "v", e.g. "1.29.5".</param>
/// <param name="ZipUrl">Download URL of the Windows x64 zip.</param>
public sealed record MailpitRelease(string Version, string ZipUrl);

/// <summary>
/// Installs or updates Mailpit from its GitHub releases into root\mailpit and points PHP's mail() at it through
/// php.ini.
/// </summary>
public static class MailpitUpdater
{
    private const string LatestApi = "https://api.github.com/repos/axllent/mailpit/releases/latest";
    private const string AssetName = "mailpit-windows-amd64.zip";

    /// <summary>Mailpit's project page, for the Versions window.</summary>
    public const string ProjectUrl = "https://github.com/axllent/mailpit";

    /// <summary>The file next to mailpit.exe that records the installed version, since the exe carries none.</summary>
    public const string VersionFile = "version.txt";

    /// <summary>The installed Mailpit version.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <returns>
    /// The version from the exe's resource or from <see cref="VersionFile"/>; "?" when the exe exists but neither
    /// says; null when Mailpit isn't installed.
    /// </returns>
    public static string? InstalledVersion(string root)
    {
        string folder = Path.Combine(root, MailpitDriver.FolderName);
        string exe = Path.Combine(folder, MailpitDriver.ExeName);
        if (!File.Exists(exe))
        {
            return null;
        }
        if (XamppPaths.FileVersion(exe) is { } resourceVersion)
        {
            return resourceVersion;
        }

        string versionFile = Path.Combine(folder, VersionFile);
        try
        {
            if (File.Exists(versionFile) && VersionUtil.FromText(File.ReadAllText(versionFile)) is { } recorded)
            {
                return recorded;
            }
        }
        catch (IOException)
        {
            // A locked or unreadable note only loses the version text, not the install.
        }
        return "?";
    }

    /// <summary>
    /// Asks mailpit.exe for its version when nothing else records it (an install made by hand or by an earlier
    /// panel) and writes it to <see cref="VersionFile"/> for next time.
    /// </summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <returns>The version it printed; null when Mailpit isn't installed, didn't answer, or is already known.</returns>
    public static async Task<string?> RecordVersionAsync(string root)
    {
        if (InstalledVersion(root) != "?")
        {
            return null;
        }

        string folder = Path.Combine(root, MailpitDriver.FolderName);
        Shell.RunResult result = await Shell.RunAsync(
            Path.Combine(folder, MailpitDriver.ExeName), "version", folder, TimeSpan.FromSeconds(10));
        string? version = VersionUtil.FromText(result.Output + result.Error);
        if (version is not null)
        {
            WriteVersionFile(folder, version);
        }
        return version;
    }

    /// <summary>Writes the version note next to the exe; a failure is ignored, the version just shows as unknown.</summary>
    /// <param name="folder">The mailpit folder.</param>
    /// <param name="version">The version to record.</param>
    private static void WriteVersionFile(string folder, string version)
    {
        try
        {
            File.WriteAllText(Path.Combine(folder, VersionFile), version + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the version label suffers; Mailpit itself runs fine without the note.
        }
    }

    /// <summary>The newest Mailpit release with a Windows x64 build.</summary>
    /// <returns>The release's version and zip URL.</returns>
    /// <exception cref="HttpRequestException">GitHub cannot be reached or answers with an error.</exception>
    /// <exception cref="OperationCanceledException">GitHub does not answer within 15 seconds.</exception>
    /// <exception cref="InvalidOperationException">The release has no Windows x64 zip.</exception>
    public static async Task<MailpitRelease> GetLatestAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using HttpResponseMessage response = await ReleaseFeeds.Http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(timeout.Token);
        return ReadRelease(json) ?? throw new InvalidOperationException("The newest Mailpit release has no Windows x64 zip.");
    }

    /// <summary>Reads the version and the Windows zip from GitHub's release object.</summary>
    /// <param name="json">The body of the latest-release endpoint.</param>
    /// <returns>The release; null when the tag has no version or the assets hold no Windows x64 zip.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    internal static MailpitRelease? ReadRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("tag_name", out JsonElement tag)
            || VersionUtil.FromText(tag.GetString()) is not { } version
            || !root.TryGetProperty("assets", out JsonElement assets)
            || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            bool isWindowsZip = asset.TryGetProperty("name", out JsonElement name)
                && string.Equals(name.GetString(), AssetName, StringComparison.OrdinalIgnoreCase);
            if (isWindowsZip && asset.TryGetProperty("browser_download_url", out JsonElement url)
                && url.GetString() is { Length: > 0 } zipUrl)
            {
                return new MailpitRelease(version, zipUrl);
            }
        }
        return null;
    }

    /// <summary>
    /// Downloads the release, replaces mailpit.exe (stopping a running Mailpit first), sets php.ini's sendmail_path
    /// so mail() delivers to it, and starts it again when it was running.
    /// </summary>
    /// <param name="ctx">The update dialog's context; status and log lines go through it.</param>
    /// <param name="release">The release to install, from <see cref="GetLatestAsync"/>.</param>
    /// <returns>Success once the new exe is in place; there is no rollback.</returns>
    /// <exception cref="HttpRequestException">The download failed.</exception>
    /// <exception cref="InvalidDataException">The zip holds no mailpit.exe.</exception>
    public static async Task<UpdateResult> InstallAsync(UpdateContext ctx, MailpitRelease release)
    {
        ModuleViewModel? module = ctx.Main.FindModule("Mailpit");
        bool wasRunning = module is { IsRunning: true };
        if (wasRunning && module is not null)
        {
            ctx.Status("Stopping Mailpit…");
            if (!await module.StopForMaintenanceAsync())
            {
                return UpdateResult.Fail("Mailpit didn't stop, so nothing was changed.");
            }
        }

        string folder = Path.Combine(ctx.Root, MailpitDriver.FolderName);
        Directory.CreateDirectory(folder);
        string zipPath = Path.Combine(Path.GetTempPath(), "XamppControl", $"mailpit-{release.Version}.zip");
        ctx.Status($"Downloading Mailpit {release.Version}…");
        await ReleaseFeeds.DownloadAsync(release.ZipUrl, zipPath, ctx.Progress);

        ctx.Status("Unpacking…");
        ctx.Progress(null);
        string exePath = Path.Combine(folder, MailpitDriver.ExeName);
        using (ZipArchive zip = ZipFile.OpenRead(zipPath))
        {
            ZipArchiveEntry exe = zip.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.Name, MailpitDriver.ExeName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("The downloaded zip holds no mailpit.exe.");
            exe.ExtractToFile(exePath, overwrite: true);
        }
        FileTools.TryDeleteFile(zipPath);
        WriteVersionFile(folder, release.Version);
        ctx.Log($"Mailpit {release.Version} is installed in \"{folder}\".", LogLevel.Ok);

        string phpNote = await ConfigurePhpAsync(ctx);
        await ctx.Main.ReloadModulesAsync();

        if (wasRunning && ctx.Main.FindModule("Mailpit") is { } updated)
        {
            ctx.Status("Starting Mailpit…");
            await updated.StartAfterMaintenanceAsync();
        }
        return new UpdateResult(true, $"Mailpit {release.Version} is installed. {phpNote}");
    }

    /// <summary>Points php.ini's mail() at Mailpit and restarts Apache when it runs.</summary>
    /// <param name="ctx">The update context, for the php.ini path and the log.</param>
    /// <returns>One sentence about what was done, for the status line.</returns>
    private static async Task<string> ConfigurePhpAsync(UpdateContext ctx)
    {
        string iniPath = Path.Combine(ctx.Root, "php", "php.ini");
        if (!File.Exists(iniPath))
        {
            return "No php.ini was found, so mail() wasn't pointed at it.";
        }

        int smtpPort = ctx.Settings.Services.MailpitSmtpPort;
        string sendmail = MailpitDriver.SendmailPath(ctx.Root, smtpPort);
        try
        {
            PhpIni ini = PhpIni.Load(iniPath);
            if (ini.GetValue("sendmail_path") == sendmail)
            {
                return "PHP's mail() already delivers to it.";
            }

            ini.SetValue("sendmail_path", sendmail);
            ini.Save();
            ctx.Log($"php.ini: sendmail_path = {sendmail} (the previous file is php.ini.bak)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Mailpit itself is installed; a locked php.ini only means the user sets sendmail_path by hand.
            ctx.Log("php.ini couldn't be updated: " + ex.Message, LogLevel.Warn);
            return $"php.ini couldn't be updated; set sendmail_path to \"{sendmail}\" yourself.";
        }

        if (ctx.Main.FindModule("Apache") is { IsRunning: true } apache)
        {
            ctx.Status("Restarting Apache so PHP reads the new php.ini…");
            await apache.RestartForConfigChangeAsync();
            return "PHP's mail() now delivers to it; Apache was restarted.";
        }
        return "PHP's mail() now delivers to it.";
    }
}
