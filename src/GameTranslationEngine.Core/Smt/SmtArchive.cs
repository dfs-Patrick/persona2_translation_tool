using System.Buffers.Binary;
using GameTranslationEngine.Core.IO;
using GameTranslationEngine.Core.Iso;

namespace GameTranslationEngine.Core.Smt;

public sealed record SmtDataEntry(int Index, long Offset, long Length);

/// <summary>
/// The DATA.BIN of the game and its table of contents, an array of u32
/// offsets (multiples of 0x800) stored inside the executable.
/// </summary>
public sealed class SmtDataArchive
{
    public const string ExecutablePath = "SLPS_031.70";
    public const string DataPath = "DATA.BIN";
    public const int TocOffset = 0x8F140;

    private readonly IByteSource _data;

    private SmtDataArchive(IByteSource data, IReadOnlyList<SmtDataEntry> entries)
    {
        _data = data;
        Entries = entries;
    }

    public IReadOnlyList<SmtDataEntry> Entries { get; }

    public static SmtDataArchive Open(IsoIndex iso)
    {
        var data = iso.Open(DataPath);
        var exe = iso.Open(ExecutablePath);
        var offsets = new List<long>();
        var position = TocOffset;
        var buffer = new byte[4];
        while (position + 4 <= exe.Length)
        {
            exe.Read(position, buffer);
            var value = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            if (value % 0x800 != 0 || value > data.Length || (offsets.Count > 0 && value < offsets[^1])) break;
            offsets.Add(value);
            position += 4;
        }
        if (offsets.Count < 100)
            throw new InvalidDataException("A tabela de arquivos do DATA.BIN não foi encontrada no executável.");
        offsets.Add(data.Length);
        var entries = new List<SmtDataEntry>();
        for (var i = 0; i < offsets.Count - 1; i++)
            entries.Add(new SmtDataEntry(i, offsets[i], offsets[i + 1] - offsets[i]));
        return new SmtDataArchive(data, entries);
    }

    public byte[] Read(SmtDataEntry entry) => _data.ReadBytes(entry.Offset, (int)entry.Length);
}

/// <summary>
/// One map script (SC02). The text block starts with a u16 word count and runs
/// to the last non-zero word of the entry. Each message ends with 0x0805
/// followed by a 0x0000 word. A table at header field 3 (u16 count n, then
/// n-2 word offsets into the block) indexes the messages; the last two
/// values overlap the block header.
/// </summary>
public sealed class SmtScript
{
    private SmtScript(
        IReadOnlyList<ushort> words, int textOffset, int tableOffset,
        IReadOnlyList<(int Start, int Length)> segments)
    {
        Words = words;
        TextOffset = textOffset;
        TableOffset = tableOffset;
        Segments = segments;
        Messages = segments.Where(s => HasText(words, s)).ToList();
    }

    private static bool HasText(IReadOnlyList<ushort> words, (int Start, int Length) s)
    {
        for (var i = s.Start; i < s.Start + s.Length; i++)
            if (words[i] is > 0 and < 0x800) return true;
        return false;
    }

    /// <summary>All words of the text block, up to the last non-zero one.</summary>
    public IReadOnlyList<ushort> Words { get; }

    /// <summary>Byte offset of the text block (its u16 length) inside the entry.</summary>
    public int TextOffset { get; }

    /// <summary>Byte offset of the message table inside the entry.</summary>
    public int TableOffset { get; }

    /// <summary>Every segment (body without the 0x0805 end marker); the rest of the words is the terminator.</summary>
    public IReadOnlyList<(int Start, int Length)> Segments { get; }

    /// <summary>Segments with text: start word and length, end marker excluded.</summary>
    public IReadOnlyList<(int Start, int Length)> Messages { get; }

    /// <summary>
    /// Rebuilds the entry replacing the body of some messages (keyed by start word).
    /// The result keeps the original length; throws if the new text does not fit.
    /// </summary>
    public byte[] Rebuild(ReadOnlySpan<byte> entry, IReadOnlyDictionary<int, ushort[]> replacements)
    {
        var output = new List<ushort>(Words.Count + 64);
        var map = new Dictionary<int, int>();
        for (var s = 0; s < Segments.Count; s++)
        {
            var (start, length) = Segments[s];
            var baseOffset = output.Count;
            map[start] = baseOffset;
            ushort[]? body = replacements.TryGetValue(start, out var replaced) ? replaced : null;
            var newBody = body ?? Words.Skip(start).Take(length).ToArray();

            // The table also points inside a message, right after each page-break word (0).
            var oldInterior = Enumerable.Range(start, length).Where(i => Words[i] == 0).Select(i => i + 1).ToList();
            var newInterior = Enumerable.Range(0, newBody.Length).Where(i => newBody[i] == 0).Select(i => i + 1).ToList();
            if (oldInterior.Count != newInterior.Count)
                throw new InvalidOperationException(
                    $"a mensagem msg_{start:d5} precisa manter {oldInterior.Count} marca(s) {{g0}} (quebra de página), tem {newInterior.Count}.");
            for (var k = 0; k < oldInterior.Count; k++) map[oldInterior[k]] = baseOffset + newInterior[k];

            output.AddRange(newBody);
            var next = s + 1 < Segments.Count ? Segments[s + 1].Start : Words.Count;
            for (var i = start + length; i < next; i++) output.Add(Words[i]);
        }
        map[Words.Count] = output.Count;

        var capacity = (entry.Length - TextOffset - 2) / 2;
        if (output.Count > capacity)
            throw new InvalidOperationException($"o texto passou o espaço do script em {output.Count - capacity} palavras.");

        var result = entry.ToArray();
        Array.Clear(result, TextOffset, result.Length - TextOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(
            result.AsSpan(TextOffset), (ushort)Remap(BinaryPrimitives.ReadUInt16LittleEndian(entry[TextOffset..]), map));
        for (var i = 0; i < output.Count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(TextOffset + 2 + i * 2), output[i]);

        var count = BinaryPrimitives.ReadUInt16LittleEndian(entry[TableOffset..]);
        for (var i = 0; i < count - 2; i++)
        {
            var at = TableOffset + 2 + i * 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                result.AsSpan(at), (ushort)Remap(BinaryPrimitives.ReadUInt16LittleEndian(entry[at..]), map));
        }
        return result;
    }

    private static int Remap(int value, Dictionary<int, int> map) => map.TryGetValue(value, out var v) ? v : value;

    public static bool IsScript(ReadOnlySpan<byte> entry) =>
        entry.Length >= 0x28 && entry[0] == 'S' && entry[1] == 'C' && entry[2] == '0' && entry[3] == '2';

    public static SmtScript? Parse(ReadOnlySpan<byte> entry)
    {
        if (!IsScript(entry)) return null;
        var textOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[0x14..]);
        if (textOffset <= 0 || textOffset + 2 > entry.Length) return null;
        var tableOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[0x10..]);
        if (tableOffset <= 0 || tableOffset + 2 > entry.Length) return null;

        var all = (entry.Length - textOffset - 2) / 2;
        var end = 0;
        for (var i = 0; i < all; i++)
            if (BinaryPrimitives.ReadUInt16LittleEndian(entry[(textOffset + 2 + i * 2)..]) != 0) end = i + 1;
        if (end == 0) return null;

        var words = new ushort[end];
        for (var i = 0; i < end; i++)
            words[i] = BinaryPrimitives.ReadUInt16LittleEndian(entry[(textOffset + 2 + i * 2)..]);

        var segments = new List<(int, int)>();
        var start = 0;
        for (var i = 0; i < end; i++)
        {
            if (words[i] != SmtMessageCodec.End) continue;
            segments.Add((start, i - start));
            start = i + 1;
            if (start < end && words[start] == 0) start++;
        }
        if (start < end) segments.Add((start, end - start));
        return new SmtScript(words, textOffset, tableOffset, segments);
    }
}
