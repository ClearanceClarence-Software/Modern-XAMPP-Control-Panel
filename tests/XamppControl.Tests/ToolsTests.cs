using System.Text;
using XamppControl.Core;
using XamppControl.Databases;
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
    public void XdebugZipUrl_UsesNtsForNonThreadSafeBuilds()
    {
        var php = new PhpBuildInfo("8.3.29", "8.3", "vs16", IsThreadSafe: false);
        Assert.EndsWith("/3.4.5/php_xdebug-3.4.5-8.3-nts-vs16-x64.zip", XdebugInstaller.ZipUrl("3.4.5", php), StringComparison.Ordinal);
    }

    [Fact]
    public void XdebugParseVersions_KeepsStableFoldersNewestFirst()
    {
        const string listing = """
            <a href="../">Parent Directory</a>
            <a href="2.9.8/">2.9.8/</a>
            <a href="3.4.5/">3.4.5/</a>
            <a href="3.5.0RC1/">3.5.0RC1/</a>
            <a href="3.5.3/">3.5.3/</a>
            <a href="3.5.3/">3.5.3/</a>
            <a href="https://downloads.php.net/~windows/pecl/releases/xdebug/3.5.1/">3.5.1/</a>
            """;
        string[] expected = ["3.5.3", "3.5.1", "3.4.5"];
        Assert.Equal(expected, XdebugInstaller.ParseVersions(listing));
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
    public void StartMenuQuote_DoublesSingleQuotes() =>
        Assert.Equal(@"C:\Users\O''Brien\XamppControl.exe", StartMenu.Quote(@"C:\Users\O'Brien\XamppControl.exe"));

    [Fact]
    public void MockOptions_AreNullWithoutASwitch() =>
        Assert.Null(MockOptions.Parse(["--shot", "x.png", "--theme", "dark"]));

    [Fact]
    public void MockOptions_ReadSceneShotSizeThemeAndLanguage()
    {
        MockOptions? options = MockOptions.Parse(
            ["--SCENE", "Netstat", "--shot", "shots/netstat.png", "--size", "1200x860", "--theme", "dark", "--lang", "nb"]);

        Assert.NotNull(options);
        Assert.Equal("netstat", options.Scene);
        Assert.Equal(Path.GetFullPath("shots/netstat.png"), options.Shot);
        Assert.Equal((1200, 860), options.Size);
        Assert.Equal("Dark", options.Theme);
        Assert.Equal("nb", options.Language);
    }

    [Fact]
    public void MockOptions_MockAloneHasNoSceneAndAShotAloneIsTheMainWindow()
    {
        MockOptions? plain = MockOptions.Parse(["--mock"]);
        Assert.NotNull(plain);
        Assert.Null(plain.Scene);
        Assert.Equal("Light", plain.Theme);

        MockOptions? shot = MockOptions.Parse(["--mock", "--shot", "main.png", "--size", "10x10"]);
        Assert.NotNull(shot);
        Assert.Equal("main", shot.Scene);
        Assert.Null(shot.Size);
    }

    [Fact]
    public void MockRun_AnswersOnlyApachesConfigTest()
    {
        Assert.Equal(0, MockMachine.Run(@"C:\xampp\apache\bin\httpd.exe", "-t").ExitCode);
        Assert.Equal(-1, MockMachine.Run(@"C:\xampp\apache\bin\httpd.exe", "-k install").ExitCode);
        Assert.Equal(-1, MockMachine.Run(@"C:\xampp\php\php.exe", "-t").ExitCode);
    }

    [Theory]
    [InlineData("8.5.12", "8.5.13", true)]
    [InlineData("8.5.13", "8.5.13", false)]
    [InlineData("8.5.14", "8.5.13", false)]
    [InlineData(null, "8.5.13", false)]
    [InlineData("?", "1.33.0", false)]
    [InlineData("8.5.12", null, false)]
    public void ComponentUpdate_IsOnlyReportedForANewerKnownVersion(string? installed, string? latest, bool expected)
    {
        ComponentUpdate? update = ComponentUpdateCheck.Newer("PHP", "Apache", installed, latest);
        Assert.Equal(expected, update is not null);
    }

    [Fact]
    public void ComponentUpdate_NeverOffersAnotherBranchAsAnUpdate()
    {
        Assert.Null(ComponentUpdateCheck.Newer("PHP", "Apache", "8.5.12", "8.6.0", isSameBranchOnly: true));
        Assert.NotNull(ComponentUpdateCheck.Newer("PHP", "Apache", "8.5.12", "8.5.13", isSameBranchOnly: true));
    }

    [Fact]
    public void ComponentUpdate_LabelNamesTheInstalledBranch()
    {
        Assert.Equal("PHP 8.5", new ComponentUpdate("PHP", "Apache", "8.5.12", "8.5.13").Label);
        Assert.Equal("MariaDB 12.3", new ComponentUpdate("MariaDB", "MySQL", "12.3.3", "12.3.4").Label);
        Assert.Equal("Mailpit", new ComponentUpdate("Mailpit", "Mailpit", "1.33.0", "1.33.1").Label);

        var xdebug = new ComponentUpdate("Xdebug", "Apache", "3.4.5", "3.5.3");
        Assert.Equal("Xdebug", xdebug.Label);
        Assert.True(xdebug.IsInPhpWindow);
    }

    [Theory]
    [InlineData("8.5.13", "8.5.12", true)]
    [InlineData("8.6.0", "8.5.12", false)]
    [InlineData("8.5.12", "8.5.12", false)]
    [InlineData("8.5.11", "8.5.12", false)]
    [InlineData(null, "8.5.12", false)]
    public void IsPatchOf_IsANewerBuildOfTheSameBranch(string? newVersion, string oldVersion, bool expected) =>
        Assert.Equal(expected, VersionUtil.IsPatchOf(newVersion, oldVersion));

    [Fact]
    public void FolderSize_AddsUpFilesInSubfolders()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "size", "sub"));
        File.WriteAllBytes(Path.Combine(_folder, "size", "a.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(_folder, "size", "sub", "b.bin"), new byte[234]);
        Assert.Equal(1234, FileTools.FolderSize(Path.Combine(_folder, "size")));
    }

    [Fact]
    public void LogLineParser_ReadsAnApacheErrorLine()
    {
        LogLine line = LogLineParser.Parse(
            "[Wed Nov 25 14:52:37.724415 2026] [ssl:warn] [pid 21304:tid 392] AH01906: example.com:443:0 server " +
            "certificate is a CA certificate");

        Assert.Equal(new DateTime(2026, 11, 25, 14, 52, 37), line.Time);
        Assert.Equal(LogSeverity.Warning, line.Severity);
        Assert.Equal("WARN", line.LevelText);
        Assert.Equal("example.com:443:0 server certificate is a CA certificate", line.Message);
        Assert.True(line.IsProblem);
        Assert.False(line.IsError);
    }

    [Fact]
    public void LogLineParser_DropsTheClientFromAnApachePhpError()
    {
        LogLine line = LogLineParser.Parse(
            "[Wed Nov 25 09:21:09.330 2026] [php:error] [pid 9328:tid 1904] [client ::1:51712] PHP Fatal error:  Uncaught");

        Assert.Equal(LogSeverity.Error, line.Severity);
        Assert.Equal("PHP Fatal error:  Uncaught", line.Message);
        Assert.True(line.IsError);
    }

    [Theory]
    [InlineData("[25-Nov-2026 09:21:04 Europe/Oslo] PHP Warning:  Undefined variable $cart", LogSeverity.Warning, "WARN", "Undefined variable $cart")]
    [InlineData("[25-Nov-2026 09:21:04 Europe/Oslo] PHP Fatal error:  Uncaught Error", LogSeverity.Error, "FATAL", "Uncaught Error")]
    [InlineData("[25-Nov-2026 09:21:04 UTC] PHP Deprecated:  Old call", LogSeverity.Notice, "DEPREC", "Old call")]
    [InlineData("2026-11-25  9:17:49 0 [Note] InnoDB: Buffer pool(s) load completed", LogSeverity.Notice, "NOTICE", "InnoDB: Buffer pool(s) load completed")]
    [InlineData("2026-11-25 19:17:49 0 [ERROR] InnoDB: Cannot open datafile", LogSeverity.Error, "ERROR", "InnoDB: Cannot open datafile")]
    [InlineData("level=info msg=\"[smtpd] starting on 127.0.0.1:1025\"", LogSeverity.Info, "INFO", "[smtpd] starting on 127.0.0.1:1025")]
    public void LogLineParser_ReadsTheOtherLogs(string text, LogSeverity severity, string level, string message)
    {
        LogLine line = LogLineParser.Parse(text);
        Assert.Equal(severity, line.Severity);
        Assert.Equal(level, line.LevelText);
        Assert.Equal(message, line.Message);
    }

    [Fact]
    public void LogLineParser_ReadsTimesInEachFormat()
    {
        Assert.Equal(
            new DateTime(2026, 11, 25, 9, 21, 4),
            LogLineParser.Parse("[25-Nov-2026 09:21:04 Europe/Oslo] PHP Warning:  x").Time);
        Assert.Equal(
            new DateTime(2026, 11, 25, 9, 17, 49),
            LogLineParser.Parse("2026-11-25  9:17:49 0 [Note] x").Time);
        Assert.Equal(
            new DateTime(2026, 11, 25, 9, 21, 9),
            LogLineParser.Parse("::1 - - [25/Nov/2026:09:21:09 +0100] \"POST /shop/ HTTP/1.1\" 500 312").Time);
    }

    [Theory]
    [InlineData("200", LogSeverity.Info)]
    [InlineData("404", LogSeverity.Warning)]
    [InlineData("500", LogSeverity.Error)]
    public void LogLineParser_GradesAnAccessLineByItsStatus(string status, LogSeverity severity)
    {
        LogLine line = LogLineParser.Parse($"::1 - - [25/Nov/2026:09:21:09 +0100] \"GET /shop/ HTTP/1.1\" {status} 312");
        Assert.Equal(severity, line.Severity);
        Assert.Equal(status, line.LevelText);
        Assert.StartsWith("GET /shop/ HTTP/1.1", line.Message);
    }

    [Fact]
    public void LogLineParser_KeepsAnUnknownLineWhole()
    {
        LogLine line = LogLineParser.Parse("#0 C:\\xampp\\htdocs\\shop\\cart.php(42): wc_get_cart()");
        Assert.Null(line.Time);
        Assert.Equal("", line.LevelText);
        Assert.Equal(line.Text, line.Message);
        Assert.False(line.IsProblem);
    }

    [Fact]
    public void LogSource_CountsRecentErrorsBeforeWarnings()
    {
        var source = new LogSource("Apache errors", @"C:\xampp\apache\logs\error.log", Avalonia.Media.Brushes.Gray);
        DateTime now = new(2026, 11, 25, 12, 0, 0);
        LogLine[] lines =
        [
            new("old", now.AddDays(-3), LogSeverity.Error, "ERROR", "old"),
            new("warn", now.AddHours(-1), LogSeverity.Warning, "WARN", "warn"),
            new("warn", now.AddHours(-2), LogSeverity.Warning, "WARN", "warn"),
            new("note", now.AddHours(-1), LogSeverity.Notice, "NOTICE", "note"),
        ];

        source.CountProblems(lines, now.AddHours(-24));
        Assert.Equal(2, source.ProblemCount);
        Assert.False(source.HasErrors);

        source.CountProblems([.. lines, new("bad", now, LogSeverity.Critical, "CRIT", "bad")], now.AddHours(-24));
        Assert.Equal(1, source.ProblemCount);
        Assert.True(source.HasErrors);
    }

    [Fact]
    public void LastExport_FindsTheNewestDumpOfThatDatabaseOnly()
    {
        string dumps = DatabaseTools.DumpFolder(_folder);
        Assert.Null(DatabaseTools.LastExport(_folder, "shop"));

        Directory.CreateDirectory(dumps);
        string older = Path.Combine(dumps, "shop_20261101-120000.sql");
        string newer = Path.Combine(dumps, "shop_20261125-104008.sql");
        string otherDatabase = Path.Combine(dumps, "shop_old_20261126-090000.sql");
        foreach (string file in new[] { older, newer, otherDatabase })
        {
            File.WriteAllText(file, "-- dump");
        }
        File.SetLastWriteTime(older, new DateTime(2026, 11, 1, 12, 0, 0));
        File.SetLastWriteTime(newer, new DateTime(2026, 11, 25, 10, 40, 8));
        File.SetLastWriteTime(otherDatabase, new DateTime(2026, 11, 26, 9, 0, 0));

        Assert.Equal(newer, DatabaseTools.LastExport(_folder, "shop")?.Path);
        Assert.Equal("shop_old_20261126-090000.sql", DatabaseTools.LastExport(_folder, "shop_old")?.Name);
        Assert.Null(DatabaseTools.LastExport(_folder, "blog"));
    }

    [Fact]
    public void DatabaseRow_SizesItsBarAgainstTheLargestDatabase()
    {
        var largest = new DatabaseRow(new DatabaseInfo("big", 3, 4000), 4000, null);
        var half = new DatabaseRow(new DatabaseInfo("half", 3, 2000), 4000, null);
        var tiny = new DatabaseRow(new DatabaseInfo("tiny", 1, 1), 4000, null);
        var empty = new DatabaseRow(new DatabaseInfo("phpmyadmin", 0, 0), 4000, null);

        Assert.Equal(largest.BarWidth / 2, half.BarWidth);
        Assert.True(tiny.BarWidth > 0);
        Assert.Equal(0, empty.BarWidth);
        Assert.True(empty.IsXampp);
        Assert.True(empty.IsEmpty);
        Assert.False(half.IsQuiet);
        Assert.False(half.HasLastExport);
    }

    [Fact]
    public void FirstFree_SkipsTakenPorts()
    {
        var listeners = new List<Listener> { new("0.0.0.0", 8080, 1234, false) };
        Assert.Equal(8081, PortConflicts.FirstFree([8080, 8081], listeners));
        Assert.Null(PortConflicts.FirstFree([8080], listeners));
    }
}
