using System.Collections.ObjectModel;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.Php;
using XamppControl.Updates;

namespace XamppControl.ViewModels;

/// <summary>One php.ini directive in the PHP settings window.</summary>
/// <param name="key">The directive, such as memory_limit.</param>
/// <param name="label">The name shown in the window.</param>
/// <param name="hint">One line under the box: the unit, an example, or what a special value means.</param>
/// <param name="currentValue">The value php.ini sets now; empty when it doesn't set it.</param>
public sealed class PhpSettingRow(string key, string label, string hint, string currentValue) : ObservableObject
{
    private string _value = currentValue;

    /// <summary>The directive, such as memory_limit.</summary>
    public string Key { get; } = key;

    /// <summary>The name shown in the window.</summary>
    public string Label { get; } = label;

    /// <summary>The line under the box.</summary>
    public string Hint { get; } = hint;

    /// <summary>The value read from php.ini, to tell whether the user changed it.</summary>
    public string OriginalValue { get; } = currentValue;

    /// <summary>The value in the box.</summary>
    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    /// <summary>True when the box holds a value other than the one read from php.ini.</summary>
    public bool IsChanged => Value.Trim() != OriginalValue && Value.Trim().Length > 0;
}

/// <summary>One extension's check box in the PHP settings window.</summary>
/// <param name="extension">The extension as php.ini loads it now.</param>
public sealed class PhpExtensionRow(PhpExtension extension) : ObservableObject
{
    private bool _isEnabled = extension.IsEnabled;

    /// <summary>The extension as read from php.ini.</summary>
    public PhpExtension Extension { get; } = extension;

    /// <summary>The check box's text: the name, with "(Zend)" for Zend extensions.</summary>
    public string Label => Extension.IsZend ? $"{Extension.Name} (Zend)" : Extension.Name;

    /// <summary>The check box.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    /// <summary>True when the check box differs from php.ini.</summary>
    public bool IsChanged => IsEnabled != Extension.IsEnabled;
}

/// <summary>The PHP settings window: common php.ini values and the extensions, saved with an Apache restart.</summary>
public sealed class PhpSettingsViewModel : ObservableObject
{
    private const string LogTag = "php";

    private static readonly (string Key, string Label, string Hint)[] _commonSettings =
    [
        ("memory_limit", "Memory limit", "Per script, such as 512M; -1 means no limit."),
        ("upload_max_filesize", "Largest upload", "Per file, such as 64M."),
        ("post_max_size", "Largest form post", "All of one request's uploads together; at least the largest upload."),
        ("max_execution_time", "Max execution time", "Seconds a script may run; 0 means no limit."),
        ("max_input_time", "Max input time", "Seconds to read a request; -1 uses the execution time."),
        ("max_input_vars", "Max input variables", "Form fields per request; raise it for large WordPress menus."),
        ("display_errors", "Display errors", "On shows errors in the page, Off only logs them."),
        ("date.timezone", "Time zone", "Such as Europe/Oslo or UTC."),
    ];

    private readonly MainViewModel _main;
    private bool _isBusy;
    private string _status = "";
    private string _xdebugStatus = "";
    private string _xdebugAction = "";
    private string? _xdebugUpdate;
    private bool _isXdebugInstalled;
    private bool _isXdebugEnabled;

    /// <summary>Reads php.ini from the active PHP and fills the settings and extensions.</summary>
    /// <param name="main">The panel; supplies the XAMPP folder, the editor and the log, and restarts Apache.</param>
    public PhpSettingsViewModel(MainViewModel main)
    {
        _main = main;
        IniPath = Path.Combine(main.Root, "php", "php.ini");
        string? phpVersion = XamppPaths.FileVersion(Path.Combine(main.Root, "php", "php.exe"));
        Title = phpVersion is null ? IniPath : $"PHP {phpVersion}   ·   {IniPath}";
        Load();
    }

    /// <summary>Full path of the active PHP's php.ini.</summary>
    public string IniPath { get; }

    /// <summary>The line above the settings: the PHP version and the php.ini path.</summary>
    public string Title { get; }

    /// <summary>The common directives, in a fixed order.</summary>
    public ObservableCollection<PhpSettingRow> Settings { get; } = [];

    /// <summary>Every extension DLL in php\ext, sorted by name.</summary>
    public ObservableCollection<PhpExtensionRow> Extensions { get; } = [];

    /// <summary>True when php.ini was read; the settings and Save are disabled otherwise.</summary>
    public bool HasIni => File.Exists(IniPath);

    /// <summary>True while saving and restarting Apache.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    /// <summary>The opposite of <see cref="IsBusy"/>, for IsEnabled bindings.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>True when php.ini exists and nothing is being saved; enables Save.</summary>
    public bool CanSave => IsIdle && HasIni;

    /// <summary>The status line: what was saved, or why nothing could be.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>The Xdebug line: the installed version and whether php.ini loads it.</summary>
    public string XdebugStatus
    {
        get => _xdebugStatus;
        private set => SetField(ref _xdebugStatus, value);
    }

    /// <summary>The Xdebug button's text: "Install Xdebug", "Turn on" or "Turn off".</summary>
    public string XdebugAction
    {
        get => _xdebugAction;
        private set => SetField(ref _xdebugAction, value);
    }

    /// <summary>"Update to 3.5.3" when PECL has a newer build than the installed one; null otherwise.</summary>
    public string? XdebugUpdate
    {
        get => _xdebugUpdate;
        private set
        {
            if (SetField(ref _xdebugUpdate, value))
            {
                OnPropertyChanged(nameof(HasXdebugUpdate));
            }
        }
    }

    /// <summary>True when the update button should show.</summary>
    public bool HasXdebugUpdate => XdebugUpdate is not null;

    /// <summary>Reads php.ini into <see cref="Settings"/> and <see cref="Extensions"/>.</summary>
    private void Load()
    {
        Settings.Clear();
        Extensions.Clear();
        LoadXdebugState(null);
        if (!HasIni)
        {
            Status = $"No php.ini in \"{Path.GetDirectoryName(IniPath)}\".";
            return;
        }

        PhpIni ini;
        List<PhpExtension> extensions;
        try
        {
            ini = PhpIni.Load(IniPath);
            extensions = ini.ListExtensions(Path.Combine(_main.Root, "php", "ext"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable php.ini or ext folder: the window opens empty with the reason in the status line.
            Status = "php.ini or the ext folder can't be read: " + ex.Message;
            return;
        }

        foreach ((string key, string label, string hint) in _commonSettings)
        {
            Settings.Add(new PhpSettingRow(key, label, hint, ini.GetValue(key) ?? ""));
        }

        foreach (PhpExtension extension in extensions)
        {
            Extensions.Add(new PhpExtensionRow(extension));
        }
        LoadXdebugState(ini);
    }

    /// <summary>Fills the Xdebug line and button from the ext folder and php.ini.</summary>
    /// <param name="ini">The loaded php.ini; null when it couldn't be read, which shows Xdebug as unavailable.</param>
    private void LoadXdebugState(PhpIni? ini)
    {
        string? installed = XdebugInstaller.InstalledVersion(PhpDir);
        _isXdebugInstalled = installed is not null;
        _isXdebugEnabled = ini is not null && _isXdebugInstalled && XdebugInstaller.IsEnabled(ini);
        if (!_isXdebugInstalled)
        {
            XdebugStatus = "Not installed. Install downloads the build that matches this PHP from PECL.";
            XdebugAction = "Install Xdebug";
        }
        else
        {
            string state = _isXdebugEnabled ? "on: step debugging on port 9003" : "off";
            string name = installed == "?" ? "Xdebug" : $"Xdebug {installed}";
            XdebugStatus = $"{name} installed, {state}.";
            XdebugAction = _isXdebugEnabled ? "Turn off" : "Turn on";
        }
        if (XdebugUpdate is { } update && VersionUtil.Parse(update) <= VersionUtil.Parse(installed))
        {
            XdebugUpdate = null;
        }
    }

    /// <summary>Asks PECL for the newest Xdebug and shows the update button when it's newer than the installed one.</summary>
    /// <returns>A task that completes when the check is done; a failed check shows nothing.</returns>
    public async Task CheckXdebugUpdateAsync()
    {
        if (!_isXdebugInstalled)
        {
            return;
        }
        try
        {
            string latest = await XdebugInstaller.LatestVersionAsync();
            string? installed = XdebugInstaller.InstalledVersion(PhpDir);
            XdebugUpdate = VersionUtil.Parse(latest) > VersionUtil.Parse(installed) ? latest : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Offline or PECL changed its page: the button simply doesn't show.
            _main.Write(LogTag, "Xdebug update check failed: " + ex.Message, LogLevel.Debug);
        }
    }

    /// <summary>The Xdebug button: installs it when missing, otherwise turns it on or off; Apache restarts.</summary>
    /// <returns>A task that completes after the restart or when the error is shown.</returns>
    public Task XdebugActionAsync() =>
        _isXdebugInstalled ? SetXdebugEnabledAsync(!_isXdebugEnabled) : InstallXdebugAsync(null);

    /// <summary>Installs the version the update button names and turns it on.</summary>
    /// <returns>A task that completes after the restart or when the error is shown.</returns>
    public Task UpdateXdebugAsync() => XdebugUpdate is { } version ? InstallXdebugAsync(version) : Task.CompletedTask;

    private async Task InstallXdebugAsync(string? version)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Status = "Reading the PHP build…";
            PhpBuildInfo php = await XdebugInstaller.ReadPhpBuildAsync(PhpDir)
                ?? throw new InvalidOperationException("php.exe didn't report a version and compiler PECL builds for.");

            Status = "Asking PECL for the newest Xdebug…";
            version ??= await XdebugInstaller.LatestVersionAsync();

            string file = await XdebugInstaller.InstallAsync(PhpDir, php, version, fraction =>
                Status = fraction is { } done
                    ? $"Downloading Xdebug {version} for PHP {php.Branch}… {done:P0}"
                    : $"Downloading Xdebug {version} for PHP {php.Branch}…");
            _main.Write(LogTag, $"Installed Xdebug {version} to \"{file}\".", LogLevel.Ok);

            await WriteXdebugAsync(true);
            Status = $"Xdebug {version} installed and on. Set your editor to listen on port 9003.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // PECL builds each Xdebug release per PHP branch; a brand-new PHP may not have one yet.
            Status = "PECL has no Xdebug build for this PHP version yet.";
        }
        catch (Exception ex)
        {
            // Downloads, zips and php.ini can all fail; the window reports it and stays usable.
            _main.Write(LogTag, "Installing Xdebug failed: " + ex.Message, LogLevel.Error);
            Status = "Xdebug couldn't be installed: " + ex.Message;
        }
        finally
        {
            Load();
            IsBusy = false;
        }
    }

    private async Task SetXdebugEnabledAsync(bool isEnabled)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await WriteXdebugAsync(isEnabled);
            Status = isEnabled
                ? "Xdebug is on. Set your editor to listen on port 9003; it starts on request when a debug cookie or " +
                  "XDEBUG_TRIGGER is present."
                : "Xdebug is off. The DLL stays in php\\ext for next time.";
        }
        catch (Exception ex)
        {
            // Writes php.ini and restarts Apache; report rather than crash.
            _main.Write(LogTag, "Changing Xdebug failed: " + ex.Message, LogLevel.Error);
            Status = "Xdebug couldn't be changed: " + ex.Message;
        }
        finally
        {
            Load();
            IsBusy = false;
        }
    }

    /// <summary>Writes the Xdebug lines to a fresh read of php.ini and restarts Apache when it runs.</summary>
    /// <param name="isEnabled">True loads the DLL; false comments it out.</param>
    private async Task WriteXdebugAsync(bool isEnabled)
    {
        PhpIni ini = PhpIni.Load(IniPath);
        XdebugInstaller.SetEnabled(ini, isEnabled);
        ini.Save();
        _main.Write(LogTag, isEnabled ? "Xdebug enabled in php.ini." : "Xdebug disabled in php.ini.");
        Load();

        if (_main.FindModule("Apache") is { IsRunning: true } apache)
        {
            Status = "Restarting Apache…";
            await apache.RestartForConfigChangeAsync();
        }
    }

    private string PhpDir => Path.Combine(_main.Root, "php");

    /// <summary>Writes the changed values and extensions to php.ini and restarts Apache when it runs.</summary>
    /// <returns>A task that completes after the restart, or at once when nothing changed.</returns>
    public async Task SaveAsync()
    {
        List<PhpSettingRow> changedSettings = Settings.Where(setting => setting.IsChanged).ToList();
        List<PhpExtensionRow> changedExtensions = Extensions.Where(extension => extension.IsChanged).ToList();
        if (changedSettings.Count == 0 && changedExtensions.Count == 0)
        {
            Status = "Nothing changed.";
            return;
        }

        IsBusy = true;
        try
        {
            WriteChanges(changedSettings, changedExtensions);
            Load();

            if (_main.FindModule("Apache") is { IsRunning: true } apache)
            {
                Status = "Restarting Apache…";
                await apache.RestartForConfigChangeAsync();
            }

            Status = "Saved. The previous php.ini is kept as php.ini.bak.";
        }
        catch (Exception ex)
        {
            // Saving writes php.ini and restarts Apache, so anything may fail; the window reports it and stays
            // usable instead of crashing the panel.
            _main.Write(LogTag, "Saving php.ini failed: " + ex.Message, LogLevel.Error);
            Status = "php.ini couldn't be saved: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Applies the changes to a fresh read of php.ini, saves it and logs each change.</summary>
    /// <param name="changedSettings">The directives whose box changed.</param>
    /// <param name="changedExtensions">The extensions whose check box changed.</param>
    /// <exception cref="IOException">php.ini cannot be read or written.</exception>
    private void WriteChanges(List<PhpSettingRow> changedSettings, List<PhpExtensionRow> changedExtensions)
    {
        // Read again, so an edit made in a text editor while the window was open isn't overwritten.
        PhpIni ini = PhpIni.Load(IniPath);
        foreach (PhpSettingRow setting in changedSettings)
        {
            ini.SetValue(setting.Key, setting.Value.Trim());
            _main.Write(LogTag, $"{setting.Key} = {setting.Value.Trim()} (was {DisplayValue(setting.OriginalValue)})");
        }
        foreach (PhpExtensionRow extension in changedExtensions)
        {
            ini.SetExtensionEnabled(extension.Extension, extension.IsEnabled);
            string change = extension.IsEnabled ? "enabled" : "disabled";
            _main.Write(LogTag, $"Extension {extension.Extension.Name} {change}");
        }
        ini.Save();
        _main.Write(LogTag, "php.ini saved.", LogLevel.Ok);
    }

    /// <summary>Opens php.ini in the configured editor.</summary>
    public void OpenIni() => Shell.OpenInEditor(IniPath, _main.Settings.Editor);

    private static string DisplayValue(string value) => value.Length == 0 ? "not set" : value;
}
