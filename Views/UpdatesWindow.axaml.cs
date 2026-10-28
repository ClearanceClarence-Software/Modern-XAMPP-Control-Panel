using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XamppControl.Core;
using XamppControl.Updates;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>
/// Installs, switches and rolls back PHP and MariaDB versions through the <see cref="UpdatesViewModel"/>
/// that the caller sets as data context.
/// </summary>
public partial class UpdatesWindow : Window
{
    /// <summary>Sets up the window, checks for new versions on open and blocks closing while an update runs.</summary>
    public UpdatesWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);

        Opened += async (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                await viewModel.CheckAsync();
            }
        };

        // Don't allow closing halfway through an update.
        Closing += (_, closingArgs) =>
        {
            if (ViewModel is { IsBusy: true })
            {
                closingArgs.Cancel = true;
            }
        };
    }

    private UpdatesViewModel? ViewModel => DataContext as UpdatesViewModel;

    private async void Check_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.CheckAsync();
        }
    }

    private async void PhpLatest_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.InstallPhpLatestAsync();
        }
    }

    private async void PhpZip_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && await PickZipAsync("Choose a PHP Thread Safe x64 zip") is { } zip)
        {
            await viewModel.InstallPhpFromZipAsync(zip);
        }
    }

    private async void MariaLatest_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.InstallMariaLatestAsync();
        }
    }

    private async void MariaZip_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && await PickZipAsync("Choose a MariaDB winx64 zip") is { } zip)
        {
            await viewModel.InstallMariaFromZipAsync(zip);
        }
    }

    private async void Rollback_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.RollbackAsync();
        }
    }

    // The buttons sit in each version's row, so the row's data context is the version they act on.
    private async void Switch_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && (sender as Control)?.DataContext is InstalledVersion version)
        {
            await viewModel.SwitchAsync(version);
        }
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && (sender as Control)?.DataContext is InstalledVersion version)
        {
            viewModel.Remove(version);
        }
    }

    private async void PhpImport_Click(object? sender, RoutedEventArgs e)
    {
        const string title = "Choose a PHP folder to add (contains php.exe)";
        if (ViewModel is { } viewModel && await PickFolderAsync(title) is { } folder)
        {
            viewModel.Import(Component.Php, folder);
        }
    }

    private async void MariaImport_Click(object? sender, RoutedEventArgs e)
    {
        const string title = "Choose a MariaDB/MySQL folder to add (contains bin\\mariadbd.exe)";
        if (ViewModel is { } viewModel && await PickFolderAsync(title) is { } folder)
        {
            viewModel.Import(Component.MariaDb, folder);
        }
    }

    /// <summary>Asks for one folder holding an existing installation.</summary>
    /// <param name="title">The picker's title bar text.</param>
    /// <returns>The chosen folder's local path; null when the picker is canceled or it has no local path.</returns>
    private async Task<string?> PickFolderAsync(string title)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Asks for one downloaded .zip archive.</summary>
    /// <param name="title">The picker's title bar text, naming the build the archive must be.</param>
    /// <returns>The chosen archive's local path; null when the picker is canceled or it has no local path.</returns>
    private async Task<string?> PickZipAsync(string title)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Zip archives") { Patterns = ["*.zip"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
