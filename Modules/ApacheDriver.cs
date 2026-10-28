using XamppControl.Core;

namespace XamppControl.Modules;

public sealed class ApacheDriver(string root, AppSettings settings) : ModuleDriver(root, settings)
{
    public override string DisplayName => "Apache";
    public override string Folder => "apache";
    protected override string[] ProcessNames => ["httpd"];

    private string Exe => Path.Combine(FolderPath, "bin", "httpd.exe");

    public override string? VersionLabel => XamppPaths.FileVersion(Exe);

    public override string? ServiceName => Services.ApacheServiceName;
    public override (string Exe, string Args)? ServiceInstallCommand => (Exe, $"-k install -n \"{ServiceName}\"");
    public override (string Exe, string Args)? ServiceUninstallCommand => (Exe, $"-k uninstall -n \"{ServiceName}\"");

    public override IReadOnlyList<int> ConfiguredPorts => [Services.ApachePort, Services.ApacheSslPort];

    public override bool HasAdmin => true;
    public override void OpenAdmin(IModuleHost host) =>
        Shell.OpenUrl(LocalUrl(Services.ApachePort), host.Settings.Browser);

    public override IEnumerable<(string Label, string Relative)> ConfigFiles =>
    [
        ("Apache (httpd.conf)", @"apache\conf\httpd.conf"),
        ("Apache SSL (httpd-ssl.conf)", @"apache\conf\extra\httpd-ssl.conf"),
        ("Apache XAMPP (httpd-xampp.conf)", @"apache\conf\extra\httpd-xampp.conf"),
        ("Virtual hosts (httpd-vhosts.conf)", @"apache\conf\extra\httpd-vhosts.conf"),
        ("PHP (php.ini)", @"php\php.ini"),
        ("phpMyAdmin (config.inc.php)", @"phpMyAdmin\config.inc.php"),
        ("<Browse> apache\\conf", @"apache\conf"),
    ];

    public override IEnumerable<(string Label, string Relative)> LogFiles =>
    [
        ("Apache (error.log)", @"apache\logs\error.log"),
        ("Apache (access.log)", @"apache\logs\access.log"),
        ("PHP (php_error_log)", @"php\logs\php_error_log"),
        ("<Browse> apache\\logs", @"apache\logs"),
    ];

    public override async Task StartAsync(IModuleHost host)
    {
        if (!File.Exists(Exe))
        {
            host.Write(Tag, @"httpd.exe not found in apache\bin", LogLevel.Error);
            return;
        }

        host.Write(Tag, "Attempting to start Apache app...");
        host.Debug(Tag, $"Executing \"{Exe}\"");
        using var process = Shell.StartHidden(Exe, "", Root);
        if (process is null)
        {
            host.Write(Tag, "Could not launch httpd.exe", LogLevel.Error);
            return;
        }

        if (await Shell.ExitsWithinAsync(process, TimeSpan.FromSeconds(3)))
        {
            host.Write(Tag,
                $"Apache shut down unexpectedly (exit code {process.ExitCode}). Last lines of error.log:",
                LogLevel.Error);
            host.TailLog(Tag, InRoot(@"apache\logs\error.log"), 6);
        }
    }

    public override async Task<bool> StopAsync(IModuleHost host)
    {
        var pids = await PidsAsync();
        host.Write(Tag, $"Attempting to stop Apache{PidText(pids)}");
        return await KillAllAsync(host);
    }
}
