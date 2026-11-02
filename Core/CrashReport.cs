using System.Text;

namespace XamppControl.Core;

/// <summary>
/// Catches exceptions nothing else handled, writes them to a crash log in the settings folder and hands the text to
/// whoever shows the user a dialog, so a crash becomes a bug report instead of a window that vanished.
/// </summary>
public static class CrashReport
{
    private static int _reportsShown;

    /// <summary>The file every unhandled exception is appended to.</summary>
    public static string LogPath => Path.Combine(AppSettings.Folder, "crash.log");

    /// <summary>
    /// Called with the report text on the thread the exception was caught on. Set by the app to show the dialog;
    /// the log is written whether or not it is set.
    /// </summary>
    public static Action<string>? Shown { get; set; }

    /// <summary>Hooks the process-wide events that fire for exceptions no handler caught.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Report(args.ExceptionObject as Exception ?? new InvalidOperationException("Unknown exception"), "process");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Report(args.Exception, "background task");
            args.SetObserved();
        };
    }

    /// <summary>Writes an exception to the crash log and shows it, at most three times per run.</summary>
    /// <param name="exception">The exception; inner exceptions are included.</param>
    /// <param name="where">Where it was caught, such as "process" or "UI thread", for the report's first line.</param>
    public static void Report(Exception exception, string where)
    {
        string text = Describe(exception, where);
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.AppendAllText(LogPath, text + Environment.NewLine + new string('-', 78) + Environment.NewLine);
        }
        catch
        {
            // The disk may be full or the folder locked; the dialog still shows the report, so nothing is lost.
        }

        // After a few reports the app is in a loop of failures; more dialogs would only stack up.
        if (Interlocked.Increment(ref _reportsShown) <= 3)
        {
            Shown?.Invoke(text);
        }
    }

    /// <summary>The report text: version, time, where, then the exception chain with stack traces.</summary>
    /// <param name="exception">The exception to describe.</param>
    /// <param name="where">Where it was caught.</param>
    /// <returns>Plain text that reads well in a dialog and in the log file.</returns>
    public static string Describe(Exception exception, string where)
    {
        var text = new StringBuilder();
        text.AppendLine($"{BuildInfo.AppName} {BuildInfo.Version} crashed in the {where}.");
        text.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}   Windows: {Environment.OSVersion.VersionString}");
        text.AppendLine();

        Exception? current = exception;
        while (current is not null)
        {
            text.AppendLine($"{current.GetType().FullName}: {current.Message}");
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                text.AppendLine(current.StackTrace);
            }
            current = current.InnerException;
            if (current is not null)
            {
                text.AppendLine().AppendLine("Caused by:");
            }
        }
        return text.ToString().TrimEnd();
    }
}
