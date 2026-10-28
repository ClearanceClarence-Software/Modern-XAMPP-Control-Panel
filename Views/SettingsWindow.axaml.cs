using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XamppControl.Core;

namespace XamppControl.Views;

/// <summary>
/// XAMPP-style "Configuration of Control Panel". Edits a copy of the settings; the caller applies it on Save.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    // Parameterless constructor for the XAML previewer.
    public SettingsWindow() : this(new AppSettings(), @"C:\xampp") { }

    public SettingsWindow(AppSettings settings, string currentRoot)
    {
        InitializeComponent();
        AppIcon.Apply(this);
        _settings = settings;

        EditorBox.Text = settings.Editor;
        BrowserBox.Text = settings.Browser;
        RootBox.Text = settings.XamppRoot ?? currentRoot;

        AutoApache.IsChecked = settings.IsAutoStart("Apache");
        AutoMySql.IsChecked = settings.IsAutoStart("MySQL");
        AutoFileZilla.IsChecked = settings.IsAutoStart("FileZilla");
        AutoMercury.IsChecked = settings.IsAutoStart("Mercury");
        AutoTomcat.IsChecked = settings.IsAutoStart("Tomcat");

        StartMinimizedBox.IsChecked = settings.StartMinimized;
        TomcatWindowBox.IsChecked = settings.TomcatOutputWindow;
        CheckPortsBox.IsChecked = settings.CheckPortsOnStartup;
        DebugBox.IsChecked = settings.ShowDebug;

        RootBox.TextChanged += (_, _) => UpdateRootHint();
        UpdateRootHint();
    }

    private void UpdateRootHint()
    {
        var path = RootBox.Text?.Trim() ?? "";
        var ok = path.Length == 0 || XamppPaths.IsXampp(path);
        RootHint.Text = path.Length == 0
            ? "Empty: looks next to this program, then in C:\\xampp."
            : ok ? "XAMPP installation found." : "No apache or mysql folder here.";
        RootHint.Foreground = ok
            ? Avalonia.Media.Brush.Parse("#5D6470")
            : Avalonia.Media.Brush.Parse("#B42318");
    }

    // ── browse buttons ───────────────────────────────────────

    private async void BrowseEditor_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickExeAsync("Choose an editor") is { } path)
            EditorBox.Text = path;
    }

    private async void BrowseBrowser_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickExeAsync("Choose a browser") is { } path)
            BrowserBox.Text = path;
    }

    private async void BrowseRoot_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose your XAMPP folder",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            RootBox.Text = path;
    }

    private async Task<string?> PickExeAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Programs") { Patterns = ["*.exe"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    // ── sub-dialogs (edit the same copy) ─────────────────────

    private async void UserFiles_Click(object? sender, RoutedEventArgs e) =>
        await new UserFilesWindow(_settings).ShowDialog<bool>(this);

    private async void ServicePorts_Click(object? sender, RoutedEventArgs e) =>
        await new ServicePortsWindow(_settings).ShowDialog<bool>(this);

    private async void LogOptions_Click(object? sender, RoutedEventArgs e) =>
        await new LogOptionsWindow(_settings).ShowDialog<bool>(this);

    // ── abort / save ─────────────────────────────────────────

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var root = RootBox.Text?.Trim() ?? "";
        if (root.Length > 0 && !XamppPaths.IsXampp(root))
        {
            UpdateRootHint();
            RootBox.Focus();
            return;
        }

        _settings.Editor = NullIfEmpty(EditorBox.Text);
        _settings.Browser = NullIfEmpty(BrowserBox.Text);
        _settings.XamppRoot = NullIfEmpty(RootBox.Text);

        _settings.AutoStart = [];
        if (AutoApache.IsChecked == true) _settings.AutoStart.Add("Apache");
        if (AutoMySql.IsChecked == true) _settings.AutoStart.Add("MySQL");
        if (AutoFileZilla.IsChecked == true) _settings.AutoStart.Add("FileZilla");
        if (AutoMercury.IsChecked == true) _settings.AutoStart.Add("Mercury");
        if (AutoTomcat.IsChecked == true) _settings.AutoStart.Add("Tomcat");

        _settings.StartMinimized = StartMinimizedBox.IsChecked == true;
        _settings.TomcatOutputWindow = TomcatWindowBox.IsChecked == true;
        _settings.CheckPortsOnStartup = CheckPortsBox.IsChecked == true;
        _settings.ShowDebug = DebugBox.IsChecked == true;
        Close(true);
    }

    private void Abort_Click(object? sender, RoutedEventArgs e) => Close(false);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
