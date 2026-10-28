using System.Collections.ObjectModel;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.Php;

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

    /// <summary>Reads php.ini into <see cref="Settings"/> and <see cref="Extensions"/>.</summary>
    private void Load()
    {
        Settings.Clear();
        Extensions.Clear();
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
    }

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
