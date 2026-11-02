using System.Text.Json;
using XamppControl.Updates;

namespace XamppControl.Tests;

public sealed class ReleaseFeedsTests
{
    private const string PhpReleasesJson = """
        {
          "8.4": {
            "version": "8.4.27",
            "nts-vs17-x64": { "zip": { "path": "php-8.4.27-nts-Win32-vs17-x64.zip", "sha256": "aaa" } },
            "ts-vs17-x64": { "zip": { "path": "php-8.4.27-Win32-vs17-x64.zip", "sha256": "bbb" } }
          },
          "8.3": {
            "version": "8.3.30",
            "ts-vs16-x64": { "zip": { "path": "php-8.3.30-Win32-vs16-x64.zip" } }
          },
          "8.2": {
            "version": "8.2.35",
            "ts-vs16-x86": { "zip": { "path": "php-8.2.35-Win32-vs16-x86.zip" } }
          }
        }
        """;

    [Fact]
    public void ParsePhpReleasesJson_PicksThreadSafeX64PerBranch()
    {
        List<PhpBranch> branches = ReleaseFeeds.ParsePhpReleasesJson(PhpReleasesJson);

        Assert.Equal(["8.4", "8.3"], branches.Select(branch => branch.Branch));
        PhpBranch newest = branches[0];
        Assert.Equal("8.4.27", newest.Version);
        Assert.Equal(ReleaseFeeds.PhpBase + "php-8.4.27-Win32-vs17-x64.zip", newest.ZipUrl);
        Assert.Equal("bbb", newest.Sha256);
        Assert.Null(branches[1].Sha256);
    }

    [Fact]
    public void ParsePhpReleasesJson_SkipsBranchesWithoutAThreadSafeX64Build() =>
        Assert.DoesNotContain(ReleaseFeeds.ParsePhpReleasesJson(PhpReleasesJson), branch => branch.Branch == "8.2");

    [Fact]
    public void ParsePhpListing_KeepsTheNewestBuildPerBranch()
    {
        const string html = """
            <a href="php-8.4.26-Win32-vs17-x64.zip">php-8.4.26-Win32-vs17-x64.zip</a>
            <a href="php-8.4.27-Win32-vs17-x64.zip">php-8.4.27-Win32-vs17-x64.zip</a>
            <a href="php-8.4.27-nts-Win32-vs17-x64.zip">php-8.4.27-nts-Win32-vs17-x64.zip</a>
            <a href="php-8.3.30-Win32-vs16-x64.zip">php-8.3.30-Win32-vs16-x64.zip</a>
            """;

        List<PhpBranch> branches = ReleaseFeeds.ParsePhpListing(html);

        Assert.Equal(2, branches.Count);
        Assert.Contains(branches, branch => branch.Branch == "8.4" && branch.Version == "8.4.27");
        Assert.Contains(branches, branch => branch.Branch == "8.3" && branch.Version == "8.3.30");
    }

    [Fact]
    public void ParseMariaDbMajors_KeepsStableSeriesNewestFirst()
    {
        const string json = """
            {
              "major_releases": [
                { "release_id": "11.4", "release_status": "Stable", "release_support_type": "Long Term Support",
                  "release_eol_date": "2029-05-29" },
                { "release_id": "12.3", "release_status": "Stable", "release_support_type": "Short Term Support" },
                { "release_id": "12.4", "release_status": "RC", "release_support_type": "Short Term Support" }
              ]
            }
            """;

        List<MariaDbMajor> majors = ReleaseFeeds.ParseMariaDbMajors(json);

        Assert.Equal(["12.3", "11.4"], majors.Select(major => major.Id));
        Assert.True(majors[1].IsLts);
        Assert.False(majors[0].IsLts);
        Assert.Equal("2029-05-29", majors[1].Eol);
    }

    [Fact]
    public void FindWindowsZip_FindsTheWinx64ZipAnywhereInTheDocument()
    {
        const string json = """
            {
              "releases": {
                "12.3.3": {
                  "files": [
                    { "file_name": "mariadb-12.3.3-linux-systemd-x86_64.tar.gz" },
                    { "file_name": "mariadb-12.3.3-winx64.msi" },
                    { "file_name": "mariadb-12.3.3-winx64.zip", "file_download_url": "https://example.test/m.zip",
                      "checksum": { "sha256sum": "abc" } }
                  ]
                }
              }
            }
            """;
        using var document = JsonDocument.Parse(json);

        MariaDbRelease? release = ReleaseFeeds.FindWindowsZip(document.RootElement);

        Assert.NotNull(release);
        Assert.Equal("12.3.3", release.Version);
        Assert.Equal("https://example.test/m.zip", release.ZipUrl);
        Assert.Equal("abc", release.Sha256);
    }
}
