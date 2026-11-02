using System.Collections.Concurrent;
using GameTranslationEngine.Core.Archives;
using GameTranslationEngine.Core.Cpk;
using GameTranslationEngine.Core.GameData;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Scripts;
using GameTranslationEngine.Core.Text;

namespace GameTranslationEngine.Core.Extraction;

/// <summary>The messages of one text source, ready to become a TBF.</summary>
public sealed record ExtractedText(
    TextSource Source,
    string OriginalDirectory,
    string OriginalFilename,
    IReadOnlyList<(string Name, Message Message)> Messages,
    IReadOnlyList<byte[]> OriginalBytes)
{
    public string MessageFileText => MessageFile.Write(Messages);
}

/// <summary>
/// A4–A6 · Locates the text files inside the ISO and decodes their messages,
/// entirely in memory: only the CPK entries that hold text are read.
/// </summary>
public sealed class TextExtractor
{
    private readonly IsoIndex _iso;
    private readonly TextSourceCatalog _catalog;
    private readonly MessageContext _context;

    public TextExtractor(IsoIndex iso, TextSourceCatalog? catalog = null, MessageContext? context = null)
    {
        _iso = iso;
        _catalog = catalog ?? TextSourceCatalog.LoadInnocentSin();
        _context = context ?? new MessageContext(EventEncoding.LoadInnocentSinEnglish(), ConstantTables.LoadInnocentSin());
    }

    public IReadOnlyList<TextSource> Sources => _catalog.Sources;

    /// <summary>Extracts every text source; one CPK entry per task, in parallel.</summary>
    public IReadOnlyList<ExtractedText> ExtractAll(IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        var cpk = CpkIndex.Read(_iso.Open(_catalog.CpkPath));
        var groups = _catalog.Sources.GroupBy(s => s.Entry.Name).ToList();
        var results = new ConcurrentBag<(int Order, ExtractedText Text)>();
        var order = _catalog.Sources.Select((s, i) => (s, i)).ToDictionary(p => p.s, p => p.i);
        var done = 0;

        Parallel.ForEach(groups, new ParallelOptions { CancellationToken = cancellation }, group =>
        {
            var first = group.First().Entry;
            var entry = (first.CpkId is { } id ? cpk.FindById(id) : null) ?? cpk.FindByName(first.Name)
                ?? throw new InvalidDataException($"Entrada {first.Name} não encontrada no CPK.");
            var root = new Node(first, cpk.ReadEntry(entry));
            foreach (var source in group)
            {
                results.Add((order[source], Extract(source, root)));
                progress?.Report((double)Interlocked.Increment(ref done) / _catalog.Sources.Count);
            }
        });
        return results.OrderBy(r => r.Order).Select(r => r.Text).ToList();
    }

    private ExtractedText Extract(TextSource source, Node root)
    {
        var directory = source.OriginalDirectory(_catalog.CpkPath);
        var file = $"{directory}/{source.OriginalFilename}";

        if (source.Type == "pdemo") return ExtractPersonaDemo(source, root.Data, directory, file);

        var node = root;
        foreach (var step in source.Chain.Skip(1)) node = node.Child(step);
        var span = node.Data.Span;
        var script = EventScript.Read(span, _context.At(file, 0));
        var section = node.Data[script.Layout.MessageSection..];
        return new ExtractedText(source, directory, source.OriginalFilename,
            script.Messages.Select(m => (m.Name, m.Message)).ToList(),
            script.Messages.Select(m => section.Slice(m.Offset, m.Length).ToArray()).ToList());
    }

    /// <summary>P_DEMO.BNP: one message per BNP entry, named after the persona index.</summary>
    private ExtractedText ExtractPersonaDemo(TextSource source, ReadOnlyMemory<byte> data, string directory, string file)
    {
        var messages = new List<(string, Message)>();
        var originals = new List<byte[]>();
        var entries = Bnp.Split(data);
        for (var i = 0; i < entries.Count; i++)
        {
            var decoded = BnpEntryCodec.Decode(entries[i].Data.Span);
            var message = Message.Decode(decoded.Data, _context.At(file, entries[i].Offset), out var length);
            messages.Add(($"msg_{_context.Constants.Name("persona", i)}", message));
            originals.Add(decoded.Data[..length]);
        }
        return new ExtractedText(source, directory, source.OriginalFilename, messages, originals);
    }

    private static byte[] Gunzip(ReadOnlyMemory<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>A container opened in memory; children are split once and reused.</summary>
    private sealed class Node(SourceStep step, ReadOnlyMemory<byte> data)
    {
        private List<ArchiveEntry>? _entries;
        private readonly Dictionary<int, Node> _children = new();

        public ReadOnlyMemory<byte> Data { get; } = data;

        public Node Child(SourceStep childStep)
        {
            var index = childStep.Index ?? throw new InvalidDataException($"{childStep.Name} sem índice no catálogo.");
            lock (_children)
            {
                if (_children.TryGetValue(index, out var cached)) return cached;
                _entries ??= step.Type switch
                {
                    "ear" => Ear.Split(Data),
                    "par" => Par.Split(Data),
                    "bnp" => Bnp.Split(Data),
                    _ => throw new InvalidDataException($"{step.Name} ({step.Type}) não é um container conhecido."),
                };
                if (index >= _entries.Count)
                    throw new InvalidDataException($"{step.Name} tem {_entries.Count} arquivos; o catálogo pede o {index}.");
                var raw = _entries[index].Data;
                var content = step.Type == "bnp" ? BnpEntryCodec.Decode(raw.Span).Data
                    : step.Compressed ? Gunzip(raw)
                    : raw;
                var child = new Node(childStep, content);
                _children[index] = child;
                return child;
            }
        }
    }
}
