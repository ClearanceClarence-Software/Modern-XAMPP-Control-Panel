using System.Net.Sockets;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.ViewModels;

namespace XamppControl.Updates;

/// <summary>Outcome of an update. <see cref="Rollback"/> restores the previous version when set.</summary>
/// <param name="Success">True when the update finished as intended; false when it failed or only partly worked.</param>
/// <param name="Message">One sentence for the dialog's status line and the log.</param>
/// <param name="Rollback">
/// Restores the previous version and reports a failure by throwing; null when there is nothing to roll back.
/// </param>
/// <param name="RollbackLabel">Text for the rollback button; null shows "Roll back".</param>
public sealed record UpdateResult(
    bool Success,
    string Message,
    Func<Task>? Rollback = null,
    string? RollbackLabel = null)
{
    /// <summary>A failed result with no rollback.</summary>
    /// <param name="message">Why it failed, as one sentence for the user.</param>
    /// <returns>A result with <see cref="Success"/> false.</returns>
    public static UpdateResult Fail(string message) => new(false, message);
}

/// <summary>What an updater can use: the panel (log, modules, settings) and the dialog's status/progress.</summary>
/// <param name="main">The panel whose modules, settings and log the updater uses.</param>
/// <param name="status">Receives each status line for the dialog, synchronously from <see cref="Status"/>.</param>
/// <param name="progress">Receives the progress as 0..1, or null for "busy, no percentage".</param>
public sealed class UpdateContext(MainViewModel main, Action<string> status, Action<double?> progress)
{
    /// <summary>The panel the update runs in.</summary>
    public MainViewModel Main => main;

    /// <summary>The XAMPP folder, e.g. C:\xampp.</summary>
    public string Root => main.Root;

    /// <summary>The panel's settings, for ports and the MySQL root password.</summary>
    public AppSettings Settings => main.Settings;

    /// <summary>Shown in the dialog and written to the log.</summary>
    /// <param name="text">One line for the dialog's status text; it is also logged at Info level.</param>
    public void Status(string text)
    {
        status(text);
        main.Write("update", text);
    }

    /// <summary>0..1, or null for "busy, no percentage".</summary>
    /// <param name="value">The fraction done, from 0 to 1; null shows an indeterminate bar.</param>
    public void Progress(double? value) => progress(value);

    /// <summary>Writes a line to the panel's log under the "update" source, without touching the dialog.</summary>
    /// <param name="text">The line to write.</param>
    /// <param name="level">The severity the log shows for the line.</param>
    public void Log(string text, LogLevel level = LogLevel.Info) => main.Write("update", text, level);

    /// <summary>MYSQL_PWD for the command-line clients, so the password never appears in arguments.</summary>
    /// <returns>Environment variables for the client process; empty when no root password is set.</returns>
    public Dictionary<string, string> MySqlEnvironment()
    {
        var environment = new Dictionary<string, string>();
        if (Settings.MySqlRootPassword is { Length: > 0 } password)
        {
            environment["MYSQL_PWD"] = password;
        }
        return environment;
    }

    /// <summary>Client arguments that connect as root over TCP to the configured MySQL port on this machine.</summary>
    public string MySqlConnectionArgs => $"-u root -h 127.0.0.1 -P {Settings.Services.MySqlPort} --protocol=tcp";

    /// <summary>Whether something accepts TCP connections on a local port.</summary>
    /// <param name="port">The TCP port on 127.0.0.1.</param>
    /// <returns>
    /// True when a connection succeeds within 800 ms; false when it is refused, times out or the port is invalid.
    /// </returns>
    public static async Task<bool> PortOpenAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
            await client.ConnectAsync("127.0.0.1", port, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException
                                       or OperationCanceledException
                                       or ArgumentOutOfRangeException)
        {
            // Refused, timed out or out of range all mean the same here: nothing is listening.
            return false;
        }
    }

    /// <summary>Polls a local port every half second until something listens on it.</summary>
    /// <param name="port">The TCP port on 127.0.0.1.</param>
    /// <param name="timeout">How long to keep trying; each attempt itself may take up to 800 ms.</param>
    /// <returns>True as soon as the port accepts a connection; false when the time runs out first.</returns>
    public static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await PortOpenAsync(port))
            {
                return true;
            }
            await Task.Delay(500);
        }
        return false;
    }

    /// <summary>Waits until the panel sees the module running (or stopped).</summary>
    /// <param name="name">The module's name in the panel, e.g. "Apache" or "MySQL".</param>
    /// <param name="running">True waits for the module to run; false waits for it to stop.</param>
    /// <param name="timeout">How long to keep refreshing the panel, which happens every half second.</param>
    /// <returns>True once the module is in that state; false on timeout or when the panel has no such module.</returns>
    public async Task<bool> WaitForModuleAsync(string name, bool running, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await main.RefreshAsync(quiet: true);
            if (main.FindModule(name)?.IsRunning == running)
            {
                return true;
            }
            await Task.Delay(500);
        }
        return false;
    }

    /// <summary>Last lines of a tool's output, for the log.</summary>
    /// <param name="result">The finished run; its standard output and error are logged together, output first.</param>
    /// <param name="level">The log level of every line written.</param>
    /// <param name="lines">How many non-empty lines to keep from the end.</param>
    public void LogOutput(Shell.RunResult result, LogLevel level, int lines = 8)
    {
        string[] outputLines = (result.Output + "\n" + result.Error)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string line in outputLines.Skip(Math.Max(0, outputLines.Length - lines)))
        {
            Log("  › " + line, level);
        }
    }
}
