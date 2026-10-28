using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XamppControl.Updates;

/// <summary>A published release of the panel itself.</summary>
/// <param name="Version">The release's version from its tag, with a leading "v", e.g. "v0.2.1".</param>
/// <param name="Url">The release page on GitHub, where the zip is downloaded.</param>
/// <param name="Notes">
/// The hand-written release notes as plain text, without GitHub's generated commit list; empty when there are none.
/// </param>
public sealed record AppRelease(string Version, string Url, string Notes);

/// <summary>Asks GitHub which newer versions of the panel have been released, with their notes.</summary>
public static partial class AppUpdateCheck
{
    /// <summary>The project's page on GitHub.</summary>
    public const string RepositoryUrl = "https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel";

    /// <summary>The project's list of releases, where every version can be downloaded.</summary>
    public const string ReleasesUrl = RepositoryUrl + "/releases";

    private const string ReleasesApi =
        "https://api.github.com/repos/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases?per_page=30";

    /// <summary>Reads the published releases and keeps those newer than the running version.</summary>
    /// <param name="currentVersion">The running version as "v0.2.0" or "0.2.0".</param>
    /// <returns>
    /// The newer stable releases, newest first, so the notes cover everything since the running version; empty when
    /// it is current or the repository has no release yet. Drafts and pre-releases are left out.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// GitHub cannot be reached or answers with an error other than 404.
    /// </exception>
    /// <exception cref="OperationCanceledException">GitHub does not answer within 15 seconds.</exception>
    /// <exception cref="JsonException">The answer is not JSON.</exception>
    public static async Task<List<AppRelease>> FindNewerAsync(string currentVersion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using HttpResponseMessage response = await ReleaseFeeds.Http.SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(timeout.Token);
        Version current = VersionUtil.Parse(currentVersion);
        return ReadReleases(json)
            .Where(release => VersionUtil.Parse(release.Version) > current)
            .OrderByDescending(release => VersionUtil.Parse(release.Version))
            .ToList();
    }

    /// <summary>The stable releases in GitHub's release list.</summary>
    /// <param name="json">The body of the releases endpoint: an array of release objects.</param>
    /// <returns>Every release that isn't a draft or pre-release and whose tag holds a version number.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    private static List<AppRelease> ReadReleases(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var releases = new List<AppRelease>();
        foreach (JsonElement release in document.RootElement.EnumerateArray())
        {
            if (ReadRelease(release) is { } appRelease)
            {
                releases.Add(appRelease);
            }
        }
        return releases;
    }

    /// <summary>Reads one release object.</summary>
    /// <param name="release">A release from GitHub's list.</param>
    /// <returns>The release; null for a draft, a pre-release, or a tag without a version number.</returns>
    private static AppRelease? ReadRelease(JsonElement release)
    {
        bool isUnpublished = IsTrue(release, "draft") || IsTrue(release, "prerelease");
        if (release.ValueKind != JsonValueKind.Object || isUnpublished)
        {
            return null;
        }

        string? version = VersionUtil.FromText(ReadString(release, "tag_name"));
        if (version is null)
        {
            return null;
        }

        string url = ReadString(release, "html_url") ?? ReleasesUrl;
        string notes = PlainNotes(ReadString(release, "body") ?? "");
        return new AppRelease("v" + version, url, notes);
    }

    /// <summary>
    /// Turns release notes written in Markdown into readable plain text, dropping GitHub's generated "What's
    /// Changed" commit list.
    /// </summary>
    /// <param name="markdown">The release body.</param>
    /// <returns>Headings as plain lines, bullets as "•", emphasis and code marks removed; trimmed.</returns>
    public static string PlainNotes(string markdown)
    {
        var text = new StringBuilder();
        foreach (string rawLine in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.TrimEnd();
            bool isGeneratedPart = line.StartsWith("## What's Changed", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("**Full Changelog**", StringComparison.OrdinalIgnoreCase);
            if (isGeneratedPart)
            {
                break;
            }

            text.Append(PlainLine(line)).Append('\n');
        }
        return text.ToString().Trim();
    }

    /// <summary>One Markdown line as plain text.</summary>
    /// <param name="line">The line, without its line break.</param>
    /// <returns>The line with heading marks, bullets, emphasis, code marks and link targets simplified.</returns>
    private static string PlainLine(string line)
    {
        string plain = HeadingMark().Replace(line, "");
        plain = BulletMark().Replace(plain, "${indent}• ");
        plain = MarkdownLink().Replace(plain, "${text}");
        return plain.Replace("**", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^#{1,6}\s+")]
    private static partial Regex HeadingMark();

    [GeneratedRegex(@"^(?<indent>\s*)[-*]\s+")]
    private static partial Regex BulletMark();

    [GeneratedRegex(@"\[(?<text>[^\]]+)\]\([^)]+\)")]
    private static partial Regex MarkdownLink();
}
