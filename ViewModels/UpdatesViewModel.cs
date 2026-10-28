using System.Collections.ObjectModel;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.Updates;

namespace XamppControl.ViewModels;

public sealed class UpdatesViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    private string _phpInstalled = "";
    private string _mariaInstalled = "";
    private PhpBranch? _selectedPhp;
    private MariaDbMajor? _selectedMajor;
    private MariaDbRelease? _mariaLatest;
    private string _phpAvailable = "Checking…";
    private string _mariaAvailable = "Checking…";
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

    public UpdatesViewModel(MainViewModel main)
    {
        _main = main;
        RefreshInstalled();
    }

    public ObservableCollection<InstalledVersion> PhpVersions { get; } = [];
    public ObservableCollection<InstalledVersion> MariaVersions { get; } = [];
    public ObservableCollection<PhpBranch> PhpBranches { get; } = [];
    public ObservableCollection<MariaDbMajor> MariaMajors { get; } = [];

    public string PhpInstalled { get => _phpInstalled; private set => SetField(ref _phpInstalled, value); }
    public string MariaInstalled { get => _mariaInstalled; private set => SetField(ref _mariaInstalled, value); }
    public string PhpAvailable { get => _phpAvailable; private set => SetField(ref _phpAvailable, value); }
    public string MariaAvailable { get => _mariaAvailable; private set => SetField(ref _mariaAvailable, value); }

    public PhpBranch? SelectedPhp
    {
        get => _selectedPhp;
        set
        {
            if (SetField(ref _selectedPhp, value))
                UpdatePhpAvailable();
        }
    }

    public MariaDbMajor? SelectedMajor
    {
        get => _selectedMajor;
        set
        {
            if (SetField(ref _selectedMajor, value))
                _ = LoadMariaLatestAsync();
        }
    }

    public bool MakeSqlDump { get => _makeSqlDump; set => SetField(ref _makeSqlDump, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !IsBusy;

    public string Status { get => _status; private set => SetField(ref _status, value); }
    public bool ShowProgress { get => _showProgress; private set => SetField(ref _showProgress, value); }
    public bool ProgressIndeterminate
    {
        get => _progressIndeterminate;
        private set => SetField(ref _progressIndeterminate, value);
    }
    public double Progress { get => _progress; private set => SetField(ref _progress, value); }

    public bool CanRollback => _rollback is not null;
    public string RollbackLabel { get => _rollbackLabel; private set => SetField(ref _rollbackLabel, value); }

    // ── installed versions ───────────────────────────────────

    private void RefreshInstalled()
    {
        var root = _main.Root;

        if (!_importedLegacy)
        {
            _importedLegacy = true;
            var moved = VersionStore.ImportLegacyBackups(root);
            if (moved.Count > 0)
                _main.Write("update", "Added earlier backups as installed versions: " + string.Join(", ", moved));
        }

        Fill(PhpVersions, VersionStore.List(root, Component.Php));
        Fill(MariaVersions, VersionStore.List(root, Component.MariaDb));
        var php = XamppPaths.FileVersion(Path.Combine(root, "php", "php.exe"));
        PhpInstalled = php is null ? "Not found" : $"PHP {php}";

        var server = FileTools.FirstExisting(Path.Combine(root, "mysql", "bin"), "mariadbd.exe", "mysqld.exe");
        var maria = XamppPaths.FileVersion(server);
        MariaInstalled = maria is null ? "Not found" : $"MariaDB {maria}";

        UpdatePhpAvailable();
        UpdateMariaAvailable();
    }

    private void UpdatePhpAvailable()
    {
        if (SelectedPhp is not { } branch)
            return;
        var installed = VersionUtil.Parse(PhpInstalled);
        var latest = VersionUtil.Parse(branch.Version);
        PhpAvailable = installed == latest ? "You're on the newest build of this branch."
            : installed > latest ? $"PHP {branch.Version} is older than the installed version."
            : $"PHP {branch.Version} is available.";
    }

    private void UpdateMariaAvailable()
    {
        if (_mariaLatest is not { } release)
            return;
        var installed = VersionUtil.Parse(MariaInstalled);
        var latest = VersionUtil.Parse(release.Version);
        MariaAvailable = installed == latest ? "You're on the newest release of this series."
            : installed > latest
                ? $"MariaDB {release.Version} is older than the installed version; downgrades aren't supported."
            : $"MariaDB {release.Version} is available.";
    }

    private static void Fill(ObservableCollection<InstalledVersion> target, List<InstalledVersion> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    // ── installed versions: switch, remove, add ──────────────

    public Task SwitchAsync(InstalledVersion version) =>
        RunAsync(ctx => VersionSwitcher.SwitchAsync(ctx, version));

    /// <summary>
    /// First click asks for confirmation in the status line; a second click on the same version deletes it.
    /// </summary>
    public void Remove(InstalledVersion version)
    {
        if (IsBusy || version.IsActive)
            return;

        if (_pendingRemove != version.Path)
        {
            _pendingRemove = version.Path;
            Status = version.Component == Component.MariaDb
                ? $"Click Remove again to delete {version.Title} and the databases stored with it ({version.Detail})."
                : $"Click Remove again to delete {version.Title} ({version.Detail}).";
            return;
        }

        _pendingRemove = null;
        try
        {
            VersionStore.Remove(version);
            Status = $"{version.Title} was removed.";
            _main.Write("update", $"Removed {version.Title} ({version.Path}).", LogLevel.Ok);
        }
        catch (Exception ex)
        {
            Status = $"Couldn't remove {version.Title}: {ex.Message}";
            _main.Write("update", Status, LogLevel.Error);
        }
        RefreshInstalled();
    }

    /// <summary>Moves an existing install (e.g. C:\xampp\mysql_old) into the version store.</summary>
    public void Import(Component component, string folder)
    {
        if (IsBusy)
            return;
        try
        {
            var version = VersionStore.Import(_main.Root, component, folder);
            var name = component == Component.Php ? "PHP" : "MariaDB";
            Status = $"{name} {version} added. Use Switch to make it active.";
            _main.Write("update", $"Added {name} {version} from \"{folder}\".", LogLevel.Ok);
        }
        catch (Exception ex)
        {
            Status = "Couldn't add that folder: " + ex.Message;
        }
        RefreshInstalled();
    }

    // ── checking feeds ───────────────────────────────────────

    public async Task CheckAsync()
    {
        RefreshInstalled();
        PhpAvailable = "Checking…";
        MariaAvailable = "Checking…";
        await Task.WhenAll(LoadPhpAsync(), LoadMariaMajorsAsync());
    }

    private async Task LoadPhpAsync()
    {
        try
        {
            var branches = await ReleaseFeeds.GetPhpBranchesAsync();
            PhpBranches.Clear();
            foreach (var b in branches)
                PhpBranches.Add(b);

            var current = VersionUtil.MajorMinor(PhpInstalled);
            SelectedPhp = branches.FirstOrDefault(b => b.Branch == current) ?? branches.FirstOrDefault();
            UpdatePhpAvailable();
            if (branches.Count == 0)
                PhpAvailable = "No Thread Safe x64 builds found on windows.php.net.";
        }
        catch (Exception ex)
        {
            PhpAvailable = "Couldn't reach windows.php.net: " + ex.Message;
        }
    }

    private async Task LoadMariaMajorsAsync()
    {
        try
        {
            var majors = await ReleaseFeeds.GetMariaDbMajorsAsync();
            MariaMajors.Clear();
            foreach (var m in majors)
                MariaMajors.Add(m);

            var current = VersionUtil.MajorMinor(MariaInstalled);
            SelectedMajor = majors.FirstOrDefault(m => m.Id == current)
                            ?? majors.FirstOrDefault(m => m.IsLts)
                            ?? majors.FirstOrDefault();
            if (majors.Count == 0)
                MariaAvailable = "No stable releases found.";
        }
        catch (Exception ex)
        {
            MariaAvailable = "Couldn't reach downloads.mariadb.org: " + ex.Message;
        }
    }

    private async Task LoadMariaLatestAsync()
    {
        _mariaLatest = null;
        if (SelectedMajor is not { } major)
            return;
        MariaAvailable = "Checking…";
        try
        {
            var release = await ReleaseFeeds.GetMariaDbLatestAsync(major.Id);
            if (SelectedMajor?.Id != major.Id)
                return; // selection changed meanwhile
            _mariaLatest = release;
            UpdateMariaAvailable();
        }
        catch (Exception ex)
        {
            MariaAvailable = "Couldn't read the release: " + ex.Message;
        }
    }

    // ── installing ───────────────────────────────────────────

    public Task InstallPhpLatestAsync() => RunAsync(async ctx =>
    {
        if (SelectedPhp is not { } branch)
            return UpdateResult.Fail("Choose a PHP branch first.");

        var file = Path.GetFileName(new Uri(branch.ZipUrl).LocalPath);
        var zip = await DownloadAsync(ctx, $"PHP {branch.Version}", branch.ZipUrl, file,
            branch.Sha256 ?? await ReleaseFeeds.GetPhpSha256Async(file));
        return zip is null
            ? UpdateResult.Fail("The download failed its checksum check, so it wasn't installed.")
            : await PhpUpdater.InstallAsync(ctx, zip);
    });

    public Task InstallPhpFromZipAsync(string zip) => RunAsync(ctx => PhpUpdater.InstallAsync(ctx, zip));

    public Task InstallMariaLatestAsync() => RunAsync(async ctx =>
    {
        if (_mariaLatest is not { } release)
            return UpdateResult.Fail("Choose a MariaDB series first.");

        var zip = await DownloadAsync(ctx, $"MariaDB {release.Version}", release.ZipUrl,
            $"mariadb-{release.Version}-winx64.zip", release.Sha256);
        return zip is null
            ? UpdateResult.Fail("The download failed its checksum check, so it wasn't installed.")
            : await MariaDbUpdater.InstallAsync(ctx, zip, MakeSqlDump);
    });

    public Task InstallMariaFromZipAsync(string zip) =>
        RunAsync(ctx => MariaDbUpdater.InstallAsync(ctx, zip, MakeSqlDump));

    public async Task RollbackAsync()
    {
        if (_rollback is not { } rollback || IsBusy)
            return;

        IsBusy = true;
        ShowProgress = true;
        ProgressIndeterminate = true;
        try
        {
            await rollback();
            Status = "Rolled back.";
            SetRollback(null, "");
        }
        catch (Exception ex)
        {
            Status = "Rollback failed: " + ex.Message;
            _main.Write("update", Status, LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
            ShowProgress = false;
            RefreshInstalled();
        }
    }

    /// <summary>Downloads to %TEMP%\XamppControl and verifies the SHA-256. Returns null on checksum mismatch.</summary>
    private static async Task<string?> DownloadAsync(
        UpdateContext ctx, string what, string url, string fileName, string? sha256)
    {
        var target = Path.Combine(Path.GetTempPath(), "XamppControl", fileName);

        ctx.Status($"Downloading {what}…");
        ctx.Log(url);
        await ReleaseFeeds.DownloadAsync(url, target, ctx.Progress);

        if (string.IsNullOrEmpty(sha256))
        {
            ctx.Log("No published checksum to compare against; continuing.", LogLevel.Warn);
            return target;
        }

        ctx.Status("Verifying the download…");
        ctx.Progress(null);
        var actual = await ReleaseFeeds.Sha256Async(target);
        if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Log($"SHA-256 mismatch: expected {sha256}, got {actual}.", LogLevel.Error);
            File.Delete(target);
            return null;
        }
        ctx.Log("SHA-256 verified.", LogLevel.Ok);
        return target;
    }

    private async Task RunAsync(Func<UpdateContext, Task<UpdateResult>> work)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ShowProgress = true;
        ProgressIndeterminate = true;
        SetRollback(null, "");
        _pendingRemove = null;

        var ctx = new UpdateContext(_main,
            status: text => Status = text,
            progress: value =>
            {
                ProgressIndeterminate = value is null;
                Progress = (value ?? 0) * 100;
            });

        try
        {
            var result = await work(ctx);
            Status = result.Message;
            _main.Write("update", result.Message, result.Success ? LogLevel.Ok : LogLevel.Error);
            SetRollback(result.Rollback, result.RollbackLabel ?? "Roll back");
        }
        catch (Exception ex)
        {
            Status = "Update failed: " + ex.Message;
            _main.Write("update", Status, LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
            ShowProgress = false;
            RefreshInstalled();
        }
    }

    private void SetRollback(Func<Task>? rollback, string label)
    {
        _rollback = rollback;
        RollbackLabel = label;
        OnPropertyChanged(nameof(CanRollback));
    }
}
