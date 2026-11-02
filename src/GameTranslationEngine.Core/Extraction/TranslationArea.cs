using System.Text.Json;
using GameTranslationEngine.Core.Scripts;

namespace GameTranslationEngine.Core.Extraction;

/// <summary>Outcome of generating the translation area.</summary>
public sealed record TranslationAreaReport(
    int Created,
    int Kept,
    int MessagesTotal,
    IReadOnlyList<string> Differences);

/// <summary>
/// A8 · Writes one TBF per text source. A source that already has a TBF in
/// the project (generated before or imported from v2, matched by its
/// original location) is never overwritten.
/// </summary>
public static class TranslationArea
{
    public static TranslationAreaReport Generate(string messagesDirectory, IReadOnlyList<ExtractedText> texts)
    {
        Directory.CreateDirectory(messagesDirectory);
        var existing = IndexExisting(messagesDirectory);
        int created = 0, kept = 0, total = 0;
        var differences = new List<string>();

        foreach (var text in texts)
        {
            var document = MessageFile.ToTbf(text.OriginalDirectory, text.OriginalFilename, text.MessageFileText);
            total += document.Entries.Count;
            var key = Key(text.OriginalDirectory, text.OriginalFilename);

            if (existing.TryGetValue(key, out var current))
            {
                kept++;
                var keys = current.Keys;
                var fresh = document.Entries.Select(e => e.Key).ToList();
                if (!keys.SequenceEqual(fresh))
                    differences.Add($"{Path.GetRelativePath(messagesDirectory, current.Path)}: mensagens diferentes da ISO ({keys.Count} no arquivo, {fresh.Count} extraídas).");
                continue;
            }

            var target = Path.Combine(messagesDirectory, text.Source.TbfPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target))
            {
                differences.Add($"{text.Source.TbfPath}: já existe um arquivo com esse nome para outra origem; mantido.");
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            document.Save(target);
            created++;
        }
        return new TranslationAreaReport(created, kept, total, differences);
    }

    private sealed record ExistingTbf(string Path, List<string> Keys);

    private static Dictionary<string, ExistingTbf> IndexExisting(string directory)
    {
        var index = new Dictionary<string, ExistingTbf>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory, "*.tbf", SearchOption.AllDirectories))
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var json = JsonDocument.Parse(stream);
                var header = json.RootElement.GetProperty("header");
                var keys = json.RootElement.GetProperty("translation").EnumerateArray()
                    .Select(t => t.GetProperty("info").GetProperty("msg_key").GetString() ?? "").ToList();
                index.TryAdd(
                    Key(header.GetProperty("originalDirectory").GetString() ?? "", header.GetProperty("originalFilename").GetString() ?? ""),
                    new ExistingTbf(path, keys));
            }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                // Unreadable files are left alone; the editor reports them when opened.
            }
        }
        return index;
    }

    /// <summary>How a TBF is matched to its text source: the original location in its header.</summary>
    public static string Key(string directory, string file) => $"{directory.Replace('\\', '/').TrimEnd('/')}/{file}";
}
