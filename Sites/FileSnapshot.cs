using XamppControl.Updates;

namespace XamppControl.Sites;

/// <summary>The exact bytes of a few files at one moment, to put them back after a change that failed.</summary>
public sealed class FileSnapshot
{
    private readonly Dictionary<string, byte[]?> _contents;

    private FileSnapshot(Dictionary<string, byte[]?> contents) => _contents = contents;

    /// <summary>Reads the files as they are now.</summary>
    /// <param name="paths">Full paths; a file that doesn't exist is remembered as missing.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="IOException">A file exists but cannot be read.</exception>
    public static async Task<FileSnapshot> TakeAsync(IEnumerable<string> paths)
    {
        var contents = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            contents[path] = File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
        }
        return new FileSnapshot(contents);
    }

    /// <summary>Writes every file back byte for byte, and deletes the ones that didn't exist.</summary>
    /// <returns>A task that completes when every file is restored.</returns>
    /// <exception cref="IOException">A file cannot be written.</exception>
    public async Task RestoreAsync()
    {
        foreach ((string path, byte[]? bytes) in _contents)
        {
            if (bytes is null)
            {
                FileTools.TryDeleteFile(path);
            }
            else
            {
                await File.WriteAllBytesAsync(path, bytes);
            }
        }
    }
}
