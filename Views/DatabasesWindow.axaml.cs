using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XamppControl.Core;
using XamppControl.Databases;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>Exports, imports and drops databases through the <see cref="DatabasesViewModel"/> set as data context.</summary>
public partial class DatabasesWindow : Window
{
    /// <summary>Sets up the window, reads the databases on open and blocks closing while work runs.</summary>
    public DatabasesWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);

        Opened += async (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                await viewModel.RefreshAsync();
            }
        };
        Closing += (_, closingArgs) =>
        {
            if (ViewModel is { IsBusy: true })
            {
                closingArgs.Cancel = true;
            }
        };
    }

    private DatabasesViewModel? ViewModel => DataContext as DatabasesViewModel;

    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is Control { DataContext: DatabaseInfo database })
        {
            await viewModel.ExportAsync(database);
        }
    }

    private async void ImportInto_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not Control { DataContext: DatabaseInfo database })
        {
            return;
        }

        if (await PickSqlAsync($"Choose the .sql file to import into {database.Name}") is { } sqlFile)
        {
            await viewModel.ImportAsync(database.Name, sqlFile);
        }
    }

    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (await PickSqlAsync("Choose the .sql file to import (a new database is created from its name)") is { } sqlFile)
        {
            await viewModel.ImportAsync(DatabasesViewModel.DatabaseNameFor(sqlFile), sqlFile);
        }
    }

    private async void Drop_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is Control { DataContext: DatabaseInfo database })
        {
            await viewModel.DropAsync(database);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Asks for one .sql file, starting in the dump folder.</summary>
    /// <param name="title">The picker's title bar text.</param>
    /// <returns>The chosen file's local path; null when the picker is canceled.</returns>
    private async Task<string?> PickSqlAsync(string title)
    {
        IStorageFolder? start = ViewModel is { } viewModel && Directory.Exists(viewModel.DumpFolder)
            ? await StorageProvider.TryGetFolderFromPathAsync(viewModel.DumpFolder)
            : null;
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = [new FilePickerFileType("SQL dumps") { Patterns = ["*.sql"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
