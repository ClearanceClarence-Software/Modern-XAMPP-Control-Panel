# Coding standards

How C# is written in Modern XAMPP Control Panel. The formatting itself (indentation, braces on their own lines, `using` order, spaces) is in `.editorconfig` and is applied by Rider, Visual Studio and `dotnet format`; this file is about the choices a formatter cannot make. Each rule has a **Don't** block and a **Do** block. The "Do" is the code as it is now, with the file or member named so you can open it and see the rule in place; the "Don't" is the shape it replaces.

The one idea behind all of them: code is read far more often than it is written, usually by someone who did not write it and is in a hurry. Write for that reader.

Contents: [Names](#names) · [Nesting](#nesting) · [Methods](#methods) · [Blank lines and line breaks](#blank-lines-and-line-breaks) · [Braces](#braces) · [`var` and types](#var-and-types) · [Null](#null) · [Comments](#comments) · [XML documentation](#xml-documentation) · [Exceptions](#exceptions) · [What a rename must not touch](#what-a-rename-must-not-touch) · [Language](#language) · [Checklist](#checklist)

---

## Names

A name says what the value is. The reader should never have to scroll to where a variable is used to find out what it holds.

### Locals

**Don't** use letters, abbreviations or words that only say "a thing": `a`, `b`, `x`, `n`, `p`, `tmp`, `data`, `obj`, `res`, `val`, `item`, `info`.

```csharp
int n = Marshal.ReadInt32(buf);
IntPtr p = buf + 4;
for (int i = 0; i < n; i++, p += size)
{
    list.Add(Read(p, v6));
}
```

**Do** say what the number counts and what the pointer points at.

```csharp
// PortTable.AddListeners
int rowCount = Marshal.ReadInt32(buffer);
IntPtr row = buffer + 4;
for (int i = 0; i < rowCount; i++, row += rowSize)
{
    listeners.Add(ReadRow(row, isIPv6));
}
```

### Lambda parameters

The same rule applies inside a lambda. A one-letter parameter looks harmless on a short line and turns every longer lambda into a puzzle.

**Don't**

```csharp
var ordered = rows.OrderBy(r => r.Address.Contains(':')).ThenBy(r => r.Address, StringComparer.Ordinal).ThenBy(r => r.Port).ToList();
```

**Do**

```csharp
// NetstatViewModel.BuildRows
var ordered = rows
    .OrderBy(row => row.Address.Contains(':'))
    .ThenBy(row => row.Address, StringComparer.Ordinal)
    .ThenBy(row => row.Port)
    .ToList();
```

For comparisons and sorts the pair is `(first, second)` or `(left, right)`. For a dictionary entry, `entry` or `pair`. For the things this app handles, the thing's name: `module`, `listener`, `row`, `build`, `branch`, `userFile`, `line`. A filter over modules that picks some of them uses `candidate`.

### Parameters

A parameter name is part of the method's documentation and shows up in every call that uses a named argument.

**Don't**

```csharp
private static Listener ReadRow(IntPtr p, bool v6)
public static List<string> MergeMissingFiles(string a, string b, params string[] skip)
public static void OpenInEditor(string f, string? ed)
```

**Do**

```csharp
private static Listener ReadRow(IntPtr row, bool isIPv6)
public static List<string> MergeMissingFiles(string from, string to, params string[] skipTopLevel)
public static void OpenInEditor(string file, string? editor)
```

### Booleans

A boolean reads as a question or a state: `isRunning`, `wasRunning`, `isOwnedByXampp`, `isAnyPortBlocked`, `isWrappedInOneFolder`, `makeDump`. Not `on`, `ok`, `set`, `flag`, `b`.

**Don't**

```csharp
bool one = topFiles.Length == 0 && topFolders.Length == 1;
return one ? topFolders[0] : staging;
```

**Do**

```csharp
// FileTools.ExtractToStaging
bool isWrappedInOneFolder = topFiles.Length == 0 && topFolders.Length == 1;
return isWrappedInOneFolder ? topFolders[0] : staging;
```

### Units and roles

When the plain noun could mean two things, put the unit or the role in the name.

| Instead of | Write | Because |
|---|---|---|
| `port` for the raw table field | `portField`, then `port` | the field is in network byte order; the decoded value is not |
| `size` | `tableSize`, `rowSize` | the buffer and one row are both sizes in bytes |
| `result` | `sizeQueryResult` | it is the answer to the first call, which only asks for the size |
| `version` next to another version | `oldVersion`, `newVersion` | an update always has two |
| `dir`, `folder` | `mysqlDir`, `staging`, `backup`, `parked` | an update moves four folders around |
| `1223`, `-2` | `ErrorCanceled`, `ElevationCanceledExitCode` | a bare number does not say it means "the user said No at the UAC prompt" |
| `1L << 30` inline | `BytesPerGigabyte` | the constant says the unit |

### Short names that are fine

- `i`, `j` for a plain loop index; `k` as a third one.
- `ex` for a caught exception.
- `ctx` for the `UpdateContext` every updater step receives: the name the whole `Updates` folder already uses.
- `_` for a parameter or deconstruction slot that is not used, such as the sender and arguments of a timer `Tick`.
- `x`, `y` for coordinates when there is only one pair in scope. Two pairs get prefixes: `pointerX`, `windowY`.

### Fields and members

Private fields are `_camelCase`, constants and everything else `PascalCase`, interfaces `I`-prefixed; `.editorconfig` checks this. Local functions get method names (`DescribeProcess`, `RollBackAsync`, `FailWithRollback`), not `Describe`, `Back`, `Fail`, and never a single letter such as `F` or `D`.

---

## Nesting

Every enclosing block is one more condition the reader must keep in mind. Handle the exceptional case first and leave; the normal path then runs down the left margin.

### Leave early

**Don't** wrap the body in the happy-path condition.

```csharp
if (ctx.Main.FindModule(Module) is { } mysql)
{
    if (!mysql.IsServiceInstalled)
    {
        if (Directory.Exists(mysqlDir))
        {
            ... the whole update ...
        }
        else
        {
            return UpdateResult.Fail($"No mysql folder in \"{root}\".");
        }
    }
    ...
}
```

**Do** reject each case that cannot go on, then let the update run at the top level.

```csharp
// MariaDbUpdater.InstallAsync
if (ctx.Main.FindModule(Module) is not { } mysql)
{
    return UpdateResult.Fail("The MySQL module isn't available.");
}
if (mysql.IsServiceInstalled)
{
    return UpdateResult.Fail("MySQL is installed as a Windows service. " +
        "Remove the service (Service checkbox) before updating.");
}
if (!Directory.Exists(mysqlDir))
{
    return UpdateResult.Fail($"No mysql folder in \"{root}\".");
}

ctx.Status("Unpacking MariaDB…");
```

### Skip early in loops

**Don't**

```csharp
foreach (JsonProperty build in branch.Value.EnumerateObject())
{
    if (isThreadSafeX64 && build.Value.ValueKind == JsonValueKind.Object)
    {
        if (build.Value.TryGetProperty("zip", out JsonElement zip) && zip.ValueKind == JsonValueKind.Object)
        {
            ... fifteen lines ...
        }
    }
}
```

**Do**

```csharp
// ReleaseFeeds.ThreadSafeX64Build
foreach (JsonProperty build in branch.Value.EnumerateObject())
{
    ...
    if (!isThreadSafeX64 || build.Value.ValueKind != JsonValueKind.Object)
    {
        continue;
    }

    if (!build.Value.TryGetProperty("zip", out JsonElement zip) || zip.ValueKind != JsonValueKind.Object)
    {
        continue;
    }

    string? zipPath = ReadString(zip, "path");
    if (string.IsNullOrEmpty(zipPath))
    {
        continue;
    }
    ...
}
```

Only invert a condition this way when nothing follows the block inside the loop body; if something does, the `continue` would skip it.

### Never nest a ternary

A ternary is one choice between two values. Two of them inside each other is a decision tree written on one line.

**Don't**

```csharp
public string ActionLabel => IsBusy ? _busyLabel : ForceStopPending ? "Force stop" : IsRunning ? "Stop" : "Start";
```

**Do** let the getter read top to bottom.

```csharp
// ModuleViewModel.ActionLabel
public string ActionLabel
{
    get
    {
        if (IsBusy)
        {
            return _busyLabel;
        }
        if (ForceStopPending)
        {
            return "Force stop";
        }
        return IsRunning ? "Stop" : "Start";
    }
}
```

The last line is a single choice and stays a ternary. When the choice is a value picked by one input, a `switch` expression is the flat form:

```csharp
// ModuleViewModel.Apply
Pids = pids.Count switch
{
    0 => "",
    <= 2 => string.Join("\n", pids),
    _ => $"{pids[0]}\n+{pids.Count - 1} more",
};
```

### Name a compound condition

Past two or three terms, an `if` stops reading as a sentence. Put the condition in a `bool` whose name is the sentence.

**Don't**

```csharp
if (!build.Name.StartsWith("ts-", StringComparison.OrdinalIgnoreCase) || !build.Name.EndsWith("-x64", StringComparison.OrdinalIgnoreCase) || build.Value.ValueKind != JsonValueKind.Object)
{
    continue;
}
```

**Do**

```csharp
// ReleaseFeeds.ThreadSafeX64Build
// "ts-vs17-x64" (and older "ts-vs16-x64"); "nts-…" builds don't work with mod_php.
bool isThreadSafeX64 = build.Name.StartsWith("ts-", StringComparison.OrdinalIgnoreCase)
    && build.Name.EndsWith("-x64", StringComparison.OrdinalIgnoreCase);
if (!isThreadSafeX64 || build.Value.ValueKind != JsonValueKind.Object)
{
    continue;
}
```

Stating the positive (`isThreadSafeX64`, `isOwnedByXampp`) and testing its negation is easier to check than a chain of negated terms.

### A `switch` dispatches; it does not do the work

A `switch` statement or expression maps a value to an action or a result. When a case needs more than a line or two, the case calls a method and the method holds the work. The `switch` expressions in `NetstatRow`, `LogEntry` and `NetstatViewModel.ProcessDisplayName` are the shape to keep: one line per case, nothing to scroll.

```csharp
// NetstatViewModel.ProcessDisplayName
return pid switch
{
    0 => "System Idle Process",
    4 => "System",
    _ => ProcessScan.NameOf(pid) is { } processName ? processName + ".exe" : "?",
};
```

---

## Methods

A method does one thing, and its name says which. If you need comments inside it to mark its parts ("// now the services", "// then the ports"), the parts want to be methods.

### Split by step

**Don't** let a method grow by accretion. Startup used to log the installation, check every module's service, log the already running modules, check the ports and start the autostart modules, all inline in one body.

**Do** keep the orchestration in the method and give each step a name. Now `StartupAsync` reads:

```csharp
// MainViewModel.StartupAsync
Write("main", "Initializing Control Panel");
Write("main", $"Control Panel Version: {BuildInfo.Version}  [ Compiled: {BuildInfo.CompiledOn} ]");
Debug("main", $"Settings file: \"{Path.Combine(AppSettings.Folder, "settings.json")}\"");
LogInstallation();

await RefreshAsync(quiet: true);

await LogInstalledServicesAsync();
LogAlreadyRunning();

if (Settings.CheckPortsOnStartup)
{
    await CheckDefaultPortsAsync();
}

_pollTimer.Start();

await AutoStartModulesAsync();

Write("main", "Control Panel Ready");
```

The updaters follow the same pattern. `MariaDbUpdater.InstallAsync` validates the zip, then calls `StartOldServerAsync`, `MakeSqlDumpAsync`, `ShutDownCompletelyAsync`, `SwapInNewBuild`, `CarryOverDataAndSettingsAsync`, `StartNewServerAsync` and `UpgradeSystemTablesAsync` in order; `PhpUpdater.InstallAsync` calls `StopApacheAsync`, `SwapInNewBuild`, `CarryOverXamppFilesAsync`, `DisableMissingExtensions`, `WarnAboutMissingIniPaths` and `DllSync.Sync`. Reading the orchestrating method tells you the order of an update without reading any step.

### One expression, one place

**Don't** repeat an expression. Every failure after the swap returns a result that offers the same rollback:

```csharp
return new UpdateResult(false, "Copying files into the new install failed: " + ex.Message, RollBackAsync, $"Switch back to MariaDB {oldVersion}");
...
return new UpdateResult(false, $"MariaDB {newVersion} didn't start. The log shows why; you can roll back.", RollBackAsync, $"Switch back to MariaDB {oldVersion}");
...
return new UpdateResult(false, upgradeFailure, RollBackAsync, $"Switch back to MariaDB {oldVersion}");
```

**Do** write it once.

```csharp
// MariaDbUpdater.InstallAsync
string rollbackLabel = $"Switch back to MariaDB {oldVersion}";
UpdateResult FailWithRollback(string message) => new(false, message, RollBackAsync, rollbackLabel);
...
return FailWithRollback("Copying files into the new install failed: " + ex.Message);
...
return FailWithRollback($"MariaDB {newVersion} didn't start. The log shows why; you can roll back.");
...
return FailWithRollback(upgradeFailure);
```

### Pass what the method needs

An extracted method takes its inputs as parameters and is `static` when it touches no field. That makes its dependencies visible at the call site, and it lets the caller read something expensive once and hand it to every step.

```csharp
// MainViewModel.CheckDefaultPortsAsync
List<Listener> listeners = await Task.Run(PortTable.ListAll);
foreach (ModuleViewModel module in Modules.Where(candidate => candidate.IsInstalled && !candidate.IsRunning))
{
    ReportBlockedPorts(module, listeners);
}
```

`ReportBlockedPorts` takes the listening sockets as a parameter instead of reading the TCP tables again for each module.

### No abstraction for its own sake

**Don't** add an interface with one implementation, a base class for two windows that share three lines, a generic helper used once, or a "manager" class that holds what used to be two methods. Splitting a method is for reading; it is not a reason to introduce a type. The seams that do exist earn their place: `ModuleDriver` has a subclass per kind of server (`ApacheDriver`, `MySqlDriver`, the batch-file drivers), and `IModuleHost` keeps the `Modules` folder free of view models, so a driver can log and read settings without knowing the window exists.

### Off the UI thread

The status poll runs every 1.5 seconds and the Netstat window refreshes every 3. Anything that reads the process list, the TCP tables, a file or the network goes through `Task.Run` or an async API, and only the results are applied on the UI thread. Copy any field the background work needs into a local first, so the two threads never share it:

```csharp
// NetstatViewModel.RefreshAsync
// Copied so the background thread never reads fields the UI thread writes.
Dictionary<string, Listener> previousListeners = _previousListeners;
bool isFirstRefresh = _isFirstRefresh;
(List<NetstatRow> rows, Dictionary<string, Listener> currentListeners) =
    await Task.Run(() => BuildRows(previousListeners, isFirstRefresh));
```

---

## Blank lines and line breaks

### One blank line between steps

Inside a method, a blank line marks where one logical step ends and the next begins: compute, then check, then act. Lines that belong together have no blank line between them. Two blank lines in a row is never right.

**Don't**

```csharp
bool isIPv6 = family == AfInet6;
int rowSize = isIPv6 ? Row6Size : Row4Size;
int tableSize = 0;
uint sizeQueryResult = GetExtendedTcpTable(IntPtr.Zero, ref tableSize, false, family, TcpTableOwnerPidListener, 0);
if (sizeQueryResult != ErrorInsufficientBuffer || tableSize <= 0)
{
    return;
}
IntPtr buffer = Marshal.AllocHGlobal(tableSize);
```

**Do**

```csharp
// PortTable.AddListeners
bool isIPv6 = family == AfInet6;
int rowSize = isIPv6 ? Row6Size : Row4Size;

// The first call only asks for the buffer size; Windows answers ERROR_INSUFFICIENT_BUFFER.
int tableSize = 0;
uint sizeQueryResult = GetExtendedTcpTable(
    IntPtr.Zero,
    ref tableSize,
    false,
    family,
    TcpTableOwnerPidListener,
    0);
if (sizeQueryResult != ErrorInsufficientBuffer || tableSize <= 0)
{
    return;
}

IntPtr buffer = Marshal.AllocHGlobal(tableSize);
```

### Break long calls

Past 120 columns a line stops being readable without scrolling, and `.editorconfig` sets that limit. Break the call after the opening parenthesis and put one argument per line, as in the `GetExtendedTcpTable` call above, or keep the first arguments on the first line and wrap the rest when they read as one sentence:

```csharp
// MainViewModel.LogInstallation
Write("main",
    $"No XAMPP installation found at \"{Root}\". Click \"Change…\" in the header or set it under Config.",
    LogLevel.Error);
```

A signature that does not fit takes one parameter per line:

```csharp
// MariaDbUpdater.StartOldServerAsync
private static async Task<bool> StartOldServerAsync(
    UpdateContext ctx,
    ModuleViewModel mysql,
    int port,
    string oldVersion)
```

Naming the result of a long call (`sizeQueryResult`) also keeps the `if` that tests it to one line.

### LINQ one operator per line

A chain of more than two operators goes one per line, each starting with the dot.

```csharp
// MainViewModel.PidList
module.Pids.Split('\n', StringSplitOptions.RemoveEmptyEntries)
    .Select(line => int.TryParse(line, out int pid) ? pid : 0)
    .Where(pid => pid > 0)
    .ToList();
```

### Initializers one property per line

An object initializer with more than two properties goes one per line.

**Don't**

```csharp
var startInfo = new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workingDirectory };
```

**Do**

```csharp
// Shell.StartHidden
var startInfo = new ProcessStartInfo(exe, arguments)
{
    UseShellExecute = false,
    CreateNoWindow = true,
    WorkingDirectory = workingDirectory,
};
```

### One declaration per line

**Don't** `string root = ctx.Root, mysqlDir = Path.Combine(root, "mysql"), staging = Path.Combine(root, "_update_mysql");`

**Do**

```csharp
// MariaDbUpdater.InstallAsync
string root = ctx.Root;
string mysqlDir = Path.Combine(root, "mysql");
string staging = Path.Combine(root, "_update_mysql");
```

Constants are the exception when they form one table, with a comment saying what the table is:

```csharp
// PortTable
// Byte offsets in MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid
private const int Row4Size = 24, Row4Addr = 4, Row4Port = 8, Row4Pid = 20;
```

---

## Braces

Every `if`, `else`, `for`, `foreach`, `while`, `using`, `try`, `catch` and `finally` body has braces on their own lines, even for a single statement. A body without braces is the easiest place to add a second statement that ends up silently outside the block.

**Don't**

```csharp
if (_isRefreshing) return;
catch (IOException) { }
finally { _isRefreshing = false; }
foreach (var module in Modules) module.Apply(pids, ports, quiet);
```

**Do**

```csharp
if (_isRefreshing)
{
    return;
}

catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
{
    // Leftover staging folders are harmless: the next update deletes the folder before extracting again.
}

finally
{
    _isRefreshing = false;
}
```

A lambda with a body follows the same rule: `(_, args) => { SaveWindowState(); args.Cancel = true; }` on one line becomes a block with one statement per line. A lambda that is a single expression stays an expression: `async (_, _) => await RefreshAsync()`.

---

## `var` and types

`var` when the right-hand side already shows the type; the explicit type when it does not.

### Use `var` for `new`, casts, parse and factory calls, and LINQ that ends in a materializing call

```csharp
var listeners = new List<Listener>();
var startInfo = new ProcessStartInfo(browser) { UseShellExecute = false };
using var document = JsonDocument.Parse(json);
using var process = Process.Start(startInfo);
var modules = Modules.ToList();
```

Writing `List<Listener> listeners = new List<Listener>();` says the type twice, and the build warns about it (IDE0007).

### Write the type when the right-hand side does not show it

A method call on your own type, a property, a field, an element of a collection, arithmetic, a ternary, an `await`: none of those tell the reader the type.

**Don't**

```csharp
var listeners = await Task.Run(PortTable.ListAll);
var ownerPath = ProcessScan.PathOf(pid);
var attributes = File.GetAttributes(path);
var rowSize = isIPv6 ? Row6Size : Row4Size;
```

**Do**

```csharp
List<Listener> listeners = await Task.Run(PortTable.ListAll);
string? ownerPath = ProcessScan.PathOf(pid);
FileAttributes attributes = File.GetAttributes(path);
int rowSize = isIPv6 ? Row6Size : Row4Size;
```

The explicit `string?` also tells the reader that `PathOf` can fail, which `var` would hide.

### Numeric types on purpose

Where the width, the sign or the unit matters, the type is explicit and chosen: `long` for a byte count or a file size, `int` for a port, a PID, a count or an index, `uint` where a Win32 function returns an error code, `IntPtr` for unmanaged memory, and `TimeSpan` for every duration (never an `int` of milliseconds or seconds).

```csharp
private const long BytesPerGigabyte = 1L << 30;
private const uint ErrorInsufficientBuffer = 122;
int port = ((portField & 0xFF) << 8) | ((portField >> 8) & 0xFF);
public static async Task<bool> ExitsWithinAsync(Process process, TimeSpan time)
```

### Watch what `var` infers

`var` can hide a surprise: a nullable return, an `IEnumerable` that is evaluated again on every loop, a `List<object>` where you expected a list of modules. When the inferred type is not the one you would have written, write the one you meant.

---

## Null

The project compiles with nullable reference types on. The compiler's flow analysis is the one null check that never goes stale, so write code it can follow.

### Test, then use

**Don't** repeat the lookup and tell the compiler to trust you.

```csharp
if (FileTools.FirstExisting(Path.Combine(content, "bin"), "mariadbd.exe", "mysqld.exe") is null)
{
    return UpdateResult.Fail(...);
}
string newVersion = XamppPaths.FileVersion(FileTools.FirstExisting(Path.Combine(content, "bin"), "mariadbd.exe", "mysqld.exe")!) ?? "new";
```

**Do** read into a local once; the compiler then knows it is non-null below the check.

```csharp
// MariaDbUpdater.InstallAsync
string? newServer = FileTools.FirstExisting(Path.Combine(content, "bin"), "mariadbd.exe", "mysqld.exe");
if (newServer is null)
{
    FileTools.TryDelete(staging);
    return UpdateResult.Fail("This zip has no bin\\mariadbd.exe, so it isn't a MariaDB Windows x64 build.");
}
```

### Patterns instead of `!`

`is { } value` binds and tests in one step, and `is not { } value` leaves early with the value bound for the rest of the method:

```csharp
// MariaDbUpdater.InstallAsync
if (VersionStore.Find(root, Component.MariaDb, newVersion) is { } existing)
{
    ...
}

// MainViewModel.ReportBlockedPorts
if (PortTable.OwnerOf(port, listeners) is not { } pid)
{
    continue;
}
```

A property pattern tests a nullable and a property together: `ctx.Main.FindModule(Module) is { IsRunning: true } running`, `Settings.MySqlRootPassword is { Length: > 0 } password`.

When a value must exist and a missing one is a bug, `?? throw` with a message says so where it happens:

```csharp
// FileTools.MergeMissingFiles
string targetFileFolder = Path.GetDirectoryName(targetFile)
    ?? throw new InvalidOperationException($"\"{targetFile}\" has no parent folder.");
```

### The null-forgiving operator

`!` turns off the check and tells the reader nothing. Before writing one, try a local, a pattern, `OfType<T>()`, or `?? throw`. The uses that remain are where a guarantee exists that the compiler cannot see, and a comment next to it says what the guarantee is:

```csharp
// BatchDriver.StartAsync
// exePath is a file path combined under Root, so it always has a parent directory.
Shell.StartHidden(exePath, "", Path.GetDirectoryName(exePath)!)?.Dispose();
```

That is the only one in the code. Anything else is a bug waiting for the case that was "impossible".

---

## Comments

A comment says why, or what the reader could not work out from the code. It never says what the next line does.

### Every method of substance has a summary

One sentence, present tense, no "This method". It says what comes back or what changes, not how.

```csharp
/// <summary>Installs a MariaDB build from a zip as the active mysql folder, keeping the old one.</summary>
/// <summary>Reads the listening sockets on a background thread and replaces <see cref="Rows"/> with them.</summary>
/// <summary>Clears read-only on a file, or on every file below a folder, so it can be moved or deleted.</summary>
```

A `<remarks>` line follows when there is a condition the caller must know. Public members always have a summary. Private one-liners whose name says everything (`KeyOf(listener)`, `CopyFile(from, to)`) do not need one.

### Say why, not what

**Don't**

```csharp
// set refreshing to true
_isRefreshing = true;
// loop over the modules
foreach (ModuleViewModel module in Modules)
// return the list
return listeners;
```

**Do** write the comment the reader will thank you for:

```csharp
// PhpUpdater.InstallAsync
// Apache loads php8apache2_4.dll, so it has to stop before php\ can be moved.
bool apacheWasRunning = ctx.Main.FindModule(ApacheModule)?.IsRunning == true;
```

```csharp
// MainViewModel.RefreshAsync
// A copy, because LoadModules may rebuild the collection on the UI thread while detection runs.
var modules = Modules.ToList();
```

```csharp
// PortTable.ReadRow
// Stored in network byte order; IPAddress(long) expects exactly that layout.
address = new IPAddress((uint)Marshal.ReadInt32(row + Row4Addr)).ToString();
```

```csharp
// MariaDbUpdater.InstallAsync
// A local function so the rollback keeps this install's root, folders and versions even if the
// XAMPP folder setting changes before the user clicks it.
async Task RollBackAsync()
```

Each of these answers a question the code raises: why stop Apache? why copy the list? why that cast? why a local function?

### An ignored exception always says why

An empty `catch` without a comment looks like a mistake, and the next person cannot tell whether it is one.

**Don't**

```csharp
catch (IOException) { }
catch (Win32Exception) { }
```

**Do**

```csharp
// FileTools.TryDelete
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
{
    // Leftover staging folders are harmless: the next update deletes the folder before extracting again.
}
```

```csharp
// Shell.OpenUrl
catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
{
    // The configured browser is missing or cannot start; the system default opens the URL instead.
}
```

```csharp
// ReleaseFeeds.GetPhpSha256Async
catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
{
    // Without the checksum file the download is installed unverified, with a warning in the log.
}
```

The comment names the consequence of ignoring it ("the next update deletes the folder", "the system default opens the URL"), which is what the reader needs to judge whether ignoring is still right.

### Keep comments true

A comment that describes code that has since changed is worse than no comment: the reader believes it. When you change a line, read the comment above it. When you move code, move its comment. When a block becomes a method, the block's comment goes on the method.

### No prose

Comments are one or two sentences. If a method needs three paragraphs to explain, the method is the problem, not the comment. Section markers in long files (`// ── ports ──`) are fine; essays are not.

---

## XML documentation

The `///` blocks are what Rider shows on hover and what the next person reads before opening the method. They follow the comment rules above, plus these.

### Every documented method is documented completely

A block with a summary and nothing else is half a contract. Once a method has a `///` block it carries one `<param>` per parameter, in declaration order and with the exact parameter name, a `<returns>` when the method returns something, and a `<typeparam>` for each generic type parameter.

**Don't**

```csharp
/// <summary>Copies files that are missing.</summary>
public static List<string> CopyMissing(string from, string to)
```

**Do**

```csharp
// FileTools.CopyMissing
/// <summary>
/// Copies every top-level file and folder in <paramref name="from"/> that doesn't exist in <paramref name="to"/>.
/// </summary>
/// <param name="from">The folder to copy from, usually the previous install.</param>
/// <param name="to">The folder to fill in; entries it already has, as a file or a folder, are left alone.</param>
/// <returns>The names (not paths) of the top-level entries copied; empty when nothing was missing.</returns>
public static List<string> CopyMissing(string from, string to)
```

Which members get a block: every public and internal method, constructor, property, type, delegate and enum; every private method of substance. Which do not: XAML event handlers (`BrowseRoot_Click`), overrides whose base is documented (`ToString`), one-line helpers whose name says everything (`KeyOf(listener)`), and auto-properties whose name says everything.

### A `<param>` says what the name cannot

The reader already sees the name and the type. The tag adds the unit, the range, what null or empty means, which order a list must have, or what a callback receives and when.

**Don't** restate the name.

```csharp
/// <param name="path">The path.</param>
/// <param name="port">The port.</param>
/// <param name="quiet">Whether to be quiet.</param>
```

**Do**

```csharp
// ModuleViewModel.Apply
/// <param name="pids">The module's PIDs; empty when it is not running or not installed.</param>
/// <param name="ports">The ports those PIDs listen on as display text; empty when there are none.</param>
/// <param name="quiet">True updates the row silently; false logs a change between running and stopped.</param>
```

```csharp
// Shell.OpenUrl
/// <param name="browser">
/// A browser exe as a full path or a name on PATH; null, blank or one that fails to start uses the system default.
/// </param>
```

```csharp
// NetstatViewModel constructor
/// <param name="root">The XAMPP folder; processes whose executable path starts with it count as XAMPP's.</param>
```

The questions each tag should answer, where they apply:

| Kind of parameter | Say |
|---|---|
| a number | the unit (bytes, seconds, percent) and the range |
| a port or PID | which machine and protocol; what 0 or 4 means where it matters |
| a path | full or relative, and relative to what; a file or a folder; whether it must exist |
| a version | the format ("8.4.27", "12.3"); what an unknown version looks like |
| a collection | the required order; what empty means; whether it is read or changed |
| a nullable | what null means (default, "all", "unknown", "not applicable") |
| a callback | what it receives, how often, on which thread, and whether it may throw |
| a duration | what happens when it runs out |
| a flag | what each value does, not "whether to ..." |
| a pass-through | what it is passed to |

Everything in a tag is read off the body and the callers. A tag that guesses is worse than none: the compiler cannot check it and the reader will trust it.

### `<returns>` covers the edge cases

The normal case is usually in the summary already. The tag's job is the rest: null when, empty when, -1 when, false when; for a tuple, what each part is.

**Don't**

```csharp
/// <returns>The result.</returns>
/// <returns>True if successful.</returns>
/// <returns>A list of lines.</returns>
```

**Do**

```csharp
/// <returns>The PID of the first socket on that port; null when none listens.</returns>
/// <returns>The exit code; -1 when no process was started; -2 when the user declined the UAC prompt.</returns>
/// <returns>
/// Up to <paramref name="lines"/> non-empty lines from the last 64 KB, oldest first; empty when the file is
/// missing or cannot be read.
/// </returns>
```

### `<typeparam>` on every generic

```csharp
// ObservableObject.SetField
/// <summary>Stores a new value in a backing field and raises the change, unless the value is equal.</summary>
/// <typeparam name="T">The property's type; compared with its default equality comparer.</typeparam>
/// <param name="field">The backing field, written only when the value differs.</param>
/// <param name="value">The new value.</param>
/// <param name="propertyName">The property's name; filled in with the calling setter's name when left out.</param>
/// <returns>True when the value changed and the event was raised; false when it was already equal.</returns>
protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
```

### Records, delegates, enums and P/Invoke

A positional record documents each positional parameter with `<param>` on the record itself; a delegate documents its parameters and return like a method; an enum gets a summary, and a summary per member when the name alone does not say it (`SocketState` and `LogLevel` members say what each means). A P/Invoke declaration gets a one-line summary naming what the Win32 function is used for here; its parameters follow the Win32 meaning.

```csharp
// PortTable
/// <summary>A listening TCP socket.</summary>
/// <param name="Address">The local address as text; 0.0.0.0 or :: when it listens on every interface.</param>
/// <param name="Port">The local port in host byte order, 0 to 65535.</param>
/// <param name="Pid">The owning process ID; 4 is the Windows kernel (System).</param>
/// <param name="IsIPv6">True for a socket from the IPv6 table.</param>
public sealed record Listener(string Address, int Port, int Pid, bool IsIPv6);
```

### `<exception>` where the caller must handle it

When a method throws something the caller is expected to catch, say which and when.

```csharp
// ReleaseFeeds.GetPhpBranchesAsync
/// <exception cref="HttpRequestException">The folder listing, read as the fallback, cannot be read.</exception>
```

### Form

- One sentence per tag, present tense, no "This method...", no "Gets or sets".
- A short block keeps `/// <summary>...</summary>` on one line, followed by one line per `<param>` and `<returns>`.
- A tag that would pass 120 columns is split: the tag on its own line, the text wrapped beneath, the closing tag on its own line.
- `<see cref="..."/>` for members, `<paramref name="..."/>` for the method's own parameters, `<c>...</c>` for literals; no bare backticks, which the tooltip shows verbatim.
- The block must be well-formed XML: `&amp;` for `&`, `&lt;` for a literal `<` (as in `versions\php\&lt;version&gt;` on `VersionStore`). `GenerateDocumentationFile` is off, so the compiler will not tell you.
- A `<remarks>` holds the one thing that does not fit the summary: a format note, a thread rule, a reference. Not a second summary.
- Comments inside the body stay `//`; a `///` block belongs only in front of a declaration.

---

## Exceptions

### Catch what you expect

Name the exceptions the operation is known to throw, with `when` to list several.

```csharp
// UpdateContext.PortOpenAsync
catch (Exception ex) when (ex is SocketException
                               or OperationCanceledException
                               or ArgumentOutOfRangeException)
{
    // Refused, timed out or out of range all mean the same here: nothing is listening.
    return false;
}
```

Not `catch (Exception)` because "something might go wrong". A `catch (Exception)` or a bare `catch` is for the few places where anything at all may fail and the app must carry on regardless, and the comment inside says that. In this code those places are: `RelayCommand.Execute` (an exception escaping an `async void` would crash the app), the status poll in `MainViewModel` (it tries again on the next tick), the button handlers and update runner in `UpdatesViewModel` and the Apply and Save of `SitesViewModel` and `PhpSettingsViewModel` (the failure goes to the status line), a module action in `ModuleViewModel`, `VersionSwitcher`'s swap (it puts the old build back), reading another program's version resource, and writing the log file.

### Translate at the boundary

Low-level failures become the one result the caller handles. An updater never lets an `IOException` reach the dialog: it catches it where the step runs and returns an `UpdateResult` with a message, and with a rollback when the new build is already in place.

```csharp
// PhpUpdater.InstallAsync
catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
{
    FileTools.TryDelete(staging);
    return UpdateResult.Fail("Couldn't unpack the zip: " + ex.Message);
}
```

`Shell.RunAsync` does the same for processes: a timeout or a process that cannot start comes back as a `RunResult` with exit code -1 and the reason, not as an exception.

### Messages for a person

A failure message is a sentence a user can act on. It says what failed, what to do about it, and whether anything changed:

```csharp
// MariaDbUpdater.SwapInNewBuild
return $"Couldn't move the mysql folder ({ex.Message}). Close programs that use it " +
    "(HeidiSQL, a terminal in that folder, …) and try again. Nothing was changed.";
```

Not `"Move failed"`. Log lines that mirror the original XAMPP panel ("Problem detected!", "Port 80 in use by ...") keep XAMPP's wording, because users search for it.

### Quiet helpers

When cleanup must not fail the operation, give it a method whose name says so, and keep the reasons inside it.

```csharp
// Shell.KillTreeQuietly
private static void KillTreeQuietly(Process process)
{
    try
    {
        process.Kill(true);
    }
    catch
    {
        // Best effort after a timeout: the process may have exited or be protected, and the caller reports
        // the timeout either way.
    }
}
```

The call site then reads `KillTreeQuietly(process);` with no `try` in sight. `FileTools.TryDelete` is the same idea for folders.

---

## What a rename must not touch

A change that is "only renames" can still break the app at runtime or break existing installs. Before renaming anything that is not a local, a parameter or a private member, search the whole repository, including `.axaml`, the workflow and the PowerShell tools.

| Surface | How it is referenced | What breaks |
|---|---|---|
| XAML | `Click="BrowseRoot_Click"`, `x:Name="RootBox"`, `{Binding ...}` | compiles, then fails at runtime or silently does nothing |
| Settings | property names in `AppSettings`, `ServiceSettings` and `UserFile` are the `settings.json` keys | the setting resets for every user |
| Folders on disk | `versions\php`, `versions\mariadb`, `_update_php`, `_update_mysql`, `mysql_dumps` | installed versions and dumps stop being found on existing installs |
| Release feeds | the JSON names read from releases.json and the MariaDB REST API (`zip`, `path`, `sha256`, `file_name`, `release_status`) | updates find no builds |
| XAMPP's own files | module names, `*_start.bat`/`*_stop.bat`, `php8apache2_4.dll`, my.ini keys | modules stop starting or detecting |
| Release workflow | the `XamppControl.exe` name and the zip name in `.github/workflows/release.yml` and the README | the release has no file, or the download instructions are wrong |
| P/Invoke | signatures, struct offsets and constants in `PortTable` and `ProcessScan` | Windows does not care what the names say; the offsets must match |

Renaming a parameter is safe only after searching for `name:` uses of it; a named argument in a caller is a compile error you will not see until the caller builds.

---

## Language

Code, comments, commit messages, changelog and UI text are in American English: `color`, `canceled`, `gray`, `behavior`. No em dashes in comments or text; use a colon, a semicolon, parentheses or a new sentence. The author is `ClearanceClarence` everywhere the project names one.

---

## Checklist

Before a pull request, run `dotnet format --verify-no-changes` and `dotnet build` with no new warnings, then read the diff once with only these in mind:

- Could a stranger say what every variable holds from its name alone? (`i` is the only one-letter name you write without thinking.)
- Is there a block three levels deep that an early `return` or `continue` would flatten?
- Is there a ternary inside a ternary? A condition with more than three terms without a name?
- Is any method longer than a screen, or doing two things its name does not say?
- Is any expression written twice?
- Does anything slow run on the UI thread?
- Is there a body without braces, a `catch` on one line, two declarations on one line?
- Is `var` used where the type is not on the right, or the type repeated where it is?
- Is there a `!` that a local, a pattern or `?? throw` would remove?
- Does every `catch` that swallows say why? Does every method of substance have a one-sentence summary?
- Does every documented method have a `<param>` per parameter in order, a `<returns>` when it returns, a `<typeparam>` when generic? Does each tag say something the name does not?
- Does any comment describe the code as it used to be?
- Did a rename touch XAML, settings keys, folder names, feed names, the release workflow or P/Invoke?
