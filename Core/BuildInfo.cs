using System.Globalization;
using System.Reflection;

namespace XamppControl.Core;

/// <summary>Version and build date of this build, for the title bar, the log and the update check.</summary>
public static class BuildInfo
{
    private static readonly Assembly _assembly = typeof(BuildInfo).Assembly;

    /// <summary>The assembly version as "v0.1.1" (major, minor, build); "v0.0.0" when it has none.</summary>
    public static string Version
    {
        get
        {
            // Qualified because the simple name Version means this property inside the class.
            System.Version? assemblyVersion = _assembly.GetName().Version;
            if (assemblyVersion is null)
            {
                return "v0.0.0";
            }

            return $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
        }
    }

    /// <summary>
    /// The build date as "Oct 27th 2026" in English, from the BuildDate stamped by the .csproj; "unknown" when it
    /// is missing or not in yyyy-MM-dd form.
    /// </summary>
    public static string CompiledOn
    {
        get
        {
            string? buildDate = _assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "BuildDate")?.Value;
            bool isValidDate = DateTime.TryParseExact(
                buildDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime buildDay);
            if (!isValidDate)
            {
                return "unknown";
            }

            string month = buildDay.ToString("MMM", CultureInfo.InvariantCulture);
            return $"{month} {buildDay.Day}{Ordinal(buildDay.Day)} {buildDay.Year}";
        }
    }

    /// <summary>The product name, without version.</summary>
    public const string AppName = "Modern XAMPP Control Panel";

    /// <summary>"Modern XAMPP Control Panel v0.1.1   [ Compiled: Oct 27th 2026 ]"</summary>
    public static string WindowTitle => $"{AppName} {Version}   [ Compiled: {CompiledOn} ]";

    /// <summary>The English ordinal suffix for a day of the month.</summary>
    /// <param name="day">Day of the month, 1 to 31.</param>
    /// <returns>"st", "nd", "rd" or "th"; 11th to 13th take "th".</returns>
    private static string Ordinal(int day)
    {
        if (day % 100 is 11 or 12 or 13)
        {
            return "th";
        }

        return (day % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
    }
}
