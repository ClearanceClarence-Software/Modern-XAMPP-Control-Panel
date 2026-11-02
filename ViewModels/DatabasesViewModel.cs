using System.Collections.ObjectModel;
using XamppControl.Core;
using XamppControl.Databases;
using XamppControl.Modules;

namespace XamppControl.ViewModels;

/// <summary>The Databases window: the server's databases with export, import and drop.</summary>
public sealed class DatabasesViewModel : ObservableObject
{
    private const string LogTag = "mysql";

    private readonly MainViewModel _main;
    private bool _isBusy;
    private string _status = "";
    private string? _pendingDrop;

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

    /// <summary>The user databases, sorted by name.</summary>
    public ObservableCollection<DatabaseInfo> Databases { get; } = [];

    /// <summary>Reads the list again.</summary>
    public RelayCommand RefreshCommand { get; }

    /// <summary>Opens mysql_dumps in Explorer.</summary>
    public RelayCommand OpenDumpFolderCommand { get; }

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

    /// <summary>The status line: progress, the last result, or a drop confirmation.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>Where exports go, for the note under the list.</summary>
    public string DumpFolder => DatabaseTools.DumpFolder(_main.Root);

    /// <summary>Reads the databases from the server.</summary>
    /// <returns>A task that completes when the list is filled or the error is shown.</returns>
    public async Task RefreshAsync()
    {
        if (_main.FindModule("MySQL") is not { IsRunning: true })
        {
            Databases.Clear();
            Status = "MySQL isn't running. Start it to see the databases.";
            return;
        }

        await RunAsync("Reading databases…", async () =>
        {
            List<DatabaseInfo> databases = await DatabaseTools.ListAsync(_main.Root, ConnectionArgs, Environment);
            Databases.Clear();
            foreach (DatabaseInfo database in databases)
            {
                Databases.Add(database);
            }
            return databases.Count == 0
                ? "No databases yet besides the system ones."
                : $"{databases.Count} database{(databases.Count == 1 ? "" : "s")}.";
        });
    }

    /// <summary>Dumps a database to mysql_dumps.</summary>
    /// <param name="database">The database to export.</param>
    /// <returns>A task that completes when the dump is written or the error is shown.</returns>
    public Task ExportAsync(DatabaseInfo database) =>
        RunAsync($"Exporting {database.Name}…", async () =>
        {
            string file = await DatabaseTools.ExportAsync(_main.Root, database.Name, ConnectionArgs, Environment);
            string size = Updates.FileTools.FormatSize(new FileInfo(file).Length);
            _main.Write(LogTag, $"Exported {database.Name} to \"{file}\" ({size}).", LogLevel.Ok);
            return $"Exported {database.Name} to {Path.GetFileName(file)} ({size}).";
        });

    /// <summary>Runs a .sql file into a database, creating it when needed, then refreshes the list.</summary>
    /// <param name="database">The database to import into; usually the file's own name without the timestamp.</param>
    /// <param name="sqlFile">Full path of the .sql file.</param>
    /// <returns>A task that completes when the import is done or the error is shown.</returns>
    public async Task ImportAsync(string database, string sqlFile)
    {
        await RunAsync($"Importing {Path.GetFileName(sqlFile)} into {database}…", async () =>
        {
            await DatabaseTools.ImportAsync(_main.Root, database, sqlFile, ConnectionArgs, Environment);
            _main.Write(LogTag, $"Imported \"{sqlFile}\" into {database}.", LogLevel.Ok);
            return $"Imported {Path.GetFileName(sqlFile)} into {database}.";
        });
        await RefreshAsync();
    }

    /// <summary>First click asks for confirmation in the status line; a second click on the same database drops it.</summary>
    /// <param name="database">The database to drop.</param>
    /// <returns>A task that completes when the drop is done, or at once after the first click.</returns>
    public async Task DropAsync(DatabaseInfo database)
    {
        if (IsBusy)
        {
            return;
        }
        if (_pendingDrop != database.Name)
        {
            _pendingDrop = database.Name;
            Status = $"Click Drop again to delete {database.Name} ({database.TableText}, {database.Size}). " +
                "Export it first if you may need it.";
            return;
        }

        _pendingDrop = null;
        await RunAsync($"Dropping {database.Name}…", async () =>
        {
            await DatabaseTools.DropAsync(_main.Root, database.Name, ConnectionArgs, Environment);
            _main.Write(LogTag, $"Dropped database {database.Name}.", LogLevel.Warn);
            return $"Dropped {database.Name}.";
        });
        await RefreshAsync();
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
    /// <returns>A task that completes when the action has finished or failed.</returns>
    private async Task RunAsync(string progress, Func<Task<string>> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        _pendingDrop = null;
        Status = progress;
        try
        {
            Status = await action();
        }
        catch (Exception ex)
        {
            // Runs straight from a button click and drives external tools, so any failure (a wrong password, a
            // locked file, a broken dump) must end up in the status line rather than crash the app.
            _main.Write(LogTag, ex.Message, LogLevel.Error);
            Status = ex.Message + " (check the root password under Config › Service and Port Settings)";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
