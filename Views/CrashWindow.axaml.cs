using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using XamppControl.Core;
using XamppControl.Updates;

namespace XamppControl.Views;

/// <summary>Shows a crash report with buttons to copy it, open an issue, carry on or quit.</summary>
public partial class CrashWindow : Window
{
    private readonly string _details;

    /// <summary>Opens the window with an empty report, for the XAML previewer.</summary>
    public CrashWindow() : this("")
    {
    }

    /// <summary>Opens the window showing a report from <see cref="CrashReport.Describe"/>.</summary>
    /// <param name="details">The report text, shown as is.</param>
    public CrashWindow(string details)
    {
        InitializeComponent();
        AppIcon.Apply(this);
        _details = details;
        DetailsText.Text = details;
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(_details);
        }
    }

    private void Issue_Click(object? sender, RoutedEventArgs e) =>
        Shell.OpenUrl(AppUpdateCheck.RepositoryUrl + "/issues/new/choose", null);

    private void Continue_Click(object? sender, RoutedEventArgs e) => Close();

    private void Quit_Click(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(1);
        }
        else
        {
            Close();
        }
    }
}
