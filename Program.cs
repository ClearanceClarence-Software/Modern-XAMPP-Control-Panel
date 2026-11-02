using Avalonia;
using XamppControl.Core;

namespace XamppControl;

/// <summary>The entry point.</summary>
internal static class Program
{
    /// <summary>Runs the Avalonia desktop app, or the elevated helper loop when started with its switch.</summary>
    /// <param name="args">The command line, passed on to the desktop lifetime.</param>
    /// <returns>The process exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        // The elevated copy serves admin commands for the panel and never shows a window.
        if (args.Length == 5 && args[0] == ElevatedHelper.Switch && int.TryParse(args[4], out int panelPid))
        {
            return ElevatedHelper.RunHelperLoop(args[1], args[2], args[3], panelPid);
        }

        CrashReport.Install();
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // An exception that unwinds the UI thread ends the app; it is at least written to crash.log first.
            CrashReport.Report(ex, "UI thread");
            throw;
        }
        finally
        {
            ElevatedHelper.Stop();
        }
    }

    /// <summary>The Avalonia configuration, shared by <see cref="Main"/> and the XAML designer.</summary>
    /// <returns>A builder for <see cref="App"/> with platform detection, the Inter font and trace logging.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
