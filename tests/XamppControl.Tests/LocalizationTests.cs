using System.Text.RegularExpressions;
using XamppControl.Core;

namespace XamppControl.Tests;

/// <summary>
/// Keeps the language files and the code in step: every key the app asks for exists in English, and no translation
/// has a key or a placeholder English doesn't.
/// </summary>
public sealed partial class LocalizationTests
{
    private static readonly string _root = FindRepositoryRoot();
    private static readonly Dictionary<string, string> _english = ReadLanguage("en");

    [Fact]
    public void English_HasANameAndNoEmptyTexts()
    {
        Assert.Equal("English", _english["$name"]);
        Assert.DoesNotContain(_english, entry => string.IsNullOrWhiteSpace(entry.Value));
    }

    [Fact]
    public void EveryKeyUsedInXaml_ExistsInEnglish()
    {
        var missing = new SortedSet<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(_root, "Views"), "*.axaml"))
        {
            // String keys have a dot (Main.Start); brush and icon keys (HeaderBrush) don't.
            foreach (Match match in ResourceKey().Matches(File.ReadAllText(file)))
            {
                string key = match.Groups["key"].Value;
                if (key.Contains('.') && !_english.ContainsKey(key))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryKeyUsedInCode_ExistsInEnglish()
    {
        var missing = new SortedSet<string>();
        foreach (string file in SourceFiles())
        {
            string code = File.ReadAllText(file);
            foreach (Match match in TextCall().Matches(code))
            {
                if (!_english.ContainsKey(match.Groups["key"].Value))
                {
                    missing.Add($"{Path.GetFileName(file)}: {match.Groups["key"].Value}");
                }
            }

            foreach (Match match in PluralCall().Matches(code))
            {
                string key = match.Groups["key"].Value;
                if (!_english.ContainsKey(key + ".One") || !_english.ContainsKey(key + ".Other"))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}.One/.Other");
                }
            }
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void Translations_OnlyUseEnglishKeysAndTheSamePlaceholders()
    {
        var problems = new SortedSet<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(_root, "Lang"), "*.json"))
        {
            string code = Path.GetFileNameWithoutExtension(file);
            if (code == "en")
            {
                continue;
            }

            Dictionary<string, string> translation = ReadLanguage(code);
            if (!translation.ContainsKey("$name"))
            {
                problems.Add($"{code}: no $name");
            }

            foreach ((string key, string text) in translation)
            {
                if (!_english.TryGetValue(key, out string? english))
                {
                    problems.Add($"{code}: unknown key {key}");
                }
                else if (!Placeholders(english).SetEquals(Placeholders(text)))
                {
                    problems.Add($"{code}: placeholders differ in {key}");
                }
            }
        }
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("nb", "nb")]
    [InlineData("nb-NO", "nb")]
    [InlineData("nn-NO", "nb")]
    [InlineData("no", "nb")]
    [InlineData("de-AT", "de")]
    [InlineData("fr-FR", "en")]
    [InlineData("EN", "en")]
    public void Resolve_PicksTheClosestLanguageFile(string wanted, string expected) =>
        Assert.Equal(expected, Loc.Resolve(wanted, ["en", "nb", "de"]));

    [Fact]
    public void T_ReturnsTheKeyWhenNothingIsLoaded() =>
        Assert.Equal("No.Such.Key", Loc.T("No.Such.Key"));

    private static Dictionary<string, string> ReadLanguage(string code) =>
        Loc.Read(() => File.OpenRead(Path.Combine(_root, "Lang", code + ".json")));

    private static HashSet<string> Placeholders(string text) =>
        Placeholder().Matches(text.Replace("{{", "", StringComparison.Ordinal).Replace("}}", "", StringComparison.Ordinal))
            .Select(match => match.Groups["index"].Value)
            .ToHashSet();

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(_root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "XamppControl.slnx")))
            {
                return folder.FullName;
            }
        }
        throw new InvalidOperationException("XamppControl.slnx not found above the test folder.");
    }

    [GeneratedRegex(@"\{DynamicResource\s+(?<key>[A-Za-z0-9_.]+)\}")]
    private static partial Regex ResourceKey();

    [GeneratedRegex(@"Loc\.T\(\s*""(?<key>[^""]+)""")]
    private static partial Regex TextCall();

    [GeneratedRegex(@"Loc\.Plural\(\s*""(?<key>[^""]+)""")]
    private static partial Regex PluralCall();

    [GeneratedRegex(@"\{(?<index>\d+)(?:[:,][^}]*)?\}")]
    private static partial Regex Placeholder();
}
