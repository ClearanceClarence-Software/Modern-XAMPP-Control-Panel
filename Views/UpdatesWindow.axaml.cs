using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XamppControl.Core;
using XamppControl.Updates;
using XamppControl.ViewModels;

namespace XamppControl.Views;

public partial class UpdatesWindow : Window
{
    public UpdatesWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);

        Opened += async (_, _) =>
        {
            if (ViewModel is { } vm)
                await vm.CheckAsync();
        };

        // Don't allow closing halfway through an update.
        Closing += (_, e) =>
        {
            if (ViewModel is { IsBusy: true })
                e.Cancel = true;
        };
    }

    private UpdatesViewModel? ViewModel => DataContext as UpdatesViewModel;

    private async void Check_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await vm.CheckAsync();
    }

    private async void PhpLatest_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await vm.InstallPhpLatestAsync();
    }

    private async void PhpZip_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && await PickZipAsync("Choose a PHP Thread Safe x64 zip") is { } zip)
            await vm.InstallPhpFromZipAsync(zip);
    }

    private async void MariaLatest_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await vm.InstallMariaLatestAsync();
    }

    private async void MariaZip_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && await PickZipAsync("Choose a MariaDB winx64 zip") is { } zip)
            await vm.InstallMariaFromZipAsync(zip);
    }

    private async void Rollback_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
            await vm.RollbackAsync();
    }

    private async void Switch_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && (sender as Control)?.DataContext is InstalledVersion version)
            await vm.SwitchAsync(version);
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && (sender as Control)?.DataContext is InstalledVersion version)
            vm.Remove(version);
    }

    private async void PhpImport_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && await PickFolderAsync("Choose a PHP folder to add (contains php.exe)") is { } folder)
            vm.Import(Component.Php, folder);
    }

    private async void MariaImport_Click(object? sender, RoutedEventArgs e)
    {
        const string title = "Choose a MariaDB/MySQL folder to add (contains bin\\mariadbd.exe)";
        if (ViewModel is { } vm && await PickFolderAsync(title) is { } folder)
            vm.Import(Component.MariaDb, folder);
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private async Task<string?> PickZipAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Zip archives") { Patterns = ["*.zip"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
