using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XamppControl.Core;
using XamppControl.Modules;

namespace XamppControl.Views;

/// <summary>
/// Edits the extra files shown in each module's Config menu; works on a copy of the list and closes with true on Save.
/// </summary>
public partial class UserFilesWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ObservableCollection<UserFile> _files;

    /// <summary>Opens the window on default settings, for the XAML previewer.</summary>
    public UserFilesWindow() : this(new AppSettings())
    {
    }

    /// <summary>Copies the settings' file list into the window and fills the module choices.</summary>
    /// <param name="settings">
    /// Its <see cref="AppSettings.UserFiles"/> is replaced only on Save; the window edits copies of the entries.
    /// </param>
    public UserFilesWindow(AppSettings settings)
    {
        InitializeComponent();
        AppIcon.Apply(this);
        _settings = settings;
        _files = new ObservableCollection<UserFile>(
            settings.UserFiles.Select(userFile => new UserFile { Module = userFile.Module, Path = userFile.Path }));

        FileList.ItemsSource = _files;
        ModuleBox.ItemsSource = ModuleCatalog.Names;
        ModuleBox.SelectedIndex = 0;
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> pickedFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a file",
            AllowMultiple = false,
        });
        if (pickedFiles.Count > 0 && pickedFiles[0].TryGetLocalPath() is { } path)
        {
            PathBox.Text = path;
        }
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        string? path = PathBox.Text?.Trim();
        if (string.IsNullOrEmpty(path) || ModuleBox.SelectedItem is not string module)
        {
            return;
        }

        _files.Add(new UserFile { Module = module, Path = path });
        PathBox.Text = "";
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is UserFile file)
        {
            _files.Remove(file);
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        _settings.UserFiles = [.. _files];
        Close(true);
    }

    private void Abort_Click(object? sender, RoutedEventArgs e) => Close(false);
}
