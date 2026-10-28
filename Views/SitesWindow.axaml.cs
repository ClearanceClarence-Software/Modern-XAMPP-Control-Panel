using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XamppControl.Core;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>Edits and applies the local sites through the <see cref="SitesViewModel"/> set as data context.</summary>
public partial class SitesWindow : Window
{
    /// <summary>Sets up the window and blocks closing while the sites are being applied.</summary>
    public SitesWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);

        // Closing halfway would leave Apache stopped or the hosts file half done.
        Closing += (_, closingArgs) =>
        {
            if (ViewModel is { IsBusy: true })
            {
                closingArgs.Cancel = true;
            }
        };
    }

    private SitesViewModel? ViewModel => DataContext as SitesViewModel;

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SiteRow row })
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Choose the folder for {(row.Host.Length > 0 ? row.Host : "the site")}",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            row.Folder = path;
        }
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SiteRow row })
        {
            ViewModel?.Remove(row);
        }
    }

    private async void Apply_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.ApplyAsync();
        }
    }

    private async void Trust_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.TrustAsync();
        }
    }

    private void ShowAuthority_Click(object? sender, RoutedEventArgs e) => SitesViewModel.ShowAuthorityFile();

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
