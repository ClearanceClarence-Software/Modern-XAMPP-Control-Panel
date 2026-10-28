using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

        // No XAMPP at the configured or default location: ask for the folder right away.
        Opened += async (_, _) =>
        {
            if (ViewModel is { HasXampp: false })
            {
                await PickRootAsync(isXamppMissing: true);
            }
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

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
        }
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

        new NetstatWindow { DataContext = new NetstatViewModel(viewModel.Root) }.Show(this);
    }

    private async void Updates_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        await new UpdatesWindow { DataContext = new UpdatesViewModel(viewModel) }.ShowDialog(this);
    }

    private void Quit_Click(object? sender, RoutedEventArgs e) => Close();

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
