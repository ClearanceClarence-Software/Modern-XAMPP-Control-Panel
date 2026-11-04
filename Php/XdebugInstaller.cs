using System.IO.Compression;
using System.Text.RegularExpressions;
using XamppControl.Core;
using XamppControl.Updates;

namespace XamppControl.Php;

/// <summary>What the active PHP build needs from an Xdebug build.</summary>
/// <param name="PhpVersion">The PHP version, e.g. "8.4.27".</param>
/// <param name="Branch">The PHP branch the build is for, e.g. "8.4".</param>
/// <param name="Compiler">The Visual Studio tag PHP was built with, e.g. "vs17"; Xdebug must match it.</param>
/// <param name="IsThreadSafe">True for a TS build (XAMPP's, since PHP runs as an Apache module); false for NTS.</param>
public sealed record PhpBuildInfo(string PhpVersion, string Branch, string Compiler, bool IsThreadSafe = true);

/// <summary>
/// Installs the Xdebug build that matches the active PHP (branch, thread safety and compiler) from PECL's Windows
/// builds, and switches it on and off in php.ini.
/// </summary>
public static partial class XdebugInstaller
{
    private const string PeclWindowsBuilds = "https://downloads.php.net/~windows/pecl/releases/xdebug/";

    /// <summary>The name of the DLL the panel installs, kept stable so php.ini keeps working across updates.</summary>
    public const string DllName = "php_xdebug.dll";

    /// <summary>Reads what the active PHP build needs.</summary>
    /// <param name="phpDir">XAMPP's php folder.</param>
    /// <returns>The build's version, branch and compiler; null when php.exe is missing or says nothing usable.</returns>
    public static async Task<PhpBuildInfo?> ReadPhpBuildAsync(string phpDir)
    {
        string phpExe = Path.Combine(phpDir, "php.exe");
        if (!File.Exists(phpExe))
        {
            return null;
        }

        // php -i prints "PHP Version => 8.4.27" and "Compiler => Visual C++ 2022"; the compiler maps to the
        // vs tag PECL uses in its file names.
        Shell.RunResult phpInfo = await Shell.RunAsync(phpExe, "-n -i", phpDir, TimeSpan.FromSeconds(20));
        if (phpInfo.ExitCode != 0)
        {
            return null;
        }

        string? version = VersionLine().Match(phpInfo.Output) is { Success: true } versionMatch
            ? versionMatch.Groups["v"].Value
            : XamppPaths.FileVersion(phpExe);
        string? compiler = CompilerLine().Match(phpInfo.Output) is { Success: true } compilerMatch
            ? CompilerTag(compilerMatch.Groups["year"].Value)
            : null;
        if (version is null || compiler is null)
        {
            return null;
        }
        bool isThreadSafe = !ThreadSafetyLine().IsMatch(phpInfo.Output);
        return new PhpBuildInfo(version, VersionUtil.MajorMinor(version), compiler, isThreadSafe);
    }

    /// <summary>
    /// The stable Xdebug versions that have Windows builds, newest first, read from the folder listing on
    /// downloads.php.net (which is where the zips come from, so a listed version really exists).
    /// </summary>
    /// <returns>Versions such as "3.5.3"; alphas, betas and release candidates are left out.</returns>
    /// <exception cref="HttpRequestException">downloads.php.net cannot be reached.</exception>
    /// <exception cref="InvalidOperationException">The listing names no stable 3.x release.</exception>
    public static async Task<List<string>> ListVersionsAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string listing = await ReleaseFeeds.Http.GetStringAsync(PeclWindowsBuilds, timeout.Token);
        List<string> versions = ParseVersions(listing);
        return versions.Count > 0
            ? versions
            : throw new InvalidOperationException("downloads.php.net lists no stable Xdebug release.");
    }

    /// <summary>The stable 3.x version folders in a listing, newest first.</summary>
    /// <param name="listing">The HTML of the releases folder.</param>
    /// <returns>Distinct versions such as "3.5.3"; pre-release folders are left out.</returns>
    internal static List<string> ParseVersions(string listing) =>
        VersionFolder().Matches(listing)
            .Select(match => match.Groups["v"].Value)
            .Distinct()
            .Where(version => VersionUtil.Parse(version) >= new Version(3, 0))
            .OrderByDescending(VersionUtil.Parse)
            .ToList();

    /// <summary>
    /// The newest of the given versions that has a build for a PHP: a brand-new PHP branch often has to wait a
    /// release or two, so the versions are tried newest first until one answers.
    /// </summary>
    /// <param name="versions">Candidate versions, newest first; from <see cref="ListVersionsAsync"/>.</param>
    /// <param name="php">The active PHP build.</param>
    /// <returns>The first version whose zip exists; null when none of them has a build for this PHP.</returns>
    /// <exception cref="HttpRequestException">downloads.php.net cannot be reached at all.</exception>
    public static async Task<string?> FindBuildAsync(IEnumerable<string> versions, PhpBuildInfo php)
    {
        foreach (string version in versions)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, ZipUrl(version, php));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using HttpResponseMessage response = await ReleaseFeeds.Http.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                return version;
            }
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
            }
        }
        return null;
    }

    /// <summary>The download URL of the x64 build that matches a PHP build.</summary>
    /// <param name="xdebugVersion">The Xdebug version, e.g. "3.5.3".</param>
    /// <param name="php">The active PHP build.</param>
    /// <returns>The zip's URL on downloads.php.net.</returns>
    public static string ZipUrl(string xdebugVersion, PhpBuildInfo php) =>
        $"{PeclWindowsBuilds}{xdebugVersion}/php_xdebug-{xdebugVersion}-{php.Branch}-" +
        $"{(php.IsThreadSafe ? "ts" : "nts")}-{php.Compiler}-x64.zip";

    /// <summary>Downloads the matching build and puts its DLL in php\ext as <see cref="DllName"/>.</summary>
    /// <param name="phpDir">XAMPP's php folder.</param>
    /// <param name="php">The active PHP build.</param>
    /// <param name="xdebugVersion">The Xdebug version to install.</param>
    /// <param name="progress">Receives the download progress as 0..1, or null while the size is unknown.</param>
    /// <returns>Full path of the installed DLL.</returns>
    /// <exception cref="HttpRequestException">The download failed; a 404 means PECL has no build for this PHP.</exception>
    /// <exception cref="InvalidDataException">The zip holds no php_xdebug DLL.</exception>
    public static async Task<string> InstallAsync(
        string phpDir,
        PhpBuildInfo php,
        string xdebugVersion,
        Action<double?> progress)
    {
        string zipPath = Path.Combine(Path.GetTempPath(), "XamppControl", $"xdebug-{xdebugVersion}-{php.Branch}.zip");
        await ReleaseFeeds.DownloadAsync(ZipUrl(xdebugVersion, php), zipPath, progress);

        string extDir = Path.Combine(phpDir, "ext");
        Directory.CreateDirectory(extDir);
        string target = Path.Combine(extDir, DllName);
        using (ZipArchive zip = ZipFile.OpenRead(zipPath))
        {
            ZipArchiveEntry dll = zip.Entries.FirstOrDefault(entry =>
                    entry.Name.StartsWith("php_xdebug", StringComparison.OrdinalIgnoreCase)
                    && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("The downloaded zip holds no php_xdebug DLL.");
            dll.ExtractToFile(target, overwrite: true);
        }
        FileTools.TryDeleteFile(zipPath);
        return target;
    }

    /// <summary>The version of the installed DLL.</summary>
    /// <param name="phpDir">XAMPP's php folder.</param>
    /// <returns>The DLL's file version, e.g. "3.5.3", or "?" when the DLL has none; null when it isn't installed.</returns>
    public static string? InstalledVersion(string phpDir)
    {
        string dll = Path.Combine(phpDir, "ext", DllName);
        return XamppPaths.FileVersion(dll) ?? (File.Exists(dll) ? "?" : null);
    }

    /// <summary>Whether php.ini loads the panel's Xdebug DLL.</summary>
    /// <param name="ini">The active php.ini.</param>
    /// <returns>True when an active zend_extension line names it.</returns>
    public static bool IsEnabled(PhpIni ini) =>
        ini.ListExtensions(Path.GetDirectoryName(ini.FilePath) is { } phpDir ? Path.Combine(phpDir, "ext") : "")
            .Any(extension => extension.Name == "xdebug" && extension.IsEnabled);

    /// <summary>
    /// Turns Xdebug on or off in php.ini. Turning it on also sets the settings a debugger needs: debug mode,
    /// starting on request, and the client port.
    /// </summary>
    /// <param name="ini">The active php.ini; the caller saves it.</param>
    /// <param name="isEnabled">True loads the DLL; false comments its line out.</param>
    public static void SetEnabled(PhpIni ini, bool isEnabled)
    {
        var extension = new PhpExtension("xdebug", IsZend: true, IsEnabled: false);
        ini.SetExtensionEnabled(extension, isEnabled);
        if (isEnabled)
        {
            ini.SetValue("xdebug.mode", ini.GetValue("xdebug.mode") ?? "debug,develop");
            ini.SetValue("xdebug.start_with_request", ini.GetValue("xdebug.start_with_request") ?? "trigger");
            ini.SetValue("xdebug.client_port", ini.GetValue("xdebug.client_port") ?? "9003");
        }
    }

    /// <summary>Maps PHP's compiler name to PECL's file-name tag.</summary>
    /// <param name="visualStudioYear">The year in "Visual C++ 2022".</param>
    /// <returns>"vs15" for 2017, "vs16" for 2019, "vs17" for 2022; null for a year PECL doesn't build for.</returns>
    private static string? CompilerTag(string visualStudioYear) => visualStudioYear switch
    {
        "2017" => "vs15",
        "2019" => "vs16",
        "2022" => "vs17",
        _ => null,
    };

    [GeneratedRegex(@"^PHP Version => (?<v>\d+\.\d+\.\d+)", RegexOptions.Multiline)]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"^Compiler => Visual C\+\+ (?<year>\d{4})", RegexOptions.Multiline)]
    private static partial Regex CompilerLine();

    [GeneratedRegex(@"^Thread Safety => disabled", RegexOptions.Multiline)]
    private static partial Regex ThreadSafetyLine();

    // Apache's folder listing: <a href="3.5.3/">3.5.3/</a>; pre-releases are folders like 3.5.0RC1/ and don't match.
    [GeneratedRegex(@"href=""(?:[^""]*/)?(?<v>\d+\.\d+\.\d+)/""")]
    private static partial Regex VersionFolder();
}
