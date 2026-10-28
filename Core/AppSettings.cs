using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamppControl.Core;

/// <summary>Service names and ports the panel uses for its checks, links and service commands.</summary>
public sealed class ServiceSettings
{
    public string ApacheServiceName { get; set; } = "Apache2.4";
    public int ApachePort { get; set; } = 80;
    public int ApacheSslPort { get; set; } = 443;

    public string MySqlServiceName { get; set; } = "mysql";
    public int MySqlPort { get; set; } = 3306;

    public int FileZillaPort { get; set; } = 21;
    public int FileZillaAdminPort { get; set; } = 14147;

    public string MercuryPorts { get; set; } = "25, 79, 105, 106, 110, 143, 2224";

    public int TomcatPort { get; set; } = 8080;
    public int TomcatShutdownPort { get; set; } = 8005;
    public int TomcatAjpPort { get; set; } = 8009;

    public IReadOnlyList<int> ParsedMercuryPorts =>
        MercuryPorts.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p, out var n) ? n : 0)
            .Where(n => n is > 0 and < 65536)
            .ToList();
}

/// <summary>An extra file the user added to a module's Config menu.</summary>
public sealed class UserFile
{
    public string Module { get; set; } = "Apache";
    public string Path { get; set; } = "";
}

/// <summary>User settings, stored as JSON in %AppData%\XamppControl\settings.json.</summary>
public sealed class AppSettings
{
    public string? XamppRoot { get; set; }
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

    public bool StartMinimized { get; set; }
    public bool TomcatOutputWindow { get; set; } = true;
    public bool CheckPortsOnStartup { get; set; } = true;
    public bool ShowDebug { get; set; }

    public ServiceSettings Services { get; set; } = new();
    public List<UserFile> UserFiles { get; set; } = [];

    // Log options
    public int LogFontSize { get; set; } = 12;
    public int MaxLogLines { get; set; } = 1000;
    public bool LogToFile { get; set; }

    // Pre-0.2 settings, migrated into AutoStart on load.
    public bool? AutoStartApache { get; set; }
    public bool? AutoStartMySql { get; set; }

    public static string Folder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XamppControl");

    private static string FilePath => System.IO.Path.Combine(Folder, "settings.json");

    public static string LogFilePath => System.IO.Path.Combine(Folder, "xampp-control.log");

    public bool IsAutoStart(string module) =>
        AutoStart.Any(m => string.Equals(m, module, StringComparison.OrdinalIgnoreCase));

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.AppSettings)
                  ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            settings = new AppSettings(); // corrupt or unreadable: defaults
        }

        if (settings.AutoStartApache == true && !settings.IsAutoStart("Apache"))
            settings.AutoStart.Add("Apache");
        if (settings.AutoStartMySql == true && !settings.IsAutoStart("MySQL"))
            settings.AutoStart.Add("MySQL");
        settings.AutoStartApache = null;
        settings.AutoStartMySql = null;

        settings.Services ??= new ServiceSettings();
        settings.UserFiles ??= [];
        settings.AutoStart ??= [];
        settings.LogFontSize = Math.Clamp(settings.LogFontSize, 10, 18);
        settings.MaxLogLines = Math.Clamp(settings.MaxLogLines, 100, 100_000);
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
    }

    /// <summary>Deep copy, so dialogs can edit without touching the live settings until Save.</summary>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize(
            JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings),
            SettingsJson.Default.AppSettings)!;

    public void CopyFrom(AppSettings other)
    {
        XamppRoot = other.XamppRoot;
        Editor = other.Editor;
        Browser = other.Browser;
        MySqlRootPassword = other.MySqlRootPassword;
        AutoStart = [.. other.AutoStart];
        StartMinimized = other.StartMinimized;
        TomcatOutputWindow = other.TomcatOutputWindow;
        CheckPortsOnStartup = other.CheckPortsOnStartup;
        ShowDebug = other.ShowDebug;
        Services = other.Services;
        UserFiles = [.. other.UserFiles];
        LogFontSize = other.LogFontSize;
        MaxLogLines = other.MaxLogLines;
        LogToFile = other.LogToFile;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
