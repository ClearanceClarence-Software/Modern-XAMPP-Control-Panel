using System.Globalization;
using System.Text.RegularExpressions;

namespace XamppControl.ViewModels;

/// <summary>How serious a server log line is, from least to most.</summary>
public enum LogSeverity
{
    /// <summary>Routine output: info, debug, a served request.</summary>
    Info,

    /// <summary>A notice or note: worth a line, not a problem.</summary>
    Notice,

    /// <summary>A warning, or a request the server refused (4xx).</summary>
    Warning,

    /// <summary>An error, or a request the server failed (5xx).</summary>
    Error,

    /// <summary>Critical, alert or emergency: the server itself is in trouble.</summary>
    Critical,
}

/// <summary>One line of a server log, split into time, level and message for the Logs window.</summary>
/// <param name="text">The line as written.</param>
/// <param name="time">When it was written; null when the line carries no time, like a stack trace line.</param>
/// <param name="severity">How serious it is.</param>
/// <param name="levelText">The level label, e.g. "WARN" or "404"; empty when the line names no level.</param>
/// <param name="message">The line without its time, level and process prefix.</param>
public sealed class LogLine(string text, DateTime? time, LogSeverity severity, string levelText, string message)
{
    /// <summary>The line as written; what Copy puts on the clipboard and the filter searches.</summary>
    public string Text { get; } = text;

    /// <summary>When the line was written; null when it carries no time.</summary>
    public DateTime? Time { get; } = time;

    /// <summary>
    /// The time for the list: "14:52:37" for today, "24 Nov 11:05" for another day, empty without one.
    /// </summary>
    public string TimeText { get; } = FormatTime(time);

    /// <summary>How serious the line is.</summary>
    public LogSeverity Severity { get; } = severity;

    /// <summary>The level label, e.g. "WARN" or "404"; empty when the line names no level.</summary>
    public string LevelText { get; } = levelText;

    /// <summary>True when there is a level label to show.</summary>
    public bool HasLevel => LevelText.Length > 0;

    /// <summary>The line without its time, level and process prefix.</summary>
    public string Message { get; } = message;

    /// <summary>True for errors and worse; the line is tinted and marked in red.</summary>
    public bool IsError => Severity >= LogSeverity.Error;

    /// <summary>True for warnings; the level label is amber.</summary>
    public bool IsWarn => Severity == LogSeverity.Warning;

    /// <summary>True for notices and routine output; the line is grayed so problems stand out.</summary>
    public bool IsQuiet => Severity <= LogSeverity.Notice;

    /// <summary>True for warnings and worse: what the "Problems" filter keeps and the counts add up.</summary>
    public bool IsProblem => Severity >= LogSeverity.Warning;

    private static string FormatTime(DateTime? time)
    {
        if (time is not { } written)
        {
            return "";
        }
        return written.Date == DateTime.Today
            ? written.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : written.ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
    }
}

/// <summary>Reads the line formats of the logs XAMPP's servers write: Apache, PHP, MariaDB and Mailpit.</summary>
public static partial class LogLineParser
{
    private static readonly string[] _errorMarkers =
        ["[error]", "[crit]", "[alert]", "[emerg]", "[ERROR]", "Fatal error", "PHP Fatal", "PHP Parse error"];

    private static readonly string[] _warningMarkers = ["[warn]", "[Warning]", "PHP Warning"];

    /// <summary>Splits a log line into time, level and message.</summary>
    /// <param name="text">The line as written, without its line break.</param>
    /// <returns>
    /// The parsed line. A line in none of the known formats keeps its whole text as the message, without a time,
    /// and takes its severity from the words in it.
    /// </returns>
    public static LogLine Parse(string text) =>
        ParseApacheError(text)
        ?? ParsePhp(text)
        ?? ParseMariaDb(text)
        ?? ParseAccess(text)
        ?? ParseMailpit(text)
        ?? new LogLine(text, null, GuessSeverity(text), "", text);

    // [Wed Nov 25 14:52:37.724415 2026] [ssl:warn] [pid 21304:tid 392] AH01906: message
    private static LogLine? ParseApacheError(string text)
    {
        Match match = ApacheErrorLine().Match(text);
        if (!match.Success)
        {
            return null;
        }

        string stamp = $"{match.Groups["month"].Value} {match.Groups["day"].Value} {match.Groups["year"].Value} " +
            match.Groups["time"].Value;
        (LogSeverity severity, string label) = Level(match.Groups["level"].Value);

        // The error code stays in the raw line (tooltip, Copy, the filter); in the list it is noise before the words.
        string message = ApacheCode().Replace(match.Groups["rest"].Value, "", 1);
        return new LogLine(text, ParseTime(stamp, "MMM d yyyy HH:mm:ss"), severity, label, message);
    }

    // [25-Nov-2026 14:52:37 Europe/Oslo] PHP Warning:  message
    private static LogLine? ParsePhp(string text)
    {
        Match match = PhpLine().Match(text);
        if (!match.Success)
        {
            return null;
        }

        string kind = match.Groups["kind"].Value;
        (LogSeverity severity, string label) = kind switch
        {
            "Fatal error" or "Parse error" or "Recoverable fatal error" => (LogSeverity.Error, "FATAL"),
            "Warning" => (LogSeverity.Warning, "WARN"),
            "Notice" => (LogSeverity.Notice, "NOTICE"),
            "Deprecated" or "Strict Standards" => (LogSeverity.Notice, "DEPREC"),
            _ => (LogSeverity.Info, ""),
        };
        return new LogLine(
            text,
            ParseTime(match.Groups["stamp"].Value, "dd-MMM-yyyy HH:mm:ss"),
            severity,
            label,
            match.Groups["rest"].Value);
    }

    // 2026-11-25  9:17:49 0 [Note] InnoDB: message
    private static LogLine? ParseMariaDb(string text)
    {
        Match match = MariaDbLine().Match(text);
        if (!match.Success)
        {
            return null;
        }

        (LogSeverity severity, string label) = Level(match.Groups["level"].Value);
        string stamp = $"{match.Groups["date"].Value} {match.Groups["time"].Value.Trim()}";
        return new LogLine(text, ParseTime(stamp, "yyyy-MM-dd H:mm:ss"), severity, label, match.Groups["rest"].Value);
    }

    // ::1 - - [25/Nov/2026:14:52:37 +0100] "GET /shop/ HTTP/1.1" 200 5123
    private static LogLine? ParseAccess(string text)
    {
        Match match = AccessLine().Match(text);
        if (!match.Success)
        {
            return null;
        }

        string status = match.Groups["status"].Value;
        LogSeverity severity = status[0] switch
        {
            '5' => LogSeverity.Error,
            '4' => LogSeverity.Warning,
            _ => LogSeverity.Info,
        };
        string message = $"{match.Groups["request"].Value}   {match.Groups["client"].Value}";
        DateTime? time = ParseTime(match.Groups["stamp"].Value, "dd/MMM/yyyy:HH:mm:ss");
        return new LogLine(text, time, severity, status, message);
    }

    // time="2026-11-25T14:52:37+01:00" level=info msg="[smtpd] starting on 127.0.0.1:1025"
    private static LogLine? ParseMailpit(string text)
    {
        Match match = MailpitLine().Match(text);
        if (!match.Success)
        {
            return null;
        }

        (LogSeverity severity, string label) = Level(match.Groups["level"].Value);
        DateTime? time = DateTimeOffset.TryParse(
            match.Groups["stamp"].Value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTimeOffset written)
            ? written.LocalDateTime
            : null;
        return new LogLine(text, time, severity, label, match.Groups["message"].Value);
    }

    /// <summary>The severity and label for a level word as Apache, MariaDB or Mailpit write it.</summary>
    /// <param name="word">The level, in any case: "warn", "Warning", "ERROR", "Note"...</param>
    /// <returns>The severity, and the label shown in the list.</returns>
    private static (LogSeverity Severity, string Label) Level(string word) =>
        word.ToLowerInvariant() switch
        {
            "emerg" or "alert" or "crit" or "fatal" or "panic" => (LogSeverity.Critical, "CRIT"),
            "error" => (LogSeverity.Error, "ERROR"),
            "warn" or "warning" => (LogSeverity.Warning, "WARN"),
            "notice" or "note" => (LogSeverity.Notice, "NOTICE"),
            "debug" => (LogSeverity.Info, "DEBUG"),
            _ => (LogSeverity.Info, "INFO"),
        };

    private static LogSeverity GuessSeverity(string text)
    {
        if (_errorMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            return LogSeverity.Error;
        }
        return _warningMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal))
            ? LogSeverity.Warning
            : LogSeverity.Info;
    }

    private static DateTime? ParseTime(string stamp, string format) =>
        DateTime.TryParseExact(stamp, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time)
            ? time
            : null;

    [GeneratedRegex(
        @"^\[\w{3} (?<month>\w{3}) (?<day>\d{1,2}) (?<time>\d{2}:\d{2}:\d{2})(?:\.\d+)? (?<year>\d{4})\] " +
        @"\[[\w.-]*:(?<level>\w+)\] (?:\[pid [^\]]*\] )?(?:\[client [^\]]*\] )?(?<rest>.*)$")]
    private static partial Regex ApacheErrorLine();

    [GeneratedRegex(@"^AH\d{5}: ")]
    private static partial Regex ApacheCode();

    [GeneratedRegex(
        @"^\[(?<stamp>\d{2}-\w{3}-\d{4} \d{2}:\d{2}:\d{2})(?: [^\]]+)?\] (?:PHP (?<kind>[A-Za-z ]+):\s+)?(?<rest>.*)$")]
    private static partial Regex PhpLine();

    [GeneratedRegex(@"^(?<date>\d{4}-\d{2}-\d{2}) (?<time>[ \d]\d:\d{2}:\d{2}) \d+ \[(?<level>\w+)\] (?<rest>.*)$")]
    private static partial Regex MariaDbLine();

    [GeneratedRegex(
        @"^(?<client>\S+) \S+ \S+ \[(?<stamp>\d{2}/\w{3}/\d{4}:\d{2}:\d{2}:\d{2}) [+-]\d{4}\] " +
        @"""(?<request>[^""]*)"" (?<status>\d{3})\b")]
    private static partial Regex AccessLine();

    [GeneratedRegex(@"^(?:time=""(?<stamp>[^""]*)"" )?level=(?<level>\w+) msg=""(?<message>.*?)""(?: \w+=.*)?$")]
    private static partial Regex MailpitLine();
}
