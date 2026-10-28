using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XamppControl.Core;
using XamppControl.Modules;

namespace XamppControl.Views;

public partial class UserFilesWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ObservableCollection<UserFile> _files;

    public UserFilesWindow() : this(new AppSettings()) { }

    public UserFilesWindow(AppSettings settings)
    {
        InitializeComponent();
        AppIcon.Apply(this);
        _settings = settings;
        _files = new ObservableCollection<UserFile>(
            settings.UserFiles.Select(f => new UserFile { Module = f.Module, Path = f.Path }));

        FileList.ItemsSource = _files;
        ModuleBox.ItemsSource = ModuleCatalog.Names;
        ModuleBox.SelectedIndex = 0;
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a file",
            AllowMultiple = false,
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            PathBox.Text = path;
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        var path = PathBox.Text?.Trim();
        if (string.IsNullOrEmpty(path) || ModuleBox.SelectedItem is not string module)
            return;
        _files.Add(new UserFile { Module = module, Path = path });
        PathBox.Text = "";
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is UserFile file)
            _files.Remove(file);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        _settings.UserFiles = [.. _files];
        Close(true);
    }

    private void Abort_Click(object? sender, RoutedEventArgs e) => Close(false);
}
