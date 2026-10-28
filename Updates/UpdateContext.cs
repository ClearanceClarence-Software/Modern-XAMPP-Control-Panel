using System.Net.Sockets;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.ViewModels;

namespace XamppControl.Updates;

/// <summary>Outcome of an update. <see cref="Rollback"/> restores the previous version when set.</summary>
public sealed record UpdateResult(
    bool Success, string Message, Func<Task>? Rollback = null, string? RollbackLabel = null)
{
    public static UpdateResult Fail(string message) => new(false, message);
}

/// <summary>What an updater can use: the panel (log, modules, settings) and the dialog's status/progress.</summary>
public sealed class UpdateContext(MainViewModel main, Action<string> status, Action<double?> progress)
{
    public MainViewModel Main => main;
    public string Root => main.Root;
    public AppSettings Settings => main.Settings;

    /// <summary>Shown in the dialog and written to the log.</summary>
    public void Status(string text)
    {
        status(text);
        main.Write("update", text);
    }

    /// <summary>0..1, or null for "busy, no percentage".</summary>
    public void Progress(double? value) => progress(value);

    public void Log(string text, LogLevel level = LogLevel.Info) => main.Write("update", text, level);

    /// <summary>MYSQL_PWD for the command-line clients, so the password never appears in arguments.</summary>
    public Dictionary<string, string> MySqlEnvironment()
    {
        var env = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(Settings.MySqlRootPassword))
            env["MYSQL_PWD"] = Settings.MySqlRootPassword!;
        return env;
    }

    public string MySqlConnectionArgs => $"-u root -h 127.0.0.1 -P {Settings.Services.MySqlPort} --protocol=tcp";

    public static async Task<bool> PortOpenAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
            await client.ConnectAsync("127.0.0.1", port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> WaitForPortAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await PortOpenAsync(port))
                return true;
            await Task.Delay(500);
        }
        return false;
    }

    /// <summary>Waits until the panel sees the module running (or stopped).</summary>
    public async Task<bool> WaitForModuleAsync(string name, bool running, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await main.RefreshAsync(quiet: true);
            if (main.FindModule(name)?.IsRunning == running)
                return true;
            await Task.Delay(500);
        }
        return false;
    }

    /// <summary>Last lines of a tool's output, for the log.</summary>
    public void LogOutput(Shell.RunResult result, LogLevel level, int lines = 8)
    {
        var text = (result.Output + "\n" + result.Error)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in text.Skip(Math.Max(0, text.Length - lines)))
            Log("  › " + line, level);
    }
}
