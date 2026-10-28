using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using XamppControl.Core;

namespace XamppControl.ViewModels;

public enum SocketState
{
    Active,
    New,
    Old,
}

public sealed class NetstatRow(Listener listener, string name, string path, bool isXampp, SocketState state)
{
    public string Address { get; } = listener.Address;
    public int Port { get; } = listener.Port;
    public int Pid { get; } = listener.Pid;
    public string Name { get; } = name;
    public string Path { get; } = path;
    public bool IsXampp { get; } = isXampp;
    public SocketState State { get; } = state;

    public string Key => $"{Address}|{Port}|{Pid}";

    // XAMPP's colors: green for new sockets, dark red for sockets that just closed.
    public IBrush RowBrush { get; } = state switch
    {
        SocketState.New => Brush.Parse("#22C55E"),
        SocketState.Old => Brush.Parse("#7F1D1D"),
        _ => Brushes.Transparent,
    };

    public IBrush TextBrush { get; } = state switch
    {
        SocketState.New => Brush.Parse("#052E16"),
        SocketState.Old => Brushes.White,
        _ => Brush.Parse("#1F2329"),
    };
}

/// <summary>
/// Netstat - TCP Listening sockets. Refreshes every few seconds; sockets that appeared since the last
/// refresh are "new" (green), sockets that disappeared stay one round as "old" (dark red).
/// </summary>
public sealed class NetstatViewModel : ObservableObject, IDisposable
{
    private readonly string _root;
    private readonly DispatcherTimer _timer;
    private Dictionary<string, Listener> _previous = new();
    private bool _first = true;
    private bool _busy;
    private string _summary = "";

    public NetstatViewModel(string root)
    {
        _root = root;
        RefreshCommand = new RelayCommand(RefreshAsync);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    public ObservableCollection<NetstatRow> Rows { get; } = [];
    public RelayCommand RefreshCommand { get; }

    public string Summary
    {
        get => _summary;
        private set => SetField(ref _summary, value);
    }

    private async Task RefreshAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var previous = _previous;
            var first = _first;
            var (rows, current) = await Task.Run(() => Build(previous, first));
            _previous = current;
            _first = false;

            Rows.Clear();
            foreach (var row in rows)
                Rows.Add(row);

            var fresh = rows.Count(r => r.State == SocketState.New);
            var gone = rows.Count(r => r.State == SocketState.Old);
            Summary = $"{current.Count} listening sockets · {fresh} new · {gone} closed · {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            _busy = false;
        }
    }

    private (List<NetstatRow> Rows, Dictionary<string, Listener> Current) Build(
        Dictionary<string, Listener> previous, bool first)
    {
        var listeners = PortTable.ListAll();
        var current = new Dictionary<string, Listener>();
        foreach (var l in listeners)
            current[Key(l)] = l;

        var names = new Dictionary<int, (string Name, string Path)>();
        (string Name, string Path) Describe(int pid)
        {
            if (names.TryGetValue(pid, out var known))
                return known;
            var path = ProcessScan.PathOf(pid) ?? "";
            var name = path.Length > 0
                ? System.IO.Path.GetFileName(path)
                : pid switch
                {
                    0 => "System Idle Process",
                    4 => "System",
                    _ => (ProcessScan.NameOf(pid) is { } n ? n + ".exe" : "?"),
                };
            return names[pid] = (name, path);
        }

        var rows = new List<NetstatRow>();
        foreach (var (key, l) in current)
        {
            var (name, path) = Describe(l.Pid);
            var state = !first && !previous.ContainsKey(key) ? SocketState.New : SocketState.Active;
            rows.Add(new NetstatRow(l, name, path, IsXampp(path), state));
        }
        foreach (var (key, l) in previous)
        {
            if (current.ContainsKey(key))
                continue;
            var (name, path) = Describe(l.Pid);
            rows.Add(new NetstatRow(l, name, path, IsXampp(path), SocketState.Old));
        }

        // Same order as XAMPP: IPv4 before IPv6, then by address and port.
        var ordered = rows
            .OrderBy(r => r.Address.Contains(':'))
            .ThenBy(r => r.Address, StringComparer.Ordinal)
            .ThenBy(r => r.Port)
            .ToList();
        return (ordered, current);
    }

    private bool IsXampp(string path) =>
        path.Length > 0 && path.StartsWith(_root, StringComparison.OrdinalIgnoreCase);

    private static string Key(Listener l) => $"{l.Address}|{l.Port}|{l.Pid}";

    public void Dispose() => _timer.Stop();
}
