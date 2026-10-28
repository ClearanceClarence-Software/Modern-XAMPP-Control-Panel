using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamppControl.Core;

// Property names in this file are the settings.json keys: renaming one resets that setting for every user.

/// <summary>Service names and ports the panel uses for its checks, links and service commands.</summary>
public sealed class ServiceSettings
{
    /// <summary>Windows service name of Apache, for the service checkbox and net start/stop.</summary>
    public string ApacheServiceName { get; set; } = "Apache2.4";

    /// <summary>Apache's HTTP port, used for the Admin links and the port conflict check.</summary>
    public int ApachePort { get; set; } = 80;

    /// <summary>Apache's HTTPS port, used for the port conflict check.</summary>
    public int ApacheSslPort { get; set; } = 443;

    /// <summary>Windows service name of MySQL/MariaDB, for the service checkbox and net start/stop.</summary>
    public string MySqlServiceName { get; set; } = "mysql";

    /// <summary>MySQL/MariaDB's TCP port, used for the port conflict check and the mysqladmin shutdown.</summary>
    public int MySqlPort { get; set; } = 3306;

    /// <summary>FileZilla Server's FTP port, used for the port conflict check.</summary>
    public int FileZillaPort { get; set; } = 21;

    /// <summary>FileZilla Server's admin interface port, used for the port conflict check.</summary>
    public int FileZillaAdminPort { get; set; } = 14147;

    /// <summary>Mercury's mail ports for the port conflict check, as typed: numbers split by , ; or space.</summary>
    public string MercuryPorts { get; set; } = "25, 79, 105, 106, 110, 143, 2224";

    /// <summary>Tomcat's HTTP port, used for the Admin link and the port conflict check.</summary>
    public int TomcatPort { get; set; } = 8080;

    /// <summary>Tomcat's shutdown port, used for the port conflict check.</summary>
    public int TomcatShutdownPort { get; set; } = 8005;

    /// <summary>Tomcat's AJP connector port, used for the port conflict check.</summary>
    public int TomcatAjpPort { get; set; } = 8009;

    /// <summary>
    /// <see cref="MercuryPorts"/> as numbers, in the order typed; entries that are not a port from 1 to 65535 are
    /// left out.
    /// </summary>
    public IReadOnlyList<int> ParsedMercuryPorts =>
        MercuryPorts.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out int parsedPort) ? parsedPort : 0)
            .Where(port => port is > 0 and < 65536)
            .ToList();
}

/// <summary>An extra file the user added to a module's Config menu.</summary>
public sealed class UserFile
{
    /// <summary>The module's display name ("Apache", "MySQL", ...), matched case-insensitively.</summary>
    public string Module { get; set; } = "Apache";

    /// <summary>Full path of the file to open.</summary>
    public string Path { get; set; } = "";
}

/// <summary>A local site served by Apache under its own host name, set up from the Sites window.</summary>
public sealed class Site
{
    /// <summary>The host name, such as myproject.test; lowercase, without scheme or port.</summary>
    public string Host { get; set; } = "";

    /// <summary>Full path of the site's document root.</summary>
    public string Folder { get; set; } = "";

    /// <summary>True also serves the site over HTTPS with the panel's local certificate.</summary>
    public bool UseHttps { get; set; } = true;
}

/// <summary>User settings, stored as JSON in %AppData%\XamppControl\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>The XAMPP folder the user chose; null is found by <see cref="XamppPaths.Resolve"/>.</summary>
    public string? XamppRoot { get; set; }

    /// <summary>Editor for config and log files, as a full path or a name on PATH; null uses Notepad.</summary>
    public string? Editor { get; set; }

    /// <summary>Browser for Admin links; empty uses the system default.</summary>
    public string? Browser { get; set; }

    /// <summary>
    /// Root password used for the clean MySQL shutdown (mysqladmin). XAMPP's default is empty.
    /// Stored in plain text on this machine; only meant for local dev setups.
    /// </summary>
    public string? MySqlRootPassword { get; set; }

    /// <summary>Module names to start with the panel, e.g. "Apache", "MySQL".</summary>
    public List<string> AutoStart { get; set; } = [];

    /// <summary>True opens the main window minimized.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>
    /// The color theme: <see cref="AppTheme.System"/>, <see cref="AppTheme.Light"/> or <see cref="AppTheme.Dark"/>.
    /// </summary>
    public string Theme { get; set; } = AppTheme.System;

    /// <summary>True hides the main window to the tray icon when it is minimized.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>True checks GitHub for a newer version of the panel when it starts.</summary>
    public bool CheckForAppUpdates { get; set; } = true;

    /// <summary>True starts Tomcat in a visible console window; false starts it hidden.</summary>
    public bool TomcatOutputWindow { get; set; } = true;

    /// <summary>True checks for port conflicts when the panel opens.</summary>
    public bool CheckPortsOnStartup { get; set; } = true;

    /// <summary>True shows debug lines in the log.</summary>
    public bool ShowDebug { get; set; }

    /// <summary>Service names and ports.</summary>
    public ServiceSettings Services { get; set; } = new();

    /// <summary>Extra files in the modules' Config menus.</summary>
    public List<UserFile> UserFiles { get; set; } = [];

    /// <summary>Local sites from the Sites window, in the order they are written to the Apache config.</summary>
    public List<Site> Sites { get; set; } = [];

    /// <summary>Font size of the log list, 10 to 18 after <see cref="Load"/>.</summary>
    public int LogFontSize { get; set; } = 12;

    /// <summary>Lines the log keeps before dropping the oldest, 100 to 100,000 after <see cref="Load"/>.</summary>
    public int MaxLogLines { get; set; } = 1000;

    /// <summary>True also appends log lines to <see cref="LogFilePath"/>.</summary>
    public bool LogToFile { get; set; }

    /// <summary>Pre-0.2 setting, migrated into <see cref="AutoStart"/> on load and then cleared.</summary>
    public bool? AutoStartApache { get; set; }

    /// <summary>Pre-0.2 setting, migrated into <see cref="AutoStart"/> on load and then cleared.</summary>
    public bool? AutoStartMySql { get; set; }

    /// <summary>The settings folder, %AppData%\XamppControl; it may not exist yet.</summary>
    public static string Folder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XamppControl");

    private static string FilePath => System.IO.Path.Combine(Folder, "settings.json");

    /// <summary>The log file written when <see cref="LogToFile"/> is on.</summary>
    public static string LogFilePath => System.IO.Path.Combine(Folder, "xampp-control.log");

    /// <summary>True when the module is in <see cref="AutoStart"/>.</summary>
    /// <param name="module">The module's display name, matched case-insensitively.</param>
    /// <returns>True when listed.</returns>
    public bool IsAutoStart(string module) =>
        AutoStart.Any(listedModule => string.Equals(listedModule, module, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads settings.json, migrates old keys and clamps values into range.</summary>
    /// <returns>The stored settings; defaults when the file is missing, unreadable or corrupt.</returns>
    public static AppSettings Load()
    {
        AppSettings settings = ReadFile();
        MigrateAutoStart(settings);

        settings.Services ??= new ServiceSettings();
        settings.UserFiles ??= [];
        settings.AutoStart ??= [];
        settings.Sites ??= [];
        settings.Theme = AppTheme.Normalize(settings.Theme);
        settings.LogFontSize = Math.Clamp(settings.LogFontSize, 10, 18);
        settings.MaxLogLines = Math.Clamp(settings.MaxLogLines, 100, 100_000);
        return settings;
    }

    /// <summary>Deserializes settings.json.</summary>
    /// <returns>The stored settings; defaults when the file is missing, unreadable, corrupt or the JSON null.</returns>
    private static AppSettings ReadFile()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.AppSettings)
                ?? new AppSettings();
        }
        catch
        {
            // Settings must never stop the panel from starting: a corrupt or unreadable file means defaults.
            return new AppSettings();
        }
    }

    /// <summary>Moves the pre-0.2 per-module flags into <see cref="AutoStart"/> and clears them.</summary>
    /// <param name="settings">The settings to change in place.</param>
    private static void MigrateAutoStart(AppSettings settings)
    {
        if (settings.AutoStartApache == true && !settings.IsAutoStart("Apache"))
        {
            settings.AutoStart.Add("Apache");
        }
        if (settings.AutoStartMySql == true && !settings.IsAutoStart("MySQL"))
        {
            settings.AutoStart.Add("MySQL");
        }

        settings.AutoStartApache = null;
        settings.AutoStartMySql = null;
    }

    /// <summary>Writes these settings to settings.json, creating the folder when needed.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
    }

    /// <summary>Deep copy, so dialogs can edit without touching the live settings until Save.</summary>
    /// <returns>A new instance with no references shared with this one.</returns>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize(
            JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings),
            SettingsJson.Default.AppSettings)
        ?? throw new InvalidOperationException("Serializing the settings produced the JSON literal null.");

    /// <summary>Copies every user-facing setting from an edited copy into this instance.</summary>
    /// <param name="other">
    /// Usually a <see cref="Clone"/> a dialog edited; its lists are copied, its <see cref="Services"/> is shared.
    /// </param>
    public void CopyFrom(AppSettings other)
    {
        XamppRoot = other.XamppRoot;
        Editor = other.Editor;
        Browser = other.Browser;
        MySqlRootPassword = other.MySqlRootPassword;
        AutoStart = [.. other.AutoStart];
        Theme = other.Theme;
        StartMinimized = other.StartMinimized;
        MinimizeToTray = other.MinimizeToTray;
        CheckForAppUpdates = other.CheckForAppUpdates;
        TomcatOutputWindow = other.TomcatOutputWindow;
        CheckPortsOnStartup = other.CheckPortsOnStartup;
        ShowDebug = other.ShowDebug;
        Services = other.Services;
        UserFiles = [.. other.UserFiles];
        Sites = [.. other.Sites];
        LogFontSize = other.LogFontSize;
        MaxLogLines = other.MaxLogLines;
        LogToFile = other.LogToFile;
    }
}

/// <summary>Source-generated JSON metadata for settings.json: indented, with null values left out.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
