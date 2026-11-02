using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using XamppControl.Updates;

namespace XamppControl.ViewModels;

/// <summary>A log file the viewer can follow.</summary>
/// <param name="Label">The name shown in the list, such as "Apache error.log".</param>
/// <param name="Path">Full path of the file.</param>
public sealed record LogSource(string Label, string Path)
{
    public override string ToString() => Label;
}

/// <summary>
/// The Logs window: follows a server log file live (like tail -f), with a filter and the usual error coloring.
/// </summary>
public sealed class LogViewerViewModel : ObservableObject, IDisposable
{
    private const int MaxLines = 2000;
    private const long TailBytes = 256 * 1024;

    private readonly DispatcherTimer _pollTimer;
    private readonly List<string> _allLines = [];
    private LogSource? _selected;
    private string _filter = "";
    private string _status = "";
    private long _position;
    private byte[] _carry = [];
    private bool _isReading;
    private int _generation;

    /// <summary>Lists the log files of every installed module and starts following the first one.</summary>
    /// <param name="main">Supplies the modules and the XAMPP folder.</param>
    public LogViewerViewModel(MainViewModel main)
    {
        foreach (ModuleViewModel module in main.Modules.Where(module => module.IsInstalled))
        {
            foreach ((string label, string relative) in module.Driver.LogFiles)
            {
                string path = System.IO.Path.Combine(main.Root, relative);
                if (File.Exists(path))
                {
                    Sources.Add(new LogSource($"{module.Name}: {label}", path));
                }
            }
        }

        ClearCommand = new RelayCommand(() =>
        {
            _allLines.Clear();
            Refilter();
        });

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pollTimer.Tick += async (_, _) => await ReadNewAsync();
        _pollTimer.Start();

        Selected = Sources.FirstOrDefault();
        if (Selected is null)
        {
            Status = "No log files found. Start a module and its log appears here.";
        }
    }

    /// <summary>The log files to choose from, grouped by module.</summary>
    public ObservableCollection<LogSource> Sources { get; } = [];

    /// <summary>The lines shown: the newest <see cref="MaxLines"/> of the file, after the filter.</summary>
    public ObservableCollection<LogLine> Lines { get; } = [];

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

    /// <summary>The status line: the file's path and size, or why it can't be read.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
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
            Status = "No log files found. Start a module and its log appears here.";
            return;
        }

        _isReading = true;
        try
        {
            (List<string> lines, long length) = await Task.Run(() => ReadTail(source.Path));
            if (generation != _generation)
            {
                return;
            }

            _allLines.AddRange(lines);
            _position = length;
            Refilter();
            Status = $"{source.Path}   ·   {FileTools.FormatSize(length)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Can't read {source.Path}: {ex.Message}";
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
            (List<string> newLines, byte[] newCarry, long length, bool needsReload) =
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

            foreach (string line in newLines.Where(Matches))
            {
                Lines.Add(new LogLine(line));
            }
            while (Lines.Count > MaxLines)
            {
                Lines.RemoveAt(0);
            }
            Status = $"{source.Path}   ·   {FileTools.FormatSize(length)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Can't read {source.Path}: {ex.Message}";
        }
        finally
        {
            if (generation == _generation)
            {
                _isReading = false;
            }
        }
    }

    /// <summary>Rebuilds <see cref="Lines"/> from every kept line and the current filter.</summary>
    private void Refilter()
    {
        Lines.Clear();
        foreach (string line in _allLines.Where(Matches))
        {
            Lines.Add(new LogLine(line));
        }
    }

    private bool Matches(string line) =>
        Filter.Length == 0 || line.Contains(Filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>The last <see cref="TailBytes"/> of a file as lines, and the file's length.</summary>
    /// <param name="path">Full path of the log file.</param>
    /// <returns>The lines, oldest first, without a partial first line; and the length read up to.</returns>
    private static (List<string> Lines, long Length) ReadTail(string path)
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
        return (lines.TakeLast(MaxLines).ToList(), start + read);
    }

    /// <summary>The complete lines written after a position.</summary>
    /// <param name="path">Full path of the log file.</param>
    /// <param name="position">Where the previous read stopped.</param>
    /// <param name="carry">Bytes of an unfinished last line from the previous read.</param>
    /// <returns>
    /// The new lines; the bytes of the still unfinished last line; the position to read from next; and true when
    /// the view should start over because the file shrank (rotated or cleared) or grew by more than the tail size.
    /// </returns>
    private static (List<string> Lines, byte[] Carry, long Length, bool NeedsReload) ReadFrom(
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
        return (SplitLines(text), bytes[(lastBreak + 1)..], position + read, false);
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

/// <summary>One line of a server log, classified for coloring.</summary>
/// <param name="text">The line as written.</param>
public sealed class LogLine(string text)
{
    /// <summary>The line as written.</summary>
    public string Text { get; } = text;

    /// <summary>True for lines Apache, PHP or MariaDB mark as errors or fatal.</summary>
    public bool IsError { get; } =
        ContainsAny(text, "[error]", "[crit]", "[alert]", "[emerg]", "[ERROR]", "Fatal error", "PHP Fatal");

    /// <summary>True for lines marked as warnings or notices.</summary>
    public bool IsWarn { get; } =
        ContainsAny(text, "[warn]", "[Warning]", "PHP Warning", "PHP Notice", "[notice]", "Deprecated");

    private static bool ContainsAny(string line, params string[] markers) =>
        markers.Any(marker => line.Contains(marker, StringComparison.Ordinal));
}
