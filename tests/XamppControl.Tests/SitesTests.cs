using XamppControl.Core;
using XamppControl.Sites;

namespace XamppControl.Tests;

public sealed class SitesTests
{
    [Fact]
    public void BuildBlock_PutsLocalhostFirstAndHttpsHostsInsideTheSslModuleCheck()
    {
        var sites = new List<Site>
        {
            new() { Host = "shop.test", Folder = @"C:\projects\shop\public", UseHttps = true },
            new() { Host = "plain.test", Folder = @"C:\projects\plain", UseHttps = false },
        };

        string block = VirtualHosts.BuildBlock(sites, "C:/xampp/htdocs", 80, 443, includeHttps: true);

        int localhostIndex = block.IndexOf("ServerName localhost", StringComparison.Ordinal);
        int shopIndex = block.IndexOf("ServerName shop.test", StringComparison.Ordinal);
        int sslIndex = block.IndexOf("<IfModule ssl_module>", StringComparison.Ordinal);
        Assert.True(localhostIndex >= 0 && localhostIndex < shopIndex);
        Assert.True(sslIndex > shopIndex);
        Assert.Contains("<VirtualHost *:443>", block, StringComparison.Ordinal);
        Assert.Contains("DocumentRoot \"C:/projects/shop/public\"", block, StringComparison.Ordinal);
        Assert.Contains("Require all granted", block, StringComparison.Ordinal);
        Assert.Equal(0, CountOf(block, "<VirtualHost *:443>\r\n    ServerName plain.test"));
        Assert.StartsWith("# BEGIN Modern XAMPP Control Panel sites", block, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteBlock_ReplacesOnlyThePanelsBlock()
    {
        string folder = Path.Combine(Path.GetTempPath(), "XamppControlTests");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".conf");
        try
        {
            File.WriteAllText(path,
                "# user's own\r\n<VirtualHost *:80>\r\n    ServerName mine.test\r\n</VirtualHost>\r\n");
            string first = BlockFor("a.test");
            string second = BlockFor("b.test");

            VirtualHosts.WriteBlock(path, first);
            VirtualHosts.WriteBlock(path, second);
            string text = File.ReadAllText(path);

            Assert.Contains("ServerName b.test", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ServerName a.test", text, StringComparison.Ordinal);
            Assert.Contains("ServerName mine.test", text, StringComparison.Ordinal);
            Assert.Equal(1, CountOf(text, "# BEGIN Modern XAMPP Control Panel sites"));

            VirtualHosts.WriteBlock(path, null);
            Assert.DoesNotContain("Modern XAMPP Control Panel", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("Include conf/extra/httpd-vhosts.conf", false)]
    [InlineData("#Include conf/extra/httpd-vhosts.conf", true)]
    [InlineData("# Include \"C:/xampp/apache/conf/extra/httpd-vhosts.conf\"", true)]
    public void EnsureIncluded_UncommentsAnExistingIncludeInstead(string line, bool expectChange)
    {
        string folder = Path.Combine(Path.GetTempPath(), "XamppControlTests");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".conf");
        try
        {
            File.WriteAllText(path, "Listen 80\r\n" + line + "\r\n");

            bool changed = VirtualHosts.EnsureIncluded(path);
            string[] lines = File.ReadAllLines(path);

            Assert.Equal(expectChange, changed);
            Assert.Equal(1, lines.Count(line => line.Contains("httpd-vhosts.conf", StringComparison.Ordinal)));
            Assert.DoesNotContain(lines, line => line.TrimStart().StartsWith('#')
                && line.Contains("httpd-vhosts.conf", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WithHosts_AddsReplacesAndRemovesTheBlock()
    {
        const string original = "# Copyright (c) 1993-2009 Microsoft Corp.\r\n127.0.0.1       localhost\r\n";

        string withTwo = HostsFile.WithHosts(original, ["a.test", "b.test"]);
        string withOne = HostsFile.WithHosts(withTwo, ["c.test"]);
        string withNone = HostsFile.WithHosts(withOne, []);

        Assert.Contains("127.0.0.1\ta.test", withTwo, StringComparison.Ordinal);
        Assert.Contains("::1\tb.test", withTwo, StringComparison.Ordinal);
        Assert.Contains("c.test", withOne, StringComparison.Ordinal);
        Assert.DoesNotContain("a.test", withOne, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(withOne, "# BEGIN Modern XAMPP Control Panel sites"));
        Assert.Equal(original, withNone);
    }

    [Theory]
    [InlineData("myproject.test", true)]
    [InlineData("sub.myproject.test", true)]
    [InlineData("localhost", false)]
    [InlineData("my project.test", false)]
    [InlineData("noDot", false)]
    public void FindProblem_ChecksHostNames(string host, bool isValid)
    {
        var sites = new List<Site> { new() { Host = host, Folder = @"C:\projects\x", UseHttps = true } };
        Assert.Equal(isValid, SiteSetup.FindProblem(sites) is null);
    }

    [Fact]
    public void FindProblem_RequiresHttpsForPreloadedDomains()
    {
        var sites = new List<Site> { new() { Host = "myproject.dev", Folder = @"C:\projects\x", UseHttps = false } };
        Assert.Contains("HTTPS", SiteSetup.FindProblem(sites), StringComparison.Ordinal);
    }

    private static string BlockFor(string host) =>
        VirtualHosts.BuildBlock([new Site { Host = host, Folder = @"C:\" + host }], "C:/htdocs", 80, 443, false);

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int index = text.IndexOf(part, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(part, index + part.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
