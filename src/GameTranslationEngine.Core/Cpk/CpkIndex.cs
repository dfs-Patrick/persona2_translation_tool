using System.Text;
using GameTranslationEngine.Core.IO;

namespace GameTranslationEngine.Core.Cpk;

/// <summary>An entry of a CPK.</summary>
public sealed record CpkEntry(
    int Id,
    string Name,
    string? Directory,
    long Offset,
    long StoredSize,
    long ExtractedSize)
{
    public bool IsCompressed => StoredSize != ExtractedSize;
    public string FullName => string.IsNullOrEmpty(Directory) ? Name : $"{Directory}/{Name}";
}

/// <summary>
/// A3 · CPK index: the table of contents of a CPK, read from the "CPK "
/// header and the "TOC " table, without extracting the entries.
/// </summary>
public sealed class CpkIndex
{
    private const int ChunkHeaderSize = 0x10;

    private CpkIndex(IByteSource source, IReadOnlyList<CpkEntry> entries)
    {
        Source = source;
        Entries = entries;
    }

    public IByteSource Source { get; }
    public IReadOnlyList<CpkEntry> Entries { get; }

    public CpkEntry? FindById(int id) => Entries.FirstOrDefault(e => e.Id == id);
    public CpkEntry? FindByName(string name) =>
        Entries.FirstOrDefault(e => string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase));

    public static CpkIndex Read(IByteSource source)
    {
        var header = ReadChunk(source, 0, "CPK ");
        var tocOffset = (long)header.GetNumber(0, "TocOffset");
        var contentOffset = header.HasColumn("ContentOffset") ? (long)header.GetNumber(0, "ContentOffset") : 0;
        if (tocOffset == 0) throw new InvalidDataException("CPK sem tabela TOC.");

        var toc = ReadChunk(source, tocOffset, "TOC ");
        // Entry offsets are relative to whichever comes first: the TOC or the content.
        var baseOffset = contentOffset > 0 ? Math.Min(tocOffset, contentOffset) : tocOffset;

        var entries = new List<CpkEntry>(toc.RowCount);
        for (var row = 0; row < toc.RowCount; row++)
        {
            entries.Add(new CpkEntry(
                Id: toc.HasColumn("ID") ? (int)toc.GetNumber(row, "ID") : row,
                Name: toc.GetString(row, "FileName") ?? $"{row}",
                Directory: toc.HasColumn("DirName") ? toc.GetString(row, "DirName") : null,
                Offset: baseOffset + (long)toc.GetNumber(row, "FileOffset"),
                StoredSize: (long)toc.GetNumber(row, "FileSize"),
                ExtractedSize: (long)toc.GetNumber(row, "ExtractSize")));
        }
        return new CpkIndex(source, entries);
    }

    /// <summary>Reads an entry, decompressing it when needed.</summary>
    public byte[] ReadEntry(CpkEntry entry)
    {
        var stored = Source.ReadBytes(entry.Offset, checked((int)entry.StoredSize));
        if (!entry.IsCompressed) return stored;
        var data = CriLayla.Decompress(stored);
        if (data.Length != entry.ExtractedSize)
            throw new InvalidDataException(
                $"{entry.FullName}: descomprimido com {data.Length} bytes, esperado {entry.ExtractedSize}.");
        return data;
    }

    /// <summary>Opens an uncompressed entry without copying it.</summary>
    public IByteSource Open(CpkEntry entry) => entry.IsCompressed
        ? new MemoryByteSource(ReadEntry(entry))
        : Source.Slice(entry.Offset, entry.StoredSize);

    private static UtfTable ReadChunk(IByteSource source, long offset, string magic)
    {
        var head = source.ReadBytes(offset, ChunkHeaderSize);
        var found = Encoding.ASCII.GetString(head, 0, 4);
        if (found != magic)
            throw new InvalidDataException($"Bloco CPK inesperado em 0x{offset:X}: esperado \"{magic}\", encontrado \"{found}\".");
        var size = BitConverter.ToInt64(head, 8);
        var table = source.ReadBytes(offset + ChunkHeaderSize, checked((int)size));
        return UtfTable.Read(table);
    }
}
