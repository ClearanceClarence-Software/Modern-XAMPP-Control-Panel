using System.ComponentModel;
using System.Diagnostics;

namespace XamppControl.Core;

/// <summary>Starts processes, opens files and URLs, and reads log tails.</summary>
public static class Shell
{
    // ERROR_CANCELLED: the user answered No to the UAC prompt.
    private const int ErrorCanceled = 1223;

    /// <summary>Opens a URL, folder or file with its default handler.</summary>
    /// <param name="target">A URL, a full folder or file path, or a shell name such as services.msc.</param>
    public static void Open(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Opens a URL in the configured browser, or the system default when none is set.</summary>
    /// <param name="url">The address to open.</param>
    /// <param name="browser">
    /// A browser exe as a full path or a name on PATH; null, blank or one that fails to start uses the system default.
    /// </param>
    public static void OpenUrl(string url, string? browser)
    {
        if (!string.IsNullOrWhiteSpace(browser))
        {
            try
            {
                var startInfo = new ProcessStartInfo(browser) { UseShellExecute = false };
                startInfo.ArgumentList.Add(url);
                Process.Start(startInfo)?.Dispose();
                return;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                // The configured browser is missing or cannot start; the system default opens the URL instead.
            }
        }

        Open(url);
    }

    /// <summary>Opens a file in the configured editor (a full path or a name on PATH, like notepad.exe).</summary>
    /// <param name="file">Full path of the file to open.</param>
    /// <param name="editor">
    /// The editor exe; null, blank or one that fails to start uses Notepad (the default handler off Windows).
    /// </param>
    public static void OpenInEditor(string file, string? editor)
    {
        if (!string.IsNullOrWhiteSpace(editor))
        {
            try
            {
                var startInfo = new ProcessStartInfo(editor) { UseShellExecute = false };
                startInfo.ArgumentList.Add(file);
                Process.Start(startInfo)?.Dispose();
                return;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                // The configured editor is missing or cannot start; Notepad opens the file instead.
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var notepadStartInfo = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
            notepadStartInfo.ArgumentList.Add(file);
            Process.Start(notepadStartInfo)?.Dispose();
        }
        else
        {
            Open(file);
        }
    }

    /// <summary>Runs a batch file in its own visible console window (e.g. Tomcat's output window).</summary>
    /// <param name="bat">Full path of the batch file; it is quoted for cmd.exe.</param>
    /// <param name="workingDirectory">The folder the batch file runs in.</param>
    public static void StartVisibleBatch(string bat, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
        {
            UseShellExecute = true,
            WorkingDirectory = workingDirectory,
        };
        Process.Start(startInfo)?.Dispose();
    }

    /// <summary>Starts a long-running server process with no console window.</summary>
    /// <param name="exe">Full path of the executable, or a name on PATH.</param>
    /// <param name="arguments">The command line after the exe, already quoted; empty for none.</param>
    /// <param name="workingDirectory">The folder the process runs in.</param>
    /// <returns>The started process, which the caller disposes; null when no process was started.</returns>
    public static Process? StartHidden(string exe, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        return Process.Start(startInfo);
    }

    /// <summary>True if the process exits within the given time (used to catch servers that die on startup).</summary>
    /// <param name="process">A process this app started.</param>
    /// <param name="time">How long to wait; the process keeps running when it is still alive after that.</param>
    /// <returns>True when it exited in time; false when it still runs.</returns>
    public static async Task<bool> ExitsWithinAsync(Process process, TimeSpan time)
    {
        using var timeoutSource = new CancellationTokenSource(time);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The outcome of <see cref="RunAsync"/>.</summary>
    /// <param name="ExitCode">The process's exit code; -1 when it could not start or timed out.</param>
    /// <param name="Output">Everything written to standard output; empty when it could not start or timed out.</param>
    /// <param name="Error">
    /// Everything written to standard error, or the reason ("Could not start ...", "Timed out") when it did not run to
    /// the end.
    /// </param>
    /// <param name="TimedOut">True when the process was killed for running past the timeout.</param>
    public sealed record RunResult(int ExitCode, string Output, string Error, bool TimedOut);

    /// <summary>Runs a short command hidden and captures its output.</summary>
    /// <param name="exe">Full path of the executable, or a name on PATH.</param>
    /// <param name="arguments">The command line after the exe, already quoted.</param>
    /// <param name="workingDirectory">The folder the process runs in.</param>
    /// <param name="timeout">How long to wait before the process and its children are killed.</param>
    /// <param name="environment">Variables added to or replacing the inherited environment; null adds none.</param>
    /// <returns>The exit code and captured output; see <see cref="RunResult"/> for the failure values.</returns>
    public static async Task<RunResult> RunAsync(
        string exe,
        string arguments,
        string workingDirectory,
        TimeSpan timeout,
        IDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new RunResult(-1, "", "Could not start " + Path.GetFileName(exe), false);
        }

        // Both streams are drained while waiting, so a full pipe buffer cannot block the process.
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();

        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            KillTreeQuietly(process);
            return new RunResult(-1, "", "Timed out", true);
        }

        return new RunResult(process.ExitCode, await outputTask, await errorTask, false);
    }

    /// <summary>Kills a process and its children, ignoring every failure.</summary>
    /// <param name="process">The process to kill; it may already have exited.</param>
    private static void KillTreeQuietly(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch
        {
            // Best effort after a timeout: the process may have exited or be protected, and the caller reports
            // the timeout either way.
        }
    }

    /// <summary>Runs a command elevated (UAC prompt). Returns the exit code, or -2 if the user canceled.</summary>
    /// <param name="exe">Full path of the executable, or a name on PATH.</param>
    /// <param name="arguments">The command line after the exe, already quoted.</param>
    /// <param name="workingDirectory">The folder the process runs in.</param>
    /// <returns>The exit code; -1 when no process was started; -2 when the user declined the UAC prompt.</returns>
    public static async Task<int> RunElevatedAsync(string exe, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workingDirectory,
        };
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return -1;
            }

            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCanceled)
        {
            return -2;
        }
    }

    /// <summary>Last lines of a log file that may still be open by the server.</summary>
    /// <param name="path">Full path of the log file.</param>
    /// <param name="lines">The most lines to return.</param>
    /// <returns>
    /// Up to <paramref name="lines"/> non-empty lines from the last 64 KB, oldest first; empty when the file is
    /// missing or cannot be read.
    /// </returns>
    public static IReadOnlyList<string> Tail(string path, int lines)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            // FileShare.ReadWrite | Delete lets the server keep writing and rotating while the tail is read.
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            const int maxBytes = 64 * 1024;
            if (stream.Length > maxBytes)
            {
                stream.Seek(-maxBytes, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream);
            var allLines = reader.ReadToEnd()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Length > 0)
                .ToList();
            return allLines.Skip(Math.Max(0, allLines.Count - lines)).ToList();
        }
        catch
        {
            // Another program's log may be locked, deleted or unreadable at any moment; the log view shows nothing.
            return [];
        }
    }
}
