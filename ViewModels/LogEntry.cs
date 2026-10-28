using XamppControl.Modules;

namespace XamppControl.ViewModels;

/// <summary>One line in the panel's log, stamped with the local time it was created.</summary>
/// <param name="source">Module tag or area, e.g. "mysql" or "main"; it picks the tag's color, ignoring case.</param>
/// <param name="message">The text of the line as the user sees it.</param>
/// <param name="level">Sets the message color.</param>
/// <remarks>
/// The colors themselves live in the theme (Styles\Theme.axaml): the log template turns <see cref="Level"/> and
/// <see cref="SourceKey"/> into style classes, so switching between light and dark repaints every line.
/// </remarks>
public sealed class LogEntry(string source, string message, LogLevel level)
{
    /// <summary>Local time of creation as "HH:mm:ss".</summary>
    public string Time { get; } = DateTime.Now.ToString("HH:mm:ss");

    /// <summary>The source in brackets, e.g. "[mysql]".</summary>
    public string Source { get; } = $"[{source}]";

    /// <summary>The source in lowercase, e.g. "mysql"; picks the tag's color class.</summary>
    public string SourceKey { get; } = source.ToLowerInvariant();

    /// <summary>The text of the line.</summary>
    public string Message { get; } = message;

    /// <summary>The severity the line was written with.</summary>
    public LogLevel Level { get; } = level;

    /// <summary>True for an error; the message shows in the error color.</summary>
    public bool IsError => Level == LogLevel.Error;

    /// <summary>True for a warning; the message shows in the warning color.</summary>
    public bool IsWarn => Level == LogLevel.Warn;

    /// <summary>True for a success; the message shows in the success color.</summary>
    public bool IsOk => Level == LogLevel.Ok;

    /// <summary>True for a debug line; the message shows dimmed.</summary>
    public bool IsDebug => Level == LogLevel.Debug;

    /// <summary>True when the line comes from Apache.</summary>
    public bool IsApache => SourceKey == "apache";

    /// <summary>True when the line comes from MySQL/MariaDB.</summary>
    public bool IsMySql => SourceKey == "mysql";

    /// <summary>True when the line comes from FileZilla.</summary>
    public bool IsFileZilla => SourceKey == "filezilla";

    /// <summary>True when the line comes from Mercury.</summary>
    public bool IsMercury => SourceKey == "mercury";

    /// <summary>True when the line comes from Tomcat.</summary>
    public bool IsTomcat => SourceKey == "tomcat";

    /// <summary>True when the line comes from an update or version switch.</summary>
    public bool IsUpdate => SourceKey == "update";

    /// <summary>True when the line comes from the Sites window.</summary>
    public bool IsSites => SourceKey == "sites";

    /// <summary>True when the line comes from the PHP settings window.</summary>
    public bool IsPhp => SourceKey == "php";

    public override string ToString() => $"{Time} {Source} {Message}";
}
