using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace XamppControl.Core;

/// <summary>A language the panel can show.</summary>
/// <param name="Code">The language file's name without ".json", e.g. "en" or "nb".</param>
/// <param name="Name">The language's own name from the file's "$name" entry, e.g. "Norsk bokmål".</param>
public sealed record Language(string Code, string Name);

/// <summary>
/// The panel's text in the chosen language. Every string lives in a flat JSON file (Lang\en.json is the source of
/// truth); XAML reads them with <c>{DynamicResource Some.Key}</c> and code with <see cref="T(string)"/>. A language
/// file may leave keys out; those fall back to English. Log lines are not translated, so logs read the same in
/// every bug report.
/// </summary>
public static class Loc
{
    /// <summary>The language every key exists in, and the fallback for the others.</summary>
    public const string English = "en";

    /// <summary>The settings value that follows Windows' display language.</summary>
    public const string FollowWindows = "";

    private const string NameKey = "$name";
    private static readonly Uri _embeddedFolder = new("avares://XamppControl/Lang");

    private static Dictionary<string, string> _english = [];
    private static Dictionary<string, string> _strings = [];
    private static ResourceDictionary? _resources;

    /// <summary>The code of the language in use, e.g. "en"; "en" until <see cref="Apply"/> has run.</summary>
    public static string Current { get; private set; } = English;

    /// <summary>Raised after <see cref="Apply"/> switched the language, for text that code builds itself.</summary>
    public static event Action? Changed;

    /// <summary>The text for a key in the current language.</summary>
    /// <param name="key">A key from Lang\en.json, e.g. "Main.Start".</param>
    /// <returns>The translation, else the English text, else the key itself so a missing entry is easy to spot.</returns>
    public static string T(string key) =>
        _strings.TryGetValue(key, out string? text) || _english.TryGetValue(key, out text) ? text : key;

    /// <summary>The text for a key with its {0}, {1}... placeholders filled in.</summary>
    /// <param name="key">A key from Lang\en.json whose text has placeholders.</param>
    /// <param name="args">The values for the placeholders, in order.</param>
    /// <returns>The formatted text; the unformatted text when a translation's placeholders don't fit the values.</returns>
    public static string T(string key, params object?[] args)
    {
        string text = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, text, args);
        }
        catch (FormatException)
        {
            // A translation with a stray brace or a placeholder the code doesn't supply: show it as written.
            return text;
        }
    }

    /// <summary>The text for a count: the key with ".One" for exactly one, ".Other" otherwise; {0} is the count.</summary>
    /// <param name="key">The key without its ".One"/".Other" ending, e.g. "Databases.Count".</param>
    /// <param name="count">The number that picks the form and fills {0}.</param>
    /// <param name="args">Further values for {1}, {2}...</param>
    /// <returns>The formatted text.</returns>
    public static string Plural(string key, int count, params object?[] args) =>
        T(key + (count == 1 ? ".One" : ".Other"), [count, .. args]);

    /// <summary>The languages that can be chosen: the embedded ones, then any extra files found on disk.</summary>
    /// <returns>English first, the rest by name.</returns>
    public static List<Language> Languages()
    {
        var languages = new Dictionary<string, Language>(StringComparer.OrdinalIgnoreCase);
        foreach ((string code, Func<Stream> open) in Sources())
        {
            if (!languages.ContainsKey(code) && Read(open).TryGetValue(NameKey, out string? name))
            {
                languages[code] = new Language(code, name);
            }
        }
        return languages.Values
            .OrderBy(language => language.Code != English)
            .ThenBy(language => language.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// Loads a language and publishes its strings as application resources, so every <c>DynamicResource</c> in the
    /// open windows changes at once.
    /// </summary>
    /// <param name="code">A language code, or <see cref="FollowWindows"/> to follow Windows; unknown codes mean English.</param>
    public static void Apply(string? code)
    {
        Dictionary<string, Func<Stream>> sources = Sources()
            .GroupBy(source => source.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Open, StringComparer.OrdinalIgnoreCase);

        _english = sources.TryGetValue(English, out Func<Stream>? openEnglish) ? Read(openEnglish) : [];
        string resolved = Resolve(code, sources.Keys);
        _strings = resolved != English && sources.TryGetValue(resolved, out Func<Stream>? open) ? Read(open) : [];
        Current = resolved;

        if (Application.Current is { } application)
        {
            var resources = new ResourceDictionary();
            foreach ((string key, string text) in _english)
            {
                resources[key] = _strings.GetValueOrDefault(key, text);
            }

            // The new strings go in before the old ones come out, so no text is ever without a value.
            application.Resources.MergedDictionaries.Add(resources);
            if (_resources is not null)
            {
                application.Resources.MergedDictionaries.Remove(_resources);
            }
            _resources = resources;
        }
        Changed?.Invoke();
    }

    /// <summary>Picks the language file for a setting.</summary>
    /// <param name="code">The setting: a code, or empty for Windows' display language.</param>
    /// <param name="available">The codes that have a file.</param>
    /// <returns>The exact code, else its language part ("nb-NO" gives "nb"), else English.</returns>
    internal static string Resolve(string? code, IEnumerable<string> available)
    {
        var codes = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
        string wanted = string.IsNullOrWhiteSpace(code) ? CultureInfo.CurrentUICulture.Name : code.Trim();

        // Windows reports Norwegian as "nb-NO", "nn-NO" or plain "no"; Bokmål serves all three until Nynorsk exists.
        bool isNorwegian = wanted.StartsWith("no", StringComparison.OrdinalIgnoreCase)
            || wanted.StartsWith("nn", StringComparison.OrdinalIgnoreCase);
        string[] candidates = [wanted, wanted.Split('-')[0], isNorwegian ? "nb" : ""];
        string? match = candidates.FirstOrDefault(candidate => candidate.Length > 0 && codes.Contains(candidate));
        return match?.ToLowerInvariant() ?? English;
    }

    /// <summary>Reads one language file: a flat JSON object of key and text.</summary>
    /// <param name="open">Opens the file.</param>
    /// <returns>The entries with string values; empty when the file is missing or not valid JSON.</returns>
    internal static Dictionary<string, string> Read(Func<Stream> open)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using Stream stream = open();
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return strings;
            }

            foreach (JsonProperty entry in document.RootElement.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.String)
                {
                    strings[entry.Name] = entry.Value.GetString() ?? "";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken translation file must not stop the panel; its keys fall back to English.
        }
        return strings;
    }

    /// <summary>
    /// Every language file: those built into the exe, then *.json in a "lang" folder next to the exe and in the
    /// settings folder, so a translation can be tried without rebuilding. Built-in files win on a name clash.
    /// </summary>
    /// <returns>The language code and a way to open each file.</returns>
    private static List<(string Code, Func<Stream> Open)> Sources()
    {
        var sources = new List<(string Code, Func<Stream> Open)>();
        try
        {
            foreach (Uri asset in AssetLoader.GetAssets(_embeddedFolder, null))
            {
                if (asset.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    sources.Add((Path.GetFileNameWithoutExtension(asset.AbsolutePath), () => AssetLoader.Open(asset)));
                }
            }
        }
        catch (Exception)
        {
            // No Avalonia asset system (unit tests, the XAML previewer): only files on disk are available.
        }

        string[] folders =
        [
            Path.Combine(AppContext.BaseDirectory, "lang"),
            Path.Combine(AppSettings.Folder, "lang"),
        ];
        foreach (string folder in folders.Where(Directory.Exists))
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.json"))
            {
                sources.Add((Path.GetFileNameWithoutExtension(file), () => File.OpenRead(file)));
            }
        }
        return sources;
    }
}
