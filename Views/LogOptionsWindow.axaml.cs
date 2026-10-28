using Avalonia.Controls;
using Avalonia.Interactivity;
using XamppControl.Core;

namespace XamppControl.Views;

public partial class LogOptionsWindow : Window
{
    private static readonly int[] _fontSizes = [10, 11, 12, 13, 14, 16, 18];
    private readonly AppSettings _settings;

    public LogOptionsWindow() : this(new AppSettings()) { }

    public LogOptionsWindow(AppSettings settings)
    {
        InitializeComponent();
        AppIcon.Apply(this);
        _settings = settings;

        FontSizeBox.ItemsSource = _fontSizes;
        FontSizeBox.SelectedItem = _fontSizes.Contains(settings.LogFontSize) ? settings.LogFontSize : 12;
        MaxLinesBox.Text = settings.MaxLogLines.ToString();
        LogToFileBox.IsChecked = settings.LogToFile;
        LogPathText.Text = AppSettings.LogFilePath;
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.Folder);
        Shell.Open(AppSettings.Folder);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MaxLinesBox.Text?.Trim(), out var lines) || lines is < 100 or > 100_000)
        {
            ErrorText.Text = "Lines kept must be between 100 and 100000.";
            ErrorText.IsVisible = true;
            return;
        }

        _settings.LogFontSize = FontSizeBox.SelectedItem is int size ? size : 12;
        _settings.MaxLogLines = lines;
        _settings.LogToFile = LogToFileBox.IsChecked == true;
        Close(true);
    }

    private void Abort_Click(object? sender, RoutedEventArgs e) => Close(false);
}
