using XamppControl.Core;
using XamppControl.Updates;

namespace XamppControl.Databases;

/// <summary>One database on the running MariaDB server.</summary>
/// <param name="Name">The schema name.</param>
/// <param name="Tables">How many tables it has.</param>
/// <param name="SizeBytes">Data plus indexes in bytes, as information_schema reports them.</param>
/// <param name="Collation">The default collation, e.g. "utf8mb4_general_ci"; empty when the server didn't say.</param>
public sealed record DatabaseInfo(string Name, int Tables, long SizeBytes, string Collation = "")
{
    /// <summary>The size for the list, e.g. "12.0 MB".</summary>
    public string Size => FileTools.FormatSize(SizeBytes);

    /// <summary>"3 tables" or "1 table".</summary>
    public string TableText => Loc.Plural("Databases.Tables", Tables);
}

/// <summary>A dump file in the dump folder.</summary>
/// <param name="Path">Full path of the .sql file.</param>
/// <param name="Written">When it was last written, in local time.</param>
public sealed record DumpFile(string Path, DateTime Written)
{
    /// <summary>The file name without its folder.</summary>
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>Lists, dumps and restores databases with the MariaDB command-line clients of the active install.</summary>
public static class DatabaseTools
{
    // "_20261125-104008": what ExportAsync puts between the database name and ".sql".
    private const int StampLength = 16;

    private static readonly string[] _systemSchemas = ["information_schema", "performance_schema", "mysql", "sys"];

    /// <summary>The folder dumps are written to.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <returns>root\mysql_dumps; it may not exist yet.</returns>
    public static string DumpFolder(string root) => Path.Combine(root, "mysql_dumps");

    /// <summary>The newest dump that Export wrote for a database.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="database">The schema name.</param>
    /// <returns>
    /// The newest "name_timestamp.sql" in the dump folder; null when there is none or the folder can't be read.
    /// </returns>
    public static DumpFile? LastExport(string root, string database)
    {
        if (MockMachine.IsActive)
        {
            return MockMachine.LastExport(database);
        }

        string prefix = FileTools.Safe(database) + "_";
        int stemLength = prefix.Length - 1 + StampLength;
        try
        {
            if (!Directory.Exists(DumpFolder(root)))
            {
                return null;
            }

            // "shop_*" also finds "shop_old_...", so the length must match the name plus one timestamp.
            return new DirectoryInfo(DumpFolder(root))
                .EnumerateFiles(prefix + "*.sql")
                .Where(file => file.Name.Length == stemLength + file.Extension.Length)
                .OrderByDescending(file => file.LastWriteTime)
                .Select(file => new DumpFile(file.FullName, file.LastWriteTime))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The line is a nicety; a folder that can't be read just means no "last export" is shown.
            return null;
        }
    }

    /// <summary>The client exe of the active install.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <returns>mariadb.exe or mysql.exe from mysql\bin; null when neither exists.</returns>
    public static string? ClientExe(string root) =>
        FileTools.FirstExisting(Path.Combine(root, "mysql", "bin"), "mariadb.exe", "mysql.exe");

    /// <summary>The dump exe of the active install.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <returns>mariadb-dump.exe or mysqldump.exe from mysql\bin; null when neither exists.</returns>
    public static string? DumpExe(string root) =>
        FileTools.FirstExisting(Path.Combine(root, "mysql", "bin"), "mariadb-dump.exe", "mysqldump.exe");

    /// <summary>The user databases on the server, with table counts and sizes.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="connectionArgs">The client arguments that connect as root.</param>
    /// <param name="environment">The client environment, carrying the root password.</param>
    /// <returns>The databases sorted by name with their default collation, without the system schemas.</returns>
    /// <exception cref="InvalidOperationException">The client is missing or the query failed; the message says why.</exception>
    public static async Task<List<DatabaseInfo>> ListAsync(
        string root,
        string connectionArgs,
        IDictionary<string, string> environment)
    {
        if (MockMachine.IsActive)
        {
            await Task.Yield();
            return MockMachine.Databases();
        }

        string client = ClientExe(root) ?? throw new InvalidOperationException(Loc.T("Databases.NoMariadbExe"));
        const string query = "SELECT s.schema_name, COUNT(t.table_name), " +
            "COALESCE(SUM(t.data_length + t.index_length), 0), s.default_collation_name " +
            "FROM information_schema.schemata s " +
            "LEFT JOIN information_schema.tables t ON t.table_schema = s.schema_name " +
            "GROUP BY s.schema_name, s.default_collation_name ORDER BY s.schema_name";
        Shell.RunResult result = await Shell.RunAsync(
            client,
            $"{connectionArgs} --batch --skip-column-names -e \"{query}\"",
            root,
            TimeSpan.FromSeconds(30),
            environment);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(FirstLine(result.Error) ?? Loc.T("Databases.ListQueryFailed"));
        }

        var databases = new List<DatabaseInfo>();
        foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] columns = line.TrimEnd('\r').Split('\t');
            bool isUserSchema = columns.Length >= 3 && !_systemSchemas.Contains(columns[0], StringComparer.OrdinalIgnoreCase);
            if (isUserSchema && int.TryParse(columns[1], out int tables) && long.TryParse(columns[2], out long bytes))
            {
                string collation = columns.Length > 3 ? columns[3] : "";
                databases.Add(new DatabaseInfo(columns[0], tables, bytes, collation));
            }
        }
        return databases;
    }

    /// <summary>Dumps one database to a timestamped .sql file in the dump folder.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="database">The schema to dump.</param>
    /// <param name="connectionArgs">The client arguments that connect as root.</param>
    /// <param name="environment">The client environment, carrying the root password.</param>
    /// <returns>Full path of the dump file.</returns>
    /// <exception cref="InvalidOperationException">The dump tool is missing or failed; the message says why.</exception>
    public static async Task<string> ExportAsync(
        string root,
        string database,
        string connectionArgs,
        IDictionary<string, string> environment)
    {
        string dumpExe = DumpExe(root) ?? throw new InvalidOperationException(Loc.T("Databases.NoMariadbDumpExe"));
        Directory.CreateDirectory(DumpFolder(root));
        string file = Path.Combine(DumpFolder(root), $"{FileTools.Safe(database)}_{FileTools.Stamp()}.sql");

        Shell.RunResult result = await Shell.RunAsync(
            dumpExe,
            $"{connectionArgs} --routines --events --triggers --single-transaction --databases \"{database}\" " +
            $"--result-file=\"{file}\"",
            root,
            TimeSpan.FromMinutes(30),
            environment);
        if (result.ExitCode != 0)
        {
            FileTools.TryDeleteFile(file);
            throw new InvalidOperationException(FirstLine(result.Error) ?? Loc.T("Databases.TheDumpFailed"));
        }
        return file;
    }

    /// <summary>Runs a .sql file against the server, creating the database first when it doesn't exist.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="database">The schema the file is imported into; a dump made with --databases switches to its own.</param>
    /// <param name="sqlFile">Full path of the .sql file.</param>
    /// <param name="connectionArgs">The client arguments that connect as root.</param>
    /// <param name="environment">The client environment, carrying the root password.</param>
    /// <exception cref="InvalidOperationException">The client is missing or the import failed; the message says why.</exception>
    public static async Task ImportAsync(
        string root,
        string database,
        string sqlFile,
        string connectionArgs,
        IDictionary<string, string> environment)
    {
        string client = ClientExe(root) ?? throw new InvalidOperationException(Loc.T("Databases.NoMariadbExe"));
        string create = $"CREATE DATABASE IF NOT EXISTS `{database.Replace("`", "``", StringComparison.Ordinal)}`";
        Shell.RunResult created = await Shell.RunAsync(
            client,
            $"{connectionArgs} -e \"{create}\"",
            root,
            TimeSpan.FromSeconds(30),
            environment);
        if (created.ExitCode != 0)
        {
            throw new InvalidOperationException(FirstLine(created.Error) ?? Loc.T("Databases.CreatingTheDatabaseFailed"));
        }

        // cmd redirects the file into the client's stdin; the client reads it statement by statement.
        string arguments = $"/c \"\"{client}\" {connectionArgs} \"{database}\" < \"{sqlFile}\"\"";
        Shell.RunResult imported = await Shell.RunAsync(
            "cmd.exe",
            arguments,
            root,
            TimeSpan.FromMinutes(60),
            environment);
        if (imported.ExitCode != 0)
        {
            throw new InvalidOperationException(FirstLine(imported.Error) ?? Loc.T("Databases.TheImportFailed"));
        }
    }

    /// <summary>Drops a database.</summary>
    /// <param name="root">The XAMPP folder.</param>
    /// <param name="database">The schema to drop.</param>
    /// <param name="connectionArgs">The client arguments that connect as root.</param>
    /// <param name="environment">The client environment, carrying the root password.</param>
    /// <exception cref="InvalidOperationException">The client is missing or the statement failed.</exception>
    public static async Task DropAsync(
        string root,
        string database,
        string connectionArgs,
        IDictionary<string, string> environment)
    {
        string client = ClientExe(root) ?? throw new InvalidOperationException(Loc.T("Databases.NoMariadbExe"));
        string drop = $"DROP DATABASE `{database.Replace("`", "``", StringComparison.Ordinal)}`";
        Shell.RunResult result = await Shell.RunAsync(
            client,
            $"{connectionArgs} -e \"{drop}\"",
            root,
            TimeSpan.FromSeconds(60),
            environment);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(FirstLine(result.Error) ?? Loc.T("Databases.DroppingTheDatabaseFailed"));
        }
    }

    /// <summary>The first non-empty line of a tool's error output.</summary>
    /// <param name="error">Standard error as captured.</param>
    /// <returns>The line, trimmed; null when there is none.</returns>
    private static string? FirstLine(string error) =>
        error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.Length > 0);
}
