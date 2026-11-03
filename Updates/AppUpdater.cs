using System.Diagnostics;
using System.IO.Compression;
using XamppControl.Core;

namespace XamppControl.Updates;

/// <summary>
/// Updates the panel in place: downloads a release's zip, checks its SHA-256, unpacks the new exe next to the
/// running one, and hands over to a small script that swaps the files once the panel has exited and starts it again.
/// Settings, the local certificate authority and XAMPP itself are untouched.
/// </summary>
public static class AppUpdater
{
    private const string ScriptName = "apply-update.cmd";

    /// <summary>Where the zip and the new exe are staged.</summary>
    public static string StagingFolder => Path.Combine(Path.GetTempPath(), "XamppControl", "update");

    /// <summary>
    /// Full path of the running exe; null when it can't be told, or when this is a debug build run from an IDE or
    /// `dotnet run` (its apphost sits next to XamppControl.dll and must not be swapped for the published exe).
    /// </summary>
    public static string? CurrentExe
    {
        get
        {
            string? path = Environment.ProcessPath;
            bool isExe = path is not null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            bool isApphost = path is not null && File.Exists(Path.ChangeExtension(path, ".dll"));
            return isExe && !isApphost ? path : null;
        }
    }

    /// <summary>True when this build can replace itself: on Windows, running as a plain exe.</summary>
    public static bool CanSelfUpdate => OperatingSystem.IsWindows() && CurrentExe is not null;

    /// <summary>Downloads a release's zip, verifies it and unpacks the new exe into the staging folder.</summary>
    /// <param name="release">The release to install; it must have a <see cref="AppRelease.ZipUrl"/>.</param>
    /// <param name="status">Receives one line per step.</param>
    /// <param name="progress">Receives the download progress as 0..1, or null while the size is unknown.</param>
    /// <returns>Full path of the unpacked exe.</returns>
    /// <exception cref="InvalidOperationException">The release has no zip, or the checksum doesn't match.</exception>
    /// <exception cref="HttpRequestException">A download failed.</exception>
    /// <exception cref="InvalidDataException">The zip holds no exe.</exception>
    public static async Task<string> DownloadAsync(AppRelease release, Action<string> status, Action<double?> progress)
    {
        string zipUrl = release.ZipUrl
            ?? throw new InvalidOperationException("This release has no win-x64 zip to install from.");

        string folder = Path.Combine(StagingFolder, release.Version.TrimStart('v'));
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
        Directory.CreateDirectory(folder);

        string zipPath = Path.Combine(folder, "update.zip");
        status($"Downloading {release.Version}…");
        await ReleaseFeeds.DownloadAsync(zipUrl, zipPath, progress);

        if (release.ShaUrl is { } shaUrl)
        {
            status("Checking the download…");
            progress(null);
            await VerifyAsync(zipPath, shaUrl);
        }

        status("Unpacking…");
        string newExe = Path.Combine(folder, "XamppControl.new.exe");
        using (ZipArchive zip = ZipFile.OpenRead(zipPath))
        {
            ZipArchiveEntry exe = zip.Entries.FirstOrDefault(entry =>
                    entry.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("The downloaded zip holds no exe.");
            exe.ExtractToFile(newExe, overwrite: true);
        }
        FileTools.TryDeleteFile(zipPath);
        return newExe;
    }

    /// <summary>
    /// Writes and starts the script that waits for this process to exit, copies the new exe over the running one and
    /// starts it again. The caller closes the panel right after; the script does the rest.
    /// </summary>
    /// <param name="newExe">The exe from <see cref="DownloadAsync"/>.</param>
    /// <returns>True when the script was started; false when this build can't replace itself.</returns>
    /// <exception cref="IOException">The script can't be written.</exception>
    public static bool ApplyOnExit(string newExe)
    {
        if (CurrentExe is not { } currentExe)
        {
            return false;
        }

        string script = Path.Combine(Path.GetDirectoryName(newExe) ?? StagingFolder, ScriptName);
        File.WriteAllText(script, BuildScript(newExe, currentExe, Environment.ProcessId));

        // A folder the user can't write to (Program Files) needs the copy to run elevated; the exe's own folder is
        // the usual case and needs nothing.
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\"\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(script) ?? StagingFolder,
        };
        if (!IsWritable(Path.GetDirectoryName(currentExe) ?? ""))
        {
            startInfo.Verb = "runas";
        }
        Process.Start(startInfo)?.Dispose();
        return true;
    }

    /// <summary>The batch script that swaps the exe after the panel exits.</summary>
    /// <param name="newExe">The downloaded exe.</param>
    /// <param name="currentExe">The running exe to replace.</param>
    /// <param name="pid">The running panel's process ID, waited for before the copy.</param>
    /// <returns>The script text, CRLF line breaks.</returns>
    internal static string BuildScript(string newExe, string currentExe, int pid) =>
        string.Join("\r\n",
        [
            "@echo off",
            "setlocal",
            $"set \"src={newExe}\"",
            $"set \"dst={currentExe}\"",
            "set /a tries=0",
            ":wait",
            // tasklist prints the process row when it still runs, and an INFO line (no digits) when it doesn't.
            $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul",
            "if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)",
            ":copy",
            "copy /y \"%src%\" \"%dst%\" >nul 2>&1",
            "if errorlevel 1 goto retry",
            "del /q \"%src%\" >nul 2>&1",
            "start \"\" \"%dst%\"",
            "del /q \"%~f0\" >nul 2>&1",
            "exit /b 0",
            // Outside a parenthesized block, so %tries% reads the value set just above it.
            ":retry",
            "set /a tries+=1",
            "if %tries% geq 30 exit /b 1",
            "timeout /t 1 /nobreak >nul",
            "goto copy",
            "",
        ]);

    /// <summary>Compares the zip's SHA-256 with the one the release publishes.</summary>
    /// <param name="zipPath">The downloaded zip.</param>
    /// <param name="shaUrl">The release's .sha256 file.</param>
    /// <exception cref="InvalidOperationException">The hashes differ or the file holds no hash.</exception>
    private static async Task VerifyAsync(string zipPath, string shaUrl)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string text = await ReleaseFeeds.Http.GetStringAsync(shaUrl, timeout.Token);
        string? expected = text.Split(' ', '\t', '*', '\n', '\r')
            .FirstOrDefault(part => part.Length == 64)?.ToLowerInvariant()
            ?? throw new InvalidOperationException("The release's .sha256 file holds no checksum.");

        string actual = await ReleaseFeeds.Sha256Async(zipPath);
        if (actual != expected)
        {
            FileTools.TryDeleteFile(zipPath);
            throw new InvalidOperationException(
                "The download's checksum doesn't match the release. Nothing was changed.");
        }
    }

    /// <summary>Whether a file can be created in a folder.</summary>
    /// <param name="folder">The folder to probe.</param>
    /// <returns>True when a probe file could be written and deleted.</returns>
    private static bool IsWritable(string folder)
    {
        try
        {
            string probe = Path.Combine(folder, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not writable: the copy runs elevated instead.
            return false;
        }
    }
}
