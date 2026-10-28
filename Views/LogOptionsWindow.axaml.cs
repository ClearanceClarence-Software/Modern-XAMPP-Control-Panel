using Avalonia.Controls;
using Avalonia.Interactivity;
using XamppControl.Core;

namespace XamppControl.Views;

/// <summary>
/// Edits the log options (font size, lines kept, log file) in the settings it is given; closes with true on Save.
/// </summary>
public partial class LogOptionsWindow : Window
{
    private const int DefaultFontSize = 12;
    private const int MinLinesKept = 100;
    private const int MaxLinesKept = 100_000;

    private static readonly int[] _fontSizes = [10, 11, 12, 13, 14, 16, 18];
    private readonly AppSettings _settings;

    /// <summary>Opens the window on default settings, for the XAML previewer.</summary>
    public LogOptionsWindow() : this(new AppSettings())
    {
    }

    /// <summary>Fills the controls from the settings that Save writes back to.</summary>
    /// <param name="settings">
    /// Changed in place only on Save; the settings window passes its own copy, applied only when that is saved.
    /// </param>
    public LogOptionsWindow(AppSettings settings)
    {
        InitializeComponent();
        AppIcon.Apply(this);
        _settings = settings;

        FontSizeBox.ItemsSource = _fontSizes;
        FontSizeBox.SelectedItem = _fontSizes.Contains(settings.LogFontSize) ? settings.LogFontSize : DefaultFontSize;
        MaxLinesBox.Text = settings.MaxLogLines.ToString();
        LogToFileBox.IsChecked = settings.LogToFile;
        LogPathText.Text = AppSettings.LogFilePath;
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        // The folder may not exist yet (nothing saved or logged to a file); Explorer needs it to exist.
        Directory.CreateDirectory(AppSettings.Folder);
        Shell.Open(AppSettings.Folder);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        bool isValidLineCount = int.TryParse(MaxLinesBox.Text?.Trim(), out int linesKept)
            && linesKept is >= MinLinesKept and <= MaxLinesKept;
        if (!isValidLineCount)
        {
            ErrorText.Text = "Lines kept must be between 100 and 100000.";
            ErrorText.IsVisible = true;
            return;
        }

        _settings.LogFontSize = FontSizeBox.SelectedItem is int fontSize ? fontSize : DefaultFontSize;
        _settings.MaxLogLines = linesKept;
        _settings.LogToFile = LogToFileBox.IsChecked == true;
        Close(true);
    }

    private void Abort_Click(object? sender, RoutedEventArgs e) => Close(false);
}
