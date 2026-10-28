using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using XamppControl.Core;

namespace XamppControl.Updates;

/// <summary>A PHP branch (e.g. 8.4) and its newest Thread Safe x64 build.</summary>
/// <param name="Branch">The major.minor number shown as the branch, e.g. "8.4".</param>
/// <param name="Version">The full version of the branch's newest build, e.g. "8.4.27".</param>
/// <param name="ZipUrl">Absolute download URL of the build's zip under <see cref="ReleaseFeeds.PhpBase"/>.</param>
/// <param name="Sha256">
/// The checksum published in releases.json; null when the feed had none or the branch came from the folder listing.
/// </param>
public sealed record PhpBranch(string Branch, string Version, string ZipUrl, string? Sha256)
{
    public override string ToString() => $"PHP {Branch}   ·   newest {Version}";
}

/// <summary>A stable MariaDB release series (e.g. 12.3).</summary>
/// <param name="Id">The series number from the feed's release_id, e.g. "12.3".</param>
/// <param name="Support">The feed's release_support_type text; empty when the feed has none.</param>
/// <param name="Eol">The end-of-life date as the feed writes it; null or empty when the feed has none.</param>
public sealed record MariaDbMajor(string Id, string Support, string? Eol)
{
    /// <summary>True when <see cref="Support"/> names long-term support; false for a rolling series.</summary>
    public bool IsLts => Support.Contains("Long", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"MariaDB {Id}   ·   {(IsLts ? "LTS" : "rolling")}" +
        (string.IsNullOrEmpty(Eol) ? "" : $", supported until {Eol}");
}

/// <summary>The newest Windows x64 zip of a MariaDB release series.</summary>
/// <param name="Version">The full version taken from the zip's file name, e.g. "12.3.3".</param>
/// <param name="ZipUrl">The feed's download URL, or the archive.mariadb.org path when the feed gives none.</param>
/// <param name="Sha256">The feed's SHA-256 checksum of the zip; null when the feed has none.</param>
public sealed record MariaDbRelease(string Version, string ZipUrl, string? Sha256);

/// <summary>Reads the official release feeds: windows.php.net and the MariaDB downloads REST API.</summary>
public static partial class ReleaseFeeds
{
    /// <summary>The windows.php.net release folder; feed files and zip paths are relative to it.</summary>
    public const string PhpBase = "https://windows.php.net/downloads/releases/";
    private const string MariaDbApi = "https://downloads.mariadb.org/rest-api/mariadb/";

    /// <summary>
    /// The shared client for feeds and downloads; its 30-minute timeout suits large zips, feed reads add their own.
    /// </summary>
    public static readonly HttpClient Http = CreateClient();

    /// <summary>Creates the shared client with a long timeout and a user agent naming this app and version.</summary>
    /// <returns>A client ready for both feed requests and zip downloads.</returns>
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        string appVersion = BuildInfo.Version.TrimStart('v');
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ModernXamppControlPanel/{appVersion}");
        return client;
    }

    /// <summary>Downloads a feed as text, giving up after 20 seconds.</summary>
    /// <param name="url">Absolute URL of the feed or listing.</param>
    /// <returns>The response body.</returns>
    private static async Task<string> GetStringAsync(string url)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return await Http.GetStringAsync(url, timeout.Token);
    }

    // ── PHP ──────────────────────────────────────────────────

    /// <summary>Newest TS x64 build per PHP branch, newest branch first.</summary>
    /// <returns>
    /// The branches from releases.json, or from the folder listing when that feed is unreadable or has no TS x64 build;
    /// empty when neither lists one.
    /// </returns>
    /// <exception cref="HttpRequestException">The folder listing, read as the fallback, cannot be read.</exception>
    public static async Task<List<PhpBranch>> GetPhpBranchesAsync()
    {
        List<PhpBranch> branches;
        try
        {
            branches = ParsePhpReleasesJson(await GetStringAsync(PhpBase + "releases.json"));
        }
        catch (Exception ex) when (ex is HttpRequestException
                                       or OperationCanceledException
                                       or JsonException
                                       or InvalidOperationException)
        {
            // An unreachable or malformed releases.json is not fatal: the folder listing below is tried instead.
            branches = [];
        }

        if (branches.Count == 0)
        {
            branches = ParsePhpListing(await GetStringAsync(PhpBase));
        }

        return branches.OrderByDescending(branch => VersionUtil.Parse(branch.Branch)).ToList();
    }

    /// <summary>Reads the first Thread Safe x64 zip of each branch in windows.php.net's releases.json.</summary>
    /// <param name="json">The releases.json text: an object keyed by branch, each holding builds keyed by name.</param>
    /// <returns>One entry per branch that has a TS x64 zip, in feed order; empty when none has.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    /// <exception cref="InvalidOperationException">The JSON root is not an object.</exception>
    private static List<PhpBranch> ParsePhpReleasesJson(string json)
    {
        var branches = new List<PhpBranch>();
        using var document = JsonDocument.Parse(json);
        foreach (JsonProperty branch in document.RootElement.EnumerateObject())
        {
            if (branch.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (ThreadSafeX64Build(branch) is { } build)
            {
                branches.Add(build);
            }
        }
        return branches;
    }

    /// <summary>The first Thread Safe x64 build listed under one branch of releases.json.</summary>
    /// <param name="branch">A branch entry whose value is a JSON object; its name becomes the branch number.</param>
    /// <returns>
    /// The build, with the version from the branch's "version" or else from the zip path; null when no build qualifies.
    /// </returns>
    private static PhpBranch? ThreadSafeX64Build(JsonProperty branch)
    {
        string? branchVersion = ReadString(branch.Value, "version");
        foreach (JsonProperty build in branch.Value.EnumerateObject())
        {
            // "ts-vs17-x64" (and older "ts-vs16-x64"); "nts-…" builds don't work with mod_php.
            bool isThreadSafeX64 = build.Name.StartsWith("ts-", StringComparison.OrdinalIgnoreCase)
                && build.Name.EndsWith("-x64", StringComparison.OrdinalIgnoreCase);
            if (!isThreadSafeX64 || build.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!build.Value.TryGetProperty("zip", out JsonElement zip) || zip.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? zipPath = ReadString(zip, "path");
            if (string.IsNullOrEmpty(zipPath))
            {
                continue;
            }

            string? version = branchVersion ?? VersionUtil.FromText(zipPath);
            if (version is null)
            {
                continue;
            }

            return new PhpBranch(branch.Name, version, PhpBase + zipPath, ReadString(zip, "sha256"));
        }
        return null;
    }

    /// <summary>Picks the newest TS x64 zip per branch from the HTML listing of the release folder.</summary>
    /// <param name="html">The folder listing page; only zip names matching <see cref="PhpZipName"/> count.</param>
    /// <returns>One entry per branch, without checksums; empty when the page names no matching zip.</returns>
    private static List<PhpBranch> ParsePhpListing(string html)
    {
        var builds = PhpZipName().Matches(html)
            .Select(match => (File: match.Groups["file"].Value, Version: match.Groups["v"].Value))
            .DistinctBy(build => build.File)
            .ToList();

        return builds
            .GroupBy(build => BranchOf(build.Version))
            .Select(branch => branch.OrderByDescending(build => VersionUtil.Parse(build.Version)).First())
            .Select(newest => new PhpBranch(BranchOf(newest.Version), newest.Version, PhpBase + newest.File, null))
            .ToList();
    }

    /// <summary>The branch a full PHP version belongs to: "8.4" for "8.4.27".</summary>
    /// <param name="version">A dotted version with at least two parts.</param>
    /// <returns>The first two dotted parts joined again.</returns>
    private static string BranchOf(string version) => string.Join('.', version.Split('.').Take(2));

    /// <summary>SHA-256 for a PHP zip from sha256sum.txt, when releases.json didn't include it.</summary>
    /// <param name="fileName">The zip's file name without a folder; matched case-insensitively in each line.</param>
    /// <returns>
    /// The lowercase hex checksum from the first matching line; null when no line has one or the file can't be read.
    /// </returns>
    public static async Task<string?> GetPhpSha256Async(string fileName)
    {
        try
        {
            string checksums = await GetStringAsync(PhpBase + "sha256sum.txt");
            foreach (string line in checksums.Split('\n'))
            {
                if (!line.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? hash = line.Trim().Split(' ', '\t', '*').FirstOrDefault(part => part.Length == 64);
                if (hash is not null)
                {
                    return hash.ToLowerInvariant();
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Without the checksum file the download is installed unverified, with a warning in the log.
        }
        return null;
    }

    // ── MariaDB ──────────────────────────────────────────────

    /// <summary>Stable release series, newest first.</summary>
    /// <returns>Every series whose release_status is Stable; empty when the feed has no major_releases array.</returns>
    public static async Task<List<MariaDbMajor>> GetMariaDbMajorsAsync()
    {
        using var document = JsonDocument.Parse(await GetStringAsync(MariaDbApi));
        if (!document.RootElement.TryGetProperty("major_releases", out JsonElement majors)
            || majors.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var stableMajors = new List<MariaDbMajor>();
        foreach (JsonElement release in majors.EnumerateArray())
        {
            string? id = ReadString(release, "release_id");
            string status = ReadString(release, "release_status") ?? "";
            if (id is null || !status.Equals("Stable", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            stableMajors.Add(new MariaDbMajor(
                id,
                ReadString(release, "release_support_type") ?? "",
                ReadString(release, "release_eol_date")));
        }
        return stableMajors.OrderByDescending(major => VersionUtil.Parse(major.Id)).ToList();
    }

    /// <summary>Newest Windows x64 ZIP in a release series.</summary>
    /// <param name="major">The series number, as in <see cref="MariaDbMajor.Id"/>.</param>
    /// <returns>The first winx64 zip found in the series' latest-release document.</returns>
    /// <exception cref="InvalidOperationException">The series' latest release has no Windows x64 zip.</exception>
    public static async Task<MariaDbRelease> GetMariaDbLatestAsync(string major)
    {
        using var document = JsonDocument.Parse(await GetStringAsync($"{MariaDbApi}{major}/latest/"));
        return FindWindowsZip(document.RootElement)
            ?? throw new InvalidOperationException($"No Windows x64 ZIP found for MariaDB {major}.");
    }

    /// <summary>Searches a JSON tree depth first, in document order, for the first winx64 zip file entry.</summary>
    /// <param name="element">Any element of the feed; objects and arrays are searched, other values skipped.</param>
    /// <returns>The first matching release; null when the tree holds none.</returns>
    private static MariaDbRelease? FindWindowsZip(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                if (FindWindowsZip(child) is { } release)
                {
                    return release;
                }
            }
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (WindowsZipFromFileEntry(element) is { } fileRelease)
        {
            return fileRelease;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (FindWindowsZip(property.Value) is { } release)
            {
                return release;
            }
        }
        return null;
    }

    /// <summary>Reads a release from a feed object whose file_name is a winx64 zip.</summary>
    /// <param name="entry">A JSON object from the feed.</param>
    /// <returns>The release; null when the object has no file_name or it is not a winx64 zip.</returns>
    private static MariaDbRelease? WindowsZipFromFileEntry(JsonElement entry)
    {
        if (ReadString(entry, "file_name") is not { } fileName
            || MariaDbZipName().Match(fileName) is not { Success: true } match)
        {
            return null;
        }

        string version = match.Groups["v"].Value;
        string url = ReadString(entry, "file_download_url")
            ?? $"https://archive.mariadb.org/mariadb-{version}/winx64-packages/{fileName}";

        string? sha256 = null;
        if (entry.TryGetProperty("checksum", out JsonElement checksum) && checksum.ValueKind == JsonValueKind.Object)
        {
            sha256 = ReadString(checksum, "sha256sum");
        }

        return new MariaDbRelease(version, url, sha256);
    }

    // ── downloads ────────────────────────────────────────────

    /// <summary>Downloads a file through a .part file that replaces the destination only once complete.</summary>
    /// <param name="url">Absolute URL of the file.</param>
    /// <param name="destination">Full path of the file to write; its folder is created, a file there replaced.</param>
    /// <param name="progress">
    /// Receives null when the size is unknown or 0 when known, then fractions 0..1 about every 512 KB, then 1.
    /// </param>
    /// <param name="ct">Cancels the request and the copy; checked on every read and write.</param>
    /// <returns>A task that completes when the destination holds the whole file.</returns>
    /// <exception cref="HttpRequestException">The request fails or the server answers with an error status.</exception>
    public static async Task DownloadAsync(
        string url,
        string destination,
        Action<double?> progress,
        CancellationToken ct = default)
    {
        string destinationFolder = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("The destination must be a file inside a folder.", nameof(destination));
        Directory.CreateDirectory(destinationFolder);
        string partialFile = destination + ".part";

        using (HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long? totalBytes = response.Content.Headers.ContentLength;
            progress(totalBytes > 0 ? 0 : null);

            await using Stream source = await response.Content.ReadAsStreamAsync(ct);
            await using FileStream target = File.Create(partialFile);
            var buffer = new byte[128 * 1024];
            long bytesWritten = 0;
            long bytesReported = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                bytesWritten += bytesRead;

                // Reporting every 512 KB keeps the UI updates cheap on a fast connection.
                if (totalBytes > 0 && bytesWritten - bytesReported > 512 * 1024)
                {
                    bytesReported = bytesWritten;
                    progress(bytesWritten / (double)totalBytes);
                }
            }
        }

        File.Move(partialFile, destination, overwrite: true);
        progress(1);
    }

    /// <summary>Computes a file's SHA-256 checksum.</summary>
    /// <param name="file">Full path of the file to hash.</param>
    /// <returns>The checksum as 64 lowercase hex characters.</returns>
    public static async Task<string> Sha256Async(string file)
    {
        await using FileStream stream = File.OpenRead(file);
        byte[] hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Reads a string property of a JSON object without throwing on other shapes.</summary>
    /// <param name="element">Any element; a non-object gives null.</param>
    /// <param name="name">The property name, matched case-sensitively.</param>
    /// <returns>The property's text; null for a non-object, or a property that is missing or not a string.</returns>
    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    [GeneratedRegex(@"(?<file>php-(?<v>\d+\.\d+\.\d+)(?:-\d+)?-Win32-vs\d+-x64\.zip)", RegexOptions.IgnoreCase)]
    private static partial Regex PhpZipName();

    [GeneratedRegex(@"^mariadb-(?<v>\d+\.\d+\.\d+)-winx64\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex MariaDbZipName();
}

/// <summary>Finds and compares the dotted version numbers in feed entries, file names and labels.</summary>
public static partial class VersionUtil
{
    /// <summary>"8.4.27" / "12.3" / "MariaDB 12.3.3" → comparable Version (0.0 when unknown).</summary>
    /// <param name="text">Any text containing a dotted number; null or text without one gives 0.0.</param>
    /// <returns>The first dotted number in the text as a <see cref="Version"/>.</returns>
    public static Version Parse(string? text)
    {
        string? versionText = FromText(text);
        if (versionText is null)
        {
            return new Version(0, 0);
        }

        string[] parts = versionText.Split('.');
        return parts.Length == 1 ? new Version(int.Parse(parts[0]), 0) : Version.Parse(versionText);
    }

    /// <summary>Finds the first dotted number of two to four parts in a text.</summary>
    /// <param name="text">Any text, such as a file name or a version label; null or empty gives null.</param>
    /// <returns>The number as written, e.g. "8.4.27" from "php-8.4.27-Win32-vs17-x64.zip"; null if none.</returns>
    public static string? FromText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        Match match = VersionNumber().Match(text);
        return match.Success ? match.Value : null;
    }

    /// <summary>"8.4" from "8.4.27"</summary>
    /// <param name="version">Any text containing a dotted number; null or text without one gives "0.0".</param>
    /// <returns>The major and minor parts joined by a dot.</returns>
    public static string MajorMinor(string? version)
    {
        Version parsed = Parse(version);
        return $"{parsed.Major}.{parsed.Minor}";
    }

    [GeneratedRegex(@"\d+(\.\d+){1,3}")]
    private static partial Regex VersionNumber();
}
