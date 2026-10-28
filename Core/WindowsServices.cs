namespace XamppControl.Core;

/// <summary>Thin wrapper over sc.exe / net.exe so no extra packages are needed.</summary>
public static class WindowsServices
{
    public static async Task<bool> IsInstalledAsync(string name)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        var result = await Shell.RunAsync(
            "sc.exe", $"query \"{name}\"", Environment.SystemDirectory, TimeSpan.FromSeconds(5));
        return result.ExitCode == 0; // 1060 = service does not exist
    }

    public static Task<int> StartAsync(string name) =>
        Shell.RunElevatedAsync("net.exe", $"start \"{name}\"", Environment.SystemDirectory);

    public static Task<int> StopAsync(string name) =>
        Shell.RunElevatedAsync("net.exe", $"stop \"{name}\"", Environment.SystemDirectory);
}
