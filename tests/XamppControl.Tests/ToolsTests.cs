using System.Text;
using XamppControl.Core;
using XamppControl.Php;
using XamppControl.Updates;
using XamppControl.ViewModels;

namespace XamppControl.Tests;

public sealed class ToolsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "XamppControlTests", Guid.NewGuid().ToString("N"));

    public ToolsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void XdebugZipUrl_UsesBranchAndCompiler()
    {
        var php = new PhpBuildInfo("8.4.27", "8.4", "vs17");
        Assert.Equal(
            "https://downloads.php.net/~windows/pecl/releases/xdebug/3.5.3/php_xdebug-3.5.3-8.4-ts-vs17-x64.zip",
            XdebugInstaller.ZipUrl("3.5.3", php));
    }

    [Fact]
    public void XdebugSetEnabled_AddsZendLineAndDefaults_ThenCommentsItOut()
    {
        string ini = Path.Combine(_folder, "php.ini");
        File.WriteAllText(ini, "extension=mbstring\n;extension=soap\n");
        PhpIni loaded = PhpIni.Load(ini);

        XdebugInstaller.SetEnabled(loaded, true);
        loaded.Save();
        string text = File.ReadAllText(ini);
        Assert.Contains("zend_extension=xdebug", text, StringComparison.Ordinal);
        Assert.Contains("xdebug.mode=", text, StringComparison.Ordinal);
        Assert.Contains("xdebug.client_port=9003", text, StringComparison.Ordinal);

        PhpIni disabled = PhpIni.Load(ini);
        XdebugInstaller.SetEnabled(disabled, false);
        disabled.Save();
        Assert.Contains(";zend_extension=xdebug", File.ReadAllText(ini), StringComparison.Ordinal);
    }

    [Fact]
    public void MailpitReadRelease_FindsTheWindowsZip()
    {
        const string json = """
            {"tag_name":"v1.29.5","assets":[
              {"name":"mailpit-linux-amd64.tar.gz","browser_download_url":"https://example.test/linux"},
              {"name":"mailpit-windows-amd64.zip","browser_download_url":"https://example.test/win.zip"}]}
            """;
        MailpitRelease? release = MailpitUpdater.ReadRelease(json);
        Assert.NotNull(release);
        Assert.Equal("1.29.5", release.Version);
        Assert.Equal("https://example.test/win.zip", release.ZipUrl);
    }

    [Fact]
    public void MailpitReadRelease_IsNullWithoutWindowsZip()
    {
        const string json = """{"tag_name":"v1.29.5","assets":[{"name":"mailpit-linux-amd64.tar.gz"}]}""";
        Assert.Null(MailpitUpdater.ReadRelease(json));
    }

    [Fact]
    public void RewritePorts_ChangesListenAndServerNameOnly_AndKeepsLineBreaks()
    {
        string conf = Path.Combine(_folder, "httpd.conf");
        const string original = "# port 80 is the default\r\nListen 80\r\nServerName localhost:80\r\n" +
            "Timeout 80\r\nListen 8080\r\n";
        File.WriteAllText(conf, original, new UTF8Encoding(false));

        Assert.True(PortConflicts.RewritePorts(conf, 80, 8081));

        string text = File.ReadAllText(conf);
        Assert.Equal("# port 80 is the default\r\nListen 8081\r\nServerName localhost:8081\r\n" +
            "Timeout 80\r\nListen 8080\r\n", text);
    }

    [Fact]
    public void RewritePorts_ChangesMyIniPortLines()
    {
        string ini = Path.Combine(_folder, "my.ini");
        File.WriteAllText(ini, "[client]\nport=3306\n[mysqld]\nport = 3306\nmax_connections=3306\n");

        Assert.True(PortConflicts.RewritePorts(ini, 3306, 3307));
        Assert.Equal("[client]\nport=3307\n[mysqld]\nport = 3307\nmax_connections=3306\n", File.ReadAllText(ini));
    }

    [Fact]
    public void RewritePorts_ReturnsFalseWhenNothingMatches()
    {
        string conf = Path.Combine(_folder, "other.conf");
        File.WriteAllText(conf, "Listen 8080\n");
        Assert.False(PortConflicts.RewritePorts(conf, 80, 8081));
        Assert.False(PortConflicts.RewritePorts(Path.Combine(_folder, "missing.conf"), 80, 8081));
    }

    [Theory]
    [InlineData("shop_20261101-120000.sql", "shop")]
    [InlineData(@"C:\xampp\mysql_dumps\my_app_20261101-120000.sql", "my_app")]
    [InlineData("backup.sql", "backup")]
    [InlineData("under_score.sql", "under_score")]
    public void DatabaseNameFor_StripsTheTimestamp(string file, string expected) =>
        Assert.Equal(expected, DatabasesViewModel.DatabaseNameFor(file));

    [Fact]
    public void UpdateScript_WaitsForThePidThenCopiesAndStarts()
    {
        string script = AppUpdater.BuildScript(@"C:\t\XamppControl.new.exe", @"C:\xampp\XamppControl.exe", 4242);

        Assert.Contains("tasklist /FI \"PID eq 4242\"", script, StringComparison.Ordinal);
        Assert.Contains("copy /y \"%src%\" \"%dst%\"", script, StringComparison.Ordinal);
        Assert.Contains("set \"dst=C:\\xampp\\XamppControl.exe\"", script, StringComparison.Ordinal);
        Assert.Contains("start \"\" \"%dst%\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstFree_SkipsTakenPorts()
    {
        var listeners = new List<Listener> { new("0.0.0.0", 8080, 1234, false) };
        Assert.Equal(8081, PortConflicts.FirstFree([8080, 8081], listeners));
        Assert.Null(PortConflicts.FirstFree([8080], listeners));
    }
}
