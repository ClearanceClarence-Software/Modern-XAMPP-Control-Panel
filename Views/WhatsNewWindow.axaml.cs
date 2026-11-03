using Avalonia.Controls;
using Avalonia.Interactivity;
using XamppControl.Core;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>
/// Shows the release notes of every version newer than the running one, from the <see cref="MainViewModel"/> set as
/// data context, and installs the newest in place or links to its release page.
/// </summary>
public partial class WhatsNewWindow : Window
{
    /// <summary>
    /// Sets up the window; the notes come from the data context. Closing is blocked while an update runs.
    /// </summary>
    public WhatsNewWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);

        Closing += (_, closingArgs) =>
        {
            if (ViewModel is { IsInstallingAppUpdate: true })
            {
                closingArgs.Cancel = true;
            }
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void Install_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        bool isRestarting = await viewModel.InstallAppUpdateAsync();
        if (isRestarting)
        {
            // The update script waits for the panel to exit, then swaps the exe and starts the new one.
            MainWindow? main = Owner as MainWindow;
            Close();
            main?.QuitForUpdate();
        }
    }

    private void Download_Click(object? sender, RoutedEventArgs e)
    {
        ViewModel?.OpenAppUpdateCommand.Execute(null);
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
