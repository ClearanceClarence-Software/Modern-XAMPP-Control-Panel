using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace XamppControl.Updates;

/// <summary>A published release of the panel itself.</summary>
/// <param name="Version">The release's version from its tag, with a leading "v", e.g. "v0.2.1".</param>
/// <param name="Url">The release page on GitHub, where the zip is downloaded.</param>
public sealed record AppRelease(string Version, string Url);

/// <summary>Asks GitHub whether a newer version of the panel has been released.</summary>
public static class AppUpdateCheck
{
    /// <summary>The project's page on GitHub.</summary>
    public const string RepositoryUrl = "https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel";

    /// <summary>The project's list of releases, where every version can be downloaded.</summary>
    public const string ReleasesUrl = RepositoryUrl + "/releases";

    private const string LatestReleaseApi =
        "https://api.github.com/repos/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/latest";

    /// <summary>Reads the latest release and compares it with the running version.</summary>
    /// <param name="currentVersion">The running version as "v0.2.0" or "0.2.0".</param>
    /// <returns>The latest release when it is newer; null when it is not or there is no release yet.</returns>
    /// <exception cref="HttpRequestException">
    /// GitHub cannot be reached or answers with an error other than 404.
    /// </exception>
    /// <exception cref="OperationCanceledException">GitHub does not answer within 15 seconds.</exception>
    /// <exception cref="JsonException">The answer is not JSON.</exception>
    public static async Task<AppRelease?> FindNewerAsync(string currentVersion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using HttpResponseMessage response = await ReleaseFeeds.Http.SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(timeout.Token);
        if (ReadRelease(json) is not { } latest)
        {
            return null;
        }

        bool isNewer = VersionUtil.Parse(latest.Version) > VersionUtil.Parse(currentVersion);
        return isNewer ? latest : null;
    }

    /// <summary>Reads the tag and page of a release from GitHub's release JSON.</summary>
    /// <param name="json">The body of the releases/latest endpoint.</param>
    /// <returns>The release; null when the tag is missing or holds no version number.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    private static AppRelease? ReadRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement release = document.RootElement;
        if (release.ValueKind != JsonValueKind.Object
            || !release.TryGetProperty("tag_name", out JsonElement tag)
            || tag.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? version = VersionUtil.FromText(tag.GetString());
        if (version is null)
        {
            return null;
        }

        string url = release.TryGetProperty("html_url", out JsonElement page) && page.ValueKind == JsonValueKind.String
            ? page.GetString() ?? ReleasesUrl
            : ReleasesUrl;
        return new AppRelease("v" + version, url);
    }
}
