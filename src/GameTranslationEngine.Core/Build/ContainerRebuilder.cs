using GameTranslationEngine.Core.Archives;
using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.Scripts;
using GameTranslationEngine.Core.Text;

namespace GameTranslationEngine.Core.Build;

/// <summary>A CPK entry rebuilt with new text, and where each of its files ended up.</summary>
public sealed record RebuiltEntry(SourceStep Entry, byte[] Data, IReadOnlyList<EntryLayout>? Layout);

/// <summary>
/// C3/C5 · Puts the compiled messages back into their scripts and the scripts
/// back into their containers, from the inside out. Only the path of a
/// changed script is rebuilt; every other file is copied as it was.
/// </summary>
public sealed class ContainerRebuilder(MessageContext context)
{
    /// <summary>Rebuilds one CPK entry with every changed text source it holds.</summary>
    public RebuiltEntry Rebuild(SourceStep entry, ReadOnlyMemory<byte> content, IReadOnlyList<CompiledText> changes)
    {
        if (changes.Any(c => c.Source.Source.Entry != entry))
            throw new ArgumentException("Todas as alterações precisam ser da mesma entrada do CPK.", nameof(changes));
        var data = Apply(0, content, changes, out var layout);
        return new RebuiltEntry(entry, data, layout);
    }

    private byte[] Apply(int depth, ReadOnlyMemory<byte> content, IReadOnlyList<CompiledText> changes, out IReadOnlyList<EntryLayout>? layout)
    {
        var chain = changes[0].Source.Source.Chain;
        var step = chain[depth];
        layout = null;

        if (depth == chain.Count - 1)
        {
            if (changes.Count != 1) throw new InvalidDataException($"{step.Name} aparece mais de uma vez no catálogo.");
            return Leaf(step, content, changes[0]);
        }

        var entries = step.Type switch
        {
            "ear" => Ear.Split(content),
            "par" => Par.Split(content),
            "bnp" => Bnp.Split(content),
            _ => throw new InvalidDataException($"{step.Name} ({step.Type}) não é um container conhecido."),
        };
        var raws = entries.Select(e => e.Data).ToList();
        foreach (var group in changes.GroupBy(c => c.Source.Source.Chain[depth + 1].Index
                                                   ?? throw new InvalidDataException($"{step.Name}: passo sem índice no catálogo.")))
        {
            var index = group.Key;
            if (index >= entries.Count)
                throw new InvalidDataException($"{step.Name} tem {entries.Count} arquivos; o catálogo pede o {index}.");
            var raw = entries[index].Data;
            switch (step.Type)
            {
                case "bnp":
                    var entry = BnpEntryCodec.Decode(raw.Span);
                    var inner = Apply(depth + 1, entry.Data, group.ToList(), out _);
                    raws[index] = BnpEntryCodec.Encode(entry.Type, entry.Compression, entry.Tag, inner);
                    break;
                case "ear" when step.Compressed:
                    var unpacked = Apply(depth + 1, Gzip.Decompress(raw), group.ToList(), out _);
                    raws[index] = Gzip.Compress(unpacked, raw.Span);
                    break;
                default:
                    raws[index] = Apply(depth + 1, raw, group.ToList(), out _);
                    break;
            }
        }

        var built = step.Type switch
        {
            "ear" => Ear.Build(raws, entries.Count > 0 ? entries[0].Offset : 0),
            "par" => Par.Build(raws),
            _ => Bnp.Build(raws, entries.Select(e => e.Offset % 2048 == 0).ToList()),
        };
        layout = built.Layout;
        return built.Data;
    }

    private byte[] Leaf(SourceStep step, ReadOnlyMemory<byte> content, CompiledText change) => change.Source.Source.Type switch
    {
        "efb" => EventScript.Read(content.Span, context.At(change.Source.Source.TbfPath, 0))
            .WithMessages(content.Span, change.Messages),
        "pdemo" => PersonaDemo(content, change),
        _ => throw new InvalidDataException($"Tipo de texto desconhecido: {change.Source.Source.Type} ({step.Name})."),
    };

    /// <summary>P_DEMO.BNP: one message per entry; each entry keeps its compression and tag.</summary>
    private static byte[] PersonaDemo(ReadOnlyMemory<byte> content, CompiledText change)
    {
        var entries = Bnp.Split(content);
        if (entries.Count != change.Messages.Count)
            throw new InvalidDataException($"P_DEMO.BNP tem {entries.Count} entradas e o texto tem {change.Messages.Count} mensagens.");
        var raws = new List<ReadOnlyMemory<byte>>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var original = change.Source.OriginalBytes[i];
            if (change.Messages[i].AsSpan().SequenceEqual(original))
            {
                raws.Add(entries[i].Data);
                continue;
            }
            var entry = BnpEntryCodec.Decode(entries[i].Data.Span);
            // Whatever followed the terminator in the entry is kept.
            var data = change.Messages[i].Concat(entry.Data.Skip(original.Length)).ToArray();
            raws.Add(BnpEntryCodec.Encode(entry.Type, entry.Compression, entry.Tag, data));
        }
        return Bnp.Build(raws, entries.Select(e => e.Offset % 2048 == 0).ToList()).Data;
    }
}
