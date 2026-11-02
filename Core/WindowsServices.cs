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

    /// <summary>True when a Windows service exists and is running.</summary>
    /// <param name="name">The service name (not the display name).</param>
    /// <returns>True when sc.exe reports the RUNNING state; false otherwise or off Windows.</returns>
    public static async Task<bool> IsRunningAsync(string name)
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
        // The state line reads "STATE : 4  RUNNING" in every language's sc.exe.
        return result.ExitCode == 0 && result.Output.Contains(": 4", StringComparison.Ordinal)
            && result.Output.Contains("RUNNING", StringComparison.Ordinal);
    }

    /// <summary>Starts a Windows service through net.exe, elevated (one UAC prompt per session).</summary>
    /// <param name="name">The service name (not the display name).</param>
    /// <returns>net.exe's exit code (0 on success); -1 when it did not start; -2 when the user declined UAC.</returns>
    public static Task<int> StartAsync(string name) =>
        ElevatedHelper.RunAsync("net.exe", $"start \"{name}\"", Environment.SystemDirectory);

    /// <summary>Stops a Windows service through net.exe, elevated (one UAC prompt per session).</summary>
    /// <param name="name">The service name (not the display name).</param>
    /// <returns>net.exe's exit code (0 on success); -1 when it did not start; -2 when the user declined UAC.</returns>
    public static Task<int> StopAsync(string name) =>
        ElevatedHelper.RunAsync("net.exe", $"stop \"{name}\"", Environment.SystemDirectory);
}
