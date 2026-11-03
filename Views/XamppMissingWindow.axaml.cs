using Avalonia.Controls;
using Avalonia.Interactivity;
using XamppControl.Core;

namespace XamppControl.Views;

/// <summary>What the user chose when no XAMPP was found.</summary>
public enum MissingXamppChoice
{
    /// <summary>Open the panel anyway; the folder can be picked later.</summary>
    Continue,

    /// <summary>Open the folder picker.</summary>
    ChooseFolder,
}

/// <summary>
/// Explains that no XAMPP was found, lists the folders checked, and offers a download or a folder choice.
/// </summary>
public partial class XamppMissingWindow : Window
{
    private const string DownloadUrl = "https://www.apachefriends.org/download.html";

    private string? _browser;

    /// <summary>Opens the window with the folders that were checked.</summary>
    public XamppMissingWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);
        if (AppIcon.LoadLogo() is { } logo)
        {
            Logo.Source = logo;
        }
    }

    /// <summary>Shows the window over the panel and waits for the choice.</summary>
    /// <param name="owner">The panel's window.</param>
    /// <param name="searchedFolders">The folders that were checked for XAMPP, in the order tried.</param>
    /// <param name="browser">The configured browser for the download link; null uses the system default.</param>
    /// <returns>The choice; Continue when the window is closed another way.</returns>
    public static async Task<MissingXamppChoice> AskAsync(
        Window owner,
        IEnumerable<string> searchedFolders,
        string? browser)
    {
        var window = new XamppMissingWindow { _browser = browser };
        window.FolderList.ItemsSource = searchedFolders.ToList();
        MissingXamppChoice? choice = await window.ShowDialog<MissingXamppChoice?>(owner);
        return choice ?? MissingXamppChoice.Continue;
    }

    private void Download_Click(object? sender, RoutedEventArgs e) => Shell.OpenUrl(DownloadUrl, _browser);

    private void Continue_Click(object? sender, RoutedEventArgs e) => Close(MissingXamppChoice.Continue);

    private void Choose_Click(object? sender, RoutedEventArgs e) => Close(MissingXamppChoice.ChooseFolder);
}
