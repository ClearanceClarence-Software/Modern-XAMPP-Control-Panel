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

    /// <summary>Opens the window on default settings and the default XAMPP folder, for the XAML previewer.</summary>
    public SettingsWindow() : this(new AppSettings(), @"C:\xampp")
    {
    }

    /// <summary>Fills the controls from the settings and starts checking the XAMPP folder as it is typed.</summary>
    /// <param name="settings">
    /// The caller's copy; Save and the sub-dialogs change it in place, and the window closes with true on Save.
    /// </param>
    /// <param name="currentRoot">The XAMPP folder in use, shown when <paramref name="settings"/> names none.</param>
    public SettingsWindow(AppSettings settings, string currentRoot)
    {
        InitializeComponent();
        AppIcon.Apply(this);
        _settings = settings;

        FillPaths(settings, currentRoot);
        FillAutoStart(settings);
        FillOptions(settings);

        RootBox.TextChanged += (_, _) => UpdateRootHint();
        UpdateRootHint();
    }

    /// <summary>The auto-start check boxes with their module names, in the order they are saved.</summary>
    /// <returns>One pair per module, named as <see cref="AppSettings.AutoStart"/> stores it.</returns>
    private (CheckBox Box, string Module)[] AutoStartBoxes() =>
    [
        (AutoApache, "Apache"),
        (AutoMySql, "MySQL"),
        (AutoFileZilla, "FileZilla"),
        (AutoMercury, "Mercury"),
        (AutoTomcat, "Tomcat"),
    ];

    /// <summary>Shows the editor, browser and XAMPP folder.</summary>
    /// <param name="settings">The settings being edited.</param>
    /// <param name="currentRoot">Shown when the settings name no XAMPP folder.</param>
    private void FillPaths(AppSettings settings, string currentRoot)
    {
        EditorBox.Text = settings.Editor;
        BrowserBox.Text = settings.Browser;
        RootBox.Text = settings.XamppRoot ?? currentRoot;
    }

    /// <summary>Checks the box of every module that starts with the panel.</summary>
    /// <param name="settings">The settings being edited.</param>
    private void FillAutoStart(AppSettings settings)
    {
        foreach ((CheckBox box, string module) in AutoStartBoxes())
        {
            box.IsChecked = settings.IsAutoStart(module);
        }
    }

    /// <summary>Shows the general on/off options.</summary>
    /// <param name="settings">The settings being edited.</param>
    private void FillOptions(AppSettings settings)
    {
        StartMinimizedBox.IsChecked = settings.StartMinimized;
        TomcatWindowBox.IsChecked = settings.TomcatOutputWindow;
        CheckPortsBox.IsChecked = settings.CheckPortsOnStartup;
        DebugBox.IsChecked = settings.ShowDebug;
    }

    /// <summary>Says under the folder box whether it holds a XAMPP installation, in red when it does not.</summary>
    private void UpdateRootHint()
    {
        string path = RootBox.Text?.Trim() ?? "";
        bool isEmpty = path.Length == 0;
        bool isRootAccepted = isEmpty || XamppPaths.IsXampp(path);

        if (isEmpty)
        {
            RootHint.Text = "Empty: looks next to this program, then in C:\\xampp.";
        }
        else if (isRootAccepted)
        {
            RootHint.Text = "XAMPP installation found.";
        }
        else
        {
            RootHint.Text = "No apache or mysql folder here.";
        }

        RootHint.Foreground = isRootAccepted
            ? Avalonia.Media.Brush.Parse("#5D6470")
            : Avalonia.Media.Brush.Parse("#B42318");
    }

    // ── browse buttons ───────────────────────────────────────

    private async void BrowseEditor_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickExeAsync("Choose an editor") is { } path)
        {
            EditorBox.Text = path;
        }
    }

    private async void BrowseBrowser_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickExeAsync("Choose a browser") is { } path)
        {
            BrowserBox.Text = path;
        }
    }

    private async void BrowseRoot_Click(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose your XAMPP folder",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            RootBox.Text = path;
        }
    }

    /// <summary>Asks for one program file.</summary>
    /// <param name="title">The picker's title bar text.</param>
    /// <returns>The chosen .exe's local path; null when the picker is canceled or the file has no local path.</returns>
    private async Task<string?> PickExeAsync(string title)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
        string root = RootBox.Text?.Trim() ?? "";
        if (root.Length > 0 && !XamppPaths.IsXampp(root))
        {
            UpdateRootHint();
            RootBox.Focus();
            return;
        }

        SavePaths();
        SaveAutoStart();
        SaveOptions();
        Close(true);
    }

    private void Abort_Click(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>Writes the editor, browser and XAMPP folder back; an empty box stores null (use the default).</summary>
    private void SavePaths()
    {
        _settings.Editor = NullIfEmpty(EditorBox.Text);
        _settings.Browser = NullIfEmpty(BrowserBox.Text);
        _settings.XamppRoot = NullIfEmpty(RootBox.Text);
    }

    /// <summary>Replaces the auto-start list with the checked modules, in <see cref="AutoStartBoxes"/> order.</summary>
    private void SaveAutoStart()
    {
        _settings.AutoStart = [];
        foreach ((CheckBox box, string module) in AutoStartBoxes())
        {
            if (box.IsChecked == true)
            {
                _settings.AutoStart.Add(module);
            }
        }
    }

    /// <summary>Writes the general on/off options back.</summary>
    private void SaveOptions()
    {
        _settings.StartMinimized = StartMinimizedBox.IsChecked == true;
        _settings.TomcatOutputWindow = TomcatWindowBox.IsChecked == true;
        _settings.CheckPortsOnStartup = CheckPortsBox.IsChecked == true;
        _settings.ShowDebug = DebugBox.IsChecked == true;
    }

    /// <summary>Turns a text box's text into a setting value.</summary>
    /// <param name="value">The box's text; null, empty and white space all count as empty.</param>
    /// <returns>The trimmed text, or null when it is empty.</returns>
    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
