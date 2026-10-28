using System.Diagnostics;
using System.Runtime.InteropServices;

namespace XamppControl.Core;

/// <summary>Finds, names and stops running processes by name, PID and exe location.</summary>
public static class ProcessScan
{
    /// <summary>
    /// PIDs of processes with one of the given names whose exe lives under <paramref name="folder"/>.
    /// If a path can't be read (e.g. a service running as SYSTEM), the process is still counted.
    /// </summary>
    /// <param name="names">Process names without the .exe extension.</param>
    /// <param name="folder">Prefix the exe path must start with, case-insensitive; null accepts any location.</param>
    /// <returns>The matching PIDs in ascending order; empty when none run.</returns>
    public static List<int> FindPids(IEnumerable<string> names, string? folder)
    {
        var pids = new List<int>();
        foreach (string name in names)
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    string? exePath = PathOf(process.Id);
                    bool isUnderFolder = folder is null
                        || exePath is null
                        || exePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
                    if (isUnderFolder)
                    {
                        pids.Add(process.Id);
                    }
                }
            }
        }

        pids.Sort();
        return pids;
    }

    /// <summary>The process name of a running process, without .exe.</summary>
    /// <param name="pid">The process ID.</param>
    /// <returns>The name; null when the process has exited or cannot be queried.</returns>
    public static string? NameOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            // Querying another process may fail in many ways (exited, access denied); the caller shows a fallback.
            return null;
        }
    }

    /// <summary>
    /// Full exe path using PROCESS_QUERY_LIMITED_INFORMATION, which works for more processes than MainModule.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    /// <returns>
    /// The full path; null off Windows, or when the process cannot be opened or its path is longer than 1024
    /// characters.
    /// </returns>
    public static string? PathOf(int pid)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new char[1024];
            int pathLength = buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref pathLength)
                ? new string(buffer, 0, pathLength)
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Kills a process and all its child processes, if it still runs and may be killed.</summary>
    /// <param name="pid">The process ID of the tree's root.</param>
    public static void KillTree(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone, or not ours to kill; either way there is nothing more to do here.
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>Opens a process handle for reading its image path.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int pid);

    /// <summary>Reads the full Win32 path of a process's executable.</summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process,
        int flags,
        [Out] char[] exeName,
        ref int size);

    /// <summary>Closes the handle from <see cref="OpenProcess"/>.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
