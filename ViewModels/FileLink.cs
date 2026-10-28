using System.Windows.Input;
using XamppControl.Core;

namespace XamppControl.ViewModels;

/// <summary>An entry in a module's Config or Logs menu: a file opens in the editor, a folder in Explorer.</summary>
public sealed class FileLink
{
    /// <summary>Creates the menu entry and its open command.</summary>
    /// <param name="label">Menu text, e.g. "Apache (httpd.conf)".</param>
    /// <param name="path">Full path of the file or folder; whether it is a folder is checked on each click.</param>
    /// <param name="editor">Read on each click for the configured editor; null or blank falls back to Notepad.</param>
    public FileLink(string label, string path, Func<string?> editor)
    {
        Label = label;
        Path = path;
        OpenCommand = new RelayCommand(() =>
        {
            if (Directory.Exists(Path))
            {
                Shell.Open(Path);
            }
            else
            {
                Shell.OpenInEditor(Path, editor());
            }
        });
    }

    /// <summary>Menu text.</summary>
    public string Label { get; }

    /// <summary>Full path of the file or folder.</summary>
    public string Path { get; }

    /// <summary>Opens a folder in Explorer or a file in the configured editor.</summary>
    public ICommand OpenCommand { get; }
}
