using System.Buffers.Binary;
using System.IO.Compression;
using GameTranslationEngine.Core.IO;

namespace GameTranslationEngine.Core.Archives;

/// <summary>Where a file ended up inside a rebuilt container (what the EBOOT tables record).</summary>
public readonly record struct EntryLayout(int Offset, int Size);

/// <summary>A rebuilt container and the position of each of its files.</summary>
public sealed record BuiltArchive(byte[] Data, IReadOnlyList<EntryLayout> Layout);

// C5 · Rebuilding the containers. Every builder reproduces the original byte
// for byte when given the original files (checked by the tests), so a
// rebuilt container differs from the original only where a file changed.

public static partial class Ear
{
    private const int Sector = 2048;

    /// <summary>
    /// Header of (start, end) pairs, then each file at the start of a sector.
    /// "end" includes the padding up to the next sector, like the original and
    /// the table the EBOOT keeps of event.bin.
    /// </summary>
    public static BuiltArchive Build(IReadOnlyList<ReadOnlyMemory<byte>> files, int headerSize)
    {
        if (headerSize < files.Count * 8 + 4) headerSize = Align(files.Count * 8 + 4);
        var layout = new List<EntryLayout>(files.Count);
        var pointer = Align(headerSize);
        foreach (var file in files)
        {
            layout.Add(new EntryLayout(pointer, Align(file.Length)));
            pointer += Align(file.Length);
        }
        var data = new byte[pointer];
        for (var i = 0; i < files.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 8), layout[i].Offset);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 8 + 4), layout[i].Offset + layout[i].Size);
            files[i].Span.CopyTo(data.AsSpan(layout[i].Offset));
        }
        return new BuiltArchive(data, layout);
    }

    private static int Align(int value) => (value + Sector - 1) & ~(Sector - 1);
}

public static partial class Par
{
    /// <summary>Count, sizes, then each file 16-byte aligned; "PSPCHECK\r\n" is appended as the last file.</summary>
    public static BuiltArchive Build(IReadOnlyList<ReadOnlyMemory<byte>> files)
    {
        var all = files.Append(PspCheck).ToList();
        var layout = new List<EntryLayout>(all.Count);
        var pointer = Align16(4 * (all.Count + 1));
        foreach (var file in all)
        {
            layout.Add(new EntryLayout(pointer, file.Length));
            pointer += Align16(file.Length);
        }
        var data = new byte[pointer];
        BinaryPrimitives.WriteInt32LittleEndian(data, all.Count);
        for (var i = 0; i < all.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4 + 4 * i), all[i].Length);
            all[i].Span.CopyTo(data.AsSpan(layout[i].Offset));
        }
        layout.RemoveAt(layout.Count - 1);
        return new BuiltArchive(data, layout);
    }
}

public static partial class Bnp
{
    /// <summary>
    /// Entries one after another, 4-byte aligned; an entry that started a
    /// sector in the original starts a sector again (MMAP*.BNP and F_BE.BNP
    /// keep one entry per sector). The container ends at a sector boundary.
    /// </summary>
    public static BuiltArchive Build(IReadOnlyList<ReadOnlyMemory<byte>> entries, IReadOnlyList<bool> startsSector)
    {
        var layout = new List<EntryLayout>(entries.Count);
        var pointer = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            pointer = startsSector[i] ? AlignSector(pointer) : (pointer + 3) & ~3;
            layout.Add(new EntryLayout(pointer, entries[i].Length));
            pointer += entries[i].Length;
        }
        var data = new byte[AlignSector(pointer)];
        for (var i = 0; i < entries.Count; i++) entries[i].Span.CopyTo(data.AsSpan(layout[i].Offset));
        return new BuiltArchive(data, layout);
    }

    private static int AlignSector(int value) => (value + Sector - 1) & ~(Sector - 1);
}

public static partial class BnpEntryCodec
{
    /// <summary>
    /// Packs data as a BNP entry of type "other" with the given compression.
    /// When compressing would not make the entry smaller, it is stored
    /// uncompressed (the loader also reads uncompressed entries, as in MMAP*.BNP).
    /// </summary>
    public static byte[] Encode(BnpEntryType type, BnpCompression compression, ushort tag, ReadOnlySpan<byte> data)
    {
        if (type != BnpEntryType.Other)
            throw new NotSupportedException($"Gravar entradas BNP do tipo {type} ainda não é suportado.");

        byte[]? packed = compression switch
        {
            BnpCompression.Lzss => Lzss.Compress(data),
            BnpCompression.Rle => Rle.Compress(data),
            _ => null,
        };
        if (packed is null || packed.Length >= data.Length || data.Length == 0)
        {
            var plain = new byte[8 + data.Length];
            WriteHeader(plain, type, BnpCompression.None, tag);
            data.CopyTo(plain.AsSpan(8));
            return plain;
        }
        var entry = new byte[12 + packed.Length];
        WriteHeader(entry, type, compression, tag);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(8), data.Length);
        packed.CopyTo(entry, 12);
        return entry;
    }

    private static void WriteHeader(Span<byte> entry, BnpEntryType type, BnpCompression compression, ushort tag)
    {
        entry[0] = (byte)type;
        entry[1] = (byte)compression;
        BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], tag);
        BinaryPrimitives.WriteInt32LittleEndian(entry[4..], entry.Length);
    }
}

public static partial class Lzss
{
    private const int Window = 256;
    private const int MinMatch = 3;
    private const int MaxMatch = 0x7F + MinMatch;
    private const int MaxLiterals = 0x80;

    /// <summary>Greedy compressor for the format <see cref="Decompress"/> reads.</summary>
    public static byte[] Compress(ReadOnlySpan<byte> input)
    {
        var output = new List<byte>(input.Length / 2 + 16);
        var literalStart = 0;
        var position = 0;

        void FlushLiterals(ReadOnlySpan<byte> source, int end)
        {
            while (literalStart < end)
            {
                var count = Math.Min(MaxLiterals, end - literalStart);
                output.Add((byte)(count - 1));
                for (var i = 0; i < count; i++) output.Add(source[literalStart + i]);
                literalStart += count;
            }
        }

        while (position < input.Length)
        {
            var bestLength = 0;
            var bestDistance = 0;
            var limit = Math.Min(MaxMatch, input.Length - position);
            if (limit >= MinMatch)
            {
                for (var distance = 1; distance <= Window && distance <= position; distance++)
                {
                    var source = position - distance;
                    if (input[source] != input[position]) continue;
                    var length = 1;
                    // Overlapping copies are fine: the decoder copies byte by byte.
                    while (length < limit && input[source + length] == input[position + length]) length++;
                    if (length > bestLength)
                    {
                        bestLength = length;
                        bestDistance = distance;
                        if (length == limit) break;
                    }
                }
            }
            if (bestLength >= MinMatch)
            {
                FlushLiterals(input, position);
                output.Add((byte)(0x80 | (bestLength - MinMatch)));
                output.Add((byte)(bestDistance - 1));
                position += bestLength;
                literalStart = position;
            }
            else
            {
                position++;
            }
        }
        FlushLiterals(input, input.Length);
        return output.ToArray();
    }
}

public static partial class Rle
{
    private const int MinRun = 3;
    private const int MaxRun = 0x7F + MinRun;
    private const int MaxLiterals = 0x80;

    /// <summary>Compressor for the format <see cref="Decompress"/> reads.</summary>
    public static byte[] Compress(ReadOnlySpan<byte> input)
    {
        var output = new List<byte>(input.Length + input.Length / 128 + 2);
        var literalStart = 0;
        var position = 0;

        void FlushLiterals(ReadOnlySpan<byte> source, int end)
        {
            while (literalStart < end)
            {
                var count = Math.Min(MaxLiterals, end - literalStart);
                output.Add((byte)(count - 1));
                for (var i = 0; i < count; i++) output.Add(source[literalStart + i]);
                literalStart += count;
            }
        }

        while (position < input.Length)
        {
            var run = 1;
            while (run < MaxRun && position + run < input.Length && input[position + run] == input[position]) run++;
            if (run >= MinRun)
            {
                FlushLiterals(input, position);
                output.Add((byte)(0x80 | (run - MinRun)));
                output.Add(input[position]);
                position += run;
                literalStart = position;
            }
            else
            {
                position++;
            }
        }
        FlushLiterals(input, input.Length);
        return output.ToArray();
    }
}

/// <summary>
/// gzip as used inside event.bin: the original header (with the file name and
/// date) is kept and only the compressed data changes.
/// </summary>
public static class Gzip
{
    public static byte[] Decompress(ReadOnlyMemory<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Compresses <paramref name="data"/> reusing the header of <paramref name="original"/>.</summary>
    public static byte[] Compress(ReadOnlySpan<byte> data, ReadOnlySpan<byte> original)
    {
        var header = original[..HeaderLength(original)];
        using var output = new MemoryStream(data.Length / 3 + 64);
        output.Write(header);
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(data);
        Span<byte> trailer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(trailer, Crc32.Compute(data));
        BinaryPrimitives.WriteUInt32LittleEndian(trailer[4..], (uint)data.Length);
        output.Write(trailer);
        return output.ToArray();
    }

    /// <summary>Length of a gzip member header (magic, flags and optional fields).</summary>
    public static int HeaderLength(ReadOnlySpan<byte> gzip)
    {
        if (gzip.Length < 18 || gzip[0] != 0x1F || gzip[1] != 0x8B || gzip[2] != 8)
            throw new InvalidDataException("Dados gzip inválidos.");
        var flags = gzip[3];
        var position = 10;
        if ((flags & 0x04) != 0) position += 2 + BinaryPrimitives.ReadUInt16LittleEndian(gzip[position..]);
        if ((flags & 0x08) != 0) position += gzip[position..].IndexOf((byte)0) + 1;
        if ((flags & 0x10) != 0) position += gzip[position..].IndexOf((byte)0) + 1;
        if ((flags & 0x02) != 0) position += 2;
        return position;
    }
}
