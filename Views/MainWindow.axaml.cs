using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using XamppControl.Core;
using XamppControl.Modules;
using XamppControl.ViewModels;

namespace XamppControl.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _subscribed;

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
                await PickRootAsync(missing: true);
        };
    }

    private async void ChangeRoot_Click(object? sender, RoutedEventArgs e) => await PickRootAsync(missing: false);

    private async Task PickRootAsync(bool missing)
    {
        if (ViewModel is not { } vm)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = missing ? "XAMPP not found - choose your XAMPP folder" : "Choose your XAMPP folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path)
            return;

        if (!XamppPaths.IsXampp(path))
        {
            vm.Write("main", $"\"{path}\" is not a XAMPP folder (no apache or mysql folder inside).", LogLevel.Error);
            return;
        }

        await vm.ChangeRootAsync(path);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Subscribe()
    {
        if (_subscribed is not null)
            _subscribed.Log.CollectionChanged -= OnLogChanged;
        _subscribed = ViewModel;
        if (_subscribed is not null)
            _subscribed.Log.CollectionChanged += OnLogChanged;
    }

    /// <summary>Keep the newest log line in view.</summary>
    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || ViewModel is not { } vm || vm.Log.Count == 0)
            return;
        var last = vm.Log[^1];
        Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(last), DispatcherPriority.Background);
    }

    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;
        var edited = vm.Settings.Clone();
        var dialog = new SettingsWindow(edited, vm.Root);
        if (await dialog.ShowDialog<bool>(this))
            await vm.ApplySettingsAsync(edited);
    }

    private void Netstat_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;
        new NetstatWindow { DataContext = new NetstatViewModel(vm.Root) }.Show(this);
    }

    private async void Updates_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;
        await new UpdatesWindow { DataContext = new UpdatesViewModel(vm) }.ShowDialog(this);
    }

    private void Quit_Click(object? sender, RoutedEventArgs e) => Close();

    private async void CopySelected_Click(object? sender, RoutedEventArgs e)
    {
        var lines = LogList.SelectedItems?.OfType<LogEntry>().Select(l => l.ToString()) ?? [];
        await CopyAsync(string.Join(Environment.NewLine, lines));
    }

    private async void CopyAll_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await CopyAsync(string.Join(Environment.NewLine, vm.Log.Select(l => l.ToString())));
    }

    private async Task CopyAsync(string text)
    {
        if (string.IsNullOrEmpty(text) || GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        await clipboard.SetTextAsync(text);
    }
}
