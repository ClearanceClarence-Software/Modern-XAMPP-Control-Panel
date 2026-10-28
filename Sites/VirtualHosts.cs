using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using XamppControl.Core;
using XamppControl.Updates;

namespace XamppControl.Sites;

/// <summary>
/// Writes the panel's sites as Apache virtual hosts in a marked block of httpd-vhosts.conf. Everything outside the
/// block is left as the user wrote it.
/// </summary>
public static partial class VirtualHosts
{
    private const string BlockStart = "# BEGIN Modern XAMPP Control Panel sites";
    private const string BlockEnd = "# END Modern XAMPP Control Panel sites";
    private const string VhostsInclude = "conf/extra/httpd-vhosts.conf";

    /// <summary>Full path of httpd-vhosts.conf.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <returns>root\apache\conf\extra\httpd-vhosts.conf; the file may not exist.</returns>
    public static string VhostsPath(string root) => Path.Combine(root, "apache", "conf", "extra", "httpd-vhosts.conf");

    /// <summary>Full path of httpd.conf.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <returns>root\apache\conf\httpd.conf; the file may not exist.</returns>
    public static string HttpdConfPath(string root) => Path.Combine(root, "apache", "conf", "httpd.conf");

    /// <summary>The main DocumentRoot from httpd.conf, which the localhost virtual hosts keep serving.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <returns>The first active DocumentRoot as written; root/htdocs when httpd.conf has none.</returns>
    public static string MainDocumentRoot(string root)
    {
        string httpdConf = HttpdConfPath(root);
        if (File.Exists(httpdConf))
        {
            foreach (string line in FileTools.ReadText(httpdConf, out _).Split('\n'))
            {
                Match documentRoot = DocumentRootLine().Match(line);
                if (documentRoot.Success)
                {
                    return documentRoot.Groups["path"].Value;
                }
            }
        }

        return ApachePath(Path.Combine(root, "htdocs"));
    }

    /// <summary>
    /// Builds the managed block: localhost first (so it stays the default host), then every site, on HTTP and, for
    /// sites that want it, on HTTPS.
    /// </summary>
    /// <param name="sites">The sites in the order they are written; folders must be full paths.</param>
    /// <param name="mainDocumentRoot">localhost's document root, from <see cref="MainDocumentRoot"/>.</param>
    /// <param name="httpPort">Apache's HTTP port from the panel's settings.</param>
    /// <param name="httpsPort">Apache's HTTPS port from the panel's settings.</param>
    /// <param name="includeHttps">True writes the HTTPS hosts; false when no site uses HTTPS.</param>
    /// <returns>The block with its BEGIN and END marker lines, with Windows line endings.</returns>
    public static string BuildBlock(
        IReadOnlyList<Site> sites,
        string mainDocumentRoot,
        int httpPort,
        int httpsPort,
        bool includeHttps)
    {
        var block = new StringBuilder();
        block.Append(BlockStart).Append("\r\n");
        block.Append("# Written by Modern XAMPP Control Panel from its Sites window. Changes inside this\r\n");
        block.Append("# block are replaced the next time sites are applied; add your own virtual hosts\r\n");
        block.Append("# outside it.\r\n\r\n");

        AppendHost(block, httpPort, "localhost", mainDocumentRoot, useSsl: false, withDirectory: false);
        foreach (Site site in sites)
        {
            AppendHost(block, httpPort, site.Host, ApachePath(site.Folder), useSsl: false, withDirectory: true);
        }

        if (includeHttps)
        {
            block.Append("<IfModule ssl_module>\r\n\r\n");
            AppendHost(block, httpsPort, "localhost", mainDocumentRoot, useSsl: true, withDirectory: false);
            foreach (Site site in sites.Where(site => site.UseHttps))
            {
                AppendHost(block, httpsPort, site.Host, ApachePath(site.Folder), useSsl: true, withDirectory: true);
            }
            block.Append("</IfModule>\r\n");
        }

        block.Append(BlockEnd).Append("\r\n");
        return block.ToString();
    }

    /// <summary>Appends one VirtualHost section followed by a blank line.</summary>
    /// <param name="block">The block being built.</param>
    /// <param name="port">The port the host answers on.</param>
    /// <param name="hostName">The name the host answers to, written as its ServerName.</param>
    /// <param name="documentRoot">The document root with forward slashes, as Apache writes paths.</param>
    /// <param name="useSsl">True adds SSLEngine and the panel's certificate.</param>
    /// <param name="withDirectory">
    /// True grants access to the folder, which Apache denies outside htdocs by default.
    /// </param>
    private static void AppendHost(
        StringBuilder block,
        int port,
        string hostName,
        string documentRoot,
        bool useSsl,
        bool withDirectory)
    {
        block.Append($"<VirtualHost *:{port}>\r\n");
        block.Append($"    ServerName {hostName}\r\n");
        block.Append($"    DocumentRoot \"{documentRoot}\"\r\n");
        if (withDirectory)
        {
            block.Append($"    <Directory \"{documentRoot}\">\r\n");
            block.Append("        Options Indexes FollowSymLinks Includes ExecCGI\r\n");
            block.Append("        AllowOverride All\r\n");
            block.Append("        Require all granted\r\n");
            block.Append("    </Directory>\r\n");
        }
        if (useSsl)
        {
            block.Append("    SSLEngine on\r\n");
            block.Append($"    SSLCertificateFile \"{LocalCertificates.ServerCertificate}\"\r\n");
            block.Append($"    SSLCertificateKeyFile \"{LocalCertificates.ServerKey}\"\r\n");
        }
        block.Append("</VirtualHost>\r\n\r\n");
    }

    /// <summary>
    /// Puts the block at the top of httpd-vhosts.conf, replacing the previous one, or removes it when
    /// <paramref name="block"/> is null.
    /// </summary>
    /// <param name="vhostsPath">Full path of httpd-vhosts.conf; created when missing.</param>
    /// <param name="block">The block from <see cref="BuildBlock"/>; null removes the panel's block.</param>
    /// <remarks>
    /// The block goes first because Apache answers unknown host names with the first virtual host, which should
    /// stay localhost.
    /// </remarks>
    public static void WriteBlock(string vhostsPath, string? block)
    {
        Encoding encoding = new UTF8Encoding(false);
        string existing = File.Exists(vhostsPath) ? FileTools.ReadText(vhostsPath, out encoding) : "";
        string withoutBlock = RemoveBlock(existing);
        string updated = block is null ? withoutBlock : block + "\r\n" + withoutBlock;
        File.WriteAllText(vhostsPath, updated, encoding);
    }

    /// <summary>The file's text without the panel's block and the blank line written after it.</summary>
    /// <param name="text">The whole of httpd-vhosts.conf.</param>
    /// <returns>The text unchanged when it has no complete block.</returns>
    private static string RemoveBlock(string text)
    {
        int start = text.IndexOf(BlockStart, StringComparison.Ordinal);
        int end = text.IndexOf(BlockEnd, StringComparison.Ordinal);
        if (start < 0 || end < start)
        {
            return text;
        }

        int afterEnd = end + BlockEnd.Length;
        while (afterEnd < text.Length && (text[afterEnd] == '\r' || text[afterEnd] == '\n'))
        {
            afterEnd++;
        }
        return string.Concat(text.AsSpan(0, start), text.AsSpan(afterEnd));
    }

    /// <summary>Makes sure httpd.conf includes httpd-vhosts.conf, uncommenting or adding the Include line.</summary>
    /// <param name="httpdConfPath">Full path of httpd.conf.</param>
    /// <returns>True when httpd.conf was changed; false when it already included the file.</returns>
    public static bool EnsureIncluded(string httpdConfPath)
    {
        string text = FileTools.ReadText(httpdConfPath, out Encoding encoding);
        string lineBreak = FileTools.LineBreakOf(text);
        List<string> lines = [.. text.Split(lineBreak)];
        int commentedLine = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            Match include = VhostsIncludeLine().Match(lines[i]);
            if (!include.Success)
            {
                continue;
            }

            if (include.Groups["comment"].Length == 0)
            {
                return false;
            }
            commentedLine = commentedLine < 0 ? i : commentedLine;
        }

        if (commentedLine >= 0)
        {
            lines[commentedLine] = lines[commentedLine].TrimStart().TrimStart('#').TrimStart();
        }
        else
        {
            // Inserted before the last line, which is the empty entry after the file's final line break.
            int insertAt = lines.Count > 0 && lines[^1].Length == 0 ? lines.Count - 1 : lines.Count;
            lines.InsertRange(insertAt,
            [
                "",
                "# Virtual hosts (added by Modern XAMPP Control Panel)",
                $"Include \"{VhostsInclude}\"",
            ]);
        }

        File.WriteAllText(httpdConfPath, string.Join(lineBreak, lines), encoding);
        return true;
    }

    /// <summary>The ports Apache listens on, from the Listen lines of a config file.</summary>
    /// <param name="confPath">Full path of httpd.conf or httpd-ssl.conf; a missing file has no ports.</param>
    /// <returns>The ports in file order, without duplicates; empty when the file sets none.</returns>
    public static List<int> ListenPorts(string confPath)
    {
        if (!File.Exists(confPath))
        {
            return [];
        }

        string text = FileTools.ReadText(confPath, out _);
        return text.Split('\n')
            .Select(line => ListenLine().Match(line))
            .Where(listen => listen.Success)
            .Select(listen => int.Parse(listen.Groups["port"].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();
    }

    /// <summary>A Windows path in the form Apache's config uses: forward slashes, no trailing slash.</summary>
    /// <param name="path">A full Windows path.</param>
    /// <returns>The path with backslashes turned into forward slashes.</returns>
    public static string ApachePath(string path) => path.Replace('\\', '/').TrimEnd('/');

    [GeneratedRegex(@"^\s*DocumentRoot\s+""?(?<path>[^""]+?)""?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentRootLine();

    // Matches relative and absolute forms: conf/extra/httpd-vhosts.conf or "C:/xampp/apache/conf/extra/...".
    [GeneratedRegex(
        @"^\s*(?<comment>#\s*)?Include(Optional)?\s+""?[^""]*httpd-vhosts\.conf""?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex VhostsIncludeLine();

    [GeneratedRegex(@"^\s*Listen\s+(?:\S*:)?(?<port>\d{1,5})\b", RegexOptions.IgnoreCase)]
    private static partial Regex ListenLine();
}
