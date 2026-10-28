using Avalonia.Media;
using XamppControl.Modules;

namespace XamppControl.ViewModels;

/// <summary>One line in the panel's log, stamped with the local time it was created.</summary>
/// <param name="source">Module tag or area, e.g. "mysql" or "main"; it picks the tag's color, ignoring case.</param>
/// <param name="message">The text of the line as the user sees it.</param>
/// <param name="level">Sets the message color.</param>
public sealed class LogEntry(string source, string message, LogLevel level)
{
    /// <summary>Local time of creation as "HH:mm:ss".</summary>
    public string Time { get; } = DateTime.Now.ToString("HH:mm:ss");

    /// <summary>The source in brackets, e.g. "[mysql]".</summary>
    public string Source { get; } = $"[{source}]";

    /// <summary>The text of the line.</summary>
    public string Message { get; } = message;

    /// <summary>The severity the line was written with.</summary>
    public LogLevel Level { get; } = level;

    /// <summary>Color of the source tag: one per module, gray for anything else.</summary>
    public IBrush SourceBrush { get; } = BrushFor(source);

    /// <summary>Color of the message, by <see cref="Level"/>.</summary>
    public IBrush MessageBrush { get; } = level switch
    {
        LogLevel.Error => Brush.Parse("#B42318"),
        LogLevel.Warn => Brush.Parse("#A15C07"),
        LogLevel.Ok => Brush.Parse("#15803D"),
        LogLevel.Debug => Brush.Parse("#6B7280"),
        _ => Brush.Parse("#1F2329"),
    };

    /// <summary>The tag color for a log source.</summary>
    /// <param name="source">Module tag or area, compared ignoring case.</param>
    /// <returns>The module's color, or gray for a source that is not a module.</returns>
    private static IBrush BrushFor(string source) => source.ToLowerInvariant() switch
    {
        "apache" => Brush.Parse("#1D4ED8"),
        "update" => Brush.Parse("#0369A1"),
        "mysql" => Brush.Parse("#C2410C"),
        "filezilla" => Brush.Parse("#7C3AED"),
        "mercury" => Brush.Parse("#0F766E"),
        "tomcat" => Brush.Parse("#B45309"),
        _ => Brush.Parse("#5D6470"),
    };

    public override string ToString() => $"{Time} {Source} {Message}";
}
