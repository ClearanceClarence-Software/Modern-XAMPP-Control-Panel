using Avalonia.Controls;
using Avalonia.Interactivity;
using XamppControl.Core;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>
/// Lists the listening TCP sockets and their processes; the caller sets a <see cref="NetstatViewModel"/> as data
/// context.
/// </summary>
public partial class NetstatWindow : Window
{
    /// <summary>Sets up the window and disposes the data context when the window closes.</summary>
    public NetstatWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);

        // Disposing the view model stops its auto-refresh timer.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private NetstatViewModel? ViewModel => DataContext as NetstatViewModel;

    private void SortPort_Click(object? sender, RoutedEventArgs e) => ViewModel?.SortBy("Port");

    private void SortAddress_Click(object? sender, RoutedEventArgs e) => ViewModel?.SortBy("Address");

    private void SortPid_Click(object? sender, RoutedEventArgs e) => ViewModel?.SortBy("Pid");

    private void SortName_Click(object? sender, RoutedEventArgs e) => ViewModel?.SortBy("Name");

    private void OpenUrl_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            ViewModel?.OpenInBrowser(row);
        }
    }

    private void Folder_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            NetstatViewModel.OpenFolder(row);
        }
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(row.Line);
        }
    }

    private async void End_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row && ViewModel is { } viewModel)
        {
            await viewModel.EndProcessAsync(row);
        }
    }

    /// <summary>The row a button inside a row template belongs to.</summary>
    /// <param name="sender">The clicked button.</param>
    /// <returns>Its data context as a row; null for anything else.</returns>
    private static NetstatRow? RowOf(object? sender) => (sender as Control)?.DataContext as NetstatRow;
}
