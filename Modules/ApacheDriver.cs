using System.Diagnostics;
using XamppControl.Core;

namespace XamppControl.Modules;

/// <summary>Apache httpd: started as a hidden process and stopped by ending its process tree.</summary>
/// <param name="root">The XAMPP installation folder.</param>
/// <param name="settings">The panel's live settings.</param>
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

        if (!await PassesConfigTestAsync(host))
        {
            return;
        }

        host.Write(Tag, "Attempting to start Apache app...");
        host.Debug(Tag, $"Executing \"{Exe}\"");
        using Process? process = Shell.StartHidden(Exe, "", Root);
        if (process is null)
        {
            host.Write(Tag, "Could not launch httpd.exe", LogLevel.Error);
            return;
        }

        // A bad config makes httpd exit within a second or two; a healthy server keeps running.
        if (await Shell.ExitsWithinAsync(process, TimeSpan.FromSeconds(3)))
        {
            host.Write(Tag,
                $"Apache shut down unexpectedly (exit code {process.ExitCode}). Last lines of error.log:",
                LogLevel.Error);
            host.TailLog(Tag, InRoot(@"apache\logs\error.log"), 6);
        }
    }

    /// <summary>Runs httpd -t and logs what it finds, so a broken config is named before Apache starts.</summary>
    /// <param name="host">Receives the error lines when the test fails.</param>
    /// <returns>True when the config is valid, or when the test timed out (Apache then starts as usual).</returns>
    private async Task<bool> PassesConfigTestAsync(IModuleHost host)
    {
        host.Debug(Tag, "Checking the Apache configuration (httpd -t)");
        Shell.RunResult result = await TestConfigAsync(Root);
        if (result.TimedOut || result.ExitCode == 0)
        {
            return true;
        }

        host.Write(Tag, "Apache configuration error, so Apache was not started:", LogLevel.Error);
        foreach (string line in ConfigTestProblems(result))
        {
            host.Write(Tag, "  › " + line, LogLevel.Error);
        }
        return false;
    }

    /// <summary>Checks Apache's configuration with httpd -t without starting the server.</summary>
    /// <param name="root">The XAMPP folder whose apache\bin\httpd.exe runs the test.</param>
    /// <returns>
    /// httpd's result: exit code 0 and "Syntax OK" when valid; -1 with the reason when httpd.exe is missing, could
    /// not start or ran past 20 seconds.
    /// </returns>
    public static async Task<Shell.RunResult> TestConfigAsync(string root)
    {
        string exe = Path.Combine(root, "apache", "bin", "httpd.exe");
        if (!File.Exists(exe))
        {
            return new Shell.RunResult(-1, "", "httpd.exe not found in apache\\bin", false);
        }

        return await Shell.RunAsync(exe, "-t", root, TimeSpan.FromSeconds(20));
    }

    /// <summary>The lines of a failed httpd -t that describe the problem.</summary>
    /// <param name="result">The test's result; httpd writes its findings to standard error.</param>
    /// <returns>The non-empty lines of both streams except "Syntax OK"; the exit code when there are none.</returns>
    public static List<string> ConfigTestProblems(Shell.RunResult result)
    {
        var problems = (result.Error + "\n" + result.Output)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.Equals("Syntax OK", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (problems.Count == 0)
        {
            problems.Add($"httpd -t failed with exit code {result.ExitCode}.");
        }
        return problems;
    }

    public override async Task<bool> StopAsync(IModuleHost host)
    {
        IReadOnlyList<int> pids = await PidsAsync();
        host.Write(Tag, $"Attempting to stop Apache{PidText(pids)}");
        return await KillAllAsync(host);
    }
}
