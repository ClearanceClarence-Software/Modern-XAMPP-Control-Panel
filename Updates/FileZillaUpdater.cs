using System.Text.RegularExpressions;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.ViewModels;

namespace XamppControl.Updates;

/// <summary>
/// Replaces XAMPP's FileZilla Server 0.9.41 with FileZilla Server 1.x, or updates 1.x, by running the official
/// installer the user downloaded. The installer itself asks where to install and for the admin password.
/// </summary>
public static partial class FileZillaUpdater
{
    private const string Module = "FileZilla";

    /// <summary>Stops the old FileZilla, runs the installer and switches the FileZilla row to the new server.</summary>
    /// <param name="ctx">The update dialog's context; status and log lines go through it.</param>
    /// <param name="installerPath">
    /// Full path of FileZilla_Server_x.y.z_win64-setup.exe from filezilla-project.org.
    /// </param>
    /// <returns>
    /// Success once FileZilla Server 1.x is found after the installer closed; a failure when the installer was canceled
    /// or isn't a FileZilla Server installer. There is no rollback: the old 0.9.41 folder is left as it was.
    /// </returns>
    public static async Task<UpdateResult> InstallAsync(UpdateContext ctx, string installerPath)
    {
        if (!InstallerName().IsMatch(Path.GetFileName(installerPath)))
        {
            return UpdateResult.Fail("That isn't a FileZilla Server installer. Pick the " +
                "FileZilla_Server_…_win64-setup.exe file from filezilla-project.org.");
        }

        string? previousFolder = FileZillaServer.FindInstallFolder();
        string previousVersion = previousFolder is null
            ? "0.9.41 (XAMPP)"
            : FileZillaServer.VersionOf(previousFolder) ?? "1.x";

        if (previousFolder is null && !await StopLegacyServerAsync(ctx))
        {
            return UpdateResult.Fail("XAMPP's FileZilla Server didn't stop, so nothing was changed.");
        }

        ctx.Status("Running the FileZilla Server installer (Windows asks for admin rights)…");
        ctx.Progress(null);
        string installerFolder = Path.GetDirectoryName(installerPath) ?? ctx.Root;
        int exitCode = await Shell.RunElevatedAsync(installerPath, "", installerFolder, showWindow: true);
        if (exitCode == -2)
        {
            return UpdateResult.Fail("Canceled at the admin prompt. Nothing was changed.");
        }

        string? installFolder = FileZillaServer.FindInstallFolder();
        if (installFolder is null)
        {
            return UpdateResult.Fail("FileZilla Server wasn't found after the installer closed " +
                $"(exit code {exitCode}). If you canceled it, nothing was changed.");
        }

        string newVersion = FileZillaServer.VersionOf(installFolder) ?? "1.x";
        if (exitCode != 0 && newVersion == previousVersion)
        {
            return UpdateResult.Fail($"The installer didn't finish (exit code {exitCode}); " +
                $"FileZilla Server {previousVersion} is still installed.");
        }

        ctx.Log($"FileZilla Server {newVersion} is installed in \"{installFolder}\" as the " +
            $"{FileZillaServer.ServiceName} service (was {previousVersion}).", LogLevel.Ok);
        await ctx.Main.ReloadModulesAsync();

        if (previousFolder is not null)
        {
            return new UpdateResult(true,
                $"FileZilla Server {newVersion} is installed. Your users and settings were kept.");
        }

        string oldSettings = Path.Combine(ctx.Root, "FileZillaFTP", "FileZilla Server.xml");
        ctx.Log("FileZilla Server 1.x can't read the old settings, so set up your FTP users again with its Admin " +
            $"program. The old users and folders are listed in \"{oldSettings}\" for reference.", LogLevel.Warn);
        return new UpdateResult(true,
            $"FileZilla Server {newVersion} is installed and the FileZilla row now controls it. " +
            "Set up your FTP users again with Admin; the old settings are kept in FileZillaFTP.");
    }

    /// <summary>Stops XAMPP's FileZilla Server 0.9.41 when it runs, so it doesn't hold the FTP port.</summary>
    /// <param name="ctx">The update dialog's context.</param>
    /// <returns>True when it is stopped or wasn't running; false when it is a Windows service or didn't stop.</returns>
    private static async Task<bool> StopLegacyServerAsync(UpdateContext ctx)
    {
        if (ctx.Main.FindModule(Module) is not { IsRunning: true } fileZilla)
        {
            return true;
        }

        ctx.Status("Stopping XAMPP's FileZilla Server…");
        return await fileZilla.StopForMaintenanceAsync();
    }

    [GeneratedRegex(@"^FileZilla_Server.*\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex InstallerName();
}
