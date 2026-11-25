using System.Globalization;
using XamppControl.Databases;
using XamppControl.Updates;

namespace XamppControl.Core;

/// <summary>The hidden developer switches: <c>--mock</c>, <c>--scene</c>, <c>--shot</c>, <c>--size</c>, <c>--theme</c>, <c>--lang</c>.</summary>
/// <param name="Scene">A screenshot scene to pose (see <c>MainWindow.Scenes</c>); null just opens the mock machine.</param>
/// <param name="Shot">With a scene: the PNG to write before the panel exits; null leaves the posed scene open.</param>
/// <param name="Size">With a scene: the main window's client size; null keeps the default size.</param>
/// <param name="Theme">"Light" or "Dark".</param>
/// <param name="Language">The language code to show, e.g. "en" or "nb".</param>
public sealed record MockOptions(
    string? Scene,
    string? Shot,
    (int Width, int Height)? Size,
    string Theme,
    string Language)
{
    /// <summary>Reads the switches; they are case-insensitive, and one that needs a value and has none is ignored.</summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The options; null when neither <c>--mock</c> nor <c>--scene</c> is present.</returns>
    public static MockOptions? Parse(string[] args)
    {
        bool isMock = false;
        string? scene = null;
        string? shot = null;
        (int Width, int Height)? size = null;
        string theme = AppTheme.Light;
        string language = Loc.English;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--mock":
                    isMock = true;
                    break;
                case "--scene" when i + 1 < args.Length:
                    // A scene is always posed on the mock machine.
                    scene = args[++i].ToLowerInvariant();
                    isMock = true;
                    break;
                case "--shot" when i + 1 < args.Length:
                    shot = Path.GetFullPath(args[++i]);
                    break;
                case "--size" when i + 1 < args.Length:
                    string[] dimensions = args[++i].ToLowerInvariant().Split('x');
                    if (dimensions.Length == 2
                        && int.TryParse(dimensions[0], out int width)
                        && int.TryParse(dimensions[1], out int height)
                        && width > 200 && height > 200)
                    {
                        size = (width, height);
                    }
                    break;
                case "--theme" when i + 1 < args.Length:
                    theme = args[++i].Equals("dark", StringComparison.OrdinalIgnoreCase) ? AppTheme.Dark : AppTheme.Light;
                    break;
                case "--lang" when i + 1 < args.Length:
                    language = args[++i];
                    break;
                default:
                    break;
            }
        }

        if (!isMock)
        {
            return null;
        }

        // A shot without a scene is the plain main window.
        return new MockOptions(shot is not null ? scene ?? "main" : scene, shot, size, theme, language);
    }
}

/// <summary>
/// The pretend PC for screenshots and for trying things. With <c>--mock</c> the panel runs against a made-up XAMPP
/// in the temp folder: the files are real (so the PHP, Sites and Logs windows read them as usual), while everything
/// that would touch the real PC is replaced: processes, ports, services, databases, downloads, the certificate
/// store, the hosts file and the settings file. Nothing is started, stopped, killed, downloaded or saved on it,
/// and nothing on screen marks it as pretend.
/// </summary>
public static class MockMachine
{
    /// <summary>The folder name shown instead of the temp folder, so screenshots read like a normal install.</summary>
    public const string PrettyRoot = @"C:\xampp";

    private const int SystemPid = 4;

    private static readonly HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock _gate = new();

    /// <summary>True while the panel runs against the mock machine.</summary>
    public static bool IsActive { get; private set; }

    /// <summary>The command-line options the mock machine was started with; null on the real one.</summary>
    public static MockOptions? Options { get; private set; }

    /// <summary>The mock XAMPP folder in the temp folder; empty on the real machine.</summary>
    public static string Root { get; private set; } = "";

    /// <summary>The empty folder the mock machine points at while <see cref="IsXamppMissing"/> is set.</summary>
    public static string MissingRoot { get; private set; } = "";

    /// <summary>True makes Windows itself hold ports 80 and 443, for the port conflict shots.</summary>
    public static bool HasPortConflict { get; set; }

    /// <summary>True makes the update check find a newer release, for the header link and What's new.</summary>
    public static bool HasAppUpdate { get; set; }

    /// <summary>True makes the mock machine have no XAMPP at all, for the "not found" shots.</summary>
    public static bool IsXamppMissing { get; set; }

    /// <summary>Where the settings folder is shown to be; the real one is in the temp folder.</summary>
    public const string PrettySettings = @"C:\Users\dev\AppData\Roaming\XamppControl";

    /// <summary>True makes the mock feeds offer a newer PHP 8.4 build, next to the newer MariaDB and Mailpit.</summary>
    public static bool HasNewVersions { get; set; }

    /// <summary>
    /// Whether the startup check for new PHP, MariaDB and Mailpit versions runs: always on the real machine; on
    /// the mock machine only in its own scene and in plain <c>--mock</c>, so the other pictures stay quiet.
    /// </summary>
    public static bool ChecksComponentUpdates => !IsActive || HasNewVersions;

    /// <summary>True makes <c>httpd -t</c> report a syntax error, so starting Apache explains it in the log.</summary>
    public static bool HasConfigError { get; set; }

    /// <summary>True makes Apache and MySQL installed as Windows services, so their Service boxes are ticked.</summary>
    public static bool HasServices { get; set; }

    /// <summary>False leaves Xdebug out of the mock PHP, for the "Install Xdebug" state.</summary>
    public static bool HasXdebug { get; set; } = true;

    /// <summary>True makes the installed Xdebug an older build, so the PHP window offers the update.</summary>
    public static bool IsXdebugOutdated { get; set; }

    /// <summary>False makes Windows not trust the local certificate authority, for the Trust button.</summary>
    public static bool IsAuthorityTrusted { get; set; } = true;

    /// <summary>False starts the mock machine without any sites.</summary>
    public static bool HasSites { get; set; } = true;

    /// <summary>How long a mock Start or Stop takes; long in the scene that shows a module starting.</summary>
    public static TimeSpan ActionDelay { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>Builds the mock XAMPP folder and switches the panel to it. Called once, before any window opens.</summary>
    /// <param name="options">The command-line options.</param>
    public static void Start(MockOptions options)
    {
        Options = options;
        Root = Path.Combine(Path.GetTempPath(), "XamppControl", "mock", "xampp");
        MissingRoot = Path.Combine(Path.GetTempPath(), "XamppControl", "mock", "empty");
        Directory.CreateDirectory(MissingRoot);

        // Each scene starts from the state it shows; the scene itself starts the modules, so the log reads like
        // a real start. Without a scene the three modules already run.
        IsXamppMissing = options.Scene is "no-xampp" or "xampp-missing";
        HasPortConflict = options.Scene is "port-conflict" or "netstat-conflict";
        HasAppUpdate = options.Scene is "update" or "whats-new" or "whats-new-installing";
        HasNewVersions = options.Scene is null or "new-versions";
        HasConfigError = options.Scene == "config-error";
        HasServices = options.Scene == "services";
        HasXdebug = options.Scene != "php-no-xdebug";
        IsXdebugOutdated = options.Scene is "php-xdebug-update" or "new-versions";
        IsAuthorityTrusted = options.Scene != "sites-untrusted";
        HasSites = options.Scene != "sites-empty";
        if (options.Scene == "starting")
        {
            ActionDelay = TimeSpan.FromMinutes(5);
        }
        if (options.Scene is null)
        {
            lock (_gate)
            {
                _running.UnionWith(["Apache", "MySQL", "Mailpit"]);
            }
        }
        BuildTree(Root);
        IsActive = true;
    }

    /// <summary>The settings of the mock machine; never read from or written to disk.</summary>
    /// <returns>Defaults pointing at the mock folder, with a few sites, user files and Autostart ticks (which start nothing here).</returns>
    public static AppSettings Settings() => new()
    {
        XamppRoot = IsXamppMissing ? MissingRoot : Root,
        Language = Options?.Language ?? Loc.English,
        Theme = Options?.Theme ?? AppTheme.Light,
        MinimizeToTray = false,
        CheckPortsOnStartup = false,
        AutoStart = ["Apache", "MySQL"],
        UserFiles =
        [
            new UserFile { Module = "Apache", Path = PrettyRoot + @"\htdocs\shop\.htaccess" },
            new UserFile { Module = "Apache", Path = PrettyRoot + @"\apache\conf\extra\httpd-proxy.conf" },
            new UserFile { Module = "MySQL", Path = PrettyRoot + @"\mysql\bin\my-dev.ini" },
        ],
        Sites = HasSites
            ?
            [
                new Site { Host = "shop.test", Folder = PrettyRoot + @"\htdocs\shop", UseHttps = true },
                new Site { Host = "blog.test", Folder = PrettyRoot + @"\htdocs\blog", UseHttps = true },
                new Site { Host = "api.acme.test", Folder = @"D:\work\acme-api\public", UseHttps = false },
                new Site { Host = "docs.acme.test", Folder = @"D:\work\acme-docs\build", UseHttps = true },
            ]
            : [],
    };

    /// <summary>What a command prints on the mock machine. Only <c>httpd -t</c> answers; nothing is ever run.</summary>
    /// <param name="exe">The program that would have been started.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>Apache's config test result, or a failed command for everything else.</returns>
    public static Shell.RunResult Run(string exe, string arguments)
    {
        bool isConfigTest = Path.GetFileName(exe).Equals("httpd.exe", StringComparison.OrdinalIgnoreCase)
            && arguments.Trim() == "-t";
        if (!isConfigTest)
        {
            return new Shell.RunResult(-1, "", "Mock machine: no program is run.", false);
        }

        return HasConfigError
            ? new Shell.RunResult(
                1,
                "",
                "AH00526: Syntax error on line 14 of C:/xampp/apache/conf/extra/httpd-vhosts.conf:\n" +
                "Invalid command 'DocumentRot', perhaps misspelled or defined by a module not included in the " +
                "server configuration",
                false)
            : new Shell.RunResult(0, "", "Syntax OK", false);
    }

    /// <summary>The disk space a parked version's folder is shown to take; the mock folders hold empty files.</summary>
    /// <param name="folder">The version's folder.</param>
    /// <returns>Around 120 MB for a PHP build and 700 MB for a MariaDB build with its databases.</returns>
    public static long PretendSize(string folder)
    {
        // The last digit of the version varies the size a little, so the rows don't all read the same.
        int vary = Math.Clamp(folder.LastOrDefault(char.IsDigit) - '0', 0, 9);
        bool isMariaDb = folder.Contains("mariadb", StringComparison.OrdinalIgnoreCase);
        return (isMariaDb ? 690L + (vary * 14) : 112L + (vary * 3)) * 1024 * 1024;
    }

    /// <summary>Whether a Windows service exists on the mock machine.</summary>
    /// <param name="name">The service name.</param>
    /// <returns>True for Apache's and MySQL's default service names while <see cref="HasServices"/> is set.</returns>
    public static bool IsServiceInstalled(string name) =>
        HasServices && (name.Equals("Apache2.4", StringComparison.OrdinalIgnoreCase)
            || name.Equals("mysql", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Plays a download: the progress runs to the end over a few seconds, then the "download" stops with an error,
    /// because nothing is ever installed on the mock machine.
    /// </summary>
    /// <param name="progress">Receives the progress as 0..1.</param>
    /// <returns>Never completes normally.</returns>
    /// <exception cref="InvalidOperationException">Always, once the progress has reached the end.</exception>
    public static async Task PlayDownloadAsync(Action<double?> progress)
    {
        const int steps = 50;
        for (int step = 1; step <= steps; step++)
        {
            progress(step / (double)steps);
            await Task.Delay(200);
        }
        throw new InvalidOperationException("Mock machine: the download was only played, nothing is installed.");
    }

    /// <summary>Replaces the temp folder with <see cref="PrettyRoot"/> in text that is shown.</summary>
    /// <param name="text">A path or a sentence that may contain the mock folder.</param>
    /// <returns>The text as a normal install would show it; unchanged on the real machine.</returns>
    public static string Pretty(string text) =>
        IsActive && Root.Length > 0
            ? text.Replace(MissingRoot, PrettyRoot, StringComparison.OrdinalIgnoreCase)
                .Replace(Root, PrettyRoot, StringComparison.OrdinalIgnoreCase)
                .Replace(AppSettings.Folder, PrettySettings, StringComparison.OrdinalIgnoreCase)
            : text;

    /// <summary>The mock folder behind a path that is shown as <see cref="PrettyRoot"/>.</summary>
    /// <param name="path">A path as the user sees or typed it, e.g. "C:\xampp\php".</param>
    /// <returns>The path inside the mock folder; unchanged on the real machine or for other paths.</returns>
    public static string Real(string path) =>
        IsActive && path.StartsWith(PrettyRoot, StringComparison.OrdinalIgnoreCase)
            ? Root + path[PrettyRoot.Length..]
            : path;

    /// <summary>Whether a module of the mock machine runs.</summary>
    /// <param name="module">The module's name, e.g. "Apache".</param>
    /// <returns>True when it was started.</returns>
    public static bool IsRunning(string module)
    {
        lock (_gate)
        {
            return _running.Contains(module);
        }
    }

    /// <summary>Starts or stops a module of the mock machine; the next status refresh shows it.</summary>
    /// <param name="module">The module's name.</param>
    /// <param name="isRunning">True to start it.</param>
    public static void SetRunning(string module, bool isRunning)
    {
        lock (_gate)
        {
            if (isRunning)
            {
                _running.Add(module);
            }
            else
            {
                _running.Remove(module);
            }
        }
    }

    /// <summary>The processes of the mock machine: the running modules plus a few of Windows' own and a dev server.</summary>
    /// <returns>PID, process name without ".exe", full path (empty for the kernel) and listening ports.</returns>
    private static List<(int Pid, string Name, string Path, int[] Ports)> Processes()
    {
        string system32 = Environment.SystemDirectory;
        var processes = new List<(int Pid, string Name, string Path, int[] Ports)>
        {
            (SystemPid, "System", "", HasPortConflict ? new[] { 80, 443, 445, 5357 } : new[] { 445, 5357 }),
            (1016, "lsass", Path.Combine(system32, "lsass.exe"), new[] { 49664 }),
            (1240, "svchost", Path.Combine(system32, "svchost.exe"), new[] { 135 }),
            (7332, "svchost", Path.Combine(system32, "svchost.exe"), new[] { 5040 }),
            (18220, "node", @"C:\Program Files\nodejs\node.exe", new[] { 3000, 5173 }),
        };

        if (IsRunning("Apache") && !HasPortConflict)
        {
            string httpd = Path.Combine(Root, "apache", "bin", "httpd.exe");
            processes.Add((21704, "httpd", httpd, new[] { 80, 443 }));
            processes.Add((9328, "httpd", httpd, Array.Empty<int>()));
        }
        if (IsRunning("MySQL"))
        {
            string mariadbd = Path.Combine(Root, "mysql", "bin", "mariadbd.exe");
            processes.Add((30140, "mariadbd", mariadbd, new[] { 3306 }));
        }
        if (IsRunning("Mailpit"))
        {
            string mailpit = Path.Combine(Root, "mailpit", "mailpit.exe");
            processes.Add((8812, "mailpit", mailpit, new[] { 1025, 8025 }));
        }
        return processes;
    }

    /// <summary>The mock machine's listening sockets, IPv4 plus IPv6 for the web ports.</summary>
    /// <returns>One listener per port and address family.</returns>
    public static List<Listener> Listeners()
    {
        var listeners = new List<Listener>();
        foreach ((int pid, _, _, int[] ports) in Processes())
        {
            foreach (int port in ports)
            {
                bool isLocalOnly = port is 1025 or 8025 or 3000 or 5173 or 5040;
                listeners.Add(new Listener(isLocalOnly ? "127.0.0.1" : "0.0.0.0", port, pid, false));
                if (port is 80 or 443 or 3306 or 445)
                {
                    listeners.Add(new Listener("::", port, pid, true));
                }
            }
        }
        return listeners;
    }

    /// <summary>The PIDs of the mock processes with one of the given names.</summary>
    /// <param name="names">Process names without ".exe".</param>
    /// <returns>The matching PIDs, lowest ports first as the real scan orders them.</returns>
    public static List<int> FindPids(IEnumerable<string> names)
    {
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return Processes().Where(process => wanted.Contains(process.Name)).Select(process => process.Pid).ToList();
    }

    /// <summary>The name of a mock process.</summary>
    /// <param name="pid">The process ID.</param>
    /// <returns>The name without ".exe"; null when the mock machine has no such process.</returns>
    public static string? ProcessName(int pid) =>
        Processes().Where(process => process.Pid == pid).Select(process => process.Name).FirstOrDefault();

    /// <summary>The executable of a mock process.</summary>
    /// <param name="pid">The process ID.</param>
    /// <returns>The full path; null for the kernel and for unknown PIDs.</returns>
    public static string? ProcessPath(int pid) =>
        Processes().Where(process => process.Pid == pid && process.Path.Length > 0)
            .Select(process => process.Path)
            .FirstOrDefault();

    /// <summary>The version a mock executable reports; the files themselves are empty.</summary>
    /// <param name="path">Full path of a file in the mock folder.</param>
    /// <returns>The version; null for files that have none.</returns>
    public static string? FileVersion(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string relative = Path.GetRelativePath(Root, path).Replace('/', '\\').ToLowerInvariant();
        string[] parts = relative.Split('\\');

        // versions\php\8.3.29\php.exe and versions\mariadb\11.8.3\bin\mariadbd.exe carry their folder's version.
        if (parts.Length >= 4 && parts[0] == "versions")
        {
            return parts[2];
        }

        return relative switch
        {
            @"apache\bin\httpd.exe" => "2.4.58",
            @"php\php.exe" => "8.4.27",
            @"php\ext\php_xdebug.dll" => IsXdebugOutdated ? "3.4.5" : "3.5.3",
            @"mysql\bin\mariadbd.exe" or @"mysql\bin\mysqld.exe" => "12.3.3",
            _ => null,
        };
    }

    /// <summary>The databases on the mock server.</summary>
    /// <returns>Five databases with table counts and sizes.</returns>
    public static List<DatabaseInfo> Databases() =>
    [
        new DatabaseInfo("acme_api", 31, 44_774_195, "utf8mb4_unicode_ci"),
        new DatabaseInfo("blog_wp", 12, 6_396_314, "utf8mb4_unicode_520_ci"),
        new DatabaseInfo("phpmyadmin", 19, 1_258_291, "utf8mb3_bin"),
        new DatabaseInfo("shop", 24, 19_293_798, "utf8mb4_general_ci"),
        new DatabaseInfo("test", 0, 0, "utf8mb4_general_ci"),
    ];

    /// <summary>The newest dump of a mock database.</summary>
    /// <param name="database">The schema name.</param>
    /// <returns>A dump from this morning for "shop", an older one for "acme_api", none for the rest.</returns>
    public static DumpFile? LastExport(string database)
    {
        DateTime? written = database switch
        {
            "shop" => DateTime.Today.AddHours(10).AddMinutes(40),
            "acme_api" => DateTime.Today.AddDays(-3).AddHours(16).AddMinutes(5),
            _ => null,
        };
        return written is { } time
            ? new DumpFile($@"C:\xampp\mysql_dumps\{database}_{time:yyyyMMdd-HHmmss}.sql", time)
            : null;
    }

    /// <summary>The PHP branches the mock download feed offers.</summary>
    /// <returns>Four branches, newest first.</returns>
    public static List<PhpBranch> PhpBranches() =>
    [
        new PhpBranch("8.5", "8.5.12", ReleaseFeeds.PhpBase + "php-8.5.12-Win32-vs17-x64.zip", null),
        HasNewVersions
            ? new PhpBranch("8.4", "8.4.28", ReleaseFeeds.PhpBase + "php-8.4.28-Win32-vs17-x64.zip", null)
            : new PhpBranch("8.4", "8.4.27", ReleaseFeeds.PhpBase + "php-8.4.27-Win32-vs17-x64.zip", null),
        new PhpBranch("8.3", "8.3.29", ReleaseFeeds.PhpBase + "php-8.3.29-Win32-vs16-x64.zip", null),
        new PhpBranch("8.2", "8.2.30", ReleaseFeeds.PhpBase + "php-8.2.30-Win32-vs16-x64.zip", null),
    ];

    /// <summary>The MariaDB series the mock feed offers.</summary>
    /// <returns>Three series, newest first.</returns>
    public static List<MariaDbMajor> MariaDbMajors() =>
    [
        new MariaDbMajor("12.3", "Long Term Support", "2029-06-30"),
        new MariaDbMajor("11.8", "Long Term Support", "2028-06-04"),
        new MariaDbMajor("11.4", "Long Term Support", "2029-05-29"),
    ];

    /// <summary>The newest build of a MariaDB series in the mock feed.</summary>
    /// <param name="major">The series, e.g. "12.3".</param>
    /// <returns>A release whose version is the series plus a patch number.</returns>
    public static MariaDbRelease MariaDbLatest(string major) =>
        new(major + (major == "12.3" ? ".4" : ".6"), "https://archive.mariadb.org/mock.zip", null);

    /// <summary>The newest Mailpit in the mock feed.</summary>
    public static MailpitRelease Mailpit =>
        new(HasNewVersions ? "1.33.1" : "1.33.0", "https://github.com/axllent/mailpit/releases/latest");

    /// <summary>The newer panel release the mock update check finds when <see cref="HasAppUpdate"/> is set.</summary>
    /// <returns>One release, or none.</returns>
    public static List<AppRelease> NewerAppReleases()
    {
        if (!HasAppUpdate)
        {
            return [];
        }

        return
        [
            new AppRelease(
                "v" + NextVersion(),
                AppUpdateCheck.ReleasesUrl,
                "Added\n\n" +
                "• Per-site PHP version: each site can run its own PHP from the versions you have installed.\n" +
                "• New site from a template: WordPress, Laravel or plain PHP, with database and certificate.\n" +
                "• Scheduled database dumps to xampp\\mysql_dumps, keeping the newest seven.\n\n" +
                "Fixed\n\n" +
                "• The Logs window keeps its place when a log file is rotated.\n" +
                "• Port check no longer reports ports held by XAMPP's own services.",
                "https://github.com/mock/update.zip",
                null),
        ];
    }

    /// <summary>The version after the running one, for the mock update.</summary>
    /// <returns>The running version with its minor number raised, e.g. "0.6.0" for "0.5.0".</returns>
    private static string NextVersion()
    {
        Version current = VersionUtil.Parse(BuildInfo.Version);
        return $"{current.Major}.{current.Minor + 1}.0";
    }

    /// <summary>Writes the mock XAMPP folder: empty executables, real config and log files.</summary>
    /// <param name="root">The folder to (re)create.</param>
    private static void BuildTree(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A console or Explorer window still open in the old mock folder: the files are overwritten below.
        }

        string[] emptyFiles =
        [
            @"htdocs\shop\.htaccess",
            @"apache\conf\extra\httpd-proxy.conf",
            @"mysql\bin\my-dev.ini",
            @"apache\bin\httpd.exe",
            @"mysql\bin\mariadbd.exe",
            @"mysql\bin\mariadb.exe",
            @"mysql\bin\mariadb-dump.exe",
            @"mysql\bin\mariadb-admin.exe",
            @"php\php.exe",
            @"mailpit\mailpit.exe",
            @"versions\php\8.5.12\php.exe",
            @"versions\php\8.3.29\php.exe",
            @"versions\php\8.2.30\php.exe",
            @"versions\php\8.1.33\php.exe",
            @"versions\mariadb\11.8.3\bin\mariadbd.exe",
            @"versions\mariadb\11.4.8\bin\mariadbd.exe",
            @"versions\mariadb\10.11.14\bin\mariadbd.exe",
            @"htdocs\index.php",
            @"phpMyAdmin\index.php",
        ];
        string[] extensions =
        [
            "bz2", "curl", "exif", "fileinfo", "ftp", "gd", "gettext", "gmp", "imap", "intl", "ldap", "mbstring",
            "mysqli", "openssl", "pdo_mysql", "pdo_pgsql", "pdo_sqlite", "pgsql", "soap", "sockets", "sodium",
            "sqlite3", "tidy", "xdebug", "xsl", "zip",
        ];
        IEnumerable<string> extensionFiles = extensions
            .Where(name => HasXdebug || name != "xdebug")
            .Select(name => $@"php\ext\php_{name}.dll");
        foreach (string file in emptyFiles.Concat(extensionFiles))
        {
            Write(root, file, "");
        }

        Write(root, @"mailpit\version.txt", "1.33.0\r\n");
        Write(root, @"mailpit\mailpit.log", "level=info msg=\"[smtpd] starting on 127.0.0.1:1025\"\r\n");
        Write(root, @"apache\conf\httpd.conf",
            "ServerRoot \"C:/xampp/apache\"\r\nListen 80\r\nServerName localhost:80\r\n" +
            "DocumentRoot \"C:/xampp/htdocs\"\r\nInclude conf/extra/httpd-ssl.conf\r\n" +
            "Include conf/extra/httpd-vhosts.conf\r\n");
        Write(root, @"apache\conf\extra\httpd-ssl.conf",
            "Listen 443\r\n<VirtualHost _default_:443>\r\nServerName localhost:443\r\n</VirtualHost>\r\n");
        Write(root, @"apache\conf\extra\httpd-vhosts.conf", "# Virtual Hosts\r\n");
        Write(root, @"mysql\bin\my.ini",
            "[client]\r\nport=3306\r\n\r\n[mysqld]\r\nport=3306\r\ndatadir=\"C:/xampp/mysql/data\"\r\n" +
            "innodb_buffer_pool_size=512M\r\n");
        Write(root, @"php\php.ini", PhpIni(extensions));

        WriteLogs(root);
    }

    /// <summary>
    /// Writes the server logs: two days of Apache starts, PHP mistakes in the shop site, and a quiet MariaDB.
    /// </summary>
    /// <param name="root">The mock XAMPP folder.</param>
    private static void WriteLogs(string root)
    {
        DateTime today = DateTime.Today;
        DateTime yesterday = today.AddDays(-1);
        const string undefinedCart =
            "PHP Warning:  Undefined variable $cart in C:\\xampp\\htdocs\\shop\\checkout.php on line 42";
        Write(root, @"apache\logs\error.log", string.Concat(
            ApacheStart(yesterday.AddHours(11).AddMinutes(5), 22940),
            ApacheLine(yesterday.AddHours(13).AddMinutes(40), "php:warn", 22940, "[client ::1:50233] " + undefinedCart),
            ApacheLine(yesterday.AddHours(16).AddMinutes(12), "php:notice", 22940,
                "[client ::1:50871] PHP Deprecated:  Implicit conversion from float 19.99 to int loses precision in " +
                "C:\\xampp\\htdocs\\shop\\cart.php on line 77"),
            ApacheStart(today.AddHours(9).AddMinutes(17), 21704),
            ApacheLine(today.AddHours(9).AddMinutes(18), "php:error", 21704,
                "[client ::1:52114] PHP Fatal error:  Uncaught Error: Call to undefined function wc_get_cart() in " +
                "C:\\xampp\\htdocs\\shop\\cart.php:42"),
            ApacheLine(today.AddHours(9).AddMinutes(18).AddSeconds(2), "core:error", 21704,
                "[client ::1:52114] AH00124: Request exceeded the limit of 10 internal redirects due to probable " +
                "configuration error."),
            ApacheLine(
                today.AddHours(9).AddMinutes(21).AddSeconds(4), "php:warn", 21704, "[client ::1:51712] " + undefinedCart),
            ApacheLine(today.AddHours(9).AddMinutes(21).AddSeconds(9), "php:error", 21704,
                "[client ::1:51712] PHP Fatal error:  Uncaught PDOException: SQLSTATE[42S02]: Base table or view not " +
                "found: 1146 Table 'shop.coupons' doesn't exist in C:\\xampp\\htdocs\\shop\\lib\\Coupons.php:18"),
            ApacheLine(today.AddHours(9).AddMinutes(24).AddSeconds(31), "php:notice", 21704,
                "[client ::1:51790] PHP Deprecated:  Implicit conversion from float 19.99 to int loses precision in " +
                "C:\\xampp\\htdocs\\shop\\cart.php on line 77"),
            ApacheLine(today.AddHours(9).AddMinutes(40), "mpm_winnt:notice", 21704,
                "AH00422: Parent: Received shutdown signal -- Shutting down the server.")));

        string accessDay = today.ToString("dd/MMM/yyyy", CultureInfo.InvariantCulture);
        Write(root, @"apache\logs\access.log",
            $"::1 - - [{accessDay}:09:21:04 +0100] \"GET /shop/checkout.php HTTP/1.1\" 200 5123\r\n" +
            $"::1 - - [{accessDay}:09:21:09 +0100] \"POST /shop/checkout.php HTTP/1.1\" 500 312\r\n" +
            $"::1 - - [{accessDay}:09:22:40 +0100] \"GET /shop/favicon.ico HTTP/1.1\" 404 196\r\n" +
            $"::1 - - [{accessDay}:09:24:31 +0100] \"GET /shop/cart.php HTTP/1.1\" 200 4410\r\n");

        string sqlDay = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Write(root, @"mysql\data\mysql_error.log",
            $"{sqlDay}  9:17:49 0 [Note] InnoDB: Buffer pool(s) load completed\r\n" +
            $"{sqlDay}  9:17:49 0 [Note] Server socket created on IP: '::'.\r\n" +
            $"{sqlDay}  9:17:49 0 [Note] c:\\xampp\\mysql\\bin\\mariadbd.exe: ready for connections.\r\n" +
            "Version: '12.3.3-MariaDB'  socket: ''  port: 3306  mariadb.org binary distribution\r\n");

        string phpDay = today.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        Write(root, @"php\logs\php_error_log",
            $"[{phpDay} 09:21:04 Europe/Oslo] PHP Warning:  Undefined variable $cart in " +
            "C:\\xampp\\htdocs\\shop\\checkout.php on line 42\r\n" +
            $"[{phpDay} 09:24:31 Europe/Oslo] PHP Deprecated:  Implicit conversion from float 19.99 to int loses " +
            "precision in C:\\xampp\\htdocs\\shop\\cart.php on line 77\r\n");
    }

    /// <summary>The lines Apache writes to its error log when it starts.</summary>
    /// <param name="time">When it started.</param>
    /// <param name="pid">The process id in the lines.</param>
    /// <returns>The certificate warning every XAMPP has, and the two "started" notices.</returns>
    private static string ApacheStart(DateTime time, int pid) =>
        ApacheLine(time, "ssl:warn", pid,
            "AH01909: localhost:443:0 server certificate does NOT include an ID which matches the server name") +
        ApacheLine(time, "mpm_winnt:notice", pid,
            "AH00455: Apache/2.4.58 (Win64) OpenSSL/3.1.3 PHP/8.4.27 configured -- resuming normal operations") +
        ApacheLine(time.AddSeconds(1), "mpm_winnt:notice", pid, "AH00354: Child: Starting 150 worker threads.");

    /// <summary>One line in the format of Apache's error log.</summary>
    /// <param name="time">When it was written.</param>
    /// <param name="level">Module and level, e.g. "ssl:warn".</param>
    /// <param name="pid">The process id.</param>
    /// <param name="message">The rest of the line.</param>
    /// <returns>The line with its line break.</returns>
    private static string ApacheLine(DateTime time, string level, int pid, string message) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"[{time:ddd MMM dd HH:mm:ss.ffffff yyyy}] [{level}] [pid {pid}:tid 344] {message}\r\n");

    /// <summary>A small but realistic php.ini: the settings the PHP window edits and one line per extension.</summary>
    /// <param name="extensions">The extension names; the common ones are switched on.</param>
    /// <returns>The file's text.</returns>
    private static string PhpIni(IEnumerable<string> extensions)
    {
        HashSet<string> enabled =
        [
            "bz2", "curl", "exif", "fileinfo", "gd", "gettext", "intl", "mbstring", "mysqli", "openssl",
            "pdo_mysql", "pdo_sqlite", "sqlite3", "zip",
        ];
        IEnumerable<string> lines = extensions
            .Where(name => name != "xdebug")
            .Select(name => (enabled.Contains(name) ? "" : ";") + "extension=" + name);
        return "[PHP]\r\n" +
            "max_execution_time=120\r\nmax_input_time=60\r\nmax_input_vars=3000\r\nmemory_limit=512M\r\n" +
            "display_errors=On\r\npost_max_size=64M\r\nupload_max_filesize=64M\r\n" +
            "extension_dir=\"C:\\xampp\\php\\ext\"\r\n" +
            string.Join("\r\n", lines) + "\r\n" +
            (HasXdebug ? "zend_extension=xdebug\r\n\r\n" : "\r\n") +
            "[Date]\r\ndate.timezone=Europe/Oslo\r\n\r\n" +
            (HasXdebug
                ? "[xdebug]\r\nxdebug.mode=debug,develop\r\nxdebug.start_with_request=trigger\r\n" +
                  "xdebug.client_port=9003\r\n\r\n"
                : "") +
            "[mail function]\r\nsendmail_path=\"C:\\xampp\\mailpit\\mailpit.exe sendmail -S 127.0.0.1:1025\"\r\n";
    }

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
        File.WriteAllText(path, content);
    }
}
