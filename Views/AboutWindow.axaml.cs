using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using XamppControl.Core;
using XamppControl.Updates;
using XamppControl.ViewModels;

namespace XamppControl.Views;

/// <summary>Version, build details, links and credits; the caller sets the <see cref="MainViewModel"/> as data context.</summary>
public partial class AboutWindow : Window
{
    private const string IssuesUrl = AppUpdateCheck.RepositoryUrl + "/issues/new/choose";
    private const string LicenseUrl = AppUpdateCheck.RepositoryUrl + "/blob/main/LICENSE";

    /// <summary>Fills the window from <see cref="BuildInfo"/>.</summary>
    public AboutWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);
        if (AppIcon.LoadLogo() is { } logo)
        {
            Logo.Source = logo;
        }

        AppName.Text = BuildInfo.AppName;
        VersionText.Text = $"{BuildInfo.Version} · compiled {BuildInfo.CompiledOn}";
        PublisherText.Text = "by " + BuildInfo.Publisher;
        VersionLine.Text = BuildInfo.Version;
        RuntimeLine.Text = $"{BuildInfo.Runtime}, {BuildInfo.AvaloniaVersion}";
        OsLine.Text = Environment.OSVersion.VersionString;
        PathLine.Text = Environment.ProcessPath ?? "";
        CopyrightText.Text = BuildInfo.Copyright;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private string? Browser => ViewModel?.Settings.Browser;

    private async void CheckUpdates_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        CheckButton.IsEnabled = false;
        UpdateText.Text = "Checking GitHub…";
        try
        {
            UpdateText.Text = await viewModel.CheckForAppUpdateNowAsync();
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }

        if (viewModel.HasAppUpdate)
        {
            // Hand over to What's new, which installs or links to the release.
            Close();
            if (Owner is Window owner)
            {
                await new WhatsNewWindow { DataContext = viewModel }.ShowDialog(owner);
            }
        }
    }

    private void Repo_Click(object? sender, RoutedEventArgs e) => Shell.OpenUrl(AppUpdateCheck.RepositoryUrl, Browser);

    private void Releases_Click(object? sender, RoutedEventArgs e) => Shell.OpenUrl(AppUpdateCheck.ReleasesUrl, Browser);

    private void Issue_Click(object? sender, RoutedEventArgs e) => Shell.OpenUrl(IssuesUrl, Browser);

    private void License_Click(object? sender, RoutedEventArgs e) => Shell.OpenUrl(LicenseUrl, Browser);

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(BuildInfo.Summary);
            UpdateText.Text = "Build details copied.";
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
