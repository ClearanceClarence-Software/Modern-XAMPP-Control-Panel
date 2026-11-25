using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using XamppControl.Core;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>
/// Lists the programs that listen on a TCP port, with the chosen one's details beside the list; the caller sets a
/// <see cref="NetstatViewModel"/> as data context.
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

    private void ScopeAll_Click(object? sender, RoutedEventArgs e) => SetScope(NetstatScope.All);

    private void ScopeXampp_Click(object? sender, RoutedEventArgs e) => SetScope(NetstatScope.Xampp);

    private void ScopeBlocking_Click(object? sender, RoutedEventArgs e) => SetScope(NetstatScope.Blocking);

    private void SetScope(NetstatScope scope)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.Scope = scope;
        }
    }

    private void System_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.ShowSystem = !viewModel.ShowSystem;
        }
    }

    // A double-click opens a program with a web port, the quickest way to "what is on 8025?"; on MariaDB it
    // opens phpMyAdmin.
    private async void Programs_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // Only on an entry: the scrollbar and the empty area below the list are not a program.
        bool isOnEntry = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null;
        if (!isOnEntry || ViewModel is not { Selected: { } program } viewModel)
        {
            return;
        }

        if (program.HasUrl)
        {
            viewModel.OpenInBrowser(program);
        }
        else if (program.IsDatabaseServer)
        {
            await viewModel.OpenPhpMyAdminAsync(() => ConfirmWindow.AskToStartApacheAsync(this));
        }
    }

    private void Open_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { Selected: { } program } viewModel)
        {
            viewModel.OpenInBrowser(program);
        }
    }

    private async void PhpMyAdmin_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.OpenPhpMyAdminAsync(() => ConfirmWindow.AskToStartApacheAsync(this));
        }
    }

    private void Folder_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.Selected is { } program)
        {
            NetstatViewModel.OpenFolder(program);
        }
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.Selected is { } program && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(program.Details);
        }
    }

    private async void End_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { Selected: { } program } viewModel)
        {
            await viewModel.EndProcessAsync(program);
        }
    }
}
