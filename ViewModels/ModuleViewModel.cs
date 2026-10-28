using XamppControl.Core;
using XamppControl.Modules;

namespace XamppControl.ViewModels;

/// <summary>One module row: status, PIDs, ports, the Start/Stop button, the service checkbox and the menus.</summary>
public sealed class ModuleViewModel : ObservableObject
{
    // Shell.RunElevatedAsync returns this when the user cancels the UAC prompt.
    private const int ElevationCanceledExitCode = -2;

    private readonly MainViewModel _main;
    private bool _isRunning;
    private bool _isBusy;
    private bool _forceStopPending;
    private bool _isServiceInstalled;
    private string _pids = "";
    private string _ports = "";
    private string _busyLabel = "";

    /// <summary>Builds the row for a driver, with the Config and Logs menus limited to files that exist.</summary>
    /// <param name="driver">The module's driver, built for the current XAMPP folder and settings.</param>
    /// <param name="main">The panel; supplies the user-defined config files and receives the log lines.</param>
    public ModuleViewModel(ModuleDriver driver, MainViewModel main)
    {
        Driver = driver;
        _main = main;

        Subtitle = !driver.IsInstalled ? "Not installed" : driver.VersionLabel ?? "";

        IEnumerable<(string Label, string Relative)> userFiles = main.Settings.UserFiles
            .Where(userFile => userFile.Path.Length > 0 &&
                               string.Equals(userFile.Module, driver.DisplayName, StringComparison.OrdinalIgnoreCase))
            .Select(userFile => (Label: $"{Path.GetFileName(userFile.Path)} (user defined)", Relative: userFile.Path));
        ConfigFiles = Links(driver.ConfigFiles.Concat(userFiles));
        LogFiles = Links(driver.LogFiles);

        ActionCommand = new RelayCommand(RunActionAsync, () => Driver.IsInstalled && !IsBusy);
        AdminCommand = new RelayCommand(() => Driver.OpenAdmin(_main), () => Driver.HasAdmin && IsRunning);
        ToggleServiceCommand = new RelayCommand(ToggleServiceAsync, () => CanUseService && !IsBusy);
    }

    /// <summary>The driver that starts, stops and detects this module.</summary>
    public ModuleDriver Driver { get; }

    /// <summary>The module's display name, such as Apache or MySQL.</summary>
    public string Name => Driver.DisplayName;

    /// <summary>The version under the name, empty when unknown, or "Not installed".</summary>
    public string Subtitle { get; }

    /// <summary>True when the module's folder exists in the XAMPP installation.</summary>
    public bool IsInstalled => Driver.IsInstalled;

    /// <summary>1 for an installed module; uninstalled rows are dimmed to 0.55.</summary>
    public double RowOpacity => IsInstalled ? 1.0 : 0.55;

    /// <summary>The Config menu: the driver's config files plus the user-defined ones, only those that exist.</summary>
    public IReadOnlyList<FileLink> ConfigFiles { get; }

    /// <summary>The Logs menu: the driver's log files and folders, only those that exist.</summary>
    public IReadOnlyList<FileLink> LogFiles { get; }

    /// <summary>True when the Config menu has at least one entry.</summary>
    public bool HasConfig => ConfigFiles.Count > 0;

    /// <summary>True when the Logs menu has at least one entry.</summary>
    public bool HasLogs => LogFiles.Count > 0;

    /// <summary>The Start/Stop/Force stop button; disabled while busy or when the module is not installed.</summary>
    public RelayCommand ActionCommand { get; }

    /// <summary>The Admin button; enabled only while a module with an admin page or tool runs.</summary>
    public RelayCommand AdminCommand { get; }

    /// <summary>The service checkbox: installs or removes the module's Windows service.</summary>
    public RelayCommand ToggleServiceCommand { get; }

    /// <summary>True for an installed module that can run as a Windows service, on Windows.</summary>
    public bool CanUseService => IsInstalled && Driver.ServiceName is not null && OperatingSystem.IsWindows();

    /// <summary>The service checkbox's tooltip, naming the service or saying there is none.</summary>
    public string ServiceTip => Driver.ServiceName is null
        ? "This module can't run as a Windows service"
        : $"Install or remove the \"{Driver.ServiceName}\" Windows service (asks for admin rights)";

    /// <summary>True while the last refresh found at least one of the module's processes.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                RaiseState();
            }
        }
    }

    /// <summary>True while a start, stop or force stop runs; the button then shows the action in progress.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RaiseState();
            }
        }
    }

    /// <summary>True after a graceful stop failed on a driver that can force stop; the button then offers it.</summary>
    public bool ForceStopPending
    {
        get => _forceStopPending;
        private set
        {
            if (SetField(ref _forceStopPending, value))
            {
                RaiseState();
            }
        }
    }

    /// <summary>True when the module's Windows service exists, as last checked.</summary>
    public bool IsServiceInstalled
    {
        get => _isServiceInstalled;
        private set => SetField(ref _isServiceInstalled, value);
    }

    /// <summary>The PID column: empty, one or two PIDs on separate lines, or the first PID and "+n more".</summary>
    public string Pids
    {
        get => _pids;
        private set => SetField(ref _pids, value);
    }

    /// <summary>The Ports column: up to four listening ports joined with ", ", or empty.</summary>
    public string Ports
    {
        get => _ports;
        private set => SetField(ref _ports, value);
    }

    /// <summary>The action button's text: the action in progress, "Force stop", "Stop" or "Start".</summary>
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

    /// <summary>True when the action button shows its start style.</summary>
    public bool ShowStart => !IsRunning && !ForceStopPending;

    /// <summary>True when the action button shows its stop style, including for "Force stop".</summary>
    public bool ShowStop => IsRunning || ForceStopPending;

    /// <summary>Raises the properties and command states that follow from running, busy and force stop.</summary>
    private void RaiseState()
    {
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(ShowStart));
        OnPropertyChanged(nameof(ShowStop));
        ActionCommand.RaiseCanExecuteChanged();
        AdminCommand.RaiseCanExecuteChanged();
        ToggleServiceCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Called by the panel's refresh loop with fresh detection results.</summary>
    /// <param name="pids">The module's PIDs; empty when it is not running or not installed.</param>
    /// <param name="ports">The ports those PIDs listen on as display text; empty when there are none.</param>
    /// <param name="quiet">True updates the row silently; false logs a change between running and stopped.</param>
    internal void Apply(IReadOnlyList<int> pids, string ports, bool quiet)
    {
        Pids = pids.Count switch
        {
            0 => "",
            <= 2 => string.Join("\n", pids),
            _ => $"{pids[0]}\n+{pids.Count - 1} more",
        };
        Ports = ports;

        bool isRunningNow = pids.Count > 0;
        if (isRunningNow == IsRunning)
        {
            return;
        }

        IsRunning = isRunningNow;
        if (!isRunningNow)
        {
            ForceStopPending = false;
        }

        if (quiet)
        {
            return;
        }

        string message = isRunningNow
            ? $"Status change detected: running{ModuleDriver.PidText(pids)}"
            : "Status change detected: stopped";
        _main.Write(Driver.Tag, message, isRunningNow ? LogLevel.Ok : LogLevel.Info);
    }

    /// <summary>Checks whether the Windows service exists and updates <see cref="IsServiceInstalled"/>.</summary>
    /// <returns>A task that completes at once for a module without a service or not installed.</returns>
    public async Task RefreshServiceStateAsync()
    {
        if (Driver.ServiceName is not { } serviceName || !IsInstalled)
        {
            return;
        }

        IsServiceInstalled = await WindowsServices.IsInstalledAsync(serviceName);
    }

    /// <summary>Runs the Start action for autostart; does nothing when running, not installed or busy.</summary>
    /// <returns>A task that completes when the start attempt and the following refresh are done.</returns>
    public async Task StartIfStoppedAsync()
    {
        if (!IsRunning && IsInstalled && !IsBusy)
        {
            await RunActionAsync();
        }
    }

    /// <summary>Stops the module for an update.</summary>
    /// <returns>False if it's still running afterwards; true when it stopped or was not running.</returns>
    public async Task<bool> StopForMaintenanceAsync()
    {
        if (!IsRunning)
        {
            return true;
        }

        _busyLabel = "Stopping…";
        IsBusy = true;
        try
        {
            return await Driver.StopAsync(_main);
        }
        finally
        {
            IsBusy = false;
            await _main.RefreshAsync(quiet: false);
        }
    }

    /// <summary>Starts the module after an update (no port pre-check; the updater checks the result).</summary>
    /// <returns>A task that completes after the start attempt and the following refresh.</returns>
    public async Task StartAfterMaintenanceAsync()
    {
        _busyLabel = "Starting…";
        IsBusy = true;
        try
        {
            await Driver.StartAsync(_main);
        }
        finally
        {
            IsBusy = false;
            await _main.RefreshAsync(quiet: false);
        }
    }

    /// <summary>
    /// The action button: force stops, starts or stops the service, stops, or starts after checking the ports,
    /// then refreshes the status.
    /// </summary>
    /// <returns>A task that completes after the action and the following refresh.</returns>
    private async Task RunActionAsync()
    {
        bool wasRunning = IsRunning;

        // Set before IsBusy, whose change raises ActionLabel.
        _busyLabel = ForceStopPending || wasRunning ? "Stopping…" : "Starting…";
        IsBusy = true;
        try
        {
            if (ForceStopPending)
            {
                await ForceStopModuleAsync();
            }
            else if (IsServiceInstalled && Driver.ServiceName is { } serviceName)
            {
                await StartOrStopServiceAsync(serviceName, wasRunning);
            }
            else if (wasRunning)
            {
                await StopModuleAsync();
            }
            else if (await _main.PortsAreFreeAsync(this))
            {
                await Driver.StartAsync(_main);
            }
        }
        catch (Exception ex)
        {
            // Drivers start processes, run batch files and call the service manager, so anything may fail; the
            // message goes to the log and the refresh below shows the module's real state.
            _main.Write(Driver.Tag, ex.Message, LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
            await _main.RefreshAsync(quiet: false);
        }
    }

    /// <summary>Kills the module's processes and leaves the force stop state.</summary>
    /// <returns>A task that completes when the driver's force stop is done.</returns>
    private async Task ForceStopModuleAsync()
    {
        await Driver.ForceStopAsync(_main);
        ForceStopPending = false;
    }

    /// <summary>Starts or stops the module's Windows service with net.exe, elevated, and logs the outcome.</summary>
    /// <param name="serviceName">The service name passed to <c>net start</c> or <c>net stop</c>.</param>
    /// <param name="wasRunning">True stops the service; false starts it.</param>
    /// <returns>A task that completes when net.exe exits or the UAC prompt is canceled.</returns>
    private async Task StartOrStopServiceAsync(string serviceName, bool wasRunning)
    {
        _main.Write(Driver.Tag, $"{(wasRunning ? "Stopping" : "Starting")} the {serviceName} service…");
        int exitCode = wasRunning
            ? await WindowsServices.StopAsync(serviceName)
            : await WindowsServices.StartAsync(serviceName);
        if (exitCode == ElevationCanceledExitCode)
        {
            _main.Write(Driver.Tag, "Canceled at the admin prompt.", LogLevel.Warn);
        }
        else if (exitCode != 0)
        {
            _main.Write(Driver.Tag,
                $"net {(wasRunning ? "stop" : "start")} {serviceName} failed (exit code {exitCode}).",
                LogLevel.Error);
        }
    }

    /// <summary>Stops the module gracefully; offers "Force stop" when that fails and the driver can.</summary>
    /// <returns>A task that completes when the driver's stop attempt is done.</returns>
    private async Task StopModuleAsync()
    {
        bool hasStopped = await Driver.StopAsync(_main);
        if (!hasStopped && Driver.SupportsForceStop)
        {
            ForceStopPending = true;
        }
    }

    /// <summary>The service checkbox: installs or removes the Windows service, elevated, and logs it.</summary>
    /// <returns>A task that completes after the service state is read back.</returns>
    private async Task ToggleServiceAsync()
    {
        if (IsRunning && !IsServiceInstalled)
        {
            _main.Write(Driver.Tag, $"Stop {Name} before installing it as a service.", LogLevel.Warn);

            // Raising the unchanged value returns the checkbox the user just clicked to the real state.
            OnPropertyChanged(nameof(IsServiceInstalled));
            return;
        }

        (string Exe, string Args)? command =
            IsServiceInstalled ? Driver.ServiceUninstallCommand : Driver.ServiceInstallCommand;
        if (command is not { } serviceCommand)
        {
            // Same: no command for this module, so the checkbox goes back to the real state.
            OnPropertyChanged(nameof(IsServiceInstalled));
            return;
        }

        string progressVerb = IsServiceInstalled ? "Removing" : "Installing";
        _main.Write(Driver.Tag, $"{progressVerb} the {Driver.ServiceName} service…");
        int exitCode = await Shell.RunElevatedAsync(serviceCommand.Exe, serviceCommand.Args, _main.Root);
        if (exitCode == ElevationCanceledExitCode)
        {
            _main.Write(Driver.Tag, "Canceled at the admin prompt.", LogLevel.Warn);
        }
        else if (exitCode != 0)
        {
            _main.Write(Driver.Tag, $"Service command failed (exit code {exitCode}).", LogLevel.Error);
        }

        await RefreshServiceStateAsync();
        OnPropertyChanged(nameof(IsServiceInstalled));
        if (exitCode == 0)
        {
            _main.Write(Driver.Tag, IsServiceInstalled ? "Service installed." : "Service removed.", LogLevel.Ok);
        }
    }

    /// <summary>Turns (label, relative path) pairs into menu entries, keeping those that exist.</summary>
    /// <param name="entries">Paths relative to the XAMPP folder, files or folders, in menu order.</param>
    /// <returns>The existing entries in the same order; a missing file or folder is left out.</returns>
    private List<FileLink> Links(IEnumerable<(string Label, string Relative)> entries) =>
        entries
            .Select(entry => (entry.Label, Path: Path.Combine(_main.Root, entry.Relative)))
            .Where(resolved => File.Exists(resolved.Path) || Directory.Exists(resolved.Path))
            // The editor is read on each click, so a change in Config applies without rebuilding the menu.
            .Select(existing => new FileLink(existing.Label, existing.Path, () => _main.Settings.Editor))
            .ToList();
}
