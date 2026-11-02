using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>
/// The control panel's main window: the module rows, the toolbar and the log, bound to a <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? _subscribedViewModel;
    private bool _isClosingConfirmed;
    private bool _isClosingInProgress;

    /// <summary>Sets up the title, icon and logo, and asks for the XAMPP folder on open when none is found.</summary>
    public MainWindow()
    {
        InitializeComponent();

        Title = BuildInfo.WindowTitle;
        AppIcon.Apply(this);
        if (AppIcon.LoadLogo() is { } logo)
        {
            HeaderLogo.Source = logo;
            HeaderLogo.IsVisible = true;
            HeaderFallback.IsVisible = false;
        }

        DataContextChanged += (_, _) => Subscribe();
        ActualThemeVariantChanged += (_, _) => UpdateThemeToggle();
        UpdateThemeToggle();

        Opened += async (_, _) =>
        {
            // Started minimized with minimize to tray on: go straight to the tray.
            bool startsInTray = WindowState == WindowState.Minimized
                && Tray is not null
                && ViewModel is { Settings.MinimizeToTray: true };
            if (startsInTray)
            {
                Hide();
            }

            // No XAMPP at the configured or default location: ask for the folder right away.
            if (ViewModel is { HasXampp: false })
            {
                await PickRootAsync(isXamppMissing: true);
            }
        };
    }

    /// <summary>The tray icon, set by the app after the window is created; null when there is none.</summary>
    public TrayController? Tray { get; set; }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>
    /// Asks what to do with running modules before the window closes: stop them, leave them running, or stay.
    /// Services are left alone either way, since Windows keeps them running.
    /// </summary>
    /// <param name="e">Canceled while the question is open and when the user chooses to stay.</param>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        bool isShuttingDown = e.CloseReason == WindowCloseReason.OSShutdown;
        if (_isClosingConfirmed || isShuttingDown || ViewModel is not { } viewModel)
        {
            return;
        }

        List<ModuleViewModel> running = viewModel.RunningModules();
        if (running.Count == 0)
        {
            return;
        }

        e.Cancel = true;
        if (_isClosingInProgress)
        {
            return;
        }

        _isClosingInProgress = true;
        try
        {
            // Quit from the tray arrives with the window hidden; a dialog needs a visible owner.
            if (!IsVisible)
            {
                Tray?.ShowWindow();
            }

            bool isOne = running.Count == 1;
            string names = string.Join(" and ", running.Select(module => module.Name));
            ConfirmChoice choice = await ConfirmWindow.AskAsync(this,
                $"{names} {(isOne ? "is" : "are")} still running",
                $"Stop {(isOne ? "it" : "them")} with a clean shutdown before quitting, or leave " +
                $"{(isOne ? "it" : "them")} running in the background? Modules installed as Windows services keep " +
                "running either way.",
                "Stop and quit",
                "Leave running");
            if (choice == ConfirmChoice.Cancel)
            {
                return;
            }

            if (choice == ConfirmChoice.Primary)
            {
                await viewModel.StopAllAsync();
            }
            _isClosingConfirmed = true;
            Close();
        }
        finally
        {
            _isClosingInProgress = false;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        bool isMinimizedToTray = change.Property == WindowStateProperty
            && WindowState == WindowState.Minimized
            && Tray is not null
            && IsVisible
            && ViewModel is { Settings.MinimizeToTray: true };
        if (isMinimizedToTray)
        {
            // Posted, so the window finishes minimizing before it leaves the taskbar.
            Dispatcher.UIThread.Post(Hide);
        }
    }

    private async void ChangeRoot_Click(object? sender, RoutedEventArgs e) =>
        await PickRootAsync(isXamppMissing: false);

    /// <summary>Asks for a XAMPP folder and switches to it; logs an error when the folder is not XAMPP.</summary>
    /// <param name="isXamppMissing">True when no XAMPP was found, which the picker's title tells the user.</param>
    /// <returns>A task that completes when the picker is closed and any root change has been applied.</returns>
    private async Task PickRootAsync(bool isXamppMissing)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = isXamppMissing ? "XAMPP not found - choose your XAMPP folder" : "Choose your XAMPP folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        if (!XamppPaths.IsXampp(path))
        {
            viewModel.Write(
                "main",
                $"\"{path}\" is not a XAMPP folder (no apache or mysql folder inside).",
                LogLevel.Error);
            return;
        }

        await viewModel.ChangeRootAsync(path);
    }

    /// <summary>Moves the log subscription from the previous view model to the current one.</summary>
    private void Subscribe()
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.Log.CollectionChanged -= OnLogChanged;
        }

        _subscribedViewModel = ViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.Log.CollectionChanged += OnLogChanged;
            _subscribedViewModel.ResolvePortConflict = AskPortConflictAsync;
        }
    }

    /// <summary>The port conflict dialog: who holds the ports, and the buttons to free them or move the module.</summary>
    /// <param name="prompt">The blocked ports and the available ways out.</param>
    /// <returns>The choice; Cancel when the window is hidden (autostart from the tray) or neither way out exists.</returns>
    private async Task<PortConflictChoice> AskPortConflictAsync(PortConflictPrompt prompt)
    {
        if (!IsVisible || (prompt.FreeLabel is null && prompt.MoveLabel is null))
        {
            return PortConflictChoice.Cancel;
        }

        string lines = string.Join(Environment.NewLine, prompt.Conflicts.Select(conflict =>
            $"Port {conflict.Port}: {conflict.OwnerName} (PID {conflict.Pid})"));
        string hint = prompt.Conflicts.Any(conflict => conflict.IsSystem)
            ? Environment.NewLine + Environment.NewLine + "\"System\" means Windows itself holds the port, usually for IIS."
            : "";
        string message = $"{lines}{hint}{Environment.NewLine}{Environment.NewLine}" +
            (prompt.MoveLabel is null
                ? "End the program to free the port, or cancel and change the port under Config."
                : $"Free the port, or move {prompt.ModuleName} to other ports (its config files and the panel's " +
                  "settings are updated).");

        // Primary is the free action when it exists; otherwise moving takes its place.
        string primary = prompt.FreeLabel ?? prompt.MoveLabel!;
        string? secondary = prompt.FreeLabel is null ? null : prompt.MoveLabel;
        ConfirmChoice choice = await ConfirmWindow.AskAsync(this, $"{prompt.ModuleName} can't start: port in use",
            message, primary, secondary);
        return choice switch
        {
            ConfirmChoice.Primary => prompt.FreeLabel is null ? PortConflictChoice.MovePorts : PortConflictChoice.FreePorts,
            ConfirmChoice.Secondary => PortConflictChoice.MovePorts,
            _ => PortConflictChoice.Cancel,
        };
    }

    /// <summary>Keeps the newest log line in view when lines are added.</summary>
    /// <param name="sender">The view model's log collection.</param>
    /// <param name="e">
    /// Only an <see cref="NotifyCollectionChangedAction.Add"/> scrolls; trimming and clearing do not.
    /// </param>
    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }
        if (ViewModel is not { } viewModel || viewModel.Log.Count == 0)
        {
            return;
        }

        LogEntry newestEntry = viewModel.Log[^1];
        // Background priority lets the list handle the added item before it is asked to scroll to it.
        Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(newestEntry), DispatcherPriority.Background);
    }

    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        AppSettings edited = viewModel.Settings.Clone();
        var dialog = new SettingsWindow(edited, viewModel.Root);
        bool isSaved = await dialog.ShowDialog<bool>(this);
        if (isSaved)
        {
            await viewModel.ApplySettingsAsync(edited);
        }
    }

    private void Netstat_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        new NetstatWindow { DataContext = new NetstatViewModel(viewModel) }.Show(this);
    }

    private async void Databases_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await new DatabasesWindow { DataContext = new DatabasesViewModel(viewModel) }.ShowDialog(this);
        }
    }

    private void Logs_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            new LogViewerWindow { DataContext = new LogViewerViewModel(viewModel) }.Show(this);
        }
    }

    private async void Sites_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        await new SitesWindow { DataContext = new SitesViewModel(viewModel) }.ShowDialog(this);
    }

    private async void PhpSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        await new PhpSettingsWindow { DataContext = new PhpSettingsViewModel(viewModel) }.ShowDialog(this);
    }

    private async void Updates_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        await new UpdatesWindow { DataContext = new UpdatesViewModel(viewModel) }.ShowDialog(this);
    }

    private async void AppUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await new WhatsNewWindow { DataContext = viewModel }.ShowDialog(this);
        }
    }

    private void Quit_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Switches to the other theme and remembers it, the same as choosing it in Config.</summary>
    /// <param name="sender">The sun/moon button in the header.</param>
    /// <param name="e">Carries nothing the switch needs.</param>
    private void ThemeToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.SetTheme(IsDark ? AppTheme.Light : AppTheme.Dark);
        }
    }

    private bool IsDark => ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>Shows the moon in the light theme and the sun in the dark one, with a tooltip for the click.</summary>
    private void UpdateThemeToggle()
    {
        string iconKey = IsDark ? "IconSun" : "IconMoon";
        if (this.TryFindResource(iconKey, ActualThemeVariant, out object? icon) && icon is Geometry geometry)
        {
            ThemeIcon.Data = geometry;
        }
        ToolTip.SetTip(ThemeToggle, IsDark ? "Switch to the light theme" : "Switch to the dark theme");
    }

    private async void CopySelected_Click(object? sender, RoutedEventArgs e)
    {
        IEnumerable<string> lines = LogList.SelectedItems?.OfType<LogEntry>().Select(entry => entry.ToString()) ?? [];
        await CopyAsync(string.Join(Environment.NewLine, lines));
    }

    private async void CopyAll_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await CopyAsync(string.Join(Environment.NewLine, viewModel.Log.Select(entry => entry.ToString())));
        }
    }

    /// <summary>Puts the text on the clipboard; does nothing when it is empty or no clipboard is available.</summary>
    /// <param name="text">The log lines to copy, already joined with newlines.</param>
    /// <returns>A task that completes when the clipboard has been set or skipped.</returns>
    private async Task CopyAsync(string text)
    {
        if (string.IsNullOrEmpty(text) || GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
    }
}
