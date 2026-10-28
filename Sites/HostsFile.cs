using System.Text;
using XamppControl.Core;
using XamppControl.Updates;

namespace XamppControl.Sites;

/// <summary>
/// Points the sites' host names at this computer in the Windows hosts file, inside a marked block. Writing the file
/// needs admin rights, so the new file is prepared here and copied into place with one UAC prompt.
/// </summary>
public static class HostsFile
{
    private const string BlockStart = "# BEGIN Modern XAMPP Control Panel sites";
    private const string BlockEnd = "# END Modern XAMPP Control Panel sites";

    /// <summary>Full path of the Windows hosts file.</summary>
    public static string HostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers",
        "etc",
        "hosts");

    /// <summary>The hosts file as it would be with the given host names in the panel's block.</summary>
    /// <param name="current">The hosts file's current text; empty when it does not exist.</param>
    /// <param name="hostNames">The sites' host names; an empty list removes the block.</param>
    /// <returns>The new text, with the block at the end and Windows line endings.</returns>
    public static string WithHosts(string current, IReadOnlyList<string> hostNames)
    {
        List<string> lines = [.. current.Replace("\r\n", "\n").Split('\n')];
        int start = lines.FindIndex(line => line.Trim() == BlockStart);
        int end = lines.FindIndex(line => line.Trim() == BlockEnd);
        if (start >= 0 && end > start)
        {
            lines.RemoveRange(start, end - start + 1);
        }

        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (hostNames.Count > 0)
        {
            lines.Add("");
            lines.Add(BlockStart);
            foreach (string hostName in hostNames)
            {
                lines.Add($"127.0.0.1\t{hostName}");
                lines.Add($"::1\t{hostName}");
            }
            lines.Add(BlockEnd);
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>The host names in the panel's block, in order, each listed once.</summary>
    /// <param name="hostsText">The hosts file's text.</param>
    /// <returns>The names; empty when the file has no block.</returns>
    private static List<string> HostsInBlock(string hostsText)
    {
        List<string> lines = [.. hostsText.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim())];
        int start = lines.IndexOf(BlockStart);
        int end = lines.IndexOf(BlockEnd);
        if (start < 0 || end < start)
        {
            return [];
        }

        return lines
            .Skip(start + 1)
            .Take(end - start - 1)
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2)
            .Select(parts => parts[1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Writes the host names into the hosts file when they changed, then flushes DNS.</summary>
    /// <param name="hostNames">The sites' host names; an empty list removes the panel's block.</param>
    /// <returns>
    /// 0 when the file was updated or already up to date; -2 when the user declined the UAC prompt; another exit code
    /// when the copy failed.
    /// </returns>
    /// <exception cref="IOException">The hosts file cannot be read or the new file cannot be prepared.</exception>
    public static async Task<int> UpdateAsync(IReadOnlyList<string> hostNames)
    {
        string current = File.Exists(HostsPath) ? await File.ReadAllTextAsync(HostsPath) : "";
        if (HostsInBlock(current).SequenceEqual(hostNames, StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }

        string updated = WithHosts(current, hostNames);

        Directory.CreateDirectory(AppSettings.Folder);
        string prepared = Path.Combine(AppSettings.Folder, "hosts.new");
        await File.WriteAllTextAsync(prepared, updated, new UTF8Encoding(false));

        // cmd's exit code is that of its last command, so a failed copy skips the flush and reports the copy's code.
        string arguments = $"/c copy /y \"{prepared}\" \"{HostsPath}\" >nul && ipconfig /flushdns >nul";
        int exitCode = await Shell.RunElevatedAsync("cmd.exe", arguments, AppSettings.Folder);
        FileTools.TryDeleteFile(prepared);
        return exitCode;
    }
}
