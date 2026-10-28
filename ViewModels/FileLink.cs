using System.Windows.Input;
using XamppControl.Core;

namespace XamppControl.ViewModels;

/// <summary>An entry in a module's Config or Logs menu: a file opens in the editor, a folder in Explorer.</summary>
public sealed class FileLink
{
    public FileLink(string label, string path, Func<string?> editor)
    {
        Label = label;
        Path = path;
        OpenCommand = new RelayCommand(() =>
        {
            if (Directory.Exists(Path))
                Shell.Open(Path);
            else
                Shell.OpenInEditor(Path, editor());
        });
    }

    public string Label { get; }
    public string Path { get; }
    public ICommand OpenCommand { get; }
}
