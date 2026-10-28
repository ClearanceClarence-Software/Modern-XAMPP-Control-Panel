using System.IO.Compression;

namespace XamppControl.Updates;

public static class FileTools
{
    /// <summary>
    /// Extracts a zip into a fresh staging folder and returns the folder holding the actual files
    /// (zips like mariadb-12.3.3-winx64.zip wrap everything in one top folder).
    /// </summary>
    public static string ExtractToStaging(string zip, string staging)
    {
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        ZipFile.ExtractToDirectory(zip, staging, overwriteFiles: true);

        var dirs = Directory.GetDirectories(staging);
        var files = Directory.GetFiles(staging);
        return files.Length == 0 && dirs.Length == 1 ? dirs[0] : staging;
    }

    /// <summary>
    /// Copies every top-level file and folder in <paramref name="from"/> that doesn't exist in <paramref name="to"/>.
    /// </summary>
    public static List<string> CopyMissing(string from, string to)
    {
        var copied = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(from))
        {
            var name = Path.GetFileName(entry);
            var target = Path.Combine(to, name);
            if (File.Exists(target) || Directory.Exists(target))
                continue;

            if (Directory.Exists(entry))
                CopyDirectory(entry, target);
            else
                CopyFile(entry, target);
            copied.Add(name);
        }
        return copied;
    }

    /// <summary>
    /// For folders that exist in both places, copies files the new side doesn't have (never overwrites).
    /// Skips the given top-level folders (e.g. PHP's ext, whose DLLs are version-specific).
    /// Returns the relative paths of the files added.
    /// </summary>
    public static List<string> MergeMissingFiles(string from, string to, params string[] skipTopLevel)
    {
        var added = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(from))
        {
            var name = Path.GetFileName(dir);
            var targetDir = Path.Combine(to, name);
            if (!Directory.Exists(targetDir) || skipTopLevel.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(from, file);
                var target = Path.Combine(to, relative);
                if (File.Exists(target))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                CopyFile(file, target);
                added.Add(relative);
            }
        }
        return added;
    }

    public static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            CopyFile(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }

    private static void CopyFile(string from, string to)
    {
        File.Copy(from, to, overwrite: true);
        ClearReadOnly(to);
    }

    public static void ClearReadOnly(string path)
    {
        if (Directory.Exists(path))
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                ClearReadOnly(file);
            return;
        }

        if (!File.Exists(path))
            return;
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReadOnly))
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    public static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Leftover staging folders are harmless.
        }
    }

    public static string? FirstExisting(string folder, params string[] names) =>
        names.Select(n => Path.Combine(folder, n)).FirstOrDefault(File.Exists);

    /// <summary>"8.4.10" → "8.4.10", "unknown version" → "unknown_version" (safe in folder names).</summary>
    public static string Safe(string text) =>
        new(text.Select(c => char.IsLetterOrDigit(c) || c == '.' ? c : '_').ToArray());

    public static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

    public static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB"
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB"
        : $"{bytes / 1024.0:0} KB";
}
