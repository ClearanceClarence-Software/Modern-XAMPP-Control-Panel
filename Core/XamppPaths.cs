using System.Diagnostics;
using System.Text.RegularExpressions;

namespace XamppControl.Core;

/// <summary>Finds the XAMPP installation and reads versions and paths inside it.</summary>
public static partial class XamppPaths
{
    /// <summary>Finds the XAMPP folder: settings, then next to the exe, then its parent, then C:\xampp.</summary>
    /// <param name="settings">Supplies the configured root, which is tried first when set.</param>
    /// <returns>
    /// The first candidate that looks like XAMPP; when none does, the configured root as typed, or C:\xampp when
    /// none is configured.
    /// </returns>
    public static string Resolve(AppSettings settings)
    {
        foreach (string candidate in Candidates(settings))
        {
            if (IsXampp(candidate))
            {
                return candidate;
            }
        }

        string? configuredRoot = settings.XamppRoot;
        return string.IsNullOrWhiteSpace(configuredRoot) ? @"C:\xampp" : configuredRoot;
    }

    /// <summary>The folders <see cref="Resolve"/> tries, in order, without trailing separators.</summary>
    /// <param name="settings">Supplies the configured root, which comes first when set.</param>
    /// <returns>The configured root, the exe's folder, that folder's parent when there is one, then C:\xampp.</returns>
    private static IEnumerable<string> Candidates(AppSettings settings)
    {
        string? configuredRoot = settings.XamppRoot;
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            yield return configuredRoot.TrimEnd('\\', '/');
        }

        string exeFolder = AppContext.BaseDirectory.TrimEnd('\\', '/');
        yield return exeFolder;

        string? parentFolder = Directory.GetParent(exeFolder)?.FullName;
        if (parentFolder is not null)
        {
            yield return parentFolder;
        }

        yield return @"C:\xampp";
    }

    /// <summary>True when the folder contains an apache or a mysql subfolder.</summary>
    /// <param name="dir">The folder to test; a missing folder gives false.</param>
    /// <returns>True when either subfolder exists.</returns>
    public static bool IsXampp(string dir) =>
        Directory.Exists(Path.Combine(dir, "apache")) || Directory.Exists(Path.Combine(dir, "mysql"));

    /// <summary>"2.4.58", "12.3.3" etc. from an exe's version resource, or null.</summary>
    /// <param name="exePath">Full path of the executable; null or a missing file gives null.</param>
    /// <returns>
    /// The leading dotted number (up to three parts) of the product version, or of the file version when the
    /// product version is blank; the whole trimmed text when it does not start with a number; null when there is
    /// no file, no version or it cannot be read.
    /// </returns>
    public static string? FileVersion(string? exePath)
    {
        if (exePath is null || !File.Exists(exePath))
        {
            return null;
        }

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            string? rawVersion = !string.IsNullOrWhiteSpace(versionInfo.ProductVersion)
                ? versionInfo.ProductVersion
                : versionInfo.FileVersion;
            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                return null;
            }

            Match match = VersionPattern().Match(rawVersion);
            return match.Success ? match.Value : rawVersion.Trim();
        }
        catch
        {
            // Reading another program's version resource may fail in many ways; the version is informational,
            // so it shows as unknown.
            return null;
        }
    }

    /// <summary>The first of the given files that exists.</summary>
    /// <param name="paths">Full file paths, tried in order.</param>
    /// <returns>The first existing path; null when none exists.</returns>
    public static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);

    [GeneratedRegex(@"^\d+(\.\d+){0,2}")]
    private static partial Regex VersionPattern();
}
