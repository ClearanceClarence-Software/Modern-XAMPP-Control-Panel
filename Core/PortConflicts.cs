using System.Text;
using System.Text.RegularExpressions;
using XamppControl.Updates;

namespace XamppControl.Core;

/// <summary>A configured port that a program outside XAMPP holds.</summary>
/// <param name="Port">The TCP port.</param>
/// <param name="Pid">The process that listens on it; 4 is the Windows kernel (IIS and other http.sys users).</param>
/// <param name="OwnerName">The program's path or name, or "System" for PID 4.</param>
public sealed record PortConflict(int Port, int Pid, string OwnerName)
{
    /// <summary>True when the kernel holds the port, which the panel can't end; IIS is the usual reason.</summary>
    public bool IsSystem => Pid == 4;

    /// <summary>The program's name without its folder, for buttons.</summary>
    public string ShortName => IsSystem ? "System" : Path.GetFileName(OwnerName);
}

/// <summary>What the user chose for a blocked start.</summary>
public enum PortConflictChoice
{
    /// <summary>Leave the module stopped.</summary>
    Cancel,

    /// <summary>End the blocking programs (or stop IIS) and try again.</summary>
    FreePorts,

    /// <summary>Move the module to free ports and try again.</summary>
    MovePorts,
}

/// <summary>What the port conflict dialog shows: the blocked ports and the way out of them.</summary>
/// <param name="ModuleName">The module that can't start.</param>
/// <param name="Conflicts">The blocked ports and who holds them.</param>
/// <param name="FreeLabel">The button that ends the blocking programs, or null when none can be ended.</param>
/// <param name="MoveLabel">The button that moves the module to other ports, or null when it can't be moved.</param>
public sealed record PortConflictPrompt(
    string ModuleName,
    IReadOnlyList<PortConflict> Conflicts,
    string? FreeLabel,
    string? MoveLabel);

/// <summary>Finds free ports and rewrites Apache's and MariaDB's config files to use them.</summary>
public static partial class PortConflicts
{
    /// <summary>The service that holds ports 80 and 443 through the kernel on most PCs: IIS.</summary>
    public const string IisService = "W3SVC";

    /// <summary>The first free port from a list of candidates.</summary>
    /// <param name="candidates">Ports to try, in order of preference.</param>
    /// <param name="listeners">The listening sockets, read once by the caller.</param>
    /// <returns>The first candidate no program listens on; null when all are taken.</returns>
    public static int? FirstFree(IEnumerable<int> candidates, IReadOnlyList<Listener> listeners) =>
        candidates.Where(port => PortTable.OwnerOf(port, listeners) is null).Cast<int?>().FirstOrDefault();

    /// <summary>
    /// Points Apache at other ports: the Listen, ServerName and VirtualHost lines of httpd.conf, httpd-ssl.conf and
    /// httpd-vhosts.conf.
    /// </summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="oldHttp">The HTTP port Apache listens on now.</param>
    /// <param name="newHttp">The HTTP port to use instead.</param>
    /// <param name="oldHttps">The HTTPS port Apache listens on now.</param>
    /// <param name="newHttps">The HTTPS port to use instead.</param>
    /// <returns>The files that were changed, for the log.</returns>
    /// <exception cref="IOException">A config file can't be read or written.</exception>
    public static List<string> MoveApache(string root, int oldHttp, int newHttp, int oldHttps, int newHttps)
    {
        var changed = new List<string>();
        string httpdConf = Path.Combine(root, "apache", "conf", "httpd.conf");
        string sslConf = Path.Combine(root, "apache", "conf", "extra", "httpd-ssl.conf");
        if (oldHttp != newHttp && RewritePorts(httpdConf, oldHttp, newHttp))
        {
            changed.Add(httpdConf);
        }
        if (oldHttps != newHttps && RewritePorts(sslConf, oldHttps, newHttps))
        {
            changed.Add(sslConf);
        }

        // The Sites window writes <VirtualHost *:80> and *:443 blocks; they must follow the ports too.
        string vhostsConf = Path.Combine(root, "apache", "conf", "extra", "httpd-vhosts.conf");
        bool vhostsChanged = oldHttp != newHttp && RewritePorts(vhostsConf, oldHttp, newHttp);
        vhostsChanged |= oldHttps != newHttps && RewritePorts(vhostsConf, oldHttps, newHttps);
        if (vhostsChanged)
        {
            changed.Add(vhostsConf);
        }
        return changed;
    }

    /// <summary>Points MariaDB at another port: the port lines of my.ini and PHP's default MySQL ports.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="oldPort">The port the server listens on now.</param>
    /// <param name="newPort">The port to use instead.</param>
    /// <returns>The files that were changed, for the log.</returns>
    /// <exception cref="IOException">A config file can't be read or written.</exception>
    public static List<string> MoveMySql(string root, int oldPort, int newPort)
    {
        var changed = new List<string>();
        string myIni = Path.Combine(root, "mysql", "bin", "my.ini");
        if (oldPort != newPort && RewritePorts(myIni, oldPort, newPort))
        {
            changed.Add(myIni);
        }
        return changed;
    }

    /// <summary>
    /// Replaces a port number on the lines that name one (Listen, ServerName, VirtualHost, port=), keeping the file's
    /// encoding and line breaks.
    /// </summary>
    /// <param name="path">The config file; a missing file is left alone.</param>
    /// <param name="oldPort">The port to replace, matched as a whole number.</param>
    /// <param name="newPort">The port to write.</param>
    /// <returns>True when the file changed.</returns>
    /// <exception cref="IOException">The file can't be read or written.</exception>
    internal static bool RewritePorts(string path, int oldPort, int newPort)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        string text = FileTools.ReadText(path, out Encoding encoding);
        string lineBreak = FileTools.LineBreakOf(text);
        string[] lines = text.Split(lineBreak);
        bool isChanged = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!PortLine().IsMatch(lines[i]))
            {
                continue;
            }

            string replaced = Regex.Replace(lines[i], $@"(?<![\d.]){oldPort}(?!\d)", newPort.ToString());
            if (replaced != lines[i])
            {
                lines[i] = replaced;
                isChanged = true;
            }
        }

        if (isChanged)
        {
            File.WriteAllText(path, string.Join(lineBreak, lines), encoding);
        }
        return isChanged;
    }

    // Apache: "Listen 80", "Listen 0.0.0.0:80", "ServerName localhost:80", "<VirtualHost _default_:443>", "NameVirtualHost *:80".
    // MariaDB: "port=3306" / "port = 3306" under [client] and [mysqld].
    [GeneratedRegex(@"^\s*(Listen\b|ServerName\b|<VirtualHost\b|NameVirtualHost\b|port\s*=)", RegexOptions.IgnoreCase)]
    private static partial Regex PortLine();
}
