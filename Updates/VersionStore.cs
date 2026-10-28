using System.Diagnostics;
using XamppControl.Core;

namespace XamppControl.Updates;

public enum Component
{
    Php,
    MariaDb,
}

/// <summary>An installed PHP or MariaDB build: the active one (php\ or mysql\) or one parked in versions\.</summary>
public sealed record InstalledVersion(Component Component, string Version, string Path, bool IsActive)
{
    public string Title => $"{(Component == Component.Php ? "PHP" : "MariaDB")} {Version}";
    public string Detail { get; init; } = "";
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
    public static string ActiveDir(string root, Component c) =>
        Path.Combine(root, c == Component.Php ? "php" : "mysql");

    public static string StoreDir(string root, Component c) =>
        Path.Combine(root, "versions", c == Component.Php ? "php" : "mariadb");

    public static string? DetectVersion(Component c, string dir)
    {
        if (!Directory.Exists(dir))
            return null;
        return c == Component.Php
            ? XamppPaths.FileVersion(Path.Combine(dir, "php.exe"))
            : XamppPaths.FileVersion(FileTools.FirstExisting(Path.Combine(dir, "bin"), "mariadbd.exe", "mysqld.exe"));
    }

    public static List<InstalledVersion> List(string root, Component c)
    {
        var list = new List<InstalledVersion>();
        var active = ActiveDir(root, c);
        if (DetectVersion(c, active) is { } activeVersion)
        {
            list.Add(new InstalledVersion(c, activeVersion, active, IsActive: true)
            {
                Detail = Relative(root, active),
            });
        }

        var store = StoreDir(root, c);
        if (Directory.Exists(store))
        {
            foreach (var dir in Directory.GetDirectories(store))
            {
                if (DetectVersion(c, dir) is { } version)
                    list.Add(new InstalledVersion(c, version, dir, IsActive: false) { Detail = Relative(root, dir) });
            }
        }

        return list
            .OrderByDescending(v => v.IsActive)
            .ThenByDescending(v => VersionUtil.Parse(v.Version))
            .ToList();
    }

    public static InstalledVersion? Find(string root, Component c, string version) =>
        List(root, c).FirstOrDefault(v => VersionUtil.Parse(v.Version) == VersionUtil.Parse(version));

    /// <summary>A free folder in the store for a version: versions\php\8.4.10, or 8.4.10-2 if taken.</summary>
    public static string ParkPath(string root, Component c, string version)
    {
        var store = StoreDir(root, c);
        Directory.CreateDirectory(store);
        var name = FileTools.Safe(version);
        var path = Path.Combine(store, name);
        for (var i = 2; Directory.Exists(path) || File.Exists(path); i++)
            path = Path.Combine(store, $"{name}-{i}");
        return path;
    }

    /// <summary>
    /// Moves backup folders made by earlier versions of the updater (php_backup_*, mysql_backup_*) into the store.
    /// </summary>
    public static List<string> ImportLegacyBackups(string root)
    {
        var moved = new List<string>();
        foreach (var (pattern, c) in new[]
                 {
                     ("php_backup_*", Component.Php), ("php_replaced_*", Component.Php),
                     ("mysql_backup_*", Component.MariaDb), ("mysql_replaced_*", Component.MariaDb),
                 })
        {
            if (!Directory.Exists(root))
                break;
            foreach (var dir in Directory.GetDirectories(root, pattern))
            {
                if (DetectVersion(c, dir) is not { } version)
                    continue;
                try
                {
                    Directory.Move(dir, ParkPath(root, c, version));
                    moved.Add(Path.GetFileName(dir));
                }
                catch
                {
                    // In use; try again next time.
                }
            }
        }
        return moved;
    }

    /// <summary>Moves an existing folder (e.g. C:\xampp\mysql_old) into the store. Returns the version.</summary>
    public static string Import(string root, Component c, string folder)
    {
        var version = DetectVersion(c, folder)
                      ?? throw new InvalidOperationException(c == Component.Php
                          ? "That folder has no php.exe."
                          : "That folder has no bin\\mariadbd.exe or bin\\mysqld.exe.");

        var full = Path.GetFullPath(folder).TrimEnd('\\', '/');
        if (string.Equals(full, Path.GetFullPath(ActiveDir(root, c)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That's the active install already.");
        if (full.StartsWith(Path.GetFullPath(StoreDir(root, c)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That folder is already in the version store.");

        Directory.Move(full, ParkPath(root, c, version));
        return version;
    }

    public static void Remove(InstalledVersion v)
    {
        if (v.IsActive)
            throw new InvalidOperationException("Switch to another version before removing the active one.");
        FileTools.ClearReadOnly(v.Path);
        Directory.Delete(v.Path, recursive: true);
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path) is var rel && !rel.StartsWith("..", StringComparison.Ordinal) ? rel : path;
}

/// <summary>
/// Apache loads its own copy of DLLs that PHP also uses (OpenSSL, libssh2, nghttp2, …). Both need a version
/// at least as new as what they were built against, so for each shared DLL the newer of PHP's copy and
/// Apache's original (kept as .bak) goes into apache\bin.
/// </summary>
public static class DllSync
{
    public static void Sync(UpdateContext ctx, string phpDir, string apacheBin)
    {
        if (!Directory.Exists(apacheBin) || !Directory.Exists(phpDir))
            return;

        var fromPhp = new List<string>();
        var kept = new List<string>();
        foreach (var phpDll in Directory.GetFiles(phpDir, "*.dll"))
        {
            var name = Path.GetFileName(phpDll);
            var target = Path.Combine(apacheBin, name);
            if (!File.Exists(target))
                continue;

            var original = target + ".bak";
            if (!File.Exists(original))
                File.Copy(target, original); // Apache's own copy, kept once

            var useOriginal = FileVersionOf(original) > FileVersionOf(phpDll);
            File.Copy(useOriginal ? original : phpDll, target, overwrite: true);
            (useOriginal ? kept : fromPhp).Add(name);
        }

        if (fromPhp.Count > 0)
            ctx.Log($"apache\\bin now uses PHP's copy of: {string.Join(", ", fromPhp)}");
        if (kept.Count > 0)
            ctx.Log($"apache\\bin keeps its own newer copy of: {string.Join(", ", kept)}");
    }

    private static Version FileVersionOf(string file)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(file);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch
        {
            return new Version(0, 0);
        }
    }
}
