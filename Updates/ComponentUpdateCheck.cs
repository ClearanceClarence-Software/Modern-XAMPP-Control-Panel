using XamppControl.Core;
using XamppControl.Php;

namespace XamppControl.Updates;

/// <summary>A newer build of something XAMPP runs, within the branch or series that is installed.</summary>
/// <param name="Component">What it is: "PHP", "MariaDB", "Mailpit" or "Xdebug".</param>
/// <param name="Module">
/// The main window row it belongs to: "Apache" for PHP and Xdebug, "MySQL" for MariaDB, "Mailpit".
/// </param>
/// <param name="Installed">The active version, e.g. "8.5.12".</param>
/// <param name="Latest">The newest version of the same branch or series, e.g. "8.5.13".</param>
public sealed record ComponentUpdate(string Component, string Module, string Installed, string Latest)
{
    /// <summary>
    /// What the update is for: "PHP 8.5" or "MariaDB 12.3" (the installed branch), or just "Mailpit" or "Xdebug".
    /// </summary>
    public string Label => Component is "PHP" or "MariaDB"
        ? $"{Component} {VersionUtil.MajorMinor(Installed)}"
        : Component;

    /// <summary>True for Xdebug, which is installed from the PHP window; everything else is in Versions.</summary>
    public bool IsInPhpWindow => Component == "Xdebug";
}

/// <summary>The newest versions the release feeds reported; a null entry means that feed could not be read.</summary>
/// <param name="Php">The newest PHP build of the installed branch.</param>
/// <param name="PhpNewBranch">
/// The newest build of a PHP branch above the installed one ("8.6.1" when 8.5 is installed); null when the
/// installed branch is the newest.
/// </param>
/// <param name="MariaDb">The newest MariaDB release of the installed series.</param>
/// <param name="Mailpit">The newest Mailpit release.</param>
/// <param name="Xdebug">The newest Xdebug that has a build for the active PHP.</param>
public sealed record LatestComponents(
    string? Php,
    string? PhpNewBranch,
    string? MariaDb,
    string? Mailpit,
    string? Xdebug);

/// <summary>
/// Looks for newer builds of the PHP, MariaDB, Mailpit and Xdebug that are installed, so the main window can say so
/// without the Versions window being opened. An update is always within the installed branch or series (PHP 8.5
/// gets 8.5.x); a whole new PHP branch (8.6, 9.0) is reported separately, as news and not as an update.
/// </summary>
public static class ComponentUpdateCheck
{
    /// <summary>Asks the release feeds for the newest version of each installed component.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <returns>The newest versions; a feed that is unreachable or has nothing for this branch gives null.</returns>
    public static async Task<LatestComponents> FetchAsync(string root)
    {
        string? php = null;
        string? phpNewBranch = null;
        string? mariaDb = null;
        string? mailpit = null;
        string? xdebug = null;

        if (PhpVersion(root) is { } phpInstalled)
        {
            // One request answers both: the installed branch's newest build, and whether a newer branch exists.
            string branch = VersionUtil.MajorMinor(phpInstalled);
            List<PhpBranch> branches = await TryAsync(ReleaseFeeds.GetPhpBranchesAsync) ?? [];
            php = branches.FirstOrDefault(found => found.Branch == branch)?.Version;
            phpNewBranch = branches
                .Where(found => VersionUtil.Parse(found.Branch) > VersionUtil.Parse(branch))
                .OrderByDescending(found => VersionUtil.Parse(found.Branch))
                .FirstOrDefault()?.Version;
        }

        if (MariaDbVersion(root) is { } mariaInstalled)
        {
            string series = VersionUtil.MajorMinor(mariaInstalled);
            mariaDb = (await TryAsync(() => ReleaseFeeds.GetMariaDbLatestAsync(series)))?.Version;
        }

        if (MailpitUpdater.InstalledVersion(root) is not null)
        {
            mailpit = (await TryAsync(MailpitUpdater.GetLatestAsync))?.Version;
        }

        string phpDir = Path.Combine(root, "php");
        if (XdebugInstaller.InstalledVersion(phpDir) is not null)
        {
            xdebug = await TryAsync(() => LatestXdebugAsync(phpDir));
        }

        return new LatestComponents(php, phpNewBranch, mariaDb, mailpit, xdebug);
    }

    /// <summary>Compares what is installed now with what the feeds reported.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="latest">The result of <see cref="FetchAsync"/>.</param>
    /// <returns>One entry per component with a newer version, in the order PHP, MariaDB, Mailpit, Xdebug.</returns>
    public static List<ComponentUpdate> Compare(string root, LatestComponents latest)
    {
        ComponentUpdate?[] found =
        [
            // The feeds were asked for the branch that was active then; after a switch to another branch the
            // answer is about a different PHP or MariaDB and must not be offered as an update of this one.
            Newer("PHP", "Apache", PhpVersion(root), latest.Php, isSameBranchOnly: true),
            Newer("MariaDB", "MySQL", MariaDbVersion(root), latest.MariaDb, isSameBranchOnly: true),
            Newer("Mailpit", "Mailpit", MailpitUpdater.InstalledVersion(root), latest.Mailpit),
            Newer("Xdebug", "Apache", XdebugInstaller.InstalledVersion(Path.Combine(root, "php")), latest.Xdebug),
        ];
        return found.OfType<ComponentUpdate>().ToList();
    }

    /// <summary>An update entry when the latest version is newer than the installed one.</summary>
    /// <param name="component">The component's name.</param>
    /// <param name="module">The main window row it belongs to.</param>
    /// <param name="installed">The installed version; null or unreadable gives no entry.</param>
    /// <param name="latest">The newest version; null gives no entry.</param>
    /// <param name="isSameBranchOnly">
    /// True reports only a newer build of the installed branch: 8.5.13 for 8.5.12, never 8.6.0.
    /// </param>
    /// <returns>The entry, or null when there is nothing newer or a version is unknown.</returns>
    internal static ComponentUpdate? Newer(
        string component,
        string module,
        string? installed,
        string? latest,
        bool isSameBranchOnly = false)
    {
        if (isSameBranchOnly && VersionUtil.MajorMinor(installed) != VersionUtil.MajorMinor(latest))
        {
            return null;
        }

        // An unreadable version parses as 0.0; without a real one there is nothing to compare.
        Version have = VersionUtil.Parse(installed);
        Version newest = VersionUtil.Parse(latest);
        bool isKnown = have > new Version(0, 0) && newest > new Version(0, 0);
        return isKnown && newest > have && installed is not null && latest is not null
            ? new ComponentUpdate(component, module, installed, latest)
            : null;
    }

    /// <summary>The newest Xdebug that PECL has built for the active PHP.</summary>
    /// <param name="phpDir">XAMPP's php folder.</param>
    /// <returns>The version, or null when PHP can't be read or no release has a build for it.</returns>
    private static async Task<string?> LatestXdebugAsync(string phpDir) =>
        await XdebugInstaller.ReadPhpBuildAsync(phpDir) is { } php
            ? await XdebugInstaller.FindBuildAsync(await XdebugInstaller.ListVersionsAsync(), php)
            : null;

    private static string? PhpVersion(string root) => XamppPaths.FileVersion(Path.Combine(root, "php", "php.exe"));

    private static string? MariaDbVersion(string root) => XamppPaths.FileVersion(
        FileTools.FirstExisting(Path.Combine(root, "mysql", "bin"), "mariadbd.exe", "mysqld.exe"));

    /// <summary>Reads one feed; being offline, or a series the feed no longer lists, is not an error here.</summary>
    /// <typeparam name="TResult">What the feed returns.</typeparam>
    /// <param name="read">The feed request.</param>
    /// <returns>What the feed gave, or null when it could not be read.</returns>
    private static async Task<TResult?> TryAsync<TResult>(Func<Task<TResult>> read)
        where TResult : class?
    {
        try
        {
            return await read();
        }
        catch (Exception)
        {
            // A background check that runs at startup and once a day: whatever a feed, the network or php.exe
            // does wrong, the panel must keep running. A local server panel is often offline; the check then
            // simply finds nothing this time.
            return null;
        }
    }
}
