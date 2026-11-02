using System.Buffers.Binary;

namespace GameTranslationEngine.Core.Images;

/// <summary>A PlayStation TIM texture (4, 8, 16 or 24 bits), read-only.</summary>
public sealed class TimImage : IImageInfo
{
    private readonly byte[] _pixels;
    private readonly ushort[] _palette;
    private readonly int _mode;

    private TimImage(int mode, int width, int height, ushort[] palette, int paletteRows, byte[] pixels)
    {
        _mode = mode;
        Width = width;
        Height = height;
        _palette = palette;
        PaletteRows = paletteRows;
        _pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public int PaletteRows { get; }
    public int ColorCount => _palette.Length;
    public int BitsPerPixel => _mode switch { 0 => 4, 1 => 8, 2 => 16, _ => 24 };
    public string PixelFormat => _mode < 2 ? $"Indexado {BitsPerPixel} bits" : $"Cor direta {BitsPerPixel} bits";
    public string PaletteFormat => _palette.Length == 0
        ? "Sem paleta (cor direta)"
        : $"CLUT 5:5:5 + bit STP · {PaletteRows} linha(s) de {_palette.Length / PaletteRows} cores";
    public bool IsSwizzled => false;
    public string ColorProfile => "Não incorporado (TIM/PS1)";
    public string GridDescription => "Linear (VRAM de 16 bits)";

    public static bool LooksLikeTim(ReadOnlySpan<byte> data) =>
        data.Length >= 20 && data[0] == 0x10 && data[1] == 0 && data[2] == 0 && data[3] == 0
        && (data[4] & 3) <= 3 && (data[4] & 0xF0) == 0 && data[5] == 0 && data[6] == 0 && data[7] == 0
        && (data[4] & 4) == 0;

    public static TimImage Read(ReadOnlySpan<byte> data)
    {
        if (!LooksLikeTim(data)) throw new InvalidDataException("Arquivo não é TIM.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var mode = (int)(flags & 3);
        var position = 8;
        var palette = Array.Empty<ushort>();
        var rows = 0;
        if ((flags & 8) != 0)
        {
            var block = Block(data, position, out var w, out var h, out var body);
            palette = new ushort[w * h];
            for (var i = 0; i < palette.Length; i++) palette[i] = BinaryPrimitives.ReadUInt16LittleEndian(body[(i * 2)..]);
            rows = Math.Max(1, h);
            position += block;
        }
        else if (mode < 2) throw new InvalidDataException("TIM indexado sem CLUT.");

        var size = Block(data, position, out var words, out var height, out var pixels);
        _ = size;
        var width = mode switch { 0 => words * 4, 1 => words * 2, 2 => words, _ => words * 2 / 3 };
        if (width == 0 || height == 0) throw new InvalidDataException("TIM sem pixels.");
        return new TimImage(mode, width, height, palette, rows, pixels.ToArray());
    }

    /// <summary>Reads a block header; returns the block size and the data that follows it.</summary>
    private static int Block(ReadOnlySpan<byte> data, int position, out int width, out int height, out ReadOnlySpan<byte> body)
    {
        if (position + 12 > data.Length) throw new InvalidDataException("TIM truncado.");
        var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
        width = BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 8)..]);
        height = BinaryPrimitives.ReadUInt16LittleEndian(data[(position + 10)..]);
        var length = width * height * 2;
        if (size < 12 + length || position + 12 + length > data.Length) throw new InvalidDataException("Bloco de TIM inválido.");
        body = data.Slice(position + 12, length);
        return size;
    }

    /// <summary>Total length of the TIM in the data (blocks included), for trimming padding.</summary>
    public static int Length(ReadOnlySpan<byte> data)
    {
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var position = 8;
        if ((flags & 8) != 0) position += Block(data, position, out _, out _, out _);
        return position + Block(data, position, out _, out _, out _);
    }

    /// <summary>
    /// Copy of the original TIM with the pixels replaced. Size, mode and palette
    /// stay the same: indexed images use the nearest colour of the original palette.
    /// </summary>
    public byte[] WithPixels(ReadOnlySpan<byte> original, RgbaImage image)
    {
        if (image.Width != Width || image.Height != Height)
            throw new InvalidDataException(
                $"A imagem nova tem {image.Width}×{image.Height}; o original tem {Width}×{Height}.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(original[4..]);
        var position = 8;
        if ((flags & 8) != 0) position += Block(original, position, out _, out _, out _);
        var body = position + 12;

        var result = original.ToArray();
        var colors = _mode < 2 ? 1 << BitsPerPixel : 0;
        var cache = new Dictionary<uint, int>();
        int Nearest(byte r, byte g, byte b, byte a)
        {
            var key = (uint)(r | g << 8 | b << 16 | a << 24);
            if (cache.TryGetValue(key, out var known)) return known;
            var best = 0;
            var distance = int.MaxValue;
            for (var i = 0; i < Math.Min(colors, _palette.Length); i++)
            {
                var c = _palette[i];
                var transparent = c == 0;
                int d;
                if (a < 128) d = transparent ? 0 : 1 << 24;
                else if (transparent) d = 1 << 24;
                else
                {
                    var dr = (c & 31) * 255 / 31 - r;
                    var dg = ((c >> 5) & 31) * 255 / 31 - g;
                    var db = ((c >> 10) & 31) * 255 / 31 - b;
                    d = dr * dr + dg * dg + db * db;
                }
                if (d < distance) { distance = d; best = i; }
            }
            cache[key] = best;
            return best;
        }

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var o = image.Offset(x, y);
                byte r = image.Pixels[o], g = image.Pixels[o + 1], b = image.Pixels[o + 2], a = image.Pixels[o + 3];
                switch (_mode)
                {
                    case 0:
                    {
                        var at = body + y * (Width / 2) + x / 2;
                        var index = Nearest(r, g, b, a);
                        result[at] = (x & 1) == 0 ? (byte)((result[at] & 0xF0) | index) : (byte)((result[at] & 0x0F) | index << 4);
                        break;
                    }
                    case 1:
                        result[body + y * Width + x] = (byte)Nearest(r, g, b, a);
                        break;
                    case 2:
                    {
                        var color = a < 128 ? 0 : (r * 31 + 127) / 255 | ((g * 31 + 127) / 255) << 5 | ((b * 31 + 127) / 255) << 10;
                        if (a >= 128 && color == 0) color = 0x8000;
                        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(body + (y * Width + x) * 2), (ushort)color);
                        break;
                    }
                    default:
                    {
                        var at = body + (y * Width + x) * 3;
                        result[at] = r; result[at + 1] = g; result[at + 2] = b;
                        break;
                    }
                }
            }
        }
        return result;
    }

    public RgbaImage ToRgba()
    {
        var image = new RgbaImage(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var o = image.Offset(x, y);
                switch (_mode)
                {
                    case 0:
                    {
                        var b = _pixels[y * (Width / 2) + x / 2];
                        SetColor(image.Pixels, o, _palette[(x & 1) == 0 ? b & 15 : b >> 4]);
                        break;
                    }
                    case 1:
                        SetColor(image.Pixels, o, _palette[_pixels[y * Width + x]]);
                        break;
                    case 2:
                        SetColor(image.Pixels, o, BinaryPrimitives.ReadUInt16LittleEndian(_pixels.AsSpan((y * Width + x) * 2)));
                        break;
                    default:
                    {
                        var s = (y * Width + x) * 3;
                        image.Pixels[o] = _pixels[s];
                        image.Pixels[o + 1] = _pixels[s + 1];
                        image.Pixels[o + 2] = _pixels[s + 2];
                        image.Pixels[o + 3] = 255;
                        break;
                    }
                }
            }
        }
        return image;
    }

    /// <summary>5:5:5 colour; 0x0000 is the transparent colour of the PlayStation.</summary>
    private static void SetColor(byte[] pixels, int offset, ushort color)
    {
        pixels[offset] = (byte)((color & 31) * 255 / 31);
        pixels[offset + 1] = (byte)(((color >> 5) & 31) * 255 / 31);
        pixels[offset + 2] = (byte)(((color >> 10) & 31) * 255 / 31);
        pixels[offset + 3] = (byte)(color == 0 ? 0 : 255);
    }
}
