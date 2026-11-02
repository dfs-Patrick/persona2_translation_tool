using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameTranslationEngine.Core.Editing;

/// <summary>A reference term: how something in the original is translated.</summary>
public sealed class GlossaryTerm
{
    public string Original { get; set; } = "";
    public string Translation { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>When false (default), "exalted one" also matches "Exalted One".</summary>
    public bool MatchCase { get; set; }
}

/// <summary>A term found in a text.</summary>
public sealed record GlossaryMatch(int Start, int Length, GlossaryTerm Term);

/// <summary>
/// B2 · The project's glossary (translation/glossary.json). Terms are found as
/// whole words; the longest term wins where two overlap, and a space in a term
/// also matches a line break, because the game's text is wrapped by hand.
/// </summary>
public sealed class Glossary
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private Matcher? _original, _translation;

    public List<GlossaryTerm> Terms { get; init; } = [];

    public static Glossary Load(string path)
    {
        if (!File.Exists(path)) return new Glossary();
        var file = JsonSerializer.Deserialize<GlossaryFile>(File.ReadAllText(path), JsonOptions)
                   ?? throw new InvalidDataException($"Glossário vazio ou inválido: {path}");
        return new Glossary { Terms = file.Terms ?? [] };
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var terms = Terms.Where(t => t.Original.Trim().Length > 0 || t.Translation.Trim().Length > 0).ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(new GlossaryFile { Terms = terms }, JsonOptions) + "\n");
    }

    /// <summary>Call after changing <see cref="Terms"/> so the next search sees the change.</summary>
    public void Invalidate() => _original = _translation = null;

    /// <summary>Terms whose original (English) text appears in <paramref name="text"/>.</summary>
    public IReadOnlyList<GlossaryMatch> FindOriginal(string text) =>
        (_original ??= new Matcher(Terms, t => t.Original)).Find(text);

    /// <summary>Terms whose translation (Portuguese) appears in <paramref name="text"/>.</summary>
    public IReadOnlyList<GlossaryMatch> FindTranslation(string text) =>
        (_translation ??= new Matcher(Terms, t => t.Translation)).Find(text);

    /// <summary>Terms of the original whose translation is missing from the translated text.</summary>
    public IReadOnlyList<GlossaryTerm> Missing(string original, string translation)
    {
        if (translation.Trim().Length == 0) return [];
        var present = FindTranslation(translation).Select(m => m.Term).ToHashSet();
        return FindOriginal(original).Select(m => m.Term)
            .Where(t => t.Translation.Trim().Length > 0 && !present.Contains(t))
            .Distinct()
            .ToList();
    }

    private sealed class GlossaryFile
    {
        public List<GlossaryTerm>? Terms { get; set; }
    }

    private sealed class Matcher
    {
        private readonly List<(Regex Pattern, GlossaryTerm Term, int Length)> _patterns = [];

        public Matcher(IEnumerable<GlossaryTerm> terms, Func<GlossaryTerm, string> side)
        {
            foreach (var term in terms)
            {
                var words = side(term).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) continue;
                var body = string.Join(@"\s+", words.Select(Regex.Escape));
                var options = RegexOptions.CultureInvariant | (term.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
                _patterns.Add((new Regex($@"(?<![\p{{L}}\p{{N}}]){body}(?![\p{{L}}\p{{N}}])", options), term, body.Length));
            }
        }

        public IReadOnlyList<GlossaryMatch> Find(string text)
        {
            var found = new List<GlossaryMatch>();
            foreach (var (pattern, term, _) in _patterns)
                foreach (Match match in pattern.Matches(text))
                    found.Add(new GlossaryMatch(match.Index, match.Length, term));
            // Longest first, then keep what does not overlap something already kept.
            var kept = new List<GlossaryMatch>();
            foreach (var match in found.OrderByDescending(m => m.Length).ThenBy(m => m.Start))
                if (kept.All(k => match.Start >= k.Start + k.Length || k.Start >= match.Start + match.Length))
                    kept.Add(match);
            kept.Sort((a, b) => a.Start.CompareTo(b.Start));
            return kept;
        }
    }
}
