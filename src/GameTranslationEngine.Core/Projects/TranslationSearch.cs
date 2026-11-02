using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using GameTranslationEngine.Core.Tbf;

namespace GameTranslationEngine.Core.Projects;

/// <summary>Which side of the messages a search looks at.</summary>
public enum SearchScope
{
    Both,
    Original,
    Translation,
}

/// <summary>A message where the text was found; <see cref="Start"/> is the first occurrence in <see cref="Text"/>.</summary>
public sealed record SearchHit(
    TbfFileInfo File,
    int Index,
    string Key,
    bool InTranslation,
    string Text,
    int Start,
    int Length,
    int Occurrences);

public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, int TotalHits, int Files, TimeSpan Elapsed)
{
    public bool Truncated => Hits.Count < TotalHits;
}

/// <summary>
/// B5 · Finds a text in every TBF of the project, in the original, the translation or both.
/// A space in the query also matches a line break, because the game's text is wrapped by hand.
/// </summary>
public static class TranslationSearch
{
    /// <summary>The query as a pattern: literal text, where any run of whitespace matches any other.</summary>
    public static Regex? CreatePattern(string query, bool matchCase)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;
        var body = string.Join(@"\s+", words.Select(Regex.Escape));
        // Spaces at the ends of the query are part of what is searched.
        if (char.IsWhiteSpace(query[0])) body = @"\s+" + body;
        if (char.IsWhiteSpace(query[^1])) body += @"\s+";
        return new Regex(body, RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase));
    }

    public static SearchResult Search(IReadOnlyList<TbfFileInfo> files, string query, SearchScope scope, bool matchCase,
        int limit = 2000, CancellationToken cancellation = default)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var pattern = CreatePattern(query, matchCase);
        if (pattern is null) return new SearchResult([], 0, 0, watch.Elapsed);

        var perFile = new ConcurrentDictionary<int, List<SearchHit>>();
        Parallel.For(0, files.Count, new ParallelOptions { CancellationToken = cancellation }, i =>
        {
            TbfDocument document;
            try
            {
                document = TbfDocument.Load(files[i].FullPath);
            }
            catch (Exception error) when (error is IOException or InvalidDataException)
            {
                return; // Broken files show their error when opened.
            }
            var hits = new List<SearchHit>();
            foreach (var entry in document.Entries)
            {
                if (scope != SearchScope.Translation) Find(files[i], entry, entry.Original, false);
                if (scope != SearchScope.Original) Find(files[i], entry, entry.Translation, true);
            }
            if (hits.Count > 0) perFile[i] = hits;

            void Find(TbfFileInfo file, TbfEntry entry, string text, bool inTranslation)
            {
                // Some files keep "\r\n"; the snippet shows plain line breaks.
                text = text.Replace("\r\n", "\n");
                var matches = pattern.Matches(text);
                if (matches.Count == 0) return;
                hits.Add(new SearchHit(file, entry.Index, entry.Key, inTranslation, text, matches[0].Index, matches[0].Length, matches.Count));
            }
        });

        var ordered = perFile.OrderBy(p => p.Key).SelectMany(p => p.Value).ToList();
        return new SearchResult(ordered.Take(limit).ToList(), ordered.Count, perFile.Count, watch.Elapsed);
    }
}
