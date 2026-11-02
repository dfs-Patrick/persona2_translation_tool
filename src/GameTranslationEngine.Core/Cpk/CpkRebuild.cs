using System.Text;
using GameTranslationEngine.Core.IO;

namespace GameTranslationEngine.Core.Cpk;

/// <summary>
/// C6 · A new CPK made from the original with some entries replaced. The
/// original is copied region by region: an entry that still fits in its old
/// space stays where it was; one that grows pushes the following entries
/// forward by whole sectors. Only numbers change in the tables: the TOC rows
/// of the moved or replaced entries and the sizes/offsets in the header.
/// Replaced entries are stored uncompressed.
/// </summary>
public sealed class CpkRebuild
{
    private readonly IByteSource _source;
    private readonly byte[] _head;
    private readonly List<Region> _regions;

    private sealed record Region(long Start, long Length, byte[]? Replacement, long NewStart, long NewLength);

    private CpkRebuild(IByteSource source, byte[] head, List<Region> regions, long length)
    {
        _source = source;
        _head = head;
        _regions = regions;
        Length = length;
    }

    /// <summary>Size of the new CPK in bytes.</summary>
    public long Length { get; }

    /// <summary>New position of each entry, by id (absolute, from the start of the CPK).</summary>
    public IReadOnlyDictionary<int, long> Offsets { get; private init; } = new Dictionary<int, long>();

    public static CpkRebuild Plan(CpkIndex cpk, IReadOnlyDictionary<int, byte[]> replacements)
    {
        var source = cpk.Source;
        var (header, headerUtf) = ReadChunk(source, 0, "CPK ");
        var tocOffset = (long)header.GetNumber(0, "TocOffset");
        var contentOffset = header.HasColumn("ContentOffset") ? (long)header.GetNumber(0, "ContentOffset") : 0;
        var align = header.HasColumn("Align") ? (long)Math.Max(1UL, header.GetNumber(0, "Align")) : 2048;
        var etocOffset = header.HasColumn("EtocOffset") ? (long)header.GetNumber(0, "EtocOffset") : 0;
        var baseOffset = contentOffset > 0 ? Math.Min(tocOffset, contentOffset) : tocOffset;
        var (toc, tocUtf) = ReadChunk(source, tocOffset, "TOC ");

        foreach (var id in replacements.Keys)
            if (cpk.FindById(id) is null) throw new InvalidDataException($"Entrada {id} não existe no CPK.");

        var ordered = cpk.Entries.Select((entry, row) => (Entry: entry, Row: row)).OrderBy(e => e.Entry.Offset).ToList();
        var first = ordered[0].Entry.Offset;
        var end = etocOffset > ordered[^1].Entry.Offset ? etocOffset : source.Length;

        var regions = new List<Region>();
        var offsets = new Dictionary<int, long>();
        long shift = 0, storedDelta = 0, extractDelta = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            var (entry, row) = ordered[i];
            var start = entry.Offset;
            var stop = i + 1 < ordered.Count ? ordered[i + 1].Entry.Offset : end;
            var length = stop - start;
            var newStart = start + shift;
            if (replacements.TryGetValue(entry.Id, out var data))
            {
                var newLength = Math.Max(length, AlignUp(data.Length, align));
                regions.Add(new Region(start, length, data, newStart, newLength));
                shift += newLength - length;
                storedDelta += data.Length - entry.StoredSize;
                extractDelta += data.Length - entry.ExtractedSize;
                toc.SetNumber(tocUtf, row, "FileSize", (ulong)data.Length);
                toc.SetNumber(tocUtf, row, "ExtractSize", (ulong)data.Length);
            }
            else
            {
                regions.Add(new Region(start, length, null, newStart, length));
            }
            if (newStart != start) toc.SetNumber(tocUtf, row, "FileOffset", (ulong)(newStart - baseOffset));
            offsets[entry.Id] = newStart;
        }
        if (end < source.Length) regions.Add(new Region(end, source.Length - end, null, end + shift, source.Length - end));

        void Adjust(string column, long delta)
        {
            if (delta != 0 && header.HasColumn(column) && header.Get(0, column) is not null)
                header.SetNumber(headerUtf, 0, column, (ulong)((long)header.GetNumber(0, column) + delta));
        }
        Adjust("ContentSize", shift);
        if (etocOffset > 0) Adjust("EtocOffset", shift);
        Adjust("EnabledPackedSize", storedDelta);
        Adjust("EnabledDataSize", extractDelta);

        // Everything before the first entry (header, TOC, ITOC, GTOC), with the patched tables.
        var head = source.ReadBytes(0, checked((int)first));
        headerUtf.CopyTo(head, 16);
        tocUtf.CopyTo(head, (int)tocOffset + 16);
        return new CpkRebuild(source, head, regions, source.Length + shift) { Offsets = offsets };
    }

    public void WriteTo(Stream output, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        output.Write(_head);
        long written = _head.Length;
        var buffer = new byte[4 * 1024 * 1024];
        foreach (var region in _regions)
        {
            cancellation.ThrowIfCancellationRequested();
            if (region.Replacement is { } data)
            {
                output.Write(data);
                WriteZeros(output, region.NewLength - data.Length, buffer);
            }
            else
            {
                for (long done = 0; done < region.Length;)
                {
                    var count = (int)Math.Min(buffer.Length, region.Length - done);
                    _source.Read(region.Start + done, buffer.AsSpan(0, count));
                    output.Write(buffer, 0, count);
                    done += count;
                }
            }
            written += region.NewLength;
            progress?.Report((double)written / Length);
        }
    }

    private static void WriteZeros(Stream output, long count, byte[] buffer)
    {
        Array.Clear(buffer);
        while (count > 0)
        {
            var chunk = (int)Math.Min(buffer.Length, count);
            output.Write(buffer, 0, chunk);
            count -= chunk;
        }
    }

    private static long AlignUp(long value, long align) => (value + align - 1) / align * align;

    private static (UtfTable Table, byte[] Utf) ReadChunk(IByteSource source, long offset, string magic)
    {
        var head = source.ReadBytes(offset, 16);
        if (Encoding.ASCII.GetString(head, 0, 4) != magic)
            throw new InvalidDataException($"Bloco CPK \"{magic}\" não encontrado em 0x{offset:X}.");
        var utf = source.ReadBytes(offset + 16, checked((int)BitConverter.ToInt64(head, 8)));
        return (UtfTable.Read(utf), utf);
    }
}
