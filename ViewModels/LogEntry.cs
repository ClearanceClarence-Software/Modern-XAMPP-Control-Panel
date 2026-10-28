using Avalonia.Media;
using XamppControl.Modules;

namespace XamppControl.ViewModels;

public sealed class LogEntry(string source, string message, LogLevel level)
{
    public string Time { get; } = DateTime.Now.ToString("HH:mm:ss");
    public string Source { get; } = $"[{source}]";
    public string Message { get; } = message;
    public LogLevel Level { get; } = level;

    public IBrush SourceBrush { get; } = BrushFor(source);
    public IBrush MessageBrush { get; } = level switch
    {
        LogLevel.Error => Brush.Parse("#B42318"),
        LogLevel.Warn => Brush.Parse("#A15C07"),
        LogLevel.Ok => Brush.Parse("#15803D"),
        LogLevel.Debug => Brush.Parse("#6B7280"),
        _ => Brush.Parse("#1F2329"),
    };

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
