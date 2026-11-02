using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XamppControl.Core;

/// <summary>
/// One admin prompt per session instead of one per action. The first elevated command starts a second copy of this
/// exe elevated (one UAC prompt); it stays open and runs commands the panel sends over a named pipe until the panel
/// exits. When the user declines the prompt, that command is canceled and later commands prompt one by one.
/// </summary>
/// <remarks>
/// The pipe is protected two ways: only the panel's Windows user may connect (its SID is passed to the helper, since
/// the helper may run as a different admin account), and every command carries a random secret the helper got on
/// its command line, so another program of the same user can't drive it. Each request and reply is one line of
/// text, so neither side needs to know the other's message size.
/// </remarks>
public static class ElevatedHelper
{
    /// <summary>The command-line switch that makes this exe run as the helper.</summary>
    public const string Switch = "--elevated-helper";

    /// <summary>Returned when the user declined the UAC prompt, as <see cref="Shell.RunElevatedAsync"/> does.</summary>
    public const int DeclinedExitCode = -2;

    private const int ErrorCanceled = 1223;

    private static readonly SemaphoreSlim _sendLock = new(1, 1);
    private static Process? _helper;
    private static string? _pipeName;
    private static string? _secret;
    private static bool _isDeclined;

    /// <summary>True while the helper runs, so the next command needs no prompt.</summary>
    public static bool IsRunning => _helper is { HasExited: false };

    /// <summary>
    /// Runs a command elevated: through the helper when it runs or can be started, else with a prompt of its own.
    /// </summary>
    /// <param name="exe">Full path of the executable, or a name on PATH.</param>
    /// <param name="arguments">The command line after the exe, already quoted.</param>
    /// <param name="workingDirectory">The folder the command runs in.</param>
    /// <returns>The exit code; -1 when the command could not start; -2 when the user declined the prompt.</returns>
    public static async Task<int> RunAsync(string exe, string arguments, string workingDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return -1;
        }

        // One command at a time: the helper serves one connection at a time, and a second connect would time out
        // and fall back to a prompt while the first command still runs.
        await _sendLock.WaitAsync();
        try
        {
            bool wasDeclinedBefore = _isDeclined;
            if (await EnsureStartedAsync())
            {
                int? exitCode = await SendAsync(exe, arguments, workingDirectory);
                if (exitCode is not null)
                {
                    return exitCode.Value;
                }
            }
            else if (_isDeclined && !wasDeclinedBefore)
            {
                // The user just said No to the helper's prompt; asking again for the same action would be rude.
                return DeclinedExitCode;
            }
        }
        finally
        {
            _sendLock.Release();
        }

        return await Shell.RunElevatedAsync(exe, arguments, workingDirectory);
    }

    /// <summary>Closes the helper; called when the panel exits. Waits at most a few seconds.</summary>
    public static void Stop()
    {
        if (_helper is not { HasExited: false } helper)
        {
            return;
        }

        // Run on the pool: after the UI loop has ended, continuations posted to its context would never run.
        Task.Run(() => SendAsync("quit", "", "")).Wait(TimeSpan.FromSeconds(3));
        if (!helper.WaitForExit(TimeSpan.FromSeconds(2)))
        {
            Shell.KillTreeQuietly(helper);
        }
        helper.Dispose();
        _helper = null;
    }

    /// <summary>Starts the helper with one UAC prompt, unless it runs or the user said No this session.</summary>
    /// <returns>True when the helper is running and answers.</returns>
    private static async Task<bool> EnsureStartedAsync()
    {
        if (IsRunning)
        {
            return true;
        }
        if (_isDeclined)
        {
            return false;
        }

        _pipeName = "XamppControl-" + Guid.NewGuid().ToString("N");
        _secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("The exe path is unknown.");
        string userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current user has no SID.");
        string arguments = $"{Switch} {_pipeName} {_secret} {userSid} {Environment.ProcessId}";
        var startInfo = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            _helper = Process.Start(startInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCanceled)
        {
            // The user answered No; the rest of the session asks per command, as before.
            _isDeclined = true;
            return false;
        }

        // The helper needs a moment to open its pipe; a ping proves it is listening and accepts this user.
        for (int attempt = 0; attempt < 30 && IsRunning; attempt++)
        {
            if (await SendAsync("ping", "", "") == 0)
            {
                return true;
            }
            await Task.Delay(100);
        }

        // It started but never answered (wrong account on the pipe, or it died): don't keep trying it.
        if (_helper is { } unresponsive)
        {
            Shell.KillTreeQuietly(unresponsive);
            unresponsive.Dispose();
            _helper = null;
        }
        return false;
    }

    /// <summary>Sends one command to the helper and waits for its exit code.</summary>
    /// <param name="exe">The program, or "ping" or "quit".</param>
    /// <param name="arguments">The program's arguments.</param>
    /// <param name="workingDirectory">The folder the program runs in.</param>
    /// <returns>The exit code; null when the helper could not be reached or gave no answer.</returns>
    private static async Task<int?> SendAsync(string exe, string arguments, string workingDirectory)
    {
        if (_pipeName is null || _secret is null)
        {
            return null;
        }

        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await pipe.ConnectAsync(connectTimeout.Token);

            var request = new HelperRequest(_secret, exe, arguments, workingDirectory);
            string line = JsonSerializer.Serialize(request, HelperJson.Default.HelperRequest) + "\n";
            byte[] body = Encoding.UTF8.GetBytes(line);
            await pipe.WriteAsync(body);
            await pipe.FlushAsync();

            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            string? reply = await reader.ReadLineAsync();
            return int.TryParse(reply, out int exitCode) ? exitCode : null;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException
                                       or UnauthorizedAccessException)
        {
            // The helper is gone, busy or not ours; the caller falls back to a prompt of its own.
            return null;
        }
    }

    /// <summary>
    /// The helper's main loop, run by the elevated copy of this exe: serves commands over the pipe until "quit" or
    /// until the panel that started it exits.
    /// </summary>
    /// <param name="pipeName">The pipe name the panel chose.</param>
    /// <param name="secret">The secret every request must carry.</param>
    /// <param name="userSid">The SID of the panel's user, the only account allowed to connect.</param>
    /// <param name="panelPid">The panel's process ID; the helper exits when that process is gone.</param>
    /// <returns>The process exit code: 0 after a clean quit.</returns>
    public static int RunHelperLoop(string pipeName, string secret, string userSid, int panelPid)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 1;
        }

        WatchPanel(panelPid);

        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(userSid),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        while (true)
        {
            using NamedPipeServerStream pipe = NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.None,
                0,
                0,
                security);
            pipe.WaitForConnection();

            HelperRequest? request = ReadRequest(pipe);
            if (request is not { Secret: { } requestSecret, Exe: { } exe }
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(requestSecret),
                    Encoding.UTF8.GetBytes(secret)))
            {
                WriteReply(pipe, -1);
                continue;
            }

            if (exe == "quit")
            {
                WriteReply(pipe, 0);
                return 0;
            }

            WriteReply(pipe, exe == "ping" ? 0 : Execute(exe, request.Arguments ?? "", request.WorkingDirectory));
        }
    }

    /// <summary>Ends the helper when the panel is gone, so a crashed panel leaves no elevated process behind.</summary>
    /// <param name="panelPid">The panel's process ID.</param>
    private static void WatchPanel(int panelPid)
    {
        var watcher = new Thread(() =>
        {
            try
            {
                using Process panel = Process.GetProcessById(panelPid);
                panel.WaitForExit();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // The panel is already gone or can't be opened; either way there is nothing left to serve.
            }
            Environment.Exit(0);
        })
        {
            IsBackground = true,
            Name = "panel watcher",
        };
        watcher.Start();
    }

    /// <summary>Runs one request's command hidden and waits for it.</summary>
    /// <param name="exe">The program to run.</param>
    /// <param name="arguments">The program's command line.</param>
    /// <param name="workingDirectory">The folder to run in; a missing or unknown folder uses System32.</param>
    /// <returns>The command's exit code; -1 when it could not start or ran past two minutes.</returns>
    private static int Execute(string exe, string arguments, string? workingDirectory)
    {
        try
        {
            var startInfo = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.Exists(workingDirectory)
                    ? workingDirectory
                    : Environment.SystemDirectory,
            };
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return -1;
            }
            if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                Shell.KillTreeQuietly(process);
                return -1;
            }
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // A missing exe or a start failure is reported as "could not start", the same as the direct path.
            return -1;
        }
    }

    /// <summary>Reads one request line from the pipe.</summary>
    /// <param name="pipe">The connected pipe.</param>
    /// <returns>The request; null when the line is missing or isn't valid JSON.</returns>
    private static HelperRequest? ReadRequest(Stream pipe)
    {
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            string? line = reader.ReadLine();
            return line is null ? null : JsonSerializer.Deserialize(line, HelperJson.Default.HelperRequest);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // A broken or malformed request is refused, and the loop waits for the next one.
            return null;
        }
    }

    /// <summary>Writes the exit code as one line and waits until the panel has read it.</summary>
    /// <param name="pipe">The connected pipe.</param>
    /// <param name="exitCode">The code to send.</param>
    private static void WriteReply(NamedPipeServerStream pipe, int exitCode)
    {
        try
        {
            byte[] reply = Encoding.UTF8.GetBytes(exitCode + "\n");
            pipe.Write(reply, 0, reply.Length);
            pipe.Flush();
            pipe.WaitForPipeDrain();
        }
        catch (IOException)
        {
            // The panel went away before reading the reply; nothing to tell it.
        }
    }
}

/// <summary>One command sent from the panel to the elevated helper.</summary>
/// <param name="Secret">The per-session secret; a request with the wrong one is refused.</param>
/// <param name="Exe">The program to run, or "ping" or "quit".</param>
/// <param name="Arguments">The program's command line.</param>
/// <param name="WorkingDirectory">The folder the program runs in.</param>
public sealed record HelperRequest(string? Secret, string? Exe, string? Arguments, string? WorkingDirectory);

/// <summary>Source-generated JSON for the helper protocol, so the single-file exe needs no reflection.</summary>
[JsonSerializable(typeof(HelperRequest))]
internal sealed partial class HelperJson : JsonSerializerContext;
