using System.Collections.ObjectModel;
using System.Globalization;
using XamppControl.Core;
using XamppControl.Databases;
using XamppControl.Modules;
using XamppControl.Updates;

namespace XamppControl.ViewModels;

/// <summary>One database in the list, with what the detail pane shows about it.</summary>
public sealed class DatabaseRow : ObservableObject
{
    // The width of the size bar's track in DatabasesWindow.axaml; the bar is a share of it.
    private const double BarTrackWidth = 120;

    // A database with any data still gets a visible sliver.
    private const double SmallestBar = 4;

    // phpMyAdmin's own storage; XAMPP ships it, so it is marked rather than hidden.
    private const string PhpMyAdminSchema = "phpmyadmin";

    private DumpFile? _lastExport;
    private string _dropWarning = "";

    /// <summary>Wraps a database for the list.</summary>
    /// <param name="database">The database as the server reported it.</param>
    /// <param name="largestBytes">The size of the largest database listed; the size bars are relative to it.</param>
    /// <param name="lastExport">The newest dump of this database; null when there is none.</param>
    public DatabaseRow(DatabaseInfo database, long largestBytes, DumpFile? lastExport)
    {
        Info = database;
        _lastExport = lastExport;
        BarWidth = database.SizeBytes <= 0 || largestBytes <= 0
            ? 0
            : Math.Max(SmallestBar, BarTrackWidth * database.SizeBytes / largestBytes);
    }

    /// <summary>The database as the server reported it.</summary>
    public DatabaseInfo Info { get; }

    /// <summary>The schema name.</summary>
    public string Name => Info.Name;

    /// <summary>"3 tables" or "1 table", under the name in the list.</summary>
    public string TableText => Info.TableText;

    /// <summary>The table count alone, for the detail pane's tile.</summary>
    public string TableCount => Info.Tables.ToString(CultureInfo.CurrentCulture);

    /// <summary>"table" or "tables", under <see cref="TableCount"/>.</summary>
    public string TableWord => Loc.Plural("Databases.TableWord", Info.Tables);

    /// <summary>The size, e.g. "12.0 MB".</summary>
    public string Size => Info.Size;

    /// <summary>The default collation; empty when the server didn't say.</summary>
    public string Collation => Info.Collation;

    /// <summary>True when the collation line has something to show.</summary>
    public bool HasCollation => Collation.Length > 0;

    /// <summary>True for the database XAMPP ships for phpMyAdmin; it gets a "XAMPP" label.</summary>
    public bool IsXampp => string.Equals(Name, PhpMyAdminSchema, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for a database without tables; it gets an "empty" label.</summary>
    public bool IsEmpty => Info.Tables == 0;

    /// <summary>True for entries drawn in gray so the user's own databases stand out.</summary>
    public bool IsQuiet => IsXampp || IsEmpty;

    /// <summary>The size bar's width in pixels, relative to the largest database.</summary>
    public double BarWidth { get; }

    /// <summary>When the newest dump was written: "today 10:40", a date, or "Not exported yet".</summary>
    public string LastExportText
    {
        get
        {
            if (_lastExport is not { } dump)
            {
                return Loc.T("Databases.NotExportedYet");
            }
            return dump.Written.Date == DateTime.Today
                ? Loc.T("Databases.TodayAt", dump.Written.ToString("HH:mm", CultureInfo.InvariantCulture))
                : dump.Written.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The newest dump's file name; empty when there is none.</summary>
    public string LastExportFile => _lastExport?.Name ?? "";

    /// <summary>True when a dump of this database exists.</summary>
    public bool HasLastExport => _lastExport is not null;

    /// <summary>The newest dump; null when there is none.</summary>
    public DumpFile? LastExport
    {
        get => _lastExport;
        set
        {
            if (SetField(ref _lastExport, value))
            {
                OnPropertyChanged(nameof(LastExportText));
                OnPropertyChanged(nameof(LastExportFile));
                OnPropertyChanged(nameof(HasLastExport));
            }
        }
    }

    /// <summary>The import button's text: "Import into shop…".</summary>
    public string ImportLabel => Loc.T("Databases.ImportIntoName", Name);

    /// <summary>What dropping would delete, shown after the first click on Drop; empty otherwise.</summary>
    public string DropWarning
    {
        get => _dropWarning;
        set
        {
            if (SetField(ref _dropWarning, value))
            {
                OnPropertyChanged(nameof(HasDropWarning));
                OnPropertyChanged(nameof(DropLabel));
            }
        }
    }

    /// <summary>True between the first and the second click on Drop.</summary>
    public bool HasDropWarning => _dropWarning.Length > 0;

    /// <summary>The drop button's text: "Drop database", then "Yes, drop shop".</summary>
    public string DropLabel => HasDropWarning ? Loc.T("Databases.YesDrop", Name) : Loc.T("Databases.DropDatabase");
}

/// <summary>
/// The Databases window: the server's databases in a list, and the chosen one's facts and actions beside it.
/// </summary>
public sealed class DatabasesViewModel : ObservableObject
{
    private const string LogTag = "mysql";

    private readonly MainViewModel _main;
    private readonly List<DatabaseRow> _all = [];
    private bool _isBusy;
    private string _status = "";
    private string _summary = "";
    private string _emptyText = "";
    private string _filter = "";
    private DatabaseRow? _selected;
    private DatabaseRow? _pendingDrop;

    /// <summary>Creates the window's state; <see cref="RefreshAsync"/> fills the list.</summary>
    /// <param name="main">The panel; supplies the XAMPP folder, the root password and the log.</param>
    public DatabasesViewModel(MainViewModel main)
    {
        _main = main;
        RefreshCommand = new RelayCommand(RefreshAsync, () => IsIdle);
        OpenDumpFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(DatabaseTools.DumpFolder(main.Root));
            Shell.Open(DatabaseTools.DumpFolder(main.Root));
        });
    }

    /// <summary>The databases the search leaves, sorted by name.</summary>
    public ObservableCollection<DatabaseRow> Databases { get; } = [];

    /// <summary>Reads the list again.</summary>
    public RelayCommand RefreshCommand { get; }

    /// <summary>Opens mysql_dumps in Explorer.</summary>
    public RelayCommand OpenDumpFolderCommand { get; }

    /// <summary>The database whose details show beside the list; null when the list is empty.</summary>
    public DatabaseRow? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                // A half-confirmed drop belongs to the database it was asked for, not to the next one clicked.
                ClearPendingDrop();
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    /// <summary>True when a database is chosen, so the detail pane has something to show.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>The search text; a database is listed when its name contains it.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetField(ref _filter, value))
            {
                ApplyFilter(Selected?.Name);
            }
        }
    }

    /// <summary>True while a list, export, import or drop runs.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The opposite of <see cref="IsBusy"/>, for IsEnabled bindings.</summary>
    public bool IsIdle => !IsBusy;

    /// <summary>The line at the bottom left: progress, the last result, or where exports go.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>The line at the bottom right: "5 databases · 68.4 MB".</summary>
    public string Summary
    {
        get => _summary;
        private set => SetField(ref _summary, value);
    }

    /// <summary>
    /// Why the list is empty (MySQL stopped, no databases, nothing matches); empty while it has entries.
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

    /// <summary>True when <see cref="EmptyText"/> shows in place of the list.</summary>
    public bool HasEmptyText => _emptyText.Length > 0;

    /// <summary>Where exports go.</summary>
    public string DumpFolder => DatabaseTools.DumpFolder(_main.Root);

    /// <summary>Reads the databases from the server and keeps the chosen one chosen.</summary>
    /// <returns>A task that completes when the list is filled or the error is shown.</returns>
    public async Task RefreshAsync()
    {
        if (_main.FindModule("MySQL") is not { IsRunning: true })
        {
            Show([], null);
            Summary = "";
            Status = "";
            EmptyText = Loc.T("Databases.MySqlIsntRunning");
            return;
        }

        string? wanted = Selected?.Name;
        bool isRead = await RunAsync(Loc.T("Databases.ReadingDatabases"), async () =>
        {
            List<DatabaseInfo> databases = await DatabaseTools.ListAsync(_main.Root, ConnectionArgs, Environment);
            long largest = databases.Count == 0 ? 0 : databases.Max(database => database.SizeBytes);
            Show(
                databases.Select(database =>
                    new DatabaseRow(database, largest, DatabaseTools.LastExport(_main.Root, database.Name))),
                wanted);
            Summary = databases.Count == 0
                ? ""
                : Loc.Plural(
                    "Databases.Total",
                    databases.Count,
                    FileTools.FormatSize(databases.Sum(database => database.SizeBytes)));
            return Loc.T("Databases.ExportsGoTo", DumpFolder);
        });
        if (!isRead && _all.Count == 0)
        {
            // Usually a wrong root password on the first read: say so where the list would be, not only in the footer.
            EmptyText = Status;
        }
    }

    /// <summary>Dumps a database to mysql_dumps.</summary>
    /// <param name="database">The database to export.</param>
    /// <returns>A task that completes when the dump is written or the error is shown.</returns>
    public Task ExportAsync(DatabaseRow database) =>
        RunAsync(Loc.T("Databases.Exporting", database.Name), async () =>
        {
            string file = await DatabaseTools.ExportAsync(_main.Root, database.Name, ConnectionArgs, Environment);
            string size = FileTools.FormatSize(new FileInfo(file).Length);
            database.LastExport = new DumpFile(file, DateTime.Now);
            _main.Write(LogTag, $"Exported {database.Name} to \"{file}\" ({size}).", LogLevel.Ok);
            return Loc.T("Databases.ExportedTo", database.Name, Path.GetFileName(file), size);
        });

    /// <summary>Runs a .sql file into a database, creating it when needed, then refreshes the list.</summary>
    /// <param name="database">The database to import into; usually the file's own name without the timestamp.</param>
    /// <param name="sqlFile">Full path of the .sql file.</param>
    /// <returns>A task that completes when the import is done or the error is shown.</returns>
    public async Task ImportAsync(string database, string sqlFile)
    {
        string result = "";
        string progress = Loc.T("Databases.ImportingInto", Path.GetFileName(sqlFile), database);
        bool isImported = await RunAsync(progress, async () =>
        {
            await DatabaseTools.ImportAsync(_main.Root, database, sqlFile, ConnectionArgs, Environment);
            _main.Write(LogTag, $"Imported \"{sqlFile}\" into {database}.", LogLevel.Ok);
            result = Loc.T("Databases.ImportedInto", Path.GetFileName(sqlFile), database);
            return result;
        });
        await RefreshAfterAsync(isImported, result, database);
    }

    /// <summary>
    /// The first click shows what would be deleted in the detail pane; a second click on the same database drops it.
    /// </summary>
    /// <param name="database">The database to drop.</param>
    /// <returns>A task that completes when the drop is done, or at once after the first click.</returns>
    public async Task DropAsync(DatabaseRow database)
    {
        if (IsBusy)
        {
            return;
        }
        if (!ReferenceEquals(_pendingDrop, database))
        {
            ClearPendingDrop();
            _pendingDrop = database;
            database.DropWarning = Loc.T("Databases.DropWarning", database.Name, database.TableText, database.Size);
            return;
        }

        string result = "";
        bool isDropped = await RunAsync(Loc.T("Databases.Dropping", database.Name), async () =>
        {
            await DatabaseTools.DropAsync(_main.Root, database.Name, ConnectionArgs, Environment);
            _main.Write(LogTag, $"Dropped database {database.Name}.", LogLevel.Warn);
            result = Loc.T("Databases.Dropped", database.Name);
            return result;
        });
        await RefreshAfterAsync(isDropped, result, null);
    }

    /// <summary>Opens a database in phpMyAdmin, offering to start Apache first when it isn't running.</summary>
    /// <param name="database">The database to open.</param>
    /// <param name="confirmStartApache">Asks whether to start Apache; only called when it isn't running.</param>
    /// <returns>A task that completes when the browser is opened or the question is answered with no.</returns>
    public async Task OpenInPhpMyAdminAsync(DatabaseRow database, Func<Task<bool>> confirmStartApache)
    {
        if (!await _main.OpenPhpMyAdminAsync(database.Name, confirmStartApache))
        {
            Status = Loc.T("Databases.ApacheDidntStart");
        }
    }

    /// <summary>A database name guessed from a dump file name: "shop_20261101-120000.sql" gives "shop".</summary>
    /// <param name="sqlFile">The file's path or name.</param>
    /// <returns>The name before the timestamp, or the whole stem when there is none.</returns>
    public static string DatabaseNameFor(string sqlFile)
    {
        string stem = Path.GetFileNameWithoutExtension(sqlFile);
        int stampStart = stem.LastIndexOf('_');
        bool hasStamp = stampStart > 0 && stem.Length - stampStart == 16 && stem[stampStart + 9] == '-';
        return hasStamp ? stem[..stampStart] : stem;
    }

    /// <summary>
    /// Reads the list again after an import or a drop, then puts that action's result back in the status line.
    /// </summary>
    /// <param name="hasSucceeded">False keeps the error the action left in the status line.</param>
    /// <param name="result">The action's result text.</param>
    /// <param name="select">A database to choose afterwards; null keeps the current choice.</param>
    /// <returns>A task that completes when the list is read.</returns>
    private async Task RefreshAfterAsync(bool hasSucceeded, string result, string? select)
    {
        if (!hasSucceeded)
        {
            return;
        }

        await RefreshAsync();
        if (select is not null && Databases.FirstOrDefault(row => row.Name == select) is { } imported)
        {
            Selected = imported;
        }
        Status = result;
    }

    /// <summary>Replaces the databases and lists those the search leaves.</summary>
    /// <param name="rows">The databases as just read.</param>
    /// <param name="wanted">The name to choose again; null chooses the first entry.</param>
    private void Show(IEnumerable<DatabaseRow> rows, string? wanted)
    {
        _all.Clear();
        _all.AddRange(rows);
        ApplyFilter(wanted);
    }

    /// <summary>Fills the list with the databases whose name contains the search text.</summary>
    /// <param name="wanted">The name to choose again; the first entry is chosen when it is no longer listed.</param>
    private void ApplyFilter(string? wanted)
    {
        string search = Filter.Trim();
        List<DatabaseRow> visible =
            [.. _all.Where(row => row.Name.Contains(search, StringComparison.OrdinalIgnoreCase))];

        // The choice is taken away first and set again afterwards, so the list box always sees it change and
        // highlights the entry in the rebuilt list.
        Selected = null;
        Databases.Clear();
        foreach (DatabaseRow row in visible)
        {
            Databases.Add(row);
        }
        Selected = visible.FirstOrDefault(row => row.Name == wanted) ?? visible.FirstOrDefault();

        if (visible.Count > 0)
        {
            EmptyText = "";
        }
        else if (_all.Count > 0)
        {
            EmptyText = Loc.T("Databases.NothingMatches");
        }
        else
        {
            EmptyText = Loc.T("Databases.NoDatabasesYet");
        }
    }

    /// <summary>Takes back the first click on Drop.</summary>
    private void ClearPendingDrop()
    {
        if (_pendingDrop is { } pending)
        {
            pending.DropWarning = "";
        }
        _pendingDrop = null;
    }

    private string ConnectionArgs =>
        $"-u root -h 127.0.0.1 -P {_main.Settings.Services.MySqlPort} --protocol=tcp";

    private Dictionary<string, string> Environment
    {
        get
        {
            var environment = new Dictionary<string, string>();
            if (_main.Settings.MySqlRootPassword is { Length: > 0 } password)
            {
                environment["MYSQL_PWD"] = password;
            }
            return environment;
        }
    }

    /// <summary>Runs one action with the window marked busy and its outcome in the status line.</summary>
    /// <param name="progress">The status line while it runs.</param>
    /// <param name="action">The work; returns the status line to show afterwards.</param>
    /// <returns>True when the action ran to its end; false when it failed or another one was still running.</returns>
    private async Task<bool> RunAsync(string progress, Func<Task<string>> action)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        ClearPendingDrop();
        Status = progress;
        try
        {
            Status = await action();
            return true;
        }
        catch (Exception ex)
        {
            // Runs straight from a button click and drives external tools, so any failure (a wrong password, a
            // locked file, a broken dump) must end up in the status line rather than crash the app.
            _main.Write(LogTag, ex.Message, LogLevel.Error);
            Status = Loc.T("Databases.CheckTheRootPassword", ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
