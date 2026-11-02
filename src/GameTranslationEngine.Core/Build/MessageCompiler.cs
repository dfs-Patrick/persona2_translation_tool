using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.Scripts;
using GameTranslationEngine.Core.Tbf;
using GameTranslationEngine.Core.Text;

namespace GameTranslationEngine.Core.Build;

public enum IssueLevel { Error, Warning, Info }

/// <summary>Something the build found: where (file and message) and what.</summary>
public sealed record BuildIssue(IssueLevel Level, string File, string? Message, string Text)
{
    public override string ToString() => Message is null ? $"{File}: {Text}" : $"{File} · {Message}: {Text}";
}

/// <summary>The bytes of every message of one text source, translated or original.</summary>
public sealed record CompiledText(ExtractedText Source, IReadOnlyList<byte[]> Messages, int Translated, int Changed)
{
    public bool HasChanges => Changed > 0;
}

/// <summary>
/// C1/C2 · Turns the translations of a TBF into the game's encoding.
/// The TBF is read the way v2 compiled it: rendered back to the .msg text
/// and split at [end], so entries that v2 cut at a line ending with ":" are
/// joined again. A message whose text is unchanged keeps its original bytes,
/// so a project with no translation rebuilds the game exactly as it was.
/// </summary>
public sealed class MessageCompiler(MessageContext context)
{
    private const string ItemCommand = "item_with_type";

    public MessageContext Context { get; } = context;

    public CompiledText Compile(ExtractedText source, TbfDocument? document, ICollection<BuildIssue> issues)
    {
        var file = source.Source.TbfPath;
        var originals = source.OriginalBytes;
        if (document is null || document.TranslatedCount == 0)
            return new CompiledText(source, originals, 0, 0);

        Dictionary<string, string> translated, untouched;
        try
        {
            translated = Index(MessageFile.ParseMessages(MessageFile.Render(document)), file, issues);
            untouched = Index(MessageFile.ParseMessages(MessageFile.Render(document, useTranslation: false)), file, null);
        }
        catch (InvalidDataException error)
        {
            issues.Add(new BuildIssue(IssueLevel.Error, file, null, error.Message));
            return new CompiledText(source, originals, 0, 0);
        }

        var known = source.Messages.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in translated.Keys.Where(k => !known.Contains(k)))
            issues.Add(new BuildIssue(IssueLevel.Warning, file, name, "mensagem não existe no script original; ignorada."));

        var output = new List<byte[]>(source.Messages.Count);
        int count = 0, changed = 0;
        for (var i = 0; i < source.Messages.Count; i++)
        {
            var (name, original) = source.Messages[i];
            if (!translated.TryGetValue(name, out var text) || untouched.GetValueOrDefault(name) == text)
            {
                output.Add(originals[i]);
                continue;
            }
            count++;
            try
            {
                var message = RestoreItems(Message.Parse(text, file), original, out var restored);
                if (restored > 0)
                    issues.Add(new BuildIssue(IssueLevel.Info, file, name,
                        $"{restored} [{ItemCommand}(item_0)] vindo(s) da v2 trocado(s) pelo item da mensagem original."));
                var bytes = message.EncodeBytes(Context.At(file, 0));
                output.Add(bytes);
                if (!bytes.AsSpan().SequenceEqual(originals[i])) changed++;
            }
            catch (InvalidDataException error)
            {
                issues.Add(new BuildIssue(IssueLevel.Error, file, name, error.Message));
                output.Add(originals[i]);
            }
        }
        return new CompiledText(source, output, count, changed);
    }

    private static Dictionary<string, string> Index(List<(string Name, string Text)> messages, string file, ICollection<BuildIssue>? issues)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, text) in messages)
        {
            if (!index.TryAdd(name, text))
                issues?.Add(new BuildIssue(IssueLevel.Warning, file, name, "mensagem repetida no TBF; vale a primeira."));
        }
        return index;
    }

    /// <summary>
    /// v2 wrote every received item as "item_0". When the translation has
    /// item_0 where the original names another item, the original item wins
    /// (matched by order of appearance).
    /// </summary>
    private static Message RestoreItems(Message translated, Message original, out int restored)
    {
        restored = 0;
        var originals = original.Parts.OfType<CommandPart>().Where(c => c.Name == ItemCommand).ToList();
        if (originals.Count == 0) return translated;

        var parts = translated.Parts.ToList();
        var seen = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] is not CommandPart { Name: ItemCommand } command) continue;
            var expected = seen < originals.Count ? originals[seen] : null;
            seen++;
            if (expected is null || command.Arguments is not ["item_0"] || expected.Arguments is ["item_0"]) continue;
            parts[i] = expected;
            restored++;
        }
        return restored == 0 ? translated : new Message(parts);
    }
}
