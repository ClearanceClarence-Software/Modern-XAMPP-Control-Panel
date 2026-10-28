using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.Sites;

namespace XamppControl.ViewModels;

/// <summary>One editable row of the Sites window.</summary>
public sealed class SiteRow : ObservableObject
{
    private readonly ServiceSettings _services;
    private string _host;
    private string _folder;
    private bool _useHttps;

    /// <summary>Creates a row from a stored site, or an empty one for "Add site".</summary>
    /// <param name="site">The stored site; its values are copied, so editing the row doesn't change it.</param>
    /// <param name="services">Supplies Apache's ports for the row's link, read when the link is built.</param>
    /// <param name="browser">Read on each click for the configured browser; null uses the system default.</param>
    public SiteRow(Site site, ServiceSettings services, Func<string?> browser)
    {
        _services = services;
        _host = site.Host;
        _folder = site.Folder;
        _useHttps = site.UseHttps;
        OpenCommand = new RelayCommand(() => Shell.OpenUrl(Url, browser()), () => Host.Length > 0);
    }

    /// <summary>The host name as typed, such as myproject.test.</summary>
    public string Host
    {
        get => _host;
        set
        {
            if (SetField(ref _host, value))
            {
                OnPropertyChanged(nameof(Url));
                OpenCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Full path of the site's document root.</summary>
    public string Folder
    {
        get => _folder;
        set => SetField(ref _folder, value);
    }

    /// <summary>True also serves the site over HTTPS.</summary>
    public bool UseHttps
    {
        get => _useHttps;
        set
        {
            if (SetField(ref _useHttps, value))
            {
                OnPropertyChanged(nameof(Url));
            }
        }
    }

    /// <summary>The site's address, HTTPS when turned on, with the port only when it isn't the default.</summary>
    public string Url
    {
        get
        {
            string host = NormalizedHost(Host);
            if (UseHttps)
            {
                int httpsPort = _services.ApacheSslPort;
                return httpsPort == 443 ? $"https://{host}/" : $"https://{host}:{httpsPort}/";
            }

            int httpPort = _services.ApachePort;
            return httpPort == 80 ? $"http://{host}/" : $"http://{host}:{httpPort}/";
        }
    }

    /// <summary>Opens <see cref="Url"/> in the configured browser.</summary>
    public RelayCommand OpenCommand { get; }

    /// <summary>The row as a site to save, with the host name trimmed and lowercase and the folder trimmed.</summary>
    /// <returns>A new <see cref="Site"/>.</returns>
    public Site ToSite() => new()
    {
        Host = NormalizedHost(Host),
        Folder = Folder.Trim().TrimEnd('\\', '/'),
        UseHttps = UseHttps,
    };

    /// <summary>A host name as typed, cleaned up: trimmed, lowercase, without a scheme or a trailing slash.</summary>
    /// <param name="host">The text in the host box.</param>
    /// <returns>The bare host name.</returns>
    private static string NormalizedHost(string host)
    {
        string trimmed = host.Trim().ToLowerInvariant();
        foreach (string scheme in new[] { "https://", "http://" })
        {
            if (trimmed.StartsWith(scheme, StringComparison.Ordinal))
            {
                trimmed = trimmed[scheme.Length..];
            }
        }
        return trimmed.TrimEnd('/');
    }
}

/// <summary>The Sites window: local sites with their own host names, applied to Apache and the hosts file.</summary>
public sealed class SitesViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _isBusy;
    private string _status = "";
    private string _trustStatus = "";
    private bool _canTrust;

    /// <summary>Copies the stored sites into editable rows and reads whether the local authority is trusted.</summary>
    /// <param name="main">The panel; supplies the settings, the XAMPP folder and the log.</param>
    public SitesViewModel(MainViewModel main)
    {
        _main = main;
        foreach (Site site in main.Settings.Sites)
        {
            Rows.Add(NewRow(site));
        }

        AddCommand = new RelayCommand(() => Rows.Add(NewRow(new Site())), () => IsIdle);
        RefreshTrustStatus();
    }

    /// <summary>The sites being edited, in the order they are written to Apache's config.</summary>
    public ObservableCollection<SiteRow> Rows { get; } = [];

    /// <summary>Adds an empty row.</summary>
    public RelayCommand AddCommand { get; }

    /// <summary>The address that keeps serving htdocs, as a reminder above the list.</summary>
    public string LocalhostNote => $"localhost keeps serving \"{VirtualHosts.MainDocumentRoot(_main.Root)}\".";

    /// <summary>True while the sites are being applied; the window's buttons are disabled meanwhile.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                AddCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The opposite of <see cref="IsBusy"/>, for IsEnabled bindings.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>The status line: progress while applying, then the result or the first problem found.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>Whether Windows trusts the local certificate authority, as a sentence.</summary>
    public string TrustStatus
    {
        get => _trustStatus;
        private set => SetField(ref _trustStatus, value);
    }

    /// <summary>True when the authority exists but isn't trusted yet; shows the Trust button.</summary>
    public bool CanTrust
    {
        get => _canTrust;
        private set => SetField(ref _canTrust, value);
    }

    /// <summary>Removes a row; the change takes effect on Apply.</summary>
    /// <param name="row">A row of <see cref="Rows"/>; a row not in the list is ignored.</param>
    public void Remove(SiteRow row) => Rows.Remove(row);

    /// <summary>Checks the rows and applies them, reporting the outcome in <see cref="Status"/>.</summary>
    /// <returns>A task that completes when the sites are applied or the first problem is shown.</returns>
    public async Task ApplyAsync()
    {
        List<Site> sites = Rows.Select(row => row.ToSite()).ToList();
        if (SiteSetup.FindProblem(sites) is { } problem)
        {
            Status = problem;
            return;
        }

        IsBusy = true;
        try
        {
            SiteSetupResult result = await SiteSetup.ApplyAsync(_main, sites, progress => Status = progress);
            Status = result.Message;
        }
        catch (Exception ex)
        {
            // Applying writes files, runs httpd and restarts Apache, so anything may fail; the window reports it
            // and stays usable instead of crashing the panel.
            _main.Write("sites", ex.Message, LogLevel.Error);
            Status = "Applying the sites failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshTrustStatus();
        }
    }

    /// <summary>Asks Windows again to trust the local certificate authority.</summary>
    /// <returns>A task that completes when the user has answered Windows' prompt.</returns>
    public async Task TrustAsync()
    {
        IsBusy = true;
        try
        {
            await SiteSetup.TrustAuthorityAsync(_main);
        }
        finally
        {
            IsBusy = false;
            RefreshTrustStatus();
        }
    }

    /// <summary>Opens the settings folder, which holds local-ca.crt for Firefox or other computers.</summary>
    public static void ShowAuthorityFile()
    {
        Directory.CreateDirectory(AppSettings.Folder);
        Shell.Open(AppSettings.Folder);
    }

    /// <summary>Reads whether the authority exists and is trusted, and updates the sentence and the button.</summary>
    private void RefreshTrustStatus()
    {
        if (!File.Exists(LocalCertificates.AuthorityPfxPath))
        {
            TrustStatus = "The local certificate authority is created the first time a site with HTTPS is applied.";
            CanTrust = false;
            return;
        }

        try
        {
            using X509Certificate2 authority = LocalCertificates.LoadOrCreateAuthority();
            bool isTrusted = LocalCertificates.IsTrusted(authority);
            TrustStatus = isTrusted
                ? "Windows trusts the local certificate authority: Chrome and Edge open the HTTPS sites safely."
                : "Windows doesn't trust the local certificate authority yet, so browsers warn about the HTTPS sites.";
            CanTrust = !isTrusted;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            TrustStatus = "The local certificate authority can't be read: " + ex.Message;
            CanTrust = false;
        }
    }

    /// <summary>Creates a row bound to the panel's ports and browser.</summary>
    /// <param name="site">The site to copy into the row.</param>
    /// <returns>The new row.</returns>
    private SiteRow NewRow(Site site) => new(site, _main.Settings.Services, () => _main.Settings.Browser);
}
