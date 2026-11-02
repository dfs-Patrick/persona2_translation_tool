using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using GameTranslationEngine.Core.GameData;
using WeCantSpell.Hunspell;

namespace GameTranslationEngine.Core.Editing;

/// <summary>
/// B3 · Portuguese spell checking: Hunspell with the LibreOffice pt_BR
/// dictionary (VERO), plus the words of the project (translation/dictionary.txt)
/// and of the glossary.
/// </summary>
public sealed partial class SpellChecker
{
    private static readonly Lazy<Task<WordList>> Portuguese = new(() => Task.Run(() =>
    {
        using var dic = GameResources.Open("dictionaries/pt-br/pt_BR.dic");
        using var aff = GameResources.Open("dictionaries/pt-br/pt_BR.aff");
        return WordList.CreateFromStreams(dic, aff);
    }));

    private readonly WordList _dictionary;
    private readonly string? _projectFile;
    private readonly HashSet<string> _projectWords = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _cache = new();
    private HashSet<string> _glossaryWords = new(StringComparer.OrdinalIgnoreCase);

    private SpellChecker(WordList dictionary, string? projectFile)
    {
        _dictionary = dictionary;
        _projectFile = projectFile;
        if (projectFile is not null && File.Exists(projectFile))
            foreach (var line in File.ReadAllLines(projectFile))
                if (line.Trim() is { Length: > 0 } word && !word.StartsWith('#'))
                    _projectWords.Add(word);
    }

    /// <summary>Loads the dictionary (once per run, ~1 s) and the project's own words.</summary>
    public static async Task<SpellChecker> LoadPortugueseAsync(string? projectDictionary) =>
        new(await Portuguese.Value.ConfigureAwait(false), projectDictionary);

    /// <summary>Words (letters, with inner apostrophes or hyphens) of a text.</summary>
    [GeneratedRegex(@"\p{L}+(?:['’-]\p{L}+)*")]
    public static partial Regex WordPattern();

    /// <summary>The words of the glossary's translations count as correct.</summary>
    public void SetGlossary(Glossary glossary)
    {
        _glossaryWords = glossary.Terms
            .SelectMany(t => WordPattern().Matches(t.Translation).Select(m => m.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _cache.Clear();
    }

    public bool Check(string word) => _cache.GetOrAdd(word, w =>
    {
        if (_projectWords.Contains(w) || _glossaryWords.Contains(w)) return true;
        var normalized = w.Replace('’', '\'');
        if (_dictionary.Check(normalized)) return true;
        // "Meio-dia" may be missing as a whole while each part is a word.
        var parts = normalized.Split('-');
        return parts.Length > 1 && parts.All(p => _projectWords.Contains(p) || _dictionary.Check(p));
    });

    public IReadOnlyList<string> Suggest(string word) =>
        _dictionary.Suggest(word.Replace('’', '\'')).Take(8).ToList();

    /// <summary>"Add to dictionary": the word becomes correct for the whole project.</summary>
    public void AddWord(string word)
    {
        if (!_projectWords.Add(word)) return;
        _cache.Clear();
        if (_projectFile is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_projectFile)!);
        File.AppendAllText(_projectFile, word + "\n");
    }
}
