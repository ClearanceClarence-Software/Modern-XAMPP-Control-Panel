namespace XamppControl.Core;

/// <summary>
/// The panel's shortcut in the current user's Start menu, so Windows search finds it by name. The exe is portable
/// and has no installer, so the shortcut is made on request and points at wherever the exe is now.
/// </summary>
public static class StartMenu
{
    /// <summary>Full path of the shortcut: Start menu › Programs › Modern XAMPP Control Panel.lnk.</summary>
    public static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), BuildInfo.AppName + ".lnk");

    /// <summary>True when the shortcut file exists.</summary>
    public static bool Exists => File.Exists(ShortcutPath);

    /// <summary>Creates or replaces the shortcut so it starts the running exe.</summary>
    /// <returns>True when the shortcut exists afterwards; false off Windows, without an exe path, or on failure.</returns>
    public static async Task<bool> CreateAsync()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } exe)
        {
            return false;
        }

        // Windows has no managed API for .lnk files; the WScript.Shell COM object writes them, and PowerShell is
        // the dependency-free way to reach it.
        string script =
            $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{Quote(ShortcutPath)}');" +
            $"$s.TargetPath='{Quote(exe)}';" +
            $"$s.WorkingDirectory='{Quote(Path.GetDirectoryName(exe) ?? "")}';" +
            $"$s.IconLocation='{Quote(exe)},0';" +
            "$s.Description='Start, stop and manage Apache, MySQL and PHP in XAMPP';" +
            "$s.Save()";
        string powershell = Path.Combine(
            Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath) ?? "");
            Shell.RunResult result = await Shell.RunAsync(
                powershell,
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                Environment.SystemDirectory,
                TimeSpan.FromSeconds(20));
            return result.ExitCode == 0 && Exists;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A locked-down PC without PowerShell or a read-only profile: the panel works the same without it.
            return false;
        }
    }

    /// <summary>Deletes the shortcut.</summary>
    /// <returns>True when no shortcut is left.</returns>
    public static bool Remove()
    {
        try
        {
            File.Delete(ShortcutPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in place; the caller reports it.
        }
        return !Exists;
    }

    /// <summary>Escapes a value for a single-quoted PowerShell string.</summary>
    /// <param name="value">A path or text.</param>
    /// <returns>The value with every single quote doubled.</returns>
    internal static string Quote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
