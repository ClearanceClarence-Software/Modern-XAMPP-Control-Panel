using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.ViewModels;

namespace XamppControl.Sites;

/// <summary>The outcome of applying the sites.</summary>
/// <param name="IsSuccess">True when Apache's config was written and passed its test.</param>
/// <param name="Message">One sentence for the Sites window's status line; the details are in the log.</param>
public sealed record SiteSetupResult(bool IsSuccess, string Message);

/// <summary>
/// Applies the Sites window's list: certificate, virtual hosts, a config test (undone when it fails), the hosts file
/// and an Apache restart.
/// </summary>
public static partial class SiteSetup
{
    private const string LogTag = "sites";
    private const string ApacheModule = "Apache";

    // Top-level domains on the HSTS preload list: browsers refuse plain HTTP for every name under them.
    private static readonly string[] _httpsOnlyDomains = ["dev", "app", "page", "new", "day", "foo"];

    /// <summary>Checks the sites before anything is written.</summary>
    /// <param name="sites">The sites as edited, host names already trimmed and lowercase.</param>
    /// <returns>The first problem as a sentence for the status line; null when every site is usable.</returns>
    public static string? FindProblem(IReadOnlyList<Site> sites)
    {
        var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Site site in sites)
        {
            if (site.Host.Length == 0)
            {
                return "Every site needs a host name, such as myproject.test.";
            }
            if (site.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return "localhost is already served from htdocs; give the site its own name, such as myproject.test.";
            }
            if (!HostName().IsMatch(site.Host))
            {
                return $"\"{site.Host}\" isn't a valid host name. Use letters, digits and hyphens with a dot, " +
                    "such as myproject.test.";
            }
            if (!seenHosts.Add(site.Host))
            {
                return $"\"{site.Host}\" is in the list twice.";
            }
            if (site.Folder.Length == 0 || !Path.IsPathFullyQualified(site.Folder))
            {
                return $"Choose a full folder path for {site.Host}.";
            }

            string topLevelDomain = site.Host[(site.Host.LastIndexOf('.') + 1)..];
            if (!site.UseHttps && _httpsOnlyDomains.Contains(topLevelDomain, StringComparer.OrdinalIgnoreCase))
            {
                return $"Browsers only open .{topLevelDomain} names over HTTPS, so {site.Host} needs HTTPS turned on.";
            }
        }
        return null;
    }

    /// <summary>Writes everything the sites need and restarts Apache when it runs.</summary>
    /// <param name="main">The panel; supplies the XAMPP folder, the settings and the log, and restarts Apache.</param>
    /// <param name="sites">
    /// The sites, already checked with <see cref="FindProblem"/>; an empty list removes them all.
    /// </param>
    /// <param name="status">Receives a short progress line before each step.</param>
    /// <returns>
    /// Success once the config passed its test and the settings were saved, even when the hosts file or the trust
    /// prompt was declined (those are logged as warnings); a failure when nothing was kept.
    /// </returns>
    public static async Task<SiteSetupResult> ApplyAsync(
        MainViewModel main,
        IReadOnlyList<Site> sites,
        Action<string> status)
    {
        string root = main.Root;
        string apacheFolder = Path.Combine(root, "apache");
        string vhostsPath = VirtualHosts.VhostsPath(root);
        string httpdConfPath = VirtualHosts.HttpdConfPath(root);
        if (!File.Exists(httpdConfPath))
        {
            return new SiteSetupResult(false, $"No Apache configuration in \"{apacheFolder}\".");
        }

        CreateMissingFolders(main, sites);
        (int Http, int Https) ports = ListeningPorts(main, httpdConfPath);

        // Everything Apply may change in Apache's folder, so a failed config test leaves Apache as it was.
        FileSnapshot snapshot = await FileSnapshot.TakeAsync(
        [
            vhostsPath,
            httpdConfPath,
            LocalCertificates.ServerCertificatePath(apacheFolder),
            LocalCertificates.ServerKeyPath(apacheFolder),
        ]);

        List<string> httpsHosts = sites.Where(site => site.UseHttps).Select(site => site.Host).ToList();
        bool includeHttps = httpsHosts.Count > 0;
        if (includeHttps)
        {
            status("Creating the HTTPS certificate…");
            if (await IssueCertificateAsync(main, httpsHosts, apacheFolder) is { } certificateFailure)
            {
                await snapshot.RestoreAsync();
                return new SiteSetupResult(false, certificateFailure);
            }
        }

        status("Writing the virtual hosts…");
        if (await WriteAndTestConfigAsync(main, sites, includeHttps, ports, vhostsPath, httpdConfPath) is { } failure)
        {
            await snapshot.RestoreAsync();
            return new SiteSetupResult(false, failure);
        }

        main.Settings.Sites = [.. sites];
        main.Settings.Save();

        status("Updating the hosts file (Windows asks for admin rights)…");
        await UpdateHostsAsync(main, sites);

        if (includeHttps)
        {
            await TrustAuthorityAsync(main);
        }

        if (main.FindModule(ApacheModule) is { IsRunning: true } apache)
        {
            status("Restarting Apache…");
            await apache.RestartForConfigChangeAsync();
        }

        string summary = sites.Count == 0
            ? "The sites were removed."
            : $"{sites.Count} site{(sites.Count == 1 ? "" : "s")} set up.";
        main.Write(LogTag, summary, LogLevel.Ok);
        return new SiteSetupResult(true, summary);
    }

    /// <summary>Creates the document roots that don't exist yet, so Apache doesn't refuse to start.</summary>
    /// <param name="main">Receives a log line per created folder.</param>
    /// <param name="sites">The sites whose folders are checked.</param>
    private static void CreateMissingFolders(MainViewModel main, IReadOnlyList<Site> sites)
    {
        foreach (Site site in sites.Where(site => !Directory.Exists(site.Folder)))
        {
            Directory.CreateDirectory(site.Folder);
            main.Write(LogTag, $"Created the folder \"{site.Folder}\" for {site.Host}.");
        }
    }

    /// <summary>Issues the server certificate for localhost and the HTTPS sites.</summary>
    /// <param name="main">Receives the log lines.</param>
    /// <param name="httpsHosts">The host names of the sites that use HTTPS.</param>
    /// <param name="apacheFolder">XAMPP's apache folder, where the certificate and key are written.</param>
    /// <returns>Null when the certificate was written; otherwise the reason for the status line.</returns>
    private static async Task<string?> IssueCertificateAsync(
        MainViewModel main,
        IReadOnlyList<string> httpsHosts,
        string apacheFolder)
    {
        try
        {
            await Task.Run(() =>
            {
                using X509Certificate2 authority = LocalCertificates.LoadOrCreateAuthority();
                LocalCertificates.IssueServerCertificate(authority, httpsHosts, apacheFolder);
            });
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            main.Write(LogTag, "Creating the certificate failed: " + ex.Message, LogLevel.Error);
            return "The HTTPS certificate couldn't be created, so nothing was changed. The log shows why.";
        }

        main.Write(LogTag, $"Certificate issued for localhost and {string.Join(", ", httpsHosts)}.");
        return null;
    }

    /// <summary>
    /// The ports to write the virtual hosts for: the panel's settings when Apache listens on them, otherwise the
    /// port Apache's own Listen line names, with a warning.
    /// </summary>
    /// <param name="main">Supplies the panel's ports and receives the warnings.</param>
    /// <param name="httpdConfPath">Full path of httpd.conf; httpd-ssl.conf is read from its extra folder.</param>
    /// <returns>The HTTP and HTTPS ports.</returns>
    private static (int Http, int Https) ListeningPorts(MainViewModel main, string httpdConfPath)
    {
        ServiceSettings services = main.Settings.Services;
        string sslConfPath = Path.Combine(
            Path.GetDirectoryName(httpdConfPath) ?? "",
            "extra",
            "httpd-ssl.conf");
        int httpPort = ListeningPort(main, VirtualHosts.ListenPorts(httpdConfPath), services.ApachePort, "HTTP");
        int httpsPort = ListeningPort(main, VirtualHosts.ListenPorts(sslConfPath), services.ApacheSslPort, "HTTPS");
        return (httpPort, httpsPort);
    }

    /// <summary>Picks the port for one protocol and warns when the panel's setting doesn't match Apache's.</summary>
    /// <param name="main">Receives the warning.</param>
    /// <param name="listenPorts">The ports from the config file's Listen lines, in file order.</param>
    /// <param name="configuredPort">The port from the panel's Service and Port Settings.</param>
    /// <param name="protocol">"HTTP" or "HTTPS", for the warning.</param>
    /// <returns>The configured port when Apache listens on it or there is no Listen line; else the first one.</returns>
    private static int ListeningPort(MainViewModel main, List<int> listenPorts, int configuredPort, string protocol)
    {
        if (listenPorts.Count == 0 || listenPorts.Contains(configuredPort))
        {
            return configuredPort;
        }

        main.Write(LogTag,
            $"Apache listens for {protocol} on port {listenPorts[0]}, not {configuredPort} as set in Service and " +
            $"Port Settings; the sites use {listenPorts[0]}.",
            LogLevel.Warn);
        return listenPorts[0];
    }

    /// <summary>Writes the virtual hosts and the Include line, then tests the config with httpd -t.</summary>
    /// <param name="main">Receives the log lines, including httpd's complaints.</param>
    /// <param name="sites">The sites to write; an empty list removes the block.</param>
    /// <param name="includeHttps">True writes the HTTPS hosts too.</param>
    /// <param name="ports">The HTTP and HTTPS ports Apache listens on.</param>
    /// <param name="vhostsPath">Full path of httpd-vhosts.conf.</param>
    /// <param name="httpdConfPath">Full path of httpd.conf.</param>
    /// <returns>
    /// Null when the config passed; otherwise the reason for the status line, and the caller restores the files.
    /// </returns>
    private static async Task<string?> WriteAndTestConfigAsync(
        MainViewModel main,
        IReadOnlyList<Site> sites,
        bool includeHttps,
        (int Http, int Https) ports,
        string vhostsPath,
        string httpdConfPath)
    {
        try
        {
            string? block = sites.Count == 0
                ? null
                : VirtualHosts.BuildBlock(
                    sites,
                    VirtualHosts.MainDocumentRoot(main.Root),
                    ports.Http,
                    ports.Https,
                    includeHttps);
            VirtualHosts.WriteBlock(vhostsPath, block);
            if (block is not null && VirtualHosts.EnsureIncluded(httpdConfPath))
            {
                main.Write(LogTag, "httpd.conf now includes httpd-vhosts.conf.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            main.Write(LogTag, "Writing the Apache configuration failed: " + ex.Message, LogLevel.Error);
            return "The Apache configuration couldn't be written, so nothing was changed.";
        }

        Shell.RunResult test = await ApacheDriver.TestConfigAsync(main.Root);
        if (test.TimedOut || test.ExitCode == 0)
        {
            return null;
        }

        main.Write(LogTag, "Apache rejected the new configuration, so it was undone:", LogLevel.Error);
        foreach (string line in ApacheDriver.ConfigTestProblems(test))
        {
            main.Write(LogTag, "  › " + line, LogLevel.Error);
        }
        return "Apache rejected the configuration, so it was undone. The log shows why.";
    }

    /// <summary>Points the host names at this computer and logs what happened.</summary>
    /// <param name="main">Receives the log lines.</param>
    /// <param name="sites">The sites whose host names go into the hosts file.</param>
    /// <returns>A task that completes after the UAC prompt and the copy.</returns>
    private static async Task UpdateHostsAsync(MainViewModel main, IReadOnlyList<Site> sites)
    {
        int exitCode;
        try
        {
            exitCode = await HostsFile.UpdateAsync(sites.Select(site => site.Host).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            main.Write(LogTag, "The hosts file couldn't be read or prepared: " + ex.Message, LogLevel.Warn);
            return;
        }

        if (exitCode == -2)
        {
            main.Write(LogTag,
                "The hosts file wasn't changed (canceled at the admin prompt), so the new names don't resolve yet. " +
                "Apply again to retry.",
                LogLevel.Warn);
        }
        else if (exitCode != 0)
        {
            main.Write(LogTag,
                $"Writing the hosts file failed (exit code {exitCode}). An antivirus may be protecting it.",
                LogLevel.Warn);
        }
        else
        {
            main.Write(LogTag, "Hosts file is up to date.");
        }
    }

    /// <summary>Asks Windows to trust the local certificate authority, unless it already does.</summary>
    /// <param name="main">Receives the log lines.</param>
    /// <returns>A task that completes when the authority is trusted or the user declined.</returns>
    public static async Task TrustAuthorityAsync(MainViewModel main)
    {
        bool isTrusted;
        try
        {
            isTrusted = await Task.Run(() =>
            {
                using X509Certificate2 authority = LocalCertificates.LoadOrCreateAuthority();
                return LocalCertificates.IsTrusted(authority) || LocalCertificates.Trust(authority);
            });
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            main.Write(LogTag, "Trusting the local certificate authority failed: " + ex.Message, LogLevel.Warn);
            return;
        }

        if (!isTrusted)
        {
            main.Write(LogTag,
                "The local certificate authority isn't trusted, so browsers will warn about the HTTPS sites. " +
                "Use \"Trust\" in the Sites window to try again.",
                LogLevel.Warn);
        }
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]([a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex HostName();
}
