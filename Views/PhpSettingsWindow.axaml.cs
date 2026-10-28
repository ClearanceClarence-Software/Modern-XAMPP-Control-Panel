using Avalonia.Controls;
using Avalonia.Interactivity;
using XamppControl.Core;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>Edits php.ini through the <see cref="PhpSettingsViewModel"/> set as data context.</summary>
public partial class PhpSettingsWindow : Window
{
    /// <summary>Sets up the window and blocks closing while php.ini is saved and Apache restarts.</summary>
    public PhpSettingsWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);

        // Closing halfway would leave Apache stopped.
        Closing += (_, closingArgs) =>
        {
            if (ViewModel is { IsBusy: true })
            {
                closingArgs.Cancel = true;
            }
        };
    }

    private PhpSettingsViewModel? ViewModel => DataContext as PhpSettingsViewModel;

    private void OpenIni_Click(object? sender, RoutedEventArgs e) => ViewModel?.OpenIni();

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SaveAsync();
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
