using System.Text;
using System.Text.RegularExpressions;
using XamppControl.Updates;

namespace XamppControl.Php;

/// <summary>An extension DLL in PHP's ext folder and whether php.ini loads it.</summary>
/// <param name="Name">The name between "php_" and ".dll", lowercase, e.g. "gd" or "opcache".</param>
/// <param name="IsZend">True for Zend extensions (OPcache, Xdebug), which load with zend_extension=.</param>
/// <param name="IsEnabled">True when an active extension= or zend_extension= line loads it.</param>
public sealed record PhpExtension(string Name, bool IsZend, bool IsEnabled);

/// <summary>
/// Reads and changes php.ini line by line, so comments, order and everything the panel doesn't touch stay as written.
/// </summary>
public sealed partial class PhpIni
{
    private readonly List<string> _lines;
    private readonly Encoding _encoding;
    private readonly string _lineBreak;

    private PhpIni(string path, List<string> lines, Encoding encoding, string lineBreak)
    {
        FilePath = path;
        _lines = lines;
        _encoding = encoding;
        _lineBreak = lineBreak;
    }

    /// <summary>Full path of the php.ini this instance was read from and saves to.</summary>
    public string FilePath { get; }

    /// <summary>Reads php.ini.</summary>
    /// <param name="path">Full path of php.ini.</param>
    /// <returns>The file's lines, ready to read and change.</returns>
    /// <exception cref="IOException">The file is missing or cannot be read.</exception>
    public static PhpIni Load(string path)
    {
        string text = FileTools.ReadText(path, out Encoding encoding);
        string lineBreak = FileTools.LineBreakOf(text);
        List<string> lines = [.. text.Split(lineBreak)];

        // A file that ends with a line break splits into a last empty entry, which Save writes back as that break.
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        return new PhpIni(path, lines, encoding, lineBreak);
    }

    /// <summary>The value PHP uses for a directive: the last active line wins, as in PHP itself.</summary>
    /// <param name="key">The directive, such as memory_limit; compared ignoring case.</param>
    /// <returns>The value without quotes or a trailing comment; null when no active line sets it.</returns>
    public string? GetValue(string key)
    {
        int index = LastValueLine(key, isActive: true);
        return index < 0 ? null : CleanValue(ValueLine().Match(_lines[index]).Groups["value"].Value);
    }

    /// <summary>
    /// Sets a directive: replaces the last active line, else uncomments the last commented one, else adds a line at
    /// the end.
    /// </summary>
    /// <param name="key">The directive, such as memory_limit.</param>
    /// <param name="value">
    /// The new value as PHP reads it; quoted when it holds spaces or characters php.ini treats specially.
    /// </param>
    public void SetValue(string key, string value)
    {
        string line = $"{key}={Quote(value)}";
        int index = LastValueLine(key, isActive: true);
        if (index < 0)
        {
            index = LastValueLine(key, isActive: false);
        }

        if (index >= 0)
        {
            _lines[index] = line;
        }
        else
        {
            _lines.Add(line);
        }
    }

    /// <summary>The extension DLLs in an ext folder and whether this php.ini loads each of them.</summary>
    /// <param name="extFolder">PHP's ext folder; a missing folder gives an empty list.</param>
    /// <returns>The extensions sorted by name.</returns>
    public List<PhpExtension> ListExtensions(string extFolder)
    {
        if (!Directory.Exists(extFolder))
        {
            return [];
        }

        return Directory.EnumerateFiles(extFolder, "php_*.dll")
            .Select(file => ExtensionName(Path.GetFileName(file)))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Select(name => new PhpExtension(name, IsZendExtension(name), IsExtensionEnabled(name)))
            .ToList();
    }

    /// <summary>Turns an extension on or off.</summary>
    /// <param name="extension">The extension from <see cref="ListExtensions"/>.</param>
    /// <param name="isEnabled">
    /// True uncomments its last commented line, or adds one after the other extension lines; false comments out every
    /// line that loads it.
    /// </param>
    public void SetExtensionEnabled(PhpExtension extension, bool isEnabled)
    {
        if (!isEnabled)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                if (ExtensionLineFor(i, extension.Name) is { IsActive: true })
                {
                    _lines[i] = ";" + _lines[i];
                }
            }
            return;
        }

        if (IsExtensionEnabled(extension.Name))
        {
            return;
        }

        int lastCommented = -1;
        int lastExtensionLine = -1;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (ExtensionLine().IsMatch(_lines[i]))
            {
                lastExtensionLine = i;
            }
            if (ExtensionLineFor(i, extension.Name) is { IsActive: false })
            {
                lastCommented = i;
            }
        }

        if (lastCommented >= 0)
        {
            _lines[lastCommented] = _lines[lastCommented].TrimStart().TrimStart(';').TrimStart();
            return;
        }

        string directive = extension.IsZend ? "zend_extension" : "extension";
        // A bare name works for simple names; others (php_xdebug-3.3.2-8.3-vs16-x86_64.dll) need the file name.
        string fileReference = SimpleName().IsMatch(extension.Name) ? extension.Name : $"php_{extension.Name}.dll";
        int insertAt = lastExtensionLine >= 0 ? lastExtensionLine + 1 : _lines.Count;
        _lines.Insert(insertAt, $"{directive}={fileReference}");
    }

    /// <summary>
    /// Writes the lines back in the file's own encoding and line breaks, keeping the previous file as php.ini.bak.
    /// </summary>
    /// <exception cref="IOException">The file or its backup cannot be written.</exception>
    public void Save()
    {
        if (File.Exists(FilePath))
        {
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
        }
        File.WriteAllText(FilePath, string.Join(_lineBreak, _lines) + _lineBreak, _encoding);
    }

    /// <summary>Whether an active line loads the extension.</summary>
    /// <param name="name">The extension's name as <see cref="ExtensionName"/> gives it.</param>
    /// <returns>True when any active extension= or zend_extension= line refers to it.</returns>
    private bool IsExtensionEnabled(string name)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            if (ExtensionLineFor(i, name) is { IsActive: true })
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether the extension loads as a Zend extension, by php.ini's own lines or by its name.</summary>
    /// <param name="name">The extension's name.</param>
    /// <returns>True when php.ini loads it with zend_extension=, or it is OPcache or Xdebug.</returns>
    private bool IsZendExtension(string name)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            if (ExtensionLineFor(i, name) is { IsZend: true })
            {
                return true;
            }
        }
        return name == "opcache" || name.StartsWith("xdebug", StringComparison.Ordinal);
    }

    /// <summary>Reads line <paramref name="index"/> as an extension line for one extension.</summary>
    /// <param name="index">The line's index.</param>
    /// <param name="name">The extension's name.</param>
    /// <returns>Whether the line is active and a Zend line; null when it doesn't load that extension.</returns>
    private (bool IsActive, bool IsZend)? ExtensionLineFor(int index, string name)
    {
        Match extension = ExtensionLine().Match(_lines[index]);
        if (!extension.Success || ExtensionName(extension.Groups["value"].Value) != name)
        {
            return null;
        }

        bool isZend = extension.Groups["kind"].Value.StartsWith("zend", StringComparison.OrdinalIgnoreCase);
        return (extension.Groups["comment"].Length == 0, isZend);
    }

    /// <summary>The index of the last line that sets a directive.</summary>
    /// <param name="key">The directive, compared ignoring case.</param>
    /// <param name="isActive">True looks for an active line; false for a commented-out one.</param>
    /// <returns>The line's index; -1 when there is none.</returns>
    private int LastValueLine(string key, bool isActive)
    {
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            Match value = ValueLine().Match(_lines[i]);
            bool isMatch = value.Success
                && value.Groups["key"].Value.Equals(key, StringComparison.OrdinalIgnoreCase)
                && (value.Groups["comment"].Length == 0) == isActive;
            if (isMatch)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>"php_gd.dll", "gd", "C:\xampp\php\ext\php_gd.dll" all give "gd".</summary>
    /// <param name="reference">An extension line's value or a DLL's file name.</param>
    /// <returns>The lowercase name without folder, "php_" prefix or ".dll".</returns>
    private static string ExtensionName(string reference)
    {
        string name = Path.GetFileName(reference.Replace('/', '\\')).ToLowerInvariant();
        if (name.EndsWith(".dll", StringComparison.Ordinal))
        {
            name = name[..^4];
        }
        if (name.StartsWith("php_", StringComparison.Ordinal))
        {
            name = name[4..];
        }
        return name;
    }

    /// <summary>A value as PHP reads it: without surrounding quotes, or without a trailing ; comment.</summary>
    /// <param name="raw">Everything after the = on the line.</param>
    /// <returns>The value, trimmed.</returns>
    private static string CleanValue(string raw)
    {
        string trimmed = raw.Trim();
        if (trimmed.StartsWith('"'))
        {
            int closingQuote = trimmed.IndexOf('"', 1);
            return closingQuote > 0 ? trimmed[1..closingQuote] : trimmed[1..];
        }

        int comment = trimmed.IndexOf(';');
        return (comment >= 0 ? trimmed[..comment] : trimmed).Trim();
    }

    /// <summary>Quotes a value when php.ini would otherwise misread it.</summary>
    /// <param name="value">The value to write.</param>
    /// <returns>The value as is when it is plain; in double quotes when it holds spaces, ; = or quotes.</returns>
    private static string Quote(string value)
    {
        bool needsQuotes = value.Any(character => char.IsWhiteSpace(character) || character is ';' or '=' or '"');
        return needsQuotes ? $"\"{value.Replace("\"", "", StringComparison.Ordinal)}\"" : value;
    }

    [GeneratedRegex(@"^\s*(?<comment>;\s*)?(?<key>[A-Za-z_][A-Za-z0-9_.]*)\s*=(?<value>.*)$")]
    private static partial Regex ValueLine();

    [GeneratedRegex(@"^\s*(?<comment>;+\s*)?(?<kind>zend_extension|extension)\s*=\s*""?(?<value>[^""\s;]+)""?",
        RegexOptions.IgnoreCase)]
    private static partial Regex ExtensionLine();

    [GeneratedRegex(@"^[a-z0-9_]+$")]
    private static partial Regex SimpleName();
}
