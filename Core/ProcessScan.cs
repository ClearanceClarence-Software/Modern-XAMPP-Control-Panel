using System.Diagnostics;
using System.Runtime.InteropServices;

namespace XamppControl.Core;

public static class ProcessScan
{
    /// <summary>
    /// PIDs of processes with one of the given names whose exe lives under <paramref name="folder"/>.
    /// If a path can't be read (e.g. a service running as SYSTEM), the process is still counted.
    /// </summary>
    public static List<int> FindPids(IEnumerable<string> names, string? folder)
    {
        var pids = new List<int>();
        foreach (var name in names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    var path = PathOf(process.Id);
                    if (folder is null || path is null || path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                        pids.Add(process.Id);
                }
            }
        }
        pids.Sort();
        return pids;
    }

    public static string? NameOf(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Full exe path using PROCESS_QUERY_LIMITED_INFORMATION, which works for more processes than MainModule.
    /// </summary>
    public static string? PathOf(int pid)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new char[1024];
            var size = buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static void KillTree(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone, or not ours to kill.
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process, int flags, [Out] char[] exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
