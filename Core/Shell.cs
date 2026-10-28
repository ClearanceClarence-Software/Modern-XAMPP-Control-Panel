using System.ComponentModel;
using System.Diagnostics;

namespace XamppControl.Core;

public static class Shell
{
    /// <summary>Opens a URL, folder or file with its default handler.</summary>
    public static void Open(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Opens a URL in the configured browser, or the system default when none is set.</summary>
    public static void OpenUrl(string url, string? browser)
    {
        if (!string.IsNullOrWhiteSpace(browser))
        {
            try
            {
                var psi = new ProcessStartInfo(browser) { UseShellExecute = false };
                psi.ArgumentList.Add(url);
                Process.Start(psi)?.Dispose();
                return;
            }
            catch
            {
                // Fall back to the default browser.
            }
        }
        Open(url);
    }

    /// <summary>Opens a file in the configured editor (a full path or a name on PATH, like notepad.exe).</summary>
    public static void OpenInEditor(string file, string? editor)
    {
        if (!string.IsNullOrWhiteSpace(editor))
        {
            try
            {
                var psi = new ProcessStartInfo(editor) { UseShellExecute = false };
                psi.ArgumentList.Add(file);
                Process.Start(psi)?.Dispose();
                return;
            }
            catch
            {
                // Fall back to Notepad.
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
            psi.ArgumentList.Add(file);
            Process.Start(psi)?.Dispose();
        }
        else
        {
            Open(file);
        }
    }

    /// <summary>Runs a batch file in its own visible console window (e.g. Tomcat's output window).</summary>
    public static void StartVisibleBatch(string bat, string workingDirectory)
    {
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
        {
            UseShellExecute = true,
            WorkingDirectory = workingDirectory,
        })?.Dispose();
    }

    /// <summary>Starts a long-running server process with no console window.</summary>
    public static Process? StartHidden(string exe, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        return Process.Start(psi);
    }

    /// <summary>True if the process exits within the given time (used to catch servers that die on startup).</summary>
    public static async Task<bool> ExitsWithinAsync(Process process, TimeSpan time)
    {
        using var cts = new CancellationTokenSource(time);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public sealed record RunResult(int ExitCode, string Output, string Error, bool TimedOut);

    /// <summary>Runs a short command hidden and captures its output.</summary>
    public static async Task<RunResult> RunAsync(
        string exe, string arguments, string workingDirectory, TimeSpan timeout,
        IDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;
        }

        using var process = Process.Start(psi);
        if (process is null)
            return new RunResult(-1, "", "Could not start " + Path.GetFileName(exe), false);

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* ignore */ }
            return new RunResult(-1, "", "Timed out", true);
        }

        return new RunResult(process.ExitCode, await stdout, await stderr, false);
    }

    /// <summary>Runs a command elevated (UAC prompt). Returns the exit code, or -2 if the user cancelled.</summary>
    public static async Task<int> RunElevatedAsync(string exe, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workingDirectory,
        };
        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return -1;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return -2; // UAC prompt cancelled
        }
    }

    /// <summary>Last lines of a log file that may still be open by the server.</summary>
    public static IReadOnlyList<string> Tail(string path, int lines)
    {
        try
        {
            if (!File.Exists(path))
                return [];
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const int maxBytes = 64 * 1024;
            if (stream.Length > maxBytes)
                stream.Seek(-maxBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            var all = reader.ReadToEnd()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 0)
                .ToList();
            return all.Skip(Math.Max(0, all.Count - lines)).ToList();
        }
        catch
        {
            return [];
        }
    }
}
