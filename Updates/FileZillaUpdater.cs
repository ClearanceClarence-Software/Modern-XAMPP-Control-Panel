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
            return UpdateResult.Fail(Loc.T("Updates.NotFileZillaInstaller"));
        }

        string? previousFolder = FileZillaServer.FindInstallFolder();
        string previousVersion = previousFolder is null
            ? "0.9.41 (XAMPP)"
            : FileZillaServer.VersionOf(previousFolder) ?? "1.x";

        if (previousFolder is null && !await StopLegacyServerAsync(ctx))
        {
            return UpdateResult.Fail(Loc.T("Updates.FileZillaDidntStop"));
        }

        ctx.Status(Loc.T("Updates.RunningFileZillaInstaller"));
        ctx.Progress(null);
        string installerFolder = Path.GetDirectoryName(installerPath) ?? ctx.Root;
        int exitCode = await Shell.RunElevatedAsync(installerPath, "", installerFolder, showWindow: true);
        if (exitCode == -2)
        {
            return UpdateResult.Fail(Loc.T("Updates.CanceledAtAdminPrompt"));
        }

        string? installFolder = FileZillaServer.FindInstallFolder();
        if (installFolder is null)
        {
            return UpdateResult.Fail(Loc.T("Updates.FileZillaNotFoundAfter", exitCode));
        }

        string newVersion = FileZillaServer.VersionOf(installFolder) ?? "1.x";
        if (exitCode != 0 && newVersion == previousVersion)
        {
            return UpdateResult.Fail(Loc.T("Updates.InstallerDidntFinish", exitCode, previousVersion));
        }

        ctx.Log($"FileZilla Server {newVersion} is installed in \"{installFolder}\" as the " +
            $"{FileZillaServer.ServiceName} service (was {previousVersion}).", LogLevel.Ok);
        await ctx.Main.ReloadModulesAsync();

        if (previousFolder is not null)
        {
            return new UpdateResult(true, Loc.T("Updates.FileZillaInstalledKept", newVersion));
        }

        string oldSettings = Path.Combine(ctx.Root, "FileZillaFTP", "FileZilla Server.xml");
        ctx.Log("FileZilla Server 1.x can't read the old settings, so set up your FTP users again with its Admin " +
            $"program. The old users and folders are listed in \"{oldSettings}\" for reference.", LogLevel.Warn);
        return new UpdateResult(true, Loc.T("Updates.FileZillaInstalledRow", newVersion));
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

        ctx.Status(Loc.T("Updates.StoppingFileZilla"));
        return await fileZilla.StopForMaintenanceAsync();
    }

    [GeneratedRegex(@"^FileZilla_Server.*\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex InstallerName();
}
