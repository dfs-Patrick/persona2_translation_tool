using System.Buffers.Binary;

namespace GameTranslationEngine.Core.Smt;

/// <summary>Where a bank of strings lives inside a DATA.BIN entry.</summary>
public sealed record SmtBankLocation(int Entry, int Start, int End, string Label);

/// <summary>
/// A bank of strings (menus, items, names, demon lines). Layout: u32 n, then
/// n-2 u16 word offsets of strings 1..n-2 (string 0 starts at 0) and the words
/// of the strings. The last string runs to the last non-zero word. Banks are
/// rewritten in place, inside the space the entry already has.
/// </summary>
public sealed class SmtStringBank
{
    public static readonly IReadOnlyList<SmtBankLocation> Locations =
    [
        new(9, 0x10, 0xa24, "Nomes e vocabulário"),
        new(9, 0xa24, -1, "Itens e locais"),
        new(25, 0, -1, "Menus"),
        new(26, 0, -1, "Itens e locais"),
        new(27, 0, -1, "Menus"),
        new(29, 0, -1, "Diálogo de igreja"),
        new(30, 0, -1, "Menus"),
        new(31, 0, -1, "Menus"),
        new(32, 0, -1, "Menus"),
        new(33, 0, -1, "Menus"),
        new(38, 0, -1, "Falas de demônios"),
        new(39, 0, -1, "Menus"),
        new(40, 0, -1, "Menus"),
        new(41, 0, -1, "Menus"),
        new(42, 0, -1, "Menus"),
        new(43, 0, -1, "Menus"),
    ];

    private SmtStringBank(IReadOnlyList<ushort> words, IReadOnlyList<int> starts, int dataOffset)
    {
        Words = words;
        Starts = starts;
        DataOffset = dataOffset;
        var strings = new List<(int Start, int Length)>();
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1] : words.Count;
            var length = end - starts[i];
            while (length > 0 && words[starts[i] + length - 1] is 0 or SmtMessageCodec.End) length--;
            var hasText = false;
            for (var w = starts[i]; w < starts[i] + length; w++) if (words[w] is > 0 and < 0x800) hasText = true;
            if (hasText) strings.Add((starts[i], length));
        }
        Strings = strings;
    }

    public IReadOnlyList<ushort> Words { get; }

    /// <summary>Start word of every string, string 0 included.</summary>
    public IReadOnlyList<int> Starts { get; }

    /// <summary>Byte offset (inside the entry) of the first string word.</summary>
    public int DataOffset { get; }

    /// <summary>Strings with text: start word and length (terminators excluded).</summary>
    public IReadOnlyList<(int Start, int Length)> Strings { get; }

    public static SmtStringBank? Parse(ReadOnlySpan<byte> entry, SmtBankLocation location)
    {
        var end = location.End < 0 ? entry.Length : Math.Min(location.End, entry.Length);
        var o = location.Start;
        if (o + 8 > end) return null;
        var k = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[o..]) - 2;
        if (k < 3 || k > 4000 || o + 4 + 2 * k > end) return null;
        var starts = new List<int> { 0 };
        for (var i = 0; i < k; i++)
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(entry[(o + 4 + 2 * i)..]);
            if (value <= starts[^1]) return null;
            starts.Add(value);
        }
        var dataOffset = o + 4 + 2 * k;
        var count = (end - dataOffset) / 2;
        var last = 0;
        var words = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt16LittleEndian(entry[(dataOffset + 2 * i)..]);
            if (words[i] != 0) last = i + 1;
        }
        if (starts[^1] >= last) return null;
        return new SmtStringBank(words[..last], starts, dataOffset);
    }

    /// <summary>
    /// The bank alone (u32 count, offsets, words, a zero word), with some strings
    /// replaced; used when the banks of a container entry are laid out again.
    /// </summary>
    public byte[] Serialize(IReadOnlyDictionary<int, ushort[]> replacements)
    {
        var (output, newStarts) = Layout(replacements);
        var table = 4 + 2 * (newStarts.Count - 1);
        var result = new byte[(table + 2 * output.Count + 2 + 3) & ~3];
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)(Starts.Count + 1));
        for (var i = 1; i < newStarts.Count; i++)
        {
            if (newStarts[i] > ushort.MaxValue) throw new InvalidOperationException("o banco passou o limite de 64K palavras.");
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4 + 2 * (i - 1)), (ushort)newStarts[i]);
        }
        for (var i = 0; i < output.Count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(table + 2 * i), output[i]);
        return result;
    }

    private (List<ushort> Output, List<int> Starts) Layout(IReadOnlyDictionary<int, ushort[]> replacements)
    {
        var bodies = Strings.ToDictionary(s => s.Start);
        var output = new List<ushort>(Words.Count + 64);
        var newStarts = new List<int>();
        for (var i = 0; i < Starts.Count; i++)
        {
            newStarts.Add(output.Count);
            var start = Starts[i];
            var end = i + 1 < Starts.Count ? Starts[i + 1] : Words.Count;
            if (bodies.TryGetValue(start, out var body) && replacements.TryGetValue(start, out var text))
            {
                output.AddRange(text);
                for (var w = start + body.Length; w < end; w++) output.Add(Words[w]);
            }
            else for (var w = start; w < end; w++) output.Add(Words[w]);
        }
        return (output, newStarts);
    }

    /// <summary>
    /// Rebuilds the entry replacing string bodies (keyed by start word). The size
    /// is kept; throws if the new text does not fit in the bank's space.
    /// </summary>
    public byte[] Rebuild(ReadOnlySpan<byte> entry, SmtBankLocation location, IReadOnlyDictionary<int, ushort[]> replacements)
    {
        var (output, newStarts) = Layout(replacements);

        var bankEnd = location.End < 0 ? entry.Length : Math.Min(location.End, entry.Length);
        var capacity = (bankEnd - DataOffset) / 2;
        if (output.Count > capacity)
            throw new InvalidOperationException($"o texto passou o espaço do banco em {output.Count - capacity} palavras.");
        foreach (var start in newStarts.Skip(1))
            if (start > ushort.MaxValue) throw new InvalidOperationException("o banco passou o limite de 64K palavras.");

        var result = entry.ToArray();
        var tableOffset = DataOffset - 2 * (Starts.Count - 1);
        for (var i = 1; i < newStarts.Count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(tableOffset + 2 * (i - 1)), (ushort)newStarts[i]);
        result.AsSpan(DataOffset, bankEnd - DataOffset).Clear();
        for (var i = 0; i < output.Count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(DataOffset + 2 * i), output[i]);
        return result;
    }
}
