namespace XamppControl.Core;

/// <summary>Thin wrapper over sc.exe / net.exe so no extra packages are needed.</summary>
public static class WindowsServices
{
    /// <summary>True when a Windows service with that name exists, running or not.</summary>
    /// <param name="name">The service name (not the display name).</param>
    /// <returns>True when sc.exe finds the service; false off Windows, when it is missing or sc.exe fails.</returns>
    public static async Task<bool> IsInstalledAsync(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        Shell.RunResult result = await Shell.RunAsync(
            "sc.exe",
            $"query \"{name}\"",
            Environment.SystemDirectory,
            TimeSpan.FromSeconds(5));
        // sc.exe exits with 1060 when the service does not exist.
        return result.ExitCode == 0;
    }

    /// <summary>Starts a Windows service through net.exe, behind a UAC prompt.</summary>
    /// <param name="name">The service name (not the display name).</param>
    /// <returns>net.exe's exit code (0 on success); -1 when it did not start; -2 when the user declined UAC.</returns>
    public static Task<int> StartAsync(string name) =>
        Shell.RunElevatedAsync("net.exe", $"start \"{name}\"", Environment.SystemDirectory);

    /// <summary>Stops a Windows service through net.exe, behind a UAC prompt.</summary>
    /// <param name="name">The service name (not the display name).</param>
    /// <returns>net.exe's exit code (0 on success); -1 when it did not start; -2 when the user declined UAC.</returns>
    public static Task<int> StopAsync(string name) =>
        Shell.RunElevatedAsync("net.exe", $"stop \"{name}\"", Environment.SystemDirectory);
}
