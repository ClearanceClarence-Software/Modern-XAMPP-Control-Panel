using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using XamppControl.Core;

namespace XamppControl.Updates;

/// <summary>A PHP branch (e.g. 8.4) and its newest Thread Safe x64 build.</summary>
public sealed record PhpBranch(string Branch, string Version, string ZipUrl, string? Sha256)
{
    public override string ToString() => $"PHP {Branch}   ·   newest {Version}";
}

/// <summary>A stable MariaDB release series (e.g. 12.3).</summary>
public sealed record MariaDbMajor(string Id, string Support, string? Eol)
{
    public bool IsLts => Support.Contains("Long", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"MariaDB {Id}   ·   {(IsLts ? "LTS" : "rolling")}" +
        (string.IsNullOrEmpty(Eol) ? "" : $", supported until {Eol}");
}

public sealed record MariaDbRelease(string Version, string ZipUrl, string? Sha256);

/// <summary>Reads the official release feeds: windows.php.net and the MariaDB downloads REST API.</summary>
public static partial class ReleaseFeeds
{
    public const string PhpBase = "https://windows.php.net/downloads/releases/";
    private const string MariaDbApi = "https://downloads.mariadb.org/rest-api/mariadb/";

    public static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ModernXamppControlPanel/{BuildInfo.Version.TrimStart('v')}");
        return client;
    }

    private static async Task<string> GetStringAsync(string url)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return await Http.GetStringAsync(url, cts.Token);
    }

    // ── PHP ──────────────────────────────────────────────────

    /// <summary>Newest TS x64 build per PHP branch, newest branch first.</summary>
    public static async Task<List<PhpBranch>> GetPhpBranchesAsync()
    {
        List<PhpBranch> branches;
        try
        {
            branches = ParsePhpReleasesJson(await GetStringAsync(PhpBase + "releases.json"));
        }
        catch
        {
            branches = [];
        }

        // Fallback: read the download folder listing.
        if (branches.Count == 0)
            branches = ParsePhpListing(await GetStringAsync(PhpBase));

        return branches.OrderByDescending(b => VersionUtil.Parse(b.Branch)).ToList();
    }

    private static List<PhpBranch> ParsePhpReleasesJson(string json)
    {
        var list = new List<PhpBranch>();
        using var doc = JsonDocument.Parse(json);
        foreach (var branch in doc.RootElement.EnumerateObject())
        {
            if (branch.Value.ValueKind != JsonValueKind.Object)
                continue;

            var version = Str(branch.Value, "version");
            foreach (var build in branch.Value.EnumerateObject())
            {
                // "ts-vs17-x64" (and older "ts-vs16-x64"); "nts-…" builds don't work with mod_php.
                if (!build.Name.StartsWith("ts-", StringComparison.OrdinalIgnoreCase) ||
                    !build.Name.EndsWith("-x64", StringComparison.OrdinalIgnoreCase) ||
                    build.Value.ValueKind != JsonValueKind.Object ||
                    !build.Value.TryGetProperty("zip", out var zip) ||
                    zip.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var path = Str(zip, "path");
                if (string.IsNullOrEmpty(path))
                    continue;

                var v = version ?? VersionUtil.FromText(path);
                if (v is null)
                    continue;

                list.Add(new PhpBranch(branch.Name, v, PhpBase + path, Str(zip, "sha256")));
                break;
            }
        }
        return list;
    }

    private static List<PhpBranch> ParsePhpListing(string html)
    {
        var builds = PhpZipName().Matches(html)
            .Select(m => (File: m.Groups["file"].Value, Version: m.Groups["v"].Value))
            .DistinctBy(b => b.File)
            .ToList();

        return builds
            .GroupBy(b => string.Join('.', b.Version.Split('.').Take(2)))
            .Select(g => g.OrderByDescending(b => VersionUtil.Parse(b.Version)).First())
            .Select(b => new PhpBranch(
                string.Join('.', b.Version.Split('.').Take(2)), b.Version, PhpBase + b.File, null))
            .ToList();
    }

    /// <summary>SHA-256 for a PHP zip from sha256sum.txt, when releases.json didn't include it.</summary>
    public static async Task<string?> GetPhpSha256Async(string fileName)
    {
        try
        {
            var text = await GetStringAsync(PhpBase + "sha256sum.txt");
            foreach (var line in text.Split('\n'))
            {
                if (!line.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var hash = line.Trim().Split(' ', '\t', '*').FirstOrDefault(t => t.Length == 64);
                if (hash is not null)
                    return hash.ToLowerInvariant();
            }
        }
        catch
        {
            // No checksum available.
        }
        return null;
    }

    // ── MariaDB ──────────────────────────────────────────────

    /// <summary>Stable release series, newest first.</summary>
    public static async Task<List<MariaDbMajor>> GetMariaDbMajorsAsync()
    {
        using var doc = JsonDocument.Parse(await GetStringAsync(MariaDbApi));
        if (!doc.RootElement.TryGetProperty("major_releases", out var majors) ||
            majors.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<MariaDbMajor>();
        foreach (var m in majors.EnumerateArray())
        {
            var id = Str(m, "release_id");
            var status = Str(m, "release_status") ?? "";
            if (id is null || !status.Equals("Stable", StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add(new MariaDbMajor(id, Str(m, "release_support_type") ?? "", Str(m, "release_eol_date")));
        }
        return list.OrderByDescending(m => VersionUtil.Parse(m.Id)).ToList();
    }

    /// <summary>Newest Windows x64 ZIP in a release series.</summary>
    public static async Task<MariaDbRelease> GetMariaDbLatestAsync(string major)
    {
        using var doc = JsonDocument.Parse(await GetStringAsync($"{MariaDbApi}{major}/latest/"));

        MariaDbRelease? found = null;
        void Walk(JsonElement e)
        {
            if (found is not null)
                return;
            if (e.ValueKind == JsonValueKind.Object)
            {
                if (Str(e, "file_name") is { } name && MariaDbZipName().Match(name) is { Success: true } m)
                {
                    var version = m.Groups["v"].Value;
                    var url = Str(e, "file_download_url")
                              ?? $"https://archive.mariadb.org/mariadb-{version}/winx64-packages/{name}";
                    string? sha = null;
                    if (e.TryGetProperty("checksum", out var checksum) && checksum.ValueKind == JsonValueKind.Object)
                        sha = Str(checksum, "sha256sum");
                    found = new MariaDbRelease(version, url, sha);
                    return;
                }
                foreach (var p in e.EnumerateObject())
                    Walk(p.Value);
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray())
                    Walk(item);
            }
        }

        Walk(doc.RootElement);
        return found ?? throw new InvalidOperationException($"No Windows x64 ZIP found for MariaDB {major}.");
    }

    // ── downloads ────────────────────────────────────────────

    public static async Task DownloadAsync(
        string url, string destination, Action<double?> progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".part";

        using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            progress(total > 0 ? 0 : null);

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(partial);
            var buffer = new byte[128 * 1024];
            long read = 0, reported = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0 && read - reported > 512 * 1024)
                {
                    reported = read;
                    progress(read / (double)total);
                }
            }
        }

        File.Move(partial, destination, overwrite: true);
        progress(1);
    }

    public static async Task<string> Sha256Async(string file)
    {
        await using var stream = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    [GeneratedRegex(@"(?<file>php-(?<v>\d+\.\d+\.\d+)(?:-\d+)?-Win32-vs\d+-x64\.zip)", RegexOptions.IgnoreCase)]
    private static partial Regex PhpZipName();

    [GeneratedRegex(@"^mariadb-(?<v>\d+\.\d+\.\d+)-winx64\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex MariaDbZipName();
}

public static partial class VersionUtil
{
    /// <summary>"8.4.27" / "12.3" / "MariaDB 12.3.3" → comparable Version (0.0 when unknown).</summary>
    public static Version Parse(string? text)
    {
        var raw = FromText(text);
        if (raw is null)
            return new Version(0, 0);
        var parts = raw.Split('.');
        return parts.Length == 1 ? new Version(int.Parse(parts[0]), 0) : Version.Parse(raw);
    }

    public static string? FromText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var m = Number().Match(text);
        return m.Success ? m.Value : null;
    }

    /// <summary>"8.4" from "8.4.27"</summary>
    public static string MajorMinor(string? version)
    {
        var v = Parse(version);
        return $"{v.Major}.{v.Minor}";
    }

    [GeneratedRegex(@"\d+(\.\d+){1,3}")]
    private static partial Regex Number();
}
