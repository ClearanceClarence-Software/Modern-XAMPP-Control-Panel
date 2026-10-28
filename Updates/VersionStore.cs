using System.Diagnostics;
using XamppControl.Core;

namespace XamppControl.Updates;

/// <summary>The server components whose builds the version store keeps side by side.</summary>
public enum Component
{
    Php,
    MariaDb,
}

/// <summary>An installed PHP or MariaDB build: the active one (php\ or mysql\) or one parked in versions\.</summary>
/// <param name="Component">Which server the build is.</param>
/// <param name="Version">The version read from php.exe or the MariaDB server exe, e.g. "8.4.27".</param>
/// <param name="Path">Full path of the build's folder.</param>
/// <param name="IsActive">True for the build in php\ or mysql\; false for one parked in the store.</param>
public sealed record InstalledVersion(Component Component, string Version, string Path, bool IsActive)
{
    /// <summary>The server and version for the list, e.g. "PHP 8.4.27".</summary>
    public string Title => $"{(Component == Component.Php ? "PHP" : "MariaDB")} {Version}";

    /// <summary>The folder relative to the XAMPP root, or its full path when outside; empty when not set.</summary>
    public string Detail { get; init; } = "";

    /// <summary>True when the build is parked and can be switched to.</summary>
    public bool CanSwitch => !IsActive;
}

/// <summary>
/// Keeps several PHP and MariaDB builds side by side. The active build stays at C:\xampp\php or
/// C:\xampp\mysql (so httpd-xampp.conf and my.ini paths never change); the others live in
/// C:\xampp\versions\php\&lt;version&gt; and C:\xampp\versions\mariadb\&lt;version&gt;. Switching swaps folders.
/// Each MariaDB build keeps its own data folder.
/// </summary>
public static class VersionStore
{
    /// <summary>The folder XAMPP runs a component from.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="component">The server whose folder to name.</param>
    /// <returns>root\php or root\mysql; the folder may not exist.</returns>
    public static string ActiveDir(string root, Component component) =>
        Path.Combine(root, component == Component.Php ? "php" : "mysql");

    /// <summary>The folder that holds a component's parked builds.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="component">The server whose store to name.</param>
    /// <returns>root\versions\php or root\versions\mariadb; the folder may not exist.</returns>
    public static string StoreDir(string root, Component component) =>
        Path.Combine(root, "versions", component == Component.Php ? "php" : "mariadb");

    /// <summary>Reads the version of the build in a folder from its executable.</summary>
    /// <param name="component">
    /// Which server the folder holds: PHP reads php.exe, MariaDB bin\mariadbd.exe or bin\mysqld.exe.
    /// </param>
    /// <param name="folder">The build's top folder.</param>
    /// <returns>The version text; null when the folder or executable is missing or has no readable version.</returns>
    public static string? DetectVersion(Component component, string folder)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        if (component == Component.Php)
        {
            return XamppPaths.FileVersion(Path.Combine(folder, "php.exe"));
        }
        string? serverExe = FileTools.FirstExisting(Path.Combine(folder, "bin"), "mariadbd.exe", "mysqld.exe");
        return XamppPaths.FileVersion(serverExe);
    }

    /// <summary>Every recognizable build of a component, active and parked.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="component">The server to list.</param>
    /// <returns>
    /// The active build first, then parked builds newest first; folders without a readable version are left out.
    /// </returns>
    public static List<InstalledVersion> List(string root, Component component)
    {
        var versions = new List<InstalledVersion>();
        string activeDir = ActiveDir(root, component);
        if (DetectVersion(component, activeDir) is { } activeVersion)
        {
            versions.Add(new InstalledVersion(component, activeVersion, activeDir, IsActive: true)
            {
                Detail = Relative(root, activeDir),
            });
        }

        string storeDir = StoreDir(root, component);
        if (Directory.Exists(storeDir))
        {
            foreach (string folder in Directory.GetDirectories(storeDir))
            {
                if (DetectVersion(component, folder) is { } version)
                {
                    versions.Add(new InstalledVersion(component, version, folder, IsActive: false)
                    {
                        Detail = Relative(root, folder),
                    });
                }
            }
        }

        return versions
            .OrderByDescending(installed => installed.IsActive)
            .ThenByDescending(installed => VersionUtil.Parse(installed.Version))
            .ToList();
    }

    /// <summary>The installed build of a component with a given version, active or parked.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="component">The server to look for.</param>
    /// <param name="version">Text holding the version; only its dotted number is compared.</param>
    /// <returns>The active build when it matches, else the newest matching parked one; null if none.</returns>
    public static InstalledVersion? Find(string root, Component component, string version) =>
        List(root, component)
            .FirstOrDefault(installed => VersionUtil.Parse(installed.Version) == VersionUtil.Parse(version));

    /// <summary>A free folder in the store for a version: versions\php\8.4.10, or 8.4.10-2 if taken.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="component">The server whose store to use; the store folder is created when missing.</param>
    /// <param name="version">The version to name the folder after; characters unsafe in a name become "_".</param>
    /// <returns>Full path of a folder that does not exist yet.</returns>
    public static string ParkPath(string root, Component component, string version)
    {
        string storeDir = StoreDir(root, component);
        Directory.CreateDirectory(storeDir);

        string name = FileTools.Safe(version);
        string path = Path.Combine(storeDir, name);
        for (int i = 2; Directory.Exists(path) || File.Exists(path); i++)
        {
            path = Path.Combine(storeDir, $"{name}-{i}");
        }
        return path;
    }

    /// <summary>
    /// Moves backup folders made by earlier versions of the updater (php_backup_*, mysql_backup_*) into the store.
    /// </summary>
    /// <param name="root">The XAMPP folder, where the old updater left its backups.</param>
    /// <returns>The names of the folders moved; empty when there were none or the root does not exist.</returns>
    public static List<string> ImportLegacyBackups(string root)
    {
        var moved = new List<string>();
        if (!Directory.Exists(root))
        {
            return moved;
        }

        (string, Component)[] legacyFolders =
        [
            ("php_backup_*", Component.Php),
            ("php_replaced_*", Component.Php),
            ("mysql_backup_*", Component.MariaDb),
            ("mysql_replaced_*", Component.MariaDb),
        ];
        foreach ((string pattern, Component component) in legacyFolders)
        {
            foreach (string folder in Directory.GetDirectories(root, pattern))
            {
                if (DetectVersion(component, folder) is not { } version)
                {
                    continue;
                }

                try
                {
                    Directory.Move(folder, ParkPath(root, component, version));
                    moved.Add(Path.GetFileName(folder));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // In use; the folder stays where it is and the next refresh tries again.
                }
            }
        }
        return moved;
    }

    /// <summary>Moves an existing folder (e.g. C:\xampp\mysql_old) into the store. Returns the version.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="component">Which server the folder holds.</param>
    /// <param name="folder">The build's top folder, anywhere on the same drive as the store.</param>
    /// <returns>The version read from the folder's executable.</returns>
    /// <exception cref="InvalidOperationException">
    /// The folder has no server executable, is the active install, or is already in the store.
    /// </exception>
    /// <exception cref="IOException">The folder is in use or on another drive than the store.</exception>
    public static string Import(string root, Component component, string folder)
    {
        string version = DetectVersion(component, folder)
            ?? throw new InvalidOperationException(component == Component.Php
                ? "That folder has no php.exe."
                : "That folder has no bin\\mariadbd.exe or bin\\mysqld.exe.");

        string fullPath = Path.GetFullPath(folder).TrimEnd('\\', '/');
        if (string.Equals(fullPath, Path.GetFullPath(ActiveDir(root, component)), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("That's the active install already.");
        }
        if (fullPath.StartsWith(Path.GetFullPath(StoreDir(root, component)), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("That folder is already in the version store.");
        }

        Directory.Move(fullPath, ParkPath(root, component, version));
        return version;
    }

    /// <summary>Deletes a parked build's folder, read-only files included.</summary>
    /// <param name="version">The build to delete; for MariaDB its databases go with it.</param>
    /// <exception cref="InvalidOperationException">The build is the active one.</exception>
    /// <exception cref="IOException">A file in the folder is in use.</exception>
    public static void Remove(InstalledVersion version)
    {
        if (version.IsActive)
        {
            throw new InvalidOperationException("Switch to another version before removing the active one.");
        }

        FileTools.ClearReadOnly(version.Path);
        Directory.Delete(version.Path, recursive: true);
    }

    /// <summary>A path relative to the XAMPP folder for display, or the path itself when it lies outside.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="path">A full path.</param>
    /// <returns>The relative path; <paramref name="path"/> unchanged when not below <paramref name="root"/>.</returns>
    private static string Relative(string root, string path)
    {
        string relativePath = Path.GetRelativePath(root, path);
        return relativePath.StartsWith("..", StringComparison.Ordinal) ? path : relativePath;
    }
}

/// <summary>
/// Apache loads its own copy of DLLs that PHP also uses (OpenSSL, libssh2, nghttp2, …). Both need a version
/// at least as new as what they were built against, so for each shared DLL the newer of PHP's copy and
/// Apache's original (kept as .bak) goes into apache\bin.
/// </summary>
public static class DllSync
{
    /// <summary>Puts the newer copy of each DLL that PHP and Apache share into apache\bin and logs which won.</summary>
    /// <param name="ctx">Receives one log line per outcome.</param>
    /// <param name="phpDir">The active PHP folder, whose top-level DLLs are compared.</param>
    /// <param name="apacheBin">
    /// Apache's bin folder; nothing is done when it or <paramref name="phpDir"/> is missing.
    /// </param>
    public static void Sync(UpdateContext ctx, string phpDir, string apacheBin)
    {
        if (!Directory.Exists(apacheBin) || !Directory.Exists(phpDir))
        {
            return;
        }

        var fromPhp = new List<string>();
        var kept = new List<string>();
        foreach (string phpDll in Directory.GetFiles(phpDir, "*.dll"))
        {
            string name = Path.GetFileName(phpDll);
            string target = Path.Combine(apacheBin, name);
            if (!File.Exists(target))
            {
                continue;
            }

            // Apache's own copy is saved once, so later syncs still compare against the original.
            string original = target + ".bak";
            if (!File.Exists(original))
            {
                File.Copy(target, original);
            }

            bool keepApacheCopy = FileVersionOf(original) > FileVersionOf(phpDll);
            File.Copy(keepApacheCopy ? original : phpDll, target, overwrite: true);
            if (keepApacheCopy)
            {
                kept.Add(name);
            }
            else
            {
                fromPhp.Add(name);
            }
        }

        if (fromPhp.Count > 0)
        {
            ctx.Log($"apache\\bin now uses PHP's copy of: {string.Join(", ", fromPhp)}");
        }
        if (kept.Count > 0)
        {
            ctx.Log($"apache\\bin keeps its own newer copy of: {string.Join(", ", kept)}");
        }
    }

    /// <summary>The four-part file version from a DLL's version resource.</summary>
    /// <param name="file">Full path of the DLL.</param>
    /// <returns>The file version; 0.0 when it cannot be read, so any readable version counts as newer.</returns>
    private static Version FileVersionOf(string file)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(file);
            return new Version(
                versionInfo.FileMajorPart,
                versionInfo.FileMinorPart,
                versionInfo.FileBuildPart,
                versionInfo.FilePrivatePart);
        }
        catch
        {
            // Reading another program's version resource may fail in many ways; treating it as 0.0 lets the sync
            // carry on, preferring whichever copy has a readable version.
            return new Version(0, 0);
        }
    }
}
