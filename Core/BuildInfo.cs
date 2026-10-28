using System.Globalization;
using System.Reflection;

namespace XamppControl.Core;

public static class BuildInfo
{
    private static readonly Assembly _assembly = typeof(BuildInfo).Assembly;

    /// <summary>"v0.1.0"</summary>
    public static string Version
    {
        get
        {
            var v = _assembly.GetName().Version;
            return v is null ? "v0.0.0" : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>"Oct 27th 2026", from the BuildDate stamped by the .csproj.</summary>
    public static string CompiledOn
    {
        get
        {
            var raw = _assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "BuildDate")?.Value;
            if (!DateTime.TryParseExact(
                    raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return "unknown";
            }
            return $"{date.ToString("MMM", CultureInfo.InvariantCulture)} {date.Day}{Ordinal(date.Day)} {date.Year}";
        }
    }

    /// <summary>"Modern XAMPP Control Panel v0.1.0   [ Compiled: Oct 27th 2026 ]"</summary>
    public const string AppName = "Modern XAMPP Control Panel";

    public static string WindowTitle => $"{AppName} {Version}   [ Compiled: {CompiledOn} ]";

    private static string Ordinal(int day) =>
        (day % 100) is 11 or 12 or 13 ? "th"
        : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
}
