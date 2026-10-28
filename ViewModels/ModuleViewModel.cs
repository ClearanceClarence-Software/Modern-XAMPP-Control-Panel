using XamppControl.Core;
using XamppControl.Modules;

namespace XamppControl.ViewModels;

public sealed class ModuleViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _isRunning;
    private bool _isBusy;
    private bool _forceStopPending;
    private bool _isServiceInstalled;
    private string _pids = "";
    private string _ports = "";
    private string _busyLabel = "";

    public ModuleViewModel(ModuleDriver driver, MainViewModel main)
    {
        Driver = driver;
        _main = main;

        Subtitle = !driver.IsInstalled ? "Not installed" : driver.VersionLabel ?? "";

        var userFiles = main.Settings.UserFiles
            .Where(u => u.Path.Length > 0 &&
                        string.Equals(u.Module, driver.DisplayName, StringComparison.OrdinalIgnoreCase))
            .Select(u => (Label: $"{Path.GetFileName(u.Path)} (user defined)", Relative: u.Path));
        ConfigFiles = Links(driver.ConfigFiles.Concat(userFiles));
        LogFiles = Links(driver.LogFiles);

        ActionCommand = new RelayCommand(RunActionAsync, () => Driver.IsInstalled && !IsBusy);
        AdminCommand = new RelayCommand(() => Driver.OpenAdmin(_main), () => Driver.HasAdmin && IsRunning);
        ToggleServiceCommand = new RelayCommand(ToggleServiceAsync, () => CanUseService && !IsBusy);
    }

    public ModuleDriver Driver { get; }

    public string Name => Driver.DisplayName;
    public string Subtitle { get; }
    public bool IsInstalled => Driver.IsInstalled;
    public double RowOpacity => IsInstalled ? 1.0 : 0.55;

    public IReadOnlyList<FileLink> ConfigFiles { get; }
    public IReadOnlyList<FileLink> LogFiles { get; }
    public bool HasConfig => ConfigFiles.Count > 0;
    public bool HasLogs => LogFiles.Count > 0;

    public RelayCommand ActionCommand { get; }
    public RelayCommand AdminCommand { get; }
    public RelayCommand ToggleServiceCommand { get; }

    public bool CanUseService => IsInstalled && Driver.ServiceName is not null && OperatingSystem.IsWindows();

    public string ServiceTip => Driver.ServiceName is null
        ? "This module can't run as a Windows service"
        : $"Install or remove the \"{Driver.ServiceName}\" Windows service (asks for admin rights)";

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
                RaiseState();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                RaiseState();
        }
    }

    public bool ForceStopPending
    {
        get => _forceStopPending;
        private set
        {
            if (SetField(ref _forceStopPending, value))
                RaiseState();
        }
    }

    public bool IsServiceInstalled
    {
        get => _isServiceInstalled;
        private set => SetField(ref _isServiceInstalled, value);
    }

    public string Pids
    {
        get => _pids;
        private set => SetField(ref _pids, value);
    }

    public string Ports
    {
        get => _ports;
        private set => SetField(ref _ports, value);
    }

    public string ActionLabel =>
        IsBusy ? _busyLabel
        : ForceStopPending ? "Force stop"
        : IsRunning ? "Stop"
        : "Start";

    public bool ShowStart => !IsRunning && !ForceStopPending;
    public bool ShowStop => IsRunning || ForceStopPending;

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
    internal void Apply(IReadOnlyList<int> pids, string ports, bool quiet)
    {
        Pids = pids.Count switch
        {
            0 => "",
            <= 2 => string.Join("\n", pids),
            _ => $"{pids[0]}\n+{pids.Count - 1} more",
        };
        Ports = ports;

        var running = pids.Count > 0;
        if (running == IsRunning)
            return;

        IsRunning = running;
        if (!running)
            ForceStopPending = false;

        if (!quiet)
        {
            _main.Write(Driver.Tag,
                running
                    ? $"Status change detected: running{ModuleDriver.PidText(pids)}"
                    : "Status change detected: stopped",
                running ? LogLevel.Ok : LogLevel.Info);
        }
    }

    public async Task RefreshServiceStateAsync()
    {
        if (Driver.ServiceName is null || !IsInstalled)
            return;
        IsServiceInstalled = await WindowsServices.IsInstalledAsync(Driver.ServiceName);
    }

    public async Task StartIfStoppedAsync()
    {
        if (!IsRunning && IsInstalled && !IsBusy)
            await RunActionAsync();
    }

    /// <summary>Stops the module for an update. Returns false if it's still running afterwards.</summary>
    public async Task<bool> StopForMaintenanceAsync()
    {
        if (!IsRunning)
            return true;

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

    private async Task RunActionAsync()
    {
        var wasRunning = IsRunning;
        _busyLabel = ForceStopPending || wasRunning ? "Stopping…" : "Starting…";
        IsBusy = true;
        try
        {
            if (ForceStopPending)
            {
                await Driver.ForceStopAsync(_main);
                ForceStopPending = false;
            }
            else if (IsServiceInstalled && Driver.ServiceName is { } service)
            {
                _main.Write(Driver.Tag, $"{(wasRunning ? "Stopping" : "Starting")} the {service} service…");
                var code = wasRunning
                    ? await WindowsServices.StopAsync(service)
                    : await WindowsServices.StartAsync(service);
                if (code == -2)
                    _main.Write(Driver.Tag, "Cancelled at the admin prompt.", LogLevel.Warn);
                else if (code != 0)
                {
                    _main.Write(Driver.Tag,
                        $"net {(wasRunning ? "stop" : "start")} {service} failed (exit code {code}).", LogLevel.Error);
                }
            }
            else if (wasRunning)
            {
                var stopped = await Driver.StopAsync(_main);
                if (!stopped && Driver.SupportsForceStop)
                    ForceStopPending = true;
            }
            else if (await _main.PortsAreFreeAsync(this))
            {
                await Driver.StartAsync(_main);
            }
        }
        catch (Exception ex)
        {
            _main.Write(Driver.Tag, ex.Message, LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
            await _main.RefreshAsync(quiet: false);
        }
    }

    private async Task ToggleServiceAsync()
    {
        if (IsRunning && !IsServiceInstalled)
        {
            _main.Write(Driver.Tag, $"Stop {Name} before installing it as a service.", LogLevel.Warn);
            OnPropertyChanged(nameof(IsServiceInstalled)); // reset the checkbox
            return;
        }

        var command = IsServiceInstalled ? Driver.ServiceUninstallCommand : Driver.ServiceInstallCommand;
        if (command is not { } cmd)
        {
            OnPropertyChanged(nameof(IsServiceInstalled));
            return;
        }

        var action = IsServiceInstalled ? "Removing" : "Installing";
        _main.Write(Driver.Tag, $"{action} the {Driver.ServiceName} service…");
        var code = await Shell.RunElevatedAsync(cmd.Exe, cmd.Args, _main.Root);
        if (code == -2)
            _main.Write(Driver.Tag, "Cancelled at the admin prompt.", LogLevel.Warn);
        else if (code != 0)
            _main.Write(Driver.Tag, $"Service command failed (exit code {code}).", LogLevel.Error);

        await RefreshServiceStateAsync();
        OnPropertyChanged(nameof(IsServiceInstalled));
        if (code == 0)
            _main.Write(Driver.Tag, IsServiceInstalled ? "Service installed." : "Service removed.", LogLevel.Ok);
    }

    private List<FileLink> Links(IEnumerable<(string Label, string Relative)> items) =>
        items
            .Select(i => (i.Label, Path: Path.Combine(_main.Root, i.Relative)))
            .Where(i => File.Exists(i.Path) || Directory.Exists(i.Path))
            .Select(i => new FileLink(i.Label, i.Path, () => _main.Settings.Editor))
            .ToList();
}
