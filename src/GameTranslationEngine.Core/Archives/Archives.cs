using System.Buffers.Binary;
using System.Text;

namespace GameTranslationEngine.Core.Archives;

/// <summary>A file inside a container: its bytes and where they start.</summary>
public readonly record struct ArchiveEntry(int Offset, ReadOnlyMemory<byte> Data);

/// <summary>
/// EAR container: a header of (start, end) pairs, one per file, ending at
/// the first file. Used by event.bin and the sound banks.
/// </summary>
public static partial class Ear
{
    public static List<ArchiveEntry> Split(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        var entries = new List<ArchiveEntry>();
        var first = BinaryPrimitives.ReadUInt32LittleEndian(span);
        var pointer = 0;
        var current = first;
        while (current != 0 && pointer < first)
        {
            var end = BinaryPrimitives.ReadUInt32LittleEndian(span[(pointer + 4)..]);
            if (end < current) break;
            if (end == current) throw new InvalidDataException("EAR com arquivo de tamanho zero.");
            if (end > data.Length) throw new InvalidDataException("EAR inválido: arquivo além do fim.");
            entries.Add(new ArchiveEntry((int)current, data[(int)current..(int)end]));
            pointer += 8;
            current = BinaryPrimitives.ReadUInt32LittleEndian(span[pointer..]);
        }
        return entries;
    }
}

/// <summary>
/// PAR container: file count, then the size of each file; files aligned to
/// 16 bytes. The last file is always the "PSPCHECK\r\n" marker.
/// </summary>
public static partial class Par
{
    private static readonly byte[] PspCheck = Encoding.ASCII.GetBytes("PSPCHECK\r\n");

    public static List<ArchiveEntry> Split(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(span);
        if (count <= 0 || 4 * (count + 1) > data.Length) throw new InvalidDataException("PAR inválido: contagem de arquivos.");
        var pointer = Align16(4 * (count + 1));
        var entries = new List<ArchiveEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(4 + 4 * i)..]);
            if (size == 0) throw new InvalidDataException("PAR com arquivo de tamanho zero.");
            if (pointer + size > data.Length) throw new InvalidDataException("PAR inválido: arquivo além do fim.");
            entries.Add(new ArchiveEntry(pointer, data.Slice(pointer, size)));
            pointer += Align16(size);
        }
        if (!entries[^1].Data.Span.SequenceEqual(PspCheck)) throw new InvalidDataException("PAR sem o marcador PSPCHECK.");
        entries.RemoveAt(entries.Count - 1);
        return entries;
    }

    private static int Align16(int value) => (value + 15) & ~15;
}

/// <summary>
/// BNP container: entries packed one after another (4-byte aligned), each
/// starting with its own header; zero padding skips to the next sector.
/// </summary>
public static partial class Bnp
{
    private const int Sector = 2048;

    public static List<ArchiveEntry> Split(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        var entries = new List<ArchiveEntry>();
        var pointer = 0;
        while (pointer < data.Length)
        {
            if (span[pointer] == 0)
            {
                if ((pointer & (Sector - 1)) == 0) throw new InvalidDataException("BNP inválido: setor vazio.");
                pointer = (pointer + Sector - 1) & ~(Sector - 1);
                continue;
            }
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(pointer + 4)..]);
            if (size < 8 || size > 1024 * 1024 * 1024) throw new InvalidDataException("BNP inválido: cabeçalho de entrada.");
            if (pointer + size > data.Length) throw new InvalidDataException("BNP inválido: entrada além do fim.");
            entries.Add(new ArchiveEntry(pointer, data.Slice(pointer, size)));
            pointer += (size + 3) & ~3;
        }
        return entries;
    }
}

/// <summary>Kind of a BNP entry (first byte of its header).</summary>
public enum BnpEntryType : byte { Other = 1, Image = 2, Audio = 3 }

public enum BnpCompression : byte { None = 0, Rle = 1, Lzss = 2 }

/// <summary>A decoded BNP entry.</summary>
public sealed record BnpEntry(BnpEntryType Type, BnpCompression Compression, ushort Tag, byte[] Data);

/// <summary>
/// Decoding of one BNP entry: 8-byte header (type, compression, tag, size),
/// plus the uncompressed size for RLE/LZSS. Images keep x/y/w/h and are
/// returned with a 20-byte CLT/PXL header, like v2.
/// </summary>
public static partial class BnpEntryCodec
{
    public static BnpEntry Decode(ReadOnlySpan<byte> input)
    {
        var type = (BnpEntryType)input[0];
        var compression = (BnpCompression)input[1];
        var tag = BinaryPrimitives.ReadUInt16LittleEndian(input[2..]);
        var storedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(input[4..]);

        int start, size;
        switch (compression)
        {
            case BnpCompression.None:
                start = 8;
                size = storedSize - 8;
                break;
            case BnpCompression.Rle or BnpCompression.Lzss:
                start = 12;
                size = (int)BinaryPrimitives.ReadUInt32LittleEndian(input[8..]);
                break;
            default:
                throw new InvalidDataException($"Compressão BNP desconhecida: {(byte)compression}.");
        }

        (ushort X, ushort Y, ushort W, ushort H)? image = null;
        switch (type)
        {
            case BnpEntryType.Other:
                break;
            case BnpEntryType.Image:
                var x = BinaryPrimitives.ReadUInt16LittleEndian(input[8..]);
                var y = BinaryPrimitives.ReadUInt16LittleEndian(input[10..]);
                var w = BinaryPrimitives.ReadUInt16LittleEndian(input[12..]);
                var h = BinaryPrimitives.ReadUInt16LittleEndian(input[14..]);
                // Some images (TM_UNIT) hold whole CLT/PXL files instead of the x/y/w/h header.
                if (x + w < 2048 && y + h < 512 && w != 0 && h != 0)
                {
                    size = w * h * 2;
                    start = 16;
                    image = (x, y, w, h);
                }
                break;
            case BnpEntryType.Audio:
                if (input[1] != 1) throw new InvalidDataException("Entrada de áudio BNP com marcador inesperado.");
                return new BnpEntry(type, compression, tag, input[8..].ToArray());
            default:
                throw new InvalidDataException($"Tipo de entrada BNP desconhecido: {(byte)type}.");
        }

        var payload = input[start..];
        var data = compression switch
        {
            BnpCompression.None => payload.Length == size
                ? payload.ToArray()
                : throw new InvalidDataException("Entrada BNP sem compressão com tamanho inconsistente."),
            BnpCompression.Rle => Rle.Decompress(payload, size),
            _ => Lzss.Decompress(payload, size),
        };

        if (image is { } header)
        {
            var withHeader = new byte[data.Length + 20];
            data.CopyTo(withHeader, 20);
            var isClt = header.W == 256 && header.H == 1;
            BinaryPrimitives.WriteUInt32LittleEndian(withHeader, isClt ? 0x12u : 0x11u);
            BinaryPrimitives.WriteUInt32LittleEndian(withHeader.AsSpan(4), isClt ? 2u : 0u);
            // v2 writes this field big-endian (DataView default); kept for identical output.
            BinaryPrimitives.WriteUInt32BigEndian(withHeader.AsSpan(8), (uint)(12 + data.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(withHeader.AsSpan(12), header.X);
            BinaryPrimitives.WriteUInt16LittleEndian(withHeader.AsSpan(14), header.Y);
            BinaryPrimitives.WriteUInt16LittleEndian(withHeader.AsSpan(16), header.W);
            BinaryPrimitives.WriteUInt16LittleEndian(withHeader.AsSpan(18), header.H);
            data = withHeader;
        }
        return new BnpEntry(type, compression, tag, data);
    }
}

/// <summary>Atlus LZSS: control byte with bit 7 set = back-reference, else literal run.</summary>
public static partial class Lzss
{
    public static byte[] Decompress(ReadOnlySpan<byte> input, int size)
    {
        if (input.Length == 0 || size == 0) throw new InvalidDataException("Dados LZSS inválidos.");
        var output = new byte[size];
        int read = 0, written = 0;
        while (written < size)
        {
            var control = input[read++];
            if ((control & 0x80) != 0)
            {
                var count = (control & 0x7F) + 3;
                var source = written - (input[read++] + 1);
                if (source < 0) throw new InvalidDataException("LZSS: referência antes do início.");
                while (count-- > 0) output[written++] = output[source++];
            }
            else
            {
                var count = control + 1;
                input.Slice(read, count).CopyTo(output.AsSpan(written));
                read += count;
                written += count;
            }
        }
        if (written != size || read != input.Length)
            throw new InvalidDataException($"LZSS: consumidos {read} de {input.Length} bytes, gerados {written} de {size}.");
        return output;
    }
}

/// <summary>Atlus RLE: control byte with bit 7 set = repeated byte, else literal run.</summary>
public static partial class Rle
{
    public static byte[] Decompress(ReadOnlySpan<byte> input, int size)
    {
        if (input.Length == 0 || size == 0 || input.Length > size) throw new InvalidDataException("Dados RLE inválidos.");
        var output = new byte[size];
        int read = 0, written = 0;
        while (written < size && read < input.Length)
        {
            var control = input[read++];
            if ((control & 0x80) != 0)
            {
                var count = (control & 0x7F) + 3;
                output.AsSpan(written, count).Fill(input[read++]);
                written += count;
            }
            else
            {
                var count = control + 1;
                input.Slice(read, count).CopyTo(output.AsSpan(written));
                read += count;
                written += count;
            }
        }
        if (written != size || read != input.Length)
            throw new InvalidDataException($"RLE: consumidos {read} de {input.Length} bytes, gerados {written} de {size}.");
        return output;
    }
}
