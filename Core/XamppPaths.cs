using System.Diagnostics;
using System.Text.RegularExpressions;

namespace XamppControl.Core;

public static partial class XamppPaths
{
    /// <summary>Finds the XAMPP folder: settings, then next to the exe, then its parent, then C:\xampp.</summary>
    public static string Resolve(AppSettings settings)
    {
        foreach (var candidate in Candidates(settings))
        {
            if (IsXampp(candidate))
                return candidate;
        }
        return string.IsNullOrWhiteSpace(settings.XamppRoot) ? @"C:\xampp" : settings.XamppRoot!;
    }

    private static IEnumerable<string> Candidates(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.XamppRoot))
            yield return settings.XamppRoot!.TrimEnd('\\', '/');

        var exeDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        yield return exeDir;

        var parent = Directory.GetParent(exeDir)?.FullName;
        if (parent is not null)
            yield return parent;

        yield return @"C:\xampp";
    }

    public static bool IsXampp(string dir) =>
        Directory.Exists(Path.Combine(dir, "apache")) || Directory.Exists(Path.Combine(dir, "mysql"));

    /// <summary>"2.4.58", "12.3.3" etc. from an exe's version resource, or null.</summary>
    public static string? FileVersion(string? exePath)
    {
        if (exePath is null || !File.Exists(exePath))
            return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var raw = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            var match = VersionPattern().Match(raw);
            return match.Success ? match.Value : raw.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);

    [GeneratedRegex(@"^\d+(\.\d+){0,2}")]
    private static partial Regex VersionPattern();
}
