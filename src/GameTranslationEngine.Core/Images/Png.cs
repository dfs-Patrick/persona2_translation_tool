using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using GameTranslationEngine.Core.IO;

namespace GameTranslationEngine.Core.Images;

/// <summary>An RGBA image, 8 bits per channel, rows top to bottom.</summary>
public sealed class RgbaImage(int width, int height, byte[]? pixels = null)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels ?? new byte[width * height * 4];

    public int Offset(int x, int y) => (y * Width + x) * 4;
    public byte Alpha(int x, int y) => Pixels[Offset(x, y) + 3];

    public RgbaImage Clone() => new(Width, Height, (byte[])Pixels.Clone());
}

/// <summary>
/// Minimal PNG support: writes RGBA images and reads the non-interlaced
/// 8-bit formats (gray, RGB, palette, gray+alpha, RGBA) that image editors save.
/// </summary>
public static class Png
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Encode(RgbaImage image)
    {
        using var output = new MemoryStream();
        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8;   // bit depth
        header[9] = 6;   // RGBA
        WriteChunk(output, "IHDR", header);

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var stride = image.Width * 4;
            for (var y = 0; y < image.Height; y++)
            {
                zlib.WriteByte(0); // no filter
                zlib.Write(image.Pixels, y * stride, stride);
            }
        }
        WriteChunk(output, "IDAT", raw.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    public static RgbaImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data[..8].SequenceEqual(Signature)) throw new InvalidDataException("Arquivo não é PNG.");
        int width = 0, height = 0, depth = 0, type = 0, interlace = 0;
        byte[]? palette = null, transparency = null;
        using var compressed = new MemoryStream();
        var position = 8;
        while (position + 8 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data[position..]);
            var name = Encoding.ASCII.GetString(data.Slice(position + 4, 4));
            var body = data.Slice(position + 8, length);
            position += 12 + length;
            switch (name)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(body);
                    height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    depth = body[8];
                    type = body[9];
                    interlace = body[12];
                    break;
                case "PLTE": palette = body.ToArray(); break;
                case "tRNS": transparency = body.ToArray(); break;
                case "IDAT": compressed.Write(body); break;
                case "IEND": position = data.Length; break;
            }
        }
        if (depth != 8 || interlace != 0)
            throw new InvalidDataException($"PNG não suportado (profundidade {depth}, entrelaçado {interlace}); salve como RGBA de 8 bits.");
        var channels = type switch
        {
            0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4,
            _ => throw new InvalidDataException($"Tipo de cor PNG {type} não suportado."),
        };
        if (type == 3 && palette is null) throw new InvalidDataException("PNG com paleta sem o bloco PLTE.");

        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var stride = width * channels;
        var rows = new byte[height * (stride + 1)];
        zlib.ReadExactly(rows);

        var image = new RgbaImage(width, height);
        var previous = new byte[stride];
        var current = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var filter = rows[y * (stride + 1)];
            rows.AsSpan(y * (stride + 1) + 1, stride).CopyTo(current);
            Unfilter(filter, current, previous, channels);
            for (var x = 0; x < width; x++)
            {
                var o = image.Offset(x, y);
                var p = image.Pixels;
                switch (type)
                {
                    case 0: p[o] = p[o + 1] = p[o + 2] = current[x]; p[o + 3] = 255; break;
                    case 2: current.AsSpan(x * 3, 3).CopyTo(p.AsSpan(o)); p[o + 3] = 255; break;
                    case 3:
                        var index = current[x];
                        palette.AsSpan(index * 3, 3).CopyTo(p.AsSpan(o));
                        p[o + 3] = transparency is not null && index < transparency.Length ? transparency[index] : (byte)255;
                        break;
                    case 4: p[o] = p[o + 1] = p[o + 2] = current[x * 2]; p[o + 3] = current[x * 2 + 1]; break;
                    default: current.AsSpan(x * 4, 4).CopyTo(p.AsSpan(o)); break;
                }
            }
            (previous, current) = (current, previous);
        }
        return image;
    }

    private static void Unfilter(byte filter, byte[] row, byte[] previous, int bpp)
    {
        for (var i = 0; i < row.Length; i++)
        {
            int left = i >= bpp ? row[i - bpp] : 0, up = previous[i], upLeft = i >= bpp ? previous[i - bpp] : 0;
            row[i] = filter switch
            {
                0 => row[i],
                1 => (byte)(row[i] + left),
                2 => (byte)(row[i] + up),
                3 => (byte)(row[i] + (left + up) / 2),
                4 => (byte)(row[i] + Paeth(left, up, upLeft)),
                _ => throw new InvalidDataException($"Filtro PNG {filter} inválido."),
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, string name, ReadOnlySpan<byte> body)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, body.Length);
        output.Write(number);
        var typed = new byte[4 + body.Length];
        Encoding.ASCII.GetBytes(name, typed);
        body.CopyTo(typed.AsSpan(4));
        output.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32.Compute(typed));
        output.Write(number);
    }
}
