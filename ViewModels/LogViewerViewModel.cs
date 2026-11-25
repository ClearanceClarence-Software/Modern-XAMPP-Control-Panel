using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia.Media;
using Avalonia.Threading;
using XamppControl.Core;
using XamppControl.Updates;

namespace XamppControl.ViewModels;

/// <summary>A log file in the Logs window's side list, with how many problems it holds.</summary>
public sealed class LogSource : ObservableObject
{
    private int _problemCount;
    private bool _hasErrors;

    /// <summary>Describes a log file.</summary>
    /// <param name="label">The name shown in the list, such as "Apache errors".</param>
    /// <param name="path">Full path of the file.</param>
    /// <param name="dotBrush">The color of the dot before the name; one per server.</param>
    public LogSource(string label, string path, IBrush dotBrush)
    {
        Label = label;
        Path = path;
        DotBrush = dotBrush;
    }

    /// <summary>The name shown in the list, such as "Apache errors".</summary>
    public string Label { get; }

    /// <summary>Full path of the file.</summary>
    public string Path { get; }

    /// <summary>The file name, shown small under the label.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>The color of the dot before the name.</summary>
    public IBrush DotBrush { get; }

    /// <summary>The number on the badge: the recent errors, or the recent warnings when there are no errors.</summary>
    public int ProblemCount
    {
        get => _problemCount;
        private set
        {
            if (SetField(ref _problemCount, value))
            {
                OnPropertyChanged(nameof(HasProblems));
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    /// <summary>True when the badge counts errors, so it is red; false when it counts warnings.</summary>
    public bool HasErrors
    {
        get => _hasErrors;
        private set => SetField(ref _hasErrors, value);
    }

    /// <summary>True when there is something to count, so the badge shows.</summary>
    public bool HasProblems => ProblemCount > 0;

    /// <summary>The badge's text; "99+" from a hundred up.</summary>
    public string CountText => ProblemCount > 99 ? "99+" : ProblemCount.ToString(CultureInfo.CurrentCulture);

    /// <summary>Counts the recent problems in the file's lines for the badge.</summary>
    /// <param name="lines">The lines read from the file.</param>
    /// <param name="since">Lines written before this don't count; lines without a time always do.</param>
    public void CountProblems(IEnumerable<LogLine> lines, DateTime since)
    {
        int errors = 0;
        int warnings = 0;
        foreach (LogLine line in lines.Where(line => line.IsProblem && (line.Time ?? DateTime.MaxValue) >= since))
        {
            if (line.IsError)
            {
                errors++;
            }
            else
            {
                warnings++;
            }
        }
        HasErrors = errors > 0;
        ProblemCount = errors > 0 ? errors : warnings;
    }

    public override string ToString() => Label;
}

/// <summary>One bar of the Logs window's timeline: the lines written in one slice of time.</summary>
/// <param name="Height">The bar's height in pixels, relative to the busiest slice.</param>
/// <param name="IsError">True when the slice holds an error; the bar is red.</param>
/// <param name="IsWarn">True when the worst in the slice is a warning; the bar is amber.</param>
/// <param name="Tip">The tooltip: the slice's times and how much it holds.</param>
/// <param name="First">The first line of the slice, where a click jumps to; null for an empty slice.</param>
public sealed record LogBucket(double Height, bool IsError, bool IsWarn, string Tip, LogLine? First);

/// <summary>
/// The Logs window: every server log in a side list with its problem count, the chosen one followed live (like
/// tail -f) with its lines split into time, level and message, and a timeline of when they were written.
/// </summary>
public sealed class LogViewerViewModel : ObservableObject, IDisposable
{
    private const int MaxLines = 2000;
    private const long TailBytes = 256 * 1024;

    // The timeline: how many bars, and how tall the busiest and an empty one are.
    private const int BucketCount = 48;
    private const double TallestBar = 34;
    private const double EmptyBar = 2;
    private const double ShortestBar = 5;

    // The files that aren't shown are read again this often, in timer ticks (seconds), to keep their badges true.
    private const int RecountEveryTicks = 15;

    // The badges count what happened lately; last month's errors are not today's problem.
    private static readonly TimeSpan _countWindow = TimeSpan.FromHours(24);

    private static readonly IBrush _apacheBrush = Brush.Parse("#3B82F6");
    private static readonly IBrush _phpBrush = Brush.Parse("#6366F1");
    private static readonly IBrush _mySqlBrush = Brush.Parse("#EA580C");
    private static readonly IBrush _mailpitBrush = Brush.Parse("#0F9488");
    private static readonly IBrush _otherBrush = Brush.Parse("#8A909A");

    private readonly DispatcherTimer _pollTimer;
    private readonly List<LogLine> _allLines = [];
    private LogSource? _selected;
    private string _filter = "";
    private string _status = "";
    private string _summary = "";
    private string _emptyText = "";
    private string _axisStart = "";
    private string _axisMiddle = "";
    private string _axisEnd = "";
    private bool _isProblemsOnly;
    private bool _isFollowing = true;
    private bool _hasTimeline;
    private int _problemCount;
    private long _position;
    private byte[] _carry = [];
    private bool _isReading;
    private bool _isCounting;
    private int _generation;
    private int _ticks;

    /// <summary>Lists the log files of every installed module and starts following the first one.</summary>
    /// <param name="main">Supplies the modules and the XAMPP folder.</param>
    public LogViewerViewModel(MainViewModel main)
    {
        foreach (ModuleViewModel module in main.Modules.Where(module => module.IsInstalled))
        {
            foreach ((string _, string relative) in module.Driver.LogFiles)
            {
                string path = System.IO.Path.Combine(main.Root, relative);
                if (File.Exists(path))
                {
                    (string label, IBrush brush) = Describe(module.Name, path);
                    Sources.Add(new LogSource(label, path, brush));
                }
            }
        }

        ClearCommand = new RelayCommand(() =>
        {
            _allLines.Clear();
            Refilter();
        });

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += async (_, _) => await OnTickAsync();
        _pollTimer.Start();

        Selected = Sources.FirstOrDefault();
        if (Selected is null)
        {
            EmptyText = Loc.T("LogViewer.NoLogFilesFound");
        }
        _ = CountOthersAsync();
    }

    /// <summary>Raised when a click on the timeline asks the list to show a line.</summary>
    public event Action<LogLine>? JumpRequested;

    /// <summary>The log files to choose from, in module order.</summary>
    public ObservableCollection<LogSource> Sources { get; } = [];

    /// <summary>The lines shown: the newest <see cref="MaxLines"/> of the file, after the filters.</summary>
    public ObservableCollection<LogLine> Lines { get; } = [];

    /// <summary>The timeline's bars, oldest first; empty when the lines carry no times.</summary>
    public ObservableCollection<LogBucket> Buckets { get; } = [];

    /// <summary>Empties the view; the file is untouched.</summary>
    public RelayCommand ClearCommand { get; }

    /// <summary>The file being followed; changing it reloads the view.</summary>
    public LogSource? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                IsFollowing = true;
                _ = LoadAsync();
            }
        }
    }

    /// <summary>Text a line must contain to be shown, ignoring case; empty shows every line.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetField(ref _filter, value))
            {
                Refilter();
            }
        }
    }

    /// <summary>True shows only warnings and errors.</summary>
    public bool IsProblemsOnly
    {
        get => _isProblemsOnly;
        set
        {
            if (SetField(ref _isProblemsOnly, value))
            {
                OnPropertyChanged(nameof(IsAllLevels));
                Refilter();
            }
        }
    }

    /// <summary>True while every level is shown; lights the "All" segment.</summary>
    public bool IsAllLevels => !IsProblemsOnly;

    /// <summary>The "Problems" segment's text with the count: "Problems · 97".</summary>
    public string ProblemsLabel => Loc.T("LogViewer.Problems", _problemCount);

    /// <summary>
    /// True keeps the newest line in view as lines arrive. A click on the timeline turns it off, so the place it
    /// jumped to stays put.
    /// </summary>
    public bool IsFollowing
    {
        get => _isFollowing;
        set
        {
            if (SetField(ref _isFollowing, value))
            {
                UpdateSummary();
            }
        }
    }

    /// <summary>True when the lines carry times, so the timeline shows.</summary>
    public bool HasTimeline
    {
        get => _hasTimeline;
        private set => SetField(ref _hasTimeline, value);
    }

    /// <summary>The time under the timeline's left end.</summary>
    public string AxisStart
    {
        get => _axisStart;
        private set => SetField(ref _axisStart, value);
    }

    /// <summary>The time under the timeline's middle.</summary>
    public string AxisMiddle
    {
        get => _axisMiddle;
        private set => SetField(ref _axisMiddle, value);
    }

    /// <summary>The time under the timeline's right end.</summary>
    public string AxisEnd
    {
        get => _axisEnd;
        private set => SetField(ref _axisEnd, value);
    }

    /// <summary>The line at the bottom left: the file's path and size, or why it can't be read.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>The line at the bottom right: "214 lines · following".</summary>
    public string Summary
    {
        get => _summary;
        private set => SetField(ref _summary, value);
    }

    /// <summary>
    /// Why no lines show (no log files, an empty file, nothing matches); empty while there are lines.
    /// </summary>
    public string EmptyText
    {
        get => _emptyText;
        private set
        {
            if (SetField(ref _emptyText, value))
            {
                OnPropertyChanged(nameof(HasEmptyText));
            }
        }
    }

    /// <summary>True when <see cref="EmptyText"/> shows in place of the lines.</summary>
    public bool HasEmptyText => _emptyText.Length > 0;

    /// <summary>Stops following and asks the list to show the first line of a timeline bar.</summary>
    /// <param name="bucket">The bar that was clicked; an empty one does nothing.</param>
    public void JumpTo(LogBucket bucket)
    {
        if (bucket.First is { } line)
        {
            IsFollowing = false;
            JumpRequested?.Invoke(line);
        }
    }

    /// <summary>The list name and dot color for a log file.</summary>
    /// <param name="moduleName">The module the file belongs to.</param>
    /// <param name="path">Full path of the file.</param>
    /// <returns>A name that says what the file holds, and its server's color.</returns>
    private static (string Label, IBrush Brush) Describe(string moduleName, string path) =>
        System.IO.Path.GetFileName(path).ToLowerInvariant() switch
        {
            "error.log" => (Loc.T("LogViewer.ApacheErrors"), _apacheBrush),
            "access.log" => (Loc.T("LogViewer.ApacheAccess"), _apacheBrush),
            "php_error_log" => (Loc.T("LogViewer.PhpErrors"), _phpBrush),
            "mysql_error.log" => (moduleName, _mySqlBrush),
            "mailpit.log" => (moduleName, _mailpitBrush),
            _ => (moduleName, _otherBrush),
        };

    /// <summary>Once a second: reads what the chosen file gained, and now and then recounts the other files.</summary>
    private async Task OnTickAsync()
    {
        await ReadNewAsync();
        if (++_ticks % RecountEveryTicks == 0)
        {
            await CountOthersAsync();
        }
    }

    /// <summary>
    /// Reads the files that aren't shown and updates their badges; the chosen one counts as it reads.
    /// </summary>
    private async Task CountOthersAsync()
    {
        if (_isCounting)
        {
            return;
        }

        _isCounting = true;
        try
        {
            foreach (LogSource source in Sources.Where(source => !ReferenceEquals(source, Selected)).ToList())
            {
                try
                {
                    List<LogLine> lines = await Task.Run(() => ReadTail(source.Path).Lines);
                    source.CountProblems(lines, DateTime.Now - _countWindow);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file that can't be read right now keeps the badge it had; the next round tries again.
                }
            }
        }
        finally
        {
            _isCounting = false;
        }
    }

    /// <summary>Reads the last part of the selected file and starts following it from the end.</summary>
    private async Task LoadAsync()
    {
        // The generation tells a read that finishes after the file changed to throw its result away.
        int generation = ++_generation;
        _allLines.Clear();
        _carry = [];
        _position = 0;
        Refilter();
        if (Selected is not { } source)
        {
            EmptyText = Loc.T("LogViewer.NoLogFilesFound");
            return;
        }

        _isReading = true;
        try
        {
            (List<LogLine> lines, long length) = await Task.Run(() => ReadTail(source.Path));
            if (generation != _generation)
            {
                return;
            }

            _allLines.AddRange(lines);
            _position = length;
            Refilter();
            Status = MockMachine.Pretty($"{source.Path}   ·   {FileTools.FormatSize(length)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The server may hold the file for a moment, or it was deleted: say so and keep trying each second.
            Status = Loc.T("LogViewer.CantRead", source.Path, ex.Message);
        }
        finally
        {
            if (generation == _generation)
            {
                _isReading = false;
            }
        }
    }

    /// <summary>Appends what the server wrote since the last read; starts over when the file was rotated.</summary>
    private async Task ReadNewAsync()
    {
        if (_isReading || Selected is not { } source)
        {
            return;
        }

        _isReading = true;
        int generation = _generation;
        try
        {
            long position = _position;
            byte[] carry = _carry;
            (List<LogLine> newLines, byte[] newCarry, long length, bool needsReload) =
                await Task.Run(() => ReadFrom(source.Path, position, carry));
            if (generation != _generation)
            {
                return;
            }
            if (needsReload)
            {
                _isReading = false;
                await LoadAsync();
                return;
            }

            _position = length;
            _carry = newCarry;
            if (newLines.Count == 0)
            {
                return;
            }

            _allLines.AddRange(newLines);
            if (_allLines.Count > MaxLines)
            {
                _allLines.RemoveRange(0, _allLines.Count - MaxLines);
            }

            foreach (LogLine line in newLines.Where(Matches))
            {
                Lines.Add(line);
            }
            while (Lines.Count > MaxLines)
            {
                Lines.RemoveAt(0);
            }
            UpdateDerived();
            Status = MockMachine.Pretty($"{source.Path}   ·   {FileTools.FormatSize(length)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The server may hold the file for a moment, or it was deleted: say so and keep trying each second.
            Status = Loc.T("LogViewer.CantRead", source.Path, ex.Message);
        }
        finally
        {
            if (generation == _generation)
            {
                _isReading = false;
            }
        }
    }

    /// <summary>Rebuilds <see cref="Lines"/> from every kept line and the current filters.</summary>
    private void Refilter()
    {
        Lines.Clear();
        foreach (LogLine line in _allLines.Where(Matches))
        {
            Lines.Add(line);
        }
        UpdateDerived();
    }

    private bool Matches(LogLine line) => MatchesText(line) && (!IsProblemsOnly || line.IsProblem);

    private bool MatchesText(LogLine line) =>
        Filter.Length == 0 || line.Text.Contains(Filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>Brings everything that is worked out from the lines up to date: counts, timeline, the footer.</summary>
    private void UpdateDerived()
    {
        _problemCount = _allLines.Count(line => line.IsProblem && MatchesText(line));
        OnPropertyChanged(nameof(ProblemsLabel));
        Selected?.CountProblems(_allLines, DateTime.Now - _countWindow);

        if (Lines.Count > 0)
        {
            EmptyText = "";
        }
        else if (Selected is null)
        {
            EmptyText = Loc.T("LogViewer.NoLogFilesFound");
        }
        else
        {
            EmptyText = _allLines.Count > 0 ? Loc.T("LogViewer.NoLinesMatch") : Loc.T("LogViewer.NoLinesYet");
        }

        RebuildTimeline();
        UpdateSummary();
    }

    private void UpdateSummary() =>
        Summary = Lines.Count == 0
            ? ""
            : Loc.T(
                IsFollowing ? "LogViewer.SummaryFollowing" : "LogViewer.SummaryPaused",
                Loc.Plural("LogViewer.Lines", Lines.Count));

    /// <summary>Cuts the time the shown lines span into equal slices and makes one bar for each.</summary>
    private void RebuildTimeline()
    {
        List<LogBucket> buckets = BuildBuckets(Lines);
        Buckets.Clear();
        foreach (LogBucket bucket in buckets)
        {
            Buckets.Add(bucket);
        }
        HasTimeline = buckets.Count > 0;
    }

    /// <summary>The timeline's bars for a set of lines.</summary>
    /// <param name="lines">The lines shown, oldest first.</param>
    /// <returns><see cref="BucketCount"/> bars; none when fewer than two different times are known.</returns>
    private List<LogBucket> BuildBuckets(IReadOnlyList<LogLine> lines)
    {
        List<DateTime> times = lines.Select(line => line.Time).OfType<DateTime>().ToList();
        if (times.Count < 2 || times.Max() - times.Min() < TimeSpan.FromSeconds(1))
        {
            return [];
        }

        DateTime start = times.Min();
        TimeSpan span = times.Max() - start;
        var counts = new int[BucketCount];
        var problems = new int[BucketCount];
        var worst = new LogSeverity[BucketCount];
        var first = new LogLine?[BucketCount];

        // A line without a time (a stack trace line) belongs to the line before it.
        DateTime current = start;
        foreach (LogLine line in lines)
        {
            current = line.Time ?? current;
            int slice = Math.Clamp((int)((current - start).Ticks * BucketCount / span.Ticks), 0, BucketCount - 1);
            counts[slice]++;
            problems[slice] += line.IsProblem ? 1 : 0;
            first[slice] ??= line;
            if (line.Severity > worst[slice])
            {
                worst[slice] = line.Severity;
            }
        }

        int busiest = counts.Max();
        var buckets = new List<LogBucket>(BucketCount);
        for (int slice = 0; slice < BucketCount; slice++)
        {
            DateTime from = start + (span * slice / BucketCount);
            DateTime until = start + (span * (slice + 1) / BucketCount);
            double height = counts[slice] == 0
                ? EmptyBar
                : ShortestBar + ((TallestBar - ShortestBar) * counts[slice] / busiest);
            string tip = Loc.T(
                "LogViewer.BucketTip",
                AxisTime(from, span),
                AxisTime(until, span),
                Loc.Plural("LogViewer.Lines", counts[slice]),
                Loc.Plural("LogViewer.ProblemsCount", problems[slice]));
            buckets.Add(new LogBucket(
                height,
                worst[slice] >= LogSeverity.Error,
                worst[slice] == LogSeverity.Warning,
                tip,
                first[slice]));
        }

        AxisStart = AxisTime(start, span);
        AxisMiddle = AxisTime(start + (span / 2), span);
        AxisEnd = AxisTime(start + span, span);
        return buckets;
    }

    /// <summary>A time for the timeline's axis: with the date when the timeline spans more than a day.</summary>
    /// <param name="time">The time to show.</param>
    /// <param name="span">How much time the whole timeline covers.</param>
    /// <returns>"14:52:37" within a day, "24 Nov 11:05" across days.</returns>
    private static string AxisTime(DateTime time, TimeSpan span) =>
        span < TimeSpan.FromHours(24) && time.Date == DateTime.Today
            ? time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : time.ToString("d MMM HH:mm", CultureInfo.CurrentCulture);

    /// <summary>The last <see cref="TailBytes"/> of a file as lines, and the file's length.</summary>
    /// <param name="path">Full path of the log file.</param>
    /// <returns>The lines, parsed, oldest first, without a partial first line; and the length read up to.</returns>
    private static (List<LogLine> Lines, long Length) ReadTail(string path)
    {
        using FileStream stream = OpenShared(path);
        long length = stream.Length;
        long start = Math.Max(0, length - TailBytes);
        stream.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[length - start];
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        List<string> lines = SplitLines(Encoding.UTF8.GetString(buffer, 0, read));
        if (start > 0 && lines.Count > 0)
        {
            lines.RemoveAt(0);
        }
        return (lines.TakeLast(MaxLines).Select(LogLineParser.Parse).ToList(), start + read);
    }

    /// <summary>The complete lines written after a position.</summary>
    /// <param name="path">Full path of the log file.</param>
    /// <param name="position">Where the previous read stopped.</param>
    /// <param name="carry">Bytes of an unfinished last line from the previous read.</param>
    /// <returns>
    /// The new lines, parsed; the bytes of the still unfinished last line; the position to read from next; and true when
    /// the view should start over because the file shrank (rotated or cleared) or grew by more than the tail size.
    /// </returns>
    private static (List<LogLine> Lines, byte[] Carry, long Length, bool NeedsReload) ReadFrom(
        string path,
        long position,
        byte[] carry)
    {
        using FileStream stream = OpenShared(path);
        long length = stream.Length;
        if (length < position || length - position > TailBytes)
        {
            return ([], [], 0, true);
        }
        if (length == position)
        {
            return ([], carry, position, false);
        }

        stream.Seek(position, SeekOrigin.Begin);
        var fresh = new byte[length - position];
        int read = stream.ReadAtLeast(fresh, fresh.Length, throwOnEndOfStream: false);
        byte[] bytes = [.. carry, .. fresh.AsSpan(0, read)];

        // A line still being written has no line break yet; its bytes wait for the next read, so a multibyte
        // character split between two reads is decoded whole.
        int lastBreak = Array.LastIndexOf(bytes, (byte)'\n');
        if (lastBreak < 0)
        {
            return ([], bytes, position + read, false);
        }

        string text = Encoding.UTF8.GetString(bytes, 0, lastBreak);
        List<LogLine> lines = SplitLines(text).Select(LogLineParser.Parse).ToList();
        return (lines, bytes[(lastBreak + 1)..], position + read, false);
    }

    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static List<string> SplitLines(string text) =>
        text.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0)
            .ToList();

    /// <summary>Stops following the file.</summary>
    public void Dispose() => _pollTimer.Stop();
}
