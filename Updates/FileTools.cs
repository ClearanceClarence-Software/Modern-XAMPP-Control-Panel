using System.IO.Compression;

namespace XamppControl.Updates;

/// <summary>File and folder helpers for unpacking builds and carrying files over from the previous install.</summary>
public static class FileTools
{
    private const long BytesPerGigabyte = 1L << 30;
    private const long BytesPerMegabyte = 1L << 20;

    /// <summary>
    /// Extracts a zip into a fresh staging folder and returns the folder holding the actual files
    /// (zips like mariadb-12.3.3-winx64.zip wrap everything in one top folder).
    /// </summary>
    /// <param name="zip">Full path of the zip file.</param>
    /// <param name="staging">The folder to extract into; deleted first when it exists.</param>
    /// <returns>
    /// The zip's only top-level folder when nothing else is at the top; otherwise <paramref name="staging"/>.
    /// </returns>
    public static string ExtractToStaging(string zip, string staging)
    {
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }
        Directory.CreateDirectory(staging);

        ZipFile.ExtractToDirectory(zip, staging, overwriteFiles: true);

        string[] topFolders = Directory.GetDirectories(staging);
        string[] topFiles = Directory.GetFiles(staging);
        bool isWrappedInOneFolder = topFiles.Length == 0 && topFolders.Length == 1;
        return isWrappedInOneFolder ? topFolders[0] : staging;
    }

    /// <summary>
    /// Copies every top-level file and folder in <paramref name="from"/> that doesn't exist in <paramref name="to"/>.
    /// </summary>
    /// <param name="from">The folder to copy from, usually the previous install.</param>
    /// <param name="to">The folder to fill in; entries it already has, as a file or a folder, are left alone.</param>
    /// <returns>The names (not paths) of the top-level entries copied; empty when nothing was missing.</returns>
    public static List<string> CopyMissing(string from, string to)
    {
        var copied = new List<string>();
        foreach (string entry in Directory.EnumerateFileSystemEntries(from))
        {
            string name = Path.GetFileName(entry);
            string target = Path.Combine(to, name);
            if (File.Exists(target) || Directory.Exists(target))
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                CopyDirectory(entry, target);
            }
            else
            {
                CopyFile(entry, target);
            }
            copied.Add(name);
        }
        return copied;
    }

    /// <summary>
    /// For folders that exist in both places, copies files the new side doesn't have (never overwrites).
    /// Skips the given top-level folders (e.g. PHP's ext, whose DLLs are version-specific).
    /// </summary>
    /// <param name="from">The folder to copy from, usually the previous install.</param>
    /// <param name="to">The new install; only its existing top-level folders receive files.</param>
    /// <param name="skipTopLevel">Top-level folder names to leave out, compared case-insensitively.</param>
    /// <returns>The paths of the files added, relative to both folders; empty when nothing was missing.</returns>
    public static List<string> MergeMissingFiles(string from, string to, params string[] skipTopLevel)
    {
        var added = new List<string>();
        foreach (string sourceFolder in Directory.EnumerateDirectories(from))
        {
            string name = Path.GetFileName(sourceFolder);
            string targetFolder = Path.Combine(to, name);
            if (!Directory.Exists(targetFolder) || skipTopLevel.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(from, file);
                string targetFile = Path.Combine(to, relativePath);
                if (File.Exists(targetFile))
                {
                    continue;
                }

                string targetFileFolder = Path.GetDirectoryName(targetFile)
                    ?? throw new InvalidOperationException($"\"{targetFile}\" has no parent folder.");
                Directory.CreateDirectory(targetFileFolder);
                CopyFile(file, targetFile);
                added.Add(relativePath);
            }
        }
        return added;
    }

    /// <summary>Copies a folder tree, overwriting files that exist and clearing read-only on every copy.</summary>
    /// <param name="from">The folder to copy, including all subfolders.</param>
    /// <param name="to">The destination folder; created when missing, and files it already has are replaced.</param>
    public static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string folder in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, folder)));
        }
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            CopyFile(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }

    private static void CopyFile(string from, string to)
    {
        File.Copy(from, to, overwrite: true);
        ClearReadOnly(to);
    }

    /// <summary>Clears read-only on a file, or on every file below a folder, so it can be moved or deleted.</summary>
    /// <param name="path">A file or a folder; folders keep their own attributes, and a missing path is ignored.</param>
    public static void ClearReadOnly(string path)
    {
        if (Directory.Exists(path))
        {
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                ClearReadOnly(file);
            }
            return;
        }

        if (!File.Exists(path))
        {
            return;
        }

        FileAttributes attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReadOnly))
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
    }

    /// <summary>Deletes a folder tree when it exists, never throwing.</summary>
    /// <param name="folder">The folder to delete; a missing folder is ignored.</param>
    public static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Leftover staging folders are harmless: the next update deletes the folder before extracting again.
        }
    }

    /// <summary>The first of several candidate files that exists in a folder.</summary>
    /// <param name="folder">The folder to look in; subfolders are not searched.</param>
    /// <param name="names">File names in order of preference.</param>
    /// <returns>The full path of the first existing file; null when none exists.</returns>
    public static string? FirstExisting(string folder, params string[] names) =>
        names.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);

    /// <summary>"8.4.10" → "8.4.10", "unknown version" → "unknown_version" (safe in folder names).</summary>
    /// <param name="text">Any text, typically a version.</param>
    /// <returns>The text with every character other than a letter, digit or dot replaced by an underscore.</returns>
    public static string Safe(string text) =>
        new(text.Select(character => char.IsLetterOrDigit(character) || character == '.' ? character : '_').ToArray());

    /// <summary>The current local time as "yyyyMMdd-HHmmss", for unique file and folder names.</summary>
    /// <returns>A timestamp that sorts in time order.</returns>
    public static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

    /// <summary>A byte count for the log, in GB or MB with one decimal, or whole KB below 1 MB.</summary>
    /// <param name="bytes">The size in bytes; the units are binary (1 KB is 1024 bytes).</param>
    /// <returns>The size with its unit, e.g. "1.5 GB", "12.0 MB" or "640 KB".</returns>
    public static string FormatSize(long bytes)
    {
        if (bytes >= BytesPerGigabyte)
        {
            return $"{bytes / (double)BytesPerGigabyte:0.0} GB";
        }
        if (bytes >= BytesPerMegabyte)
        {
            return $"{bytes / (double)BytesPerMegabyte:0.0} MB";
        }
        return $"{bytes / 1024.0:0} KB";
    }
}
