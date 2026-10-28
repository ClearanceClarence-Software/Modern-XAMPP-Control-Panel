using Avalonia.Controls;
using Avalonia.Interactivity;
using XamppControl.Core;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>
/// Shows the release notes of every version newer than the running one, from the <see cref="MainViewModel"/> set as
/// data context, with a link to download the newest.
/// </summary>
public partial class WhatsNewWindow : Window
{
    /// <summary>Sets up the window; the notes come from the data context.</summary>
    public WhatsNewWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);
    }

    private void Download_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.OpenAppUpdateCommand.Execute(null);
        }
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
