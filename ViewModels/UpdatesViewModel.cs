using System.Collections.ObjectModel;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.Updates;

namespace XamppControl.ViewModels;

/// <summary>
/// State of the PHP and MariaDB updates dialog: installed versions, the official feeds, and the running update.
/// </summary>
public sealed class UpdatesViewModel : ObservableObject
{
    private const string LogSource = "update";

    private readonly MainViewModel _main;

    private string _phpInstalled = "";
    private string _mariaInstalled = "";
    private PhpBranch? _selectedPhp;
    private MariaDbMajor? _selectedMajor;
    private MariaDbRelease? _mariaLatest;
    private string _phpAvailable = Loc.T("Updates.Checking");
    private string _mariaAvailable = Loc.T("Updates.Checking");
    private bool _isBusy;
    private string _status = "";
    private bool _showProgress;
    private bool _progressIndeterminate = true;
    private double _progress;
    private bool _makeSqlDump = true;
    private Func<Task>? _rollback;
    private string _rollbackLabel = "";
    private string? _pendingRemove;
    private bool _importedLegacy;
    private string _fileZillaInstalled = "";
    private string _fileZillaAvailable = Loc.T("Updates.Checking");
    private string? _fileZillaLatest;
    private string _mailpitInstalled = "";
    private string _mailpitAvailable = Loc.T("Updates.Checking");
    private string _mailpitAction = Loc.T("Updates.Install");
    private MailpitRelease? _mailpitLatest;
    private bool _isMailpitInstalled;

    // The installed versions as numbers only; the *Installed properties are display text and may be translated.
    private string? _phpVersion;
    private string? _mariaVersion;
    private string? _mailpitVersion;

    /// <summary>
    /// Creates the dialog's state and reads the installed versions; the feeds load in <see cref="CheckAsync"/>.
    /// </summary>
    /// <param name="main">The main window's view model; supplies the XAMPP folder, the modules and the log.</param>
    public UpdatesViewModel(MainViewModel main)
    {
        _main = main;
        RefreshInstalled();
    }

    /// <summary>Every recognizable PHP build, the active one first, then parked builds newest first.</summary>
    public ObservableCollection<InstalledVersion> PhpVersions { get; } = [];

    /// <summary>Every recognizable MariaDB build, the active one first, then parked builds newest first.</summary>
    public ObservableCollection<InstalledVersion> MariaVersions { get; } = [];

    /// <summary>PHP branches from windows.php.net, newest branch first; empty until the feed has loaded.</summary>
    public ObservableCollection<PhpBranch> PhpBranches { get; } = [];

    /// <summary>Stable MariaDB release series from downloads.mariadb.org; empty until the feed has loaded.</summary>
    public ObservableCollection<MariaDbMajor> MariaMajors { get; } = [];

    /// <summary>The active PHP as "PHP 8.4.10", or "Not found" when php\php.exe is missing.</summary>
    public string PhpInstalled
    {
        get => _phpInstalled;
        private set => SetField(ref _phpInstalled, value);
    }

    /// <summary>The active MariaDB as "MariaDB 11.4.5", or "Not found" when mysql\bin has no server.</summary>
    public string MariaInstalled
    {
        get => _mariaInstalled;
        private set => SetField(ref _mariaInstalled, value);
    }

    /// <summary>The installed FileZilla Server, such as "FileZilla Server 0.9.41 (bundled with XAMPP)".</summary>
    public string FileZillaInstalled
    {
        get => _fileZillaInstalled;
        private set => SetField(ref _fileZillaInstalled, value);
    }

    /// <summary>One line comparing the newest FileZilla Server with the installed one, or why it can't.</summary>
    public string FileZillaAvailable
    {
        get => _fileZillaAvailable;
        private set => SetField(ref _fileZillaAvailable, value);
    }

    /// <summary>The installed Mailpit, such as "Mailpit 1.29.5" or "Not installed".</summary>
    public string MailpitInstalled
    {
        get => _mailpitInstalled;
        private set => SetField(ref _mailpitInstalled, value);
    }

    /// <summary>One line comparing the newest Mailpit with the installed one, or why it can't.</summary>
    public string MailpitAvailable
    {
        get => _mailpitAvailable;
        private set => SetField(ref _mailpitAvailable, value);
    }

    /// <summary>The Mailpit button's text: "Install", "Update" or "Reinstall".</summary>
    public string MailpitAction
    {
        get => _mailpitAction;
        private set => SetField(ref _mailpitAction, value);
    }

    /// <summary>True once GitHub has answered, so the Mailpit button can install.</summary>
    public bool CanInstallMailpit => IsIdle && _mailpitLatest is not null;

    /// <summary>
    /// One line comparing the selected branch's newest build with the active PHP, or the feed's error.
    /// </summary>
    public string PhpAvailable
    {
        get => _phpAvailable;
        private set => SetField(ref _phpAvailable, value);
    }

    /// <summary>
    /// One line comparing the selected series' newest release with the active MariaDB, or the feed's error.
    /// </summary>
    public string MariaAvailable
    {
        get => _mariaAvailable;
        private set => SetField(ref _mariaAvailable, value);
    }

    /// <summary>The PHP branch to download; null until the feed has loaded or when it has no builds.</summary>
    public PhpBranch? SelectedPhp
    {
        get => _selectedPhp;
        set
        {
            if (SetField(ref _selectedPhp, value))
            {
                UpdatePhpAvailable();
            }
        }
    }

    /// <summary>
    /// The MariaDB series to download; changing it loads that series' newest release in the background.
    /// </summary>
    public MariaDbMajor? SelectedMajor
    {
        get => _selectedMajor;
        set
        {
            if (SetField(ref _selectedMajor, value))
            {
                _ = LoadMariaLatestAsync();
            }
        }
    }

    /// <summary>True makes a MariaDB update write an SQL dump of all databases before it changes anything.</summary>
    public bool MakeSqlDump
    {
        get => _makeSqlDump;
        set => SetField(ref _makeSqlDump, value);
    }

    /// <summary>True while an update, switch or rollback runs; the window refuses to close meanwhile.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanInstallMailpit));
            }
        }
    }

    /// <summary>The opposite of <see cref="IsBusy"/>, for enabling controls in XAML.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>The dialog's status line: the current step, the last result, or a remove confirmation.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>True while work runs, which shows the progress bar.</summary>
    public bool ShowProgress
    {
        get => _showProgress;
        private set => SetField(ref _showProgress, value);
    }

    /// <summary>True when the current step has no percentage, which shows the bar as a sweep.</summary>
    public bool ProgressIndeterminate
    {
        get => _progressIndeterminate;
        private set => SetField(ref _progressIndeterminate, value);
    }

    /// <summary>Progress of the current step in percent, 0..100; ignored while the bar is indeterminate.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    /// <summary>True when the last update left a rollback to offer.</summary>
    public bool CanRollback => _rollback is not null;

    /// <summary>
    /// The rollback button's text, e.g. "Switch back to PHP 8.3.14"; empty when there is no rollback.
    /// </summary>
    public string RollbackLabel
    {
        get => _rollbackLabel;
        private set => SetField(ref _rollbackLabel, value);
    }

    // ── installed versions ───────────────────────────────────

    /// <summary>
    /// Re-reads the installed and stored versions and refreshes the comparison lines; the first call also moves
    /// backups made by earlier panel versions into the version store.
    /// </summary>
    private void RefreshInstalled()
    {
        string root = _main.Root;

        if (!_importedLegacy)
        {
            _importedLegacy = true;
            List<string> moved = VersionStore.ImportLegacyBackups(root);
            if (moved.Count > 0)
            {
                _main.Write(LogSource, "Added earlier backups as installed versions: " + string.Join(", ", moved));
            }
        }

        Fill(PhpVersions, VersionStore.List(root, Component.Php));
        Fill(MariaVersions, VersionStore.List(root, Component.MariaDb));
        string? phpVersion = XamppPaths.FileVersion(Path.Combine(root, "php", "php.exe"));
        _phpVersion = phpVersion;
        PhpInstalled = phpVersion is null ? Loc.T("Updates.NotFound") : $"PHP {phpVersion}";

        string? server = FileTools.FirstExisting(Path.Combine(root, "mysql", "bin"), "mariadbd.exe", "mysqld.exe");
        string? mariaVersion = XamppPaths.FileVersion(server);
        _mariaVersion = mariaVersion;
        MariaInstalled = mariaVersion is null ? Loc.T("Updates.NotFound") : $"MariaDB {mariaVersion}";

        FileZillaInstalled = DescribeInstalledFileZilla(root);
        string? mailpitVersion = MailpitUpdater.InstalledVersion(root);
        _isMailpitInstalled = mailpitVersion is not null;
        _mailpitVersion = mailpitVersion == "?" ? null : mailpitVersion;
        MailpitInstalled = mailpitVersion switch
        {
            null => Loc.T("Updates.NotInstalled"),
            "?" => Loc.T("Updates.MailpitVersionUnknown"),
            _ => $"Mailpit {mailpitVersion}",
        };

        UpdatePhpAvailable();
        UpdateMariaAvailable();
        UpdateFileZillaAvailable();
        UpdateMailpitAvailable();
    }

    /// <summary>Sets <see cref="MailpitAvailable"/> and the button text from the release GitHub reported.</summary>
    private void UpdateMailpitAvailable()
    {
        if (_mailpitLatest is not { } latest)
        {
            return;
        }

        // An exe without a version resource parses as 0.0 and counts as not current, so the button offers the update.
        Version installed = VersionUtil.Parse(_mailpitVersion);
        bool isInstalled = _isMailpitInstalled;
        bool isCurrent = isInstalled && installed >= VersionUtil.Parse(latest.Version);
        MailpitAvailable = isCurrent
            ? Loc.T("Updates.MailpitNewestYouHaveIt", latest.Version)
            : Loc.T("Updates.MailpitIsAvailable", latest.Version);
        if (!isInstalled)
        {
            MailpitAction = Loc.T("Updates.Install");
        }
        else
        {
            MailpitAction = isCurrent ? Loc.T("Updates.Reinstall") : Loc.T("Updates.UpdateTo", latest.Version);
        }
        OnPropertyChanged(nameof(CanInstallMailpit));
    }

    /// <summary>Names the FileZilla Server the FileZilla row controls.</summary>
    /// <param name="root">The XAMPP folder, where the bundled 0.9.41 lives.</param>
    /// <returns>The server and version, or "Not installed".</returns>
    private static string DescribeInstalledFileZilla(string root)
    {
        if (FileZillaServer.FindInstallFolder() is { } installFolder)
        {
            return Loc.T("Updates.FileZillaWindowsService", FileZillaServer.VersionOf(installFolder) ?? "1.x");
        }

        string legacyExe = Path.Combine(root, "FileZillaFTP", "FileZillaServer.exe");
        if (XamppPaths.FileVersion(legacyExe) is { } legacyVersion)
        {
            return Loc.T("Updates.FileZillaBundledFrom2012", legacyVersion);
        }
        return File.Exists(legacyExe) ? Loc.T("Updates.FileZillaBundled") : Loc.T("Updates.NotInstalled");
    }

    /// <summary>Sets <see cref="FileZillaAvailable"/> from the version the website states.</summary>
    private void UpdateFileZillaAvailable()
    {
        if (_fileZillaLatest is not { } latest)
        {
            return;
        }

        bool isCurrent = FileZillaServer.FindInstallFolder() is { } installFolder
            && VersionUtil.Parse(FileZillaServer.VersionOf(installFolder)) >= VersionUtil.Parse(latest);
        FileZillaAvailable = isCurrent
            ? Loc.T("Updates.OnNewestVersion", latest)
            : Loc.T("Updates.FileZillaIsAvailable", latest);
    }

    /// <summary>
    /// Sets <see cref="PhpAvailable"/> from the selected branch; leaves it as is when none is selected.
    /// </summary>
    private void UpdatePhpAvailable()
    {
        if (SelectedPhp is not { } branch)
        {
            return;
        }

        Version installed = VersionUtil.Parse(_phpVersion);
        Version latest = VersionUtil.Parse(branch.Version);
        if (installed == latest)
        {
            PhpAvailable = Loc.T("Updates.OnNewestBuildOfBranch");
        }
        else if (installed > latest)
        {
            PhpAvailable = Loc.T("Updates.PhpOlderThanInstalled", branch.Version);
        }
        else
        {
            PhpAvailable = Loc.T("Updates.PhpIsAvailable", branch.Version);
        }
    }

    /// <summary>
    /// Sets <see cref="MariaAvailable"/> from the loaded release; leaves it as is while none is loaded.
    /// </summary>
    private void UpdateMariaAvailable()
    {
        if (_mariaLatest is not { } release)
        {
            return;
        }

        Version installed = VersionUtil.Parse(_mariaVersion);
        Version latest = VersionUtil.Parse(release.Version);
        if (installed == latest)
        {
            MariaAvailable = Loc.T("Updates.OnNewestReleaseOfSeries");
        }
        else if (installed > latest)
        {
            MariaAvailable = Loc.T("Updates.MariaOlderThanInstalled", release.Version);
        }
        else
        {
            MariaAvailable = Loc.T("Updates.MariaIsAvailable", release.Version);
        }
    }

    private static void Fill(ObservableCollection<InstalledVersion> target, List<InstalledVersion> versions)
    {
        target.Clear();
        foreach (InstalledVersion version in versions)
        {
            target.Add(version);
        }
    }

    // ── installed versions: switch, remove, add ──────────────

    /// <summary>
    /// Makes a stored version the active one; a running server is stopped for the swap and restarted.
    /// </summary>
    /// <param name="version">
    /// A stored, inactive version from <see cref="PhpVersions"/> or <see cref="MariaVersions"/>.
    /// </param>
    /// <returns>
    /// A task that completes when the switch has finished or failed; the outcome is in <see cref="Status"/>.
    /// </returns>
    public Task SwitchAsync(InstalledVersion version) =>
        RunAsync(ctx => VersionSwitcher.SwitchAsync(ctx, version));

    /// <summary>
    /// First click asks for confirmation in the status line; a second click on the same version deletes it.
    /// </summary>
    /// <param name="version">A stored version; the active one and calls while busy are ignored.</param>
    public void Remove(InstalledVersion version)
    {
        if (IsBusy || version.IsActive)
        {
            return;
        }

        if (_pendingRemove != version.Path)
        {
            _pendingRemove = version.Path;
            Status = version.Component == Component.MariaDb
                ? Loc.T("Updates.ClickRemoveAgainMaria", version.Title, version.Detail)
                : Loc.T("Updates.ClickRemoveAgain", version.Title, version.Detail);
            return;
        }

        _pendingRemove = null;
        try
        {
            VersionStore.Remove(version);
            Status = Loc.T("Updates.WasRemoved", version.Title);
            _main.Write(LogSource, $"Removed {version.Title} ({version.Path}).", LogLevel.Ok);
        }
        catch (Exception ex)
        {
            // Runs straight from a button click, so any failure (a locked file, a folder that vanished, a refusal
            // to remove the active version) must end up in the status line rather than crash the app.
            Status = Loc.T("Updates.CouldntRemove", version.Title, ex.Message);
            _main.Write(LogSource, Status, LogLevel.Error);
        }

        RefreshInstalled();
    }

    /// <summary>Moves an existing install (e.g. C:\xampp\mysql_old) into the version store.</summary>
    /// <param name="component">
    /// Which kind of install the folder holds; decides which server executable proves it.
    /// </param>
    /// <param name="folder">Full path of the install's top folder; it is moved, not copied.</param>
    public void Import(Component component, string folder)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            string version = VersionStore.Import(_main.Root, component, folder);
            string name = component == Component.Php ? "PHP" : "MariaDB";
            Status = Loc.T("Updates.AddedUseSwitch", name, version);
            _main.Write(LogSource, $"Added {name} {version} from \"{folder}\".", LogLevel.Ok);
        }
        catch (Exception ex)
        {
            // Runs straight from a button click, so any failure (not an install, already in the store, a locked
            // folder) must end up in the status line rather than crash the app.
            Status = Loc.T("Updates.CouldntAddFolder", ex.Message);
        }

        RefreshInstalled();
    }

    // ── checking feeds ───────────────────────────────────────

    /// <summary>Re-reads the installed versions and loads the PHP and MariaDB feeds in parallel.</summary>
    /// <returns>
    /// A task that completes when both feeds have loaded or failed; failures show in the comparison lines.
    /// </returns>
    public async Task CheckAsync()
    {
        RefreshInstalled();
        PhpAvailable = Loc.T("Updates.Checking");
        MariaAvailable = Loc.T("Updates.Checking");
        FileZillaAvailable = Loc.T("Updates.Checking");
        MailpitAvailable = Loc.T("Updates.Checking");
        await Task.WhenAll(LoadPhpAsync(), LoadMariaMajorsAsync(), LoadFileZillaLatestAsync(), LoadMailpitLatestAsync());
    }

    /// <summary>Reads the newest Mailpit release from GitHub.</summary>
    private async Task LoadMailpitLatestAsync()
    {
        try
        {
            // An install without a version note (made by hand or by an earlier panel) is asked once and remembered.
            if (await MailpitUpdater.RecordVersionAsync(_main.Root) is { } recorded)
            {
                _mailpitVersion = recorded;
                MailpitInstalled = $"Mailpit {recorded}";
            }
            _mailpitLatest = await MailpitUpdater.GetLatestAsync();
            UpdateMailpitAvailable();
        }
        catch (Exception ex)
        {
            // An update check: whatever goes wrong with the network or the API is shown in the line and the
            // dialog stays usable.
            MailpitAvailable = Loc.T("Updates.CouldntReachGitHub", ex.Message);
        }
    }

    /// <summary>Reads the newest FileZilla Server version from filezilla-project.org.</summary>
    private async Task LoadFileZillaLatestAsync()
    {
        try
        {
            _fileZillaLatest = await ReleaseFeeds.GetFileZillaServerLatestAsync();
            if (_fileZillaLatest is null)
            {
                FileZillaAvailable = Loc.T("Updates.DownloadPageNoNewest");
                return;
            }
            UpdateFileZillaAvailable();
        }
        catch (Exception ex)
        {
            // An update check: whatever goes wrong with the network or the page is shown in the line and the
            // dialog stays usable (installing a downloaded installer still works).
            FileZillaAvailable = Loc.T("Updates.CouldntReachFileZilla", ex.Message);
        }
    }

    /// <summary>Fills <see cref="PhpBranches"/> and selects the active PHP's branch, or the newest one.</summary>
    private async Task LoadPhpAsync()
    {
        try
        {
            List<PhpBranch> branches = await ReleaseFeeds.GetPhpBranchesAsync();
            PhpBranches.Clear();
            foreach (PhpBranch branch in branches)
            {
                PhpBranches.Add(branch);
            }

            string currentBranch = VersionUtil.MajorMinor(_phpVersion ?? "");
            SelectedPhp = branches.FirstOrDefault(branch => branch.Branch == currentBranch)
                          ?? branches.FirstOrDefault();
            UpdatePhpAvailable();
            if (branches.Count == 0)
            {
                PhpAvailable = Loc.T("Updates.NoThreadSafeBuildsFound");
            }
        }
        catch (Exception ex)
        {
            // An update check: whatever goes wrong with the network or the feed is shown in the line and the
            // dialog stays usable (install from zip still works).
            PhpAvailable = Loc.T("Updates.CouldntReachPhpNet", ex.Message);
        }
    }

    /// <summary>
    /// Fills <see cref="MariaMajors"/> and selects the active series, else the newest LTS, else the first.
    /// </summary>
    private async Task LoadMariaMajorsAsync()
    {
        try
        {
            List<MariaDbMajor> majors = await ReleaseFeeds.GetMariaDbMajorsAsync();
            MariaMajors.Clear();
            foreach (MariaDbMajor major in majors)
            {
                MariaMajors.Add(major);
            }

            string currentSeries = VersionUtil.MajorMinor(_mariaVersion ?? "");
            SelectedMajor = majors.FirstOrDefault(major => major.Id == currentSeries)
                            ?? majors.FirstOrDefault(major => major.IsLts)
                            ?? majors.FirstOrDefault();
            if (majors.Count == 0)
            {
                MariaAvailable = Loc.T("Updates.NoStableReleasesFound");
            }
        }
        catch (Exception ex)
        {
            // An update check: whatever goes wrong with the network or the feed is shown in the line and the
            // dialog stays usable (install from zip still works).
            MariaAvailable = Loc.T("Updates.CouldntReachMariaDb", ex.Message);
        }
    }

    /// <summary>Loads the newest release of <see cref="SelectedMajor"/> and updates the comparison line.</summary>
    private async Task LoadMariaLatestAsync()
    {
        _mariaLatest = null;
        if (SelectedMajor is not { } major)
        {
            return;
        }

        MariaAvailable = Loc.T("Updates.Checking");
        try
        {
            MariaDbRelease release = await ReleaseFeeds.GetMariaDbLatestAsync(major.Id);
            if (SelectedMajor?.Id != major.Id)
            {
                // The selection changed while this release loaded; the newer request owns the line.
                return;
            }

            _mariaLatest = release;
            UpdateMariaAvailable();
        }
        catch (Exception ex)
        {
            // Runs fire-and-forget from the SelectedMajor setter, so nothing may escape; the error shows in
            // the line and the install button reports that no series is chosen.
            MariaAvailable = Loc.T("Updates.CouldntReadRelease", ex.Message);
        }
    }

    // ── installing ───────────────────────────────────────────

    /// <summary>Downloads the selected branch's newest build, verifies its checksum and installs it.</summary>
    /// <returns>
    /// A task that completes when the update has finished or failed; the outcome is in <see cref="Status"/>.
    /// </returns>
    public Task InstallPhpLatestAsync() => RunAsync(async ctx =>
    {
        if (SelectedPhp is not { } branch)
        {
            return UpdateResult.Fail(Loc.T("Updates.ChoosePhpBranchFirst"));
        }

        string fileName = Path.GetFileName(new Uri(branch.ZipUrl).LocalPath);
        string? expectedSha256 = branch.Sha256 ?? await ReleaseFeeds.GetPhpSha256Async(fileName);
        string? zip = await DownloadAsync(ctx, $"PHP {branch.Version}", branch.ZipUrl, fileName, expectedSha256);
        if (zip is null)
        {
            return UpdateResult.Fail(Loc.T("Updates.DownloadFailedChecksum"));
        }
        return await PhpUpdater.InstallAsync(ctx, zip);
    });

    /// <summary>Installs PHP from a zip the user picked.</summary>
    /// <param name="zip">Full path of a PHP 8 Thread Safe x64 zip.</param>
    /// <returns>
    /// A task that completes when the update has finished or failed; the outcome is in <see cref="Status"/>.
    /// </returns>
    public Task InstallPhpFromZipAsync(string zip) => RunAsync(ctx => PhpUpdater.InstallAsync(ctx, zip));

    /// <summary>Downloads the selected series' newest release, verifies its checksum and installs it.</summary>
    /// <returns>
    /// A task that completes when the update has finished or failed; the outcome is in <see cref="Status"/>.
    /// </returns>
    public Task InstallMariaLatestAsync() => RunAsync(async ctx =>
    {
        if (_mariaLatest is not { } release)
        {
            return UpdateResult.Fail(Loc.T("Updates.ChooseMariaSeriesFirst"));
        }

        string? zip = await DownloadAsync(
            ctx,
            $"MariaDB {release.Version}",
            release.ZipUrl,
            $"mariadb-{release.Version}-winx64.zip",
            release.Sha256);
        if (zip is null)
        {
            return UpdateResult.Fail(Loc.T("Updates.DownloadFailedChecksum"));
        }
        return await MariaDbUpdater.InstallAsync(ctx, zip, MakeSqlDump);
    });

    /// <summary>
    /// Installs MariaDB from a zip the user picked, with an SQL dump first when <see cref="MakeSqlDump"/> is set.
    /// </summary>
    /// <param name="zip">Full path of a MariaDB winx64 zip.</param>
    /// <returns>
    /// A task that completes when the update has finished or failed; the outcome is in <see cref="Status"/>.
    /// </returns>
    public Task InstallMariaFromZipAsync(string zip) =>
        RunAsync(ctx => MariaDbUpdater.InstallAsync(ctx, zip, MakeSqlDump));

    /// <summary>Runs a FileZilla Server installer the user downloaded and switches the FileZilla row to it.</summary>
    /// <param name="installer">Full path of FileZilla_Server_…_win64-setup.exe.</param>
    /// <returns>
    /// A task that completes when the installer has closed and the result is known; the outcome is in
    /// <see cref="Status"/>.
    /// </returns>
    public Task InstallFileZillaAsync(string installer) =>
        RunAsync(ctx => FileZillaUpdater.InstallAsync(ctx, installer));

    /// <summary>Downloads the newest Mailpit into root\mailpit and points PHP's mail() at it.</summary>
    /// <returns>A task that completes when the install has finished or failed; the outcome is in <see cref="Status"/>.</returns>
    public Task InstallMailpitAsync() =>
        _mailpitLatest is { } release ? RunAsync(ctx => MailpitUpdater.InstallAsync(ctx, release)) : Task.CompletedTask;

    /// <summary>Opens Mailpit's project page in the configured browser.</summary>
    public void OpenMailpitPage() => Shell.OpenUrl(MailpitUpdater.ProjectUrl, _main.Settings.Browser);

    /// <summary>Opens FileZilla Server's official download page in the configured browser.</summary>
    public void OpenFileZillaDownloadPage() => Shell.OpenUrl(FileZillaServer.DownloadPage, _main.Settings.Browser);

    /// <summary>Runs the rollback the last update offered; does nothing while busy or when there is none.</summary>
    /// <returns>
    /// A task that completes when the rollback has finished or failed; the outcome is in <see cref="Status"/>.
    /// </returns>
    public async Task RollbackAsync()
    {
        if (_rollback is not { } rollback || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ShowProgress = true;
        ProgressIndeterminate = true;
        try
        {
            await rollback();
            Status = Loc.T("Updates.RolledBack");
            SetRollback(null, "");
        }
        catch (Exception ex)
        {
            // A rollback moves folders and drives servers, so anything may fail; the dialog reports it and stays
            // open with the rollback still offered.
            Status = Loc.T("Updates.RollbackFailed", ex.Message);
            _main.Write(LogSource, Status, LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
            ShowProgress = false;
            RefreshInstalled();
        }
    }

    /// <summary>Downloads to %TEMP%\XamppControl and verifies the SHA-256.</summary>
    /// <param name="ctx">The update dialog's context; download progress goes to its progress bar.</param>
    /// <param name="displayName">What is downloaded, e.g. "PHP 8.4.10", shown in the status line.</param>
    /// <param name="url">The zip's download address.</param>
    /// <param name="fileName">The file name to save under in the temp folder; an existing file is replaced.</param>
    /// <param name="sha256">
    /// The published SHA-256 in hex, any case; null or empty skips the check with a warning.
    /// </param>
    /// <returns>
    /// Full path of the downloaded zip; null when the checksum did not match (the file is then deleted).
    /// </returns>
    private static async Task<string?> DownloadAsync(
        UpdateContext ctx,
        string displayName,
        string url,
        string fileName,
        string? sha256)
    {
        string target = Path.Combine(Path.GetTempPath(), "XamppControl", fileName);

        ctx.Status(Loc.T("Updates.Downloading", displayName));
        ctx.Log(url);
        await ReleaseFeeds.DownloadAsync(url, target, ctx.Progress);

        if (string.IsNullOrEmpty(sha256))
        {
            ctx.Log("No published checksum to compare against; continuing.", LogLevel.Warn);
            return target;
        }

        ctx.Status(Loc.T("Updates.VerifyingDownload"));
        ctx.Progress(null);
        string actual = await ReleaseFeeds.Sha256Async(target);
        if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Log($"SHA-256 mismatch: expected {sha256}, got {actual}.", LogLevel.Error);
            File.Delete(target);
            return null;
        }

        ctx.Log("SHA-256 verified.", LogLevel.Ok);
        return target;
    }

    /// <summary>
    /// Runs one update step with the dialog marked busy and reports its result in the status line and log.
    /// </summary>
    /// <param name="work">The update to run; receives a context wired to this dialog's status and progress bar.</param>
    private async Task RunAsync(Func<UpdateContext, Task<UpdateResult>> work)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ShowProgress = true;
        ProgressIndeterminate = true;
        SetRollback(null, "");
        _pendingRemove = null;

        var ctx = new UpdateContext(
            _main,
            status: text => Status = text,
            progress: fraction =>
            {
                ProgressIndeterminate = fraction is null;
                Progress = (fraction ?? 0) * 100;
            });

        try
        {
            UpdateResult result = await work(ctx);
            Status = result.Message;
            _main.Write(LogSource, result.Message, result.Success ? LogLevel.Ok : LogLevel.Error);
            SetRollback(result.Rollback, result.RollbackLabel ?? Loc.T("Updates.RollBack"));
        }
        catch (Exception ex)
        {
            // The top-level handler for every update: downloads, file moves and server starts can fail in any way,
            // and the dialog must report it and become usable again rather than crash the panel.
            Status = Loc.T("Updates.UpdateFailed", ex.Message);
            _main.Write(LogSource, Status, LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
            ShowProgress = false;
            RefreshInstalled();
        }
    }

    /// <summary>Sets or clears the rollback the dialog offers.</summary>
    /// <param name="rollback">The action the rollback button runs; null hides the button.</param>
    /// <param name="label">The button's text; empty when <paramref name="rollback"/> is null.</param>
    private void SetRollback(Func<Task>? rollback, string label)
    {
        _rollback = rollback;
        RollbackLabel = label;
        OnPropertyChanged(nameof(CanRollback));
    }
}
