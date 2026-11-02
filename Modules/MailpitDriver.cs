using XamppControl.Core;

namespace XamppControl.Modules;

/// <summary>
/// Mailpit, the local mail catcher the Versions window installs into root\mailpit: PHP's mail() delivers to its
/// SMTP port and every message shows in its web interface instead of leaving the computer.
/// </summary>
/// <param name="root">The XAMPP installation folder.</param>
/// <param name="settings">The panel's live settings; the ports come from Service and Port Settings.</param>
public sealed class MailpitDriver(string root, AppSettings settings) : ModuleDriver(root, settings)
{
    /// <summary>The module's folder under the XAMPP root.</summary>
    public const string FolderName = "mailpit";

    /// <summary>The program's file name inside <see cref="FolderName"/>.</summary>
    public const string ExeName = "mailpit.exe";

    public override string DisplayName => "Mailpit";
    public override string Tag => "mailpit";
    public override string Folder => FolderName;
    protected override string[] ProcessNames => ["mailpit"];
    public override bool IsInstalled => File.Exists(ExePath);
    public override string? VersionLabel => Updates.MailpitUpdater.InstalledVersion(Root) switch
    {
        null => null,
        "?" => "installed",
        { } version => version,
    };
    public override IReadOnlyList<int> ConfiguredPorts => [Services.MailpitSmtpPort, Services.MailpitHttpPort];
    public override bool HasAdmin => true;
    public override bool SupportsForceStop => true;
    public override IEnumerable<(string Label, string Relative)> LogFiles => [("mailpit.log", @"mailpit\mailpit.log")];

    /// <summary>Full path of mailpit.exe.</summary>
    public string ExePath => Path.Combine(FolderPath, ExeName);

    /// <summary>The web interface's address.</summary>
    public string WebUrl => LocalUrl(Services.MailpitHttpPort);

    /// <summary>The value for php.ini's sendmail_path: Mailpit's own sendmail command, pointed at its SMTP port.</summary>
    /// <param name="root">The XAMPP installation folder.</param>
    /// <param name="smtpPort">Mailpit's SMTP port.</param>
    /// <returns>The command line, e.g. "C:\xampp\mailpit\mailpit.exe sendmail -S 127.0.0.1:1025".</returns>
    public static string SendmailPath(string root, int smtpPort) =>
        $"{Path.Combine(root, FolderName, ExeName)} sendmail -S 127.0.0.1:{smtpPort}";

    public override void OpenAdmin(IModuleHost host) => Shell.OpenUrl(WebUrl, host.Settings.Browser);

    public override async Task StartAsync(IModuleHost host)
    {
        host.Write(Tag, "Attempting to start Mailpit app...");
        if (!IsInstalled)
        {
            host.Write(Tag, "Mailpit isn't installed. Install it from the Versions window.", LogLevel.Error);
            return;
        }

        // Messages are kept in a database file next to the exe, so they survive a restart; the log goes beside it.
        string arguments = $"--listen 127.0.0.1:{Services.MailpitHttpPort} --smtp 127.0.0.1:{Services.MailpitSmtpPort} " +
            $"--database \"{Path.Combine(FolderPath, "mailpit.db")}\" --log-file \"{Path.Combine(FolderPath, "mailpit.log")}\"";
        host.Debug(Tag, $"Executing \"{ExePath}\" {arguments}");
        using System.Diagnostics.Process? process = Shell.StartHidden(ExePath, arguments, FolderPath);
        if (process is null)
        {
            host.Write(Tag, "Mailpit couldn't be started.", LogLevel.Error);
            return;
        }

        if (await Shell.ExitsWithinAsync(process, TimeSpan.FromSeconds(2)))
        {
            host.Write(Tag, $"Mailpit exited right after starting (exit code {process.ExitCode}).", LogLevel.Error);
            host.TailLog(Tag, Path.Combine(FolderPath, "mailpit.log"), 5);
            return;
        }
        host.Write(Tag, $"Mailpit is catching mail on port {Services.MailpitSmtpPort}; messages show at {WebUrl}");
    }

    public override async Task<bool> StopAsync(IModuleHost host)
    {
        IReadOnlyList<int> pids = await PidsAsync();
        host.Write(Tag, $"Attempting to stop Mailpit{PidText(pids)}");
        // Mailpit has no stop command; ending the process is how it is meant to be stopped, and the database
        // is closed cleanly on exit.
        return await KillAllAsync(host);
    }
}
