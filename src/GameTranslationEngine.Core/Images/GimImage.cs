using System.Buffers.Binary;

namespace GameTranslationEngine.Core.Images;

/// <summary>
/// A PSP GIM picture with an indexed image (4 or 8 bits) and its palette.
/// It can be turned into RGBA and its pixels replaced in place: the file
/// keeps its size, format and palette, so whatever points into it stays valid.
/// </summary>
public sealed class GimImage : IImageInfo
{
    private enum Format : ushort { Rgb5650 = 0, Rgba5551 = 1, Rgba4444 = 2, Rgba8888 = 3, Index4 = 4, Index8 = 5 }

    private sealed record Plane(Format Format, bool Swizzled, int Width, int Height, int Bpp, int PitchAlign, int PixelStart, int PixelEnd)
    {
        public int Stride => ((Width * Bpp + 7) / 8 + PitchAlign - 1) / PitchAlign * PitchAlign;

        public int ByteOffset(int x, int y)
        {
            var xByte = x * Bpp >> 3;
            return Swizzled
                ? (xByte & 0xF) + 0x10 * (y & 7) + 0x80 * ((xByte >> 4) + (y >> 3) * (Stride >> 4))
                : xByte + y * Stride;
        }
    }

    private readonly byte[] _data;
    private readonly Plane _image;
    private readonly uint[] _palette;

    private GimImage(byte[] data, Plane image, Plane? palettePlane, uint[] palette)
    {
        _data = data;
        _image = image;
        PaletteFormat = palettePlane is null ? "Sem paleta (cor direta)" : FormatName(palettePlane.Format);
        _palette = palette;
    }

    public int Width => _image.Width;
    public int Height => _image.Height;
    public int ColorCount => _palette.Length;
    public int BitsPerPixel => _image.Bpp;
    public string PixelFormat => FormatName(_image.Format);
    public string PaletteFormat { get; }
    public bool IsSwizzled => _image.Swizzled;
    public string ColorProfile => "Não incorporado (GIM/PSP)";
    public string GridDescription => IsSwizzled ? "Swizzled (16 bytes × 8 linhas)" : "Linear";

    public static GimImage Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 32 || !file[..4].SequenceEqual("MIG."u8))
            throw new InvalidDataException("Arquivo não é GIM.");
        var data = file.ToArray();
        Plane? image = null, palette = null;
        Walk(data, 16, data.Length, ref image, ref palette);
        if (image is null) throw new InvalidDataException("GIM sem bloco de imagem.");
        if (image.Format is not (Format.Rgb5650 or Format.Rgba5551 or Format.Rgba4444 or Format.Rgba8888 or Format.Index4 or Format.Index8))
            throw new NotSupportedException($"Formato de imagem GIM não suportado: {image.Format}.");
        if ((image.Format is Format.Index4 or Format.Index8) && palette is null)
            throw new InvalidDataException("GIM indexado sem paleta.");
        var colors = palette is null ? [] : new uint[palette.Width * palette.Height];
        if (palette is not null)
            for (var i = 0; i < colors.Length; i++)
                colors[i] = ReadColor(data, palette, i % palette.Width, i / palette.Width);
        return new GimImage(data, image, palette, colors);
    }

    private static string FormatName(Format format) => format switch
    {
        Format.Rgb5650 => "RGB 5650",
        Format.Rgba5551 => "RGBA 5551",
        Format.Rgba4444 => "RGBA 4444",
        Format.Rgba8888 => "RGBA 8888",
        Format.Index4 => "Indexado 4 bits",
        Format.Index8 => "Indexado 8 bits",
        _ => $"Formato GIM {(ushort)format}",
    };

    private static void Walk(byte[] data, int start, int end, ref Plane? image, ref Plane? palette)
    {
        for (var position = start; position + 16 <= end;)
        {
            var id = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position));
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 4));
            var dataOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 12));
            if (size < 16 || position + size > end) throw new InvalidDataException("Bloco GIM inválido.");
            var body = position + dataOffset;
            switch (id)
            {
                case 0x02 or 0x03: Walk(data, body, position + size, ref image, ref palette); break;
                case 0x04: image ??= ReadPlane(data, body); break;
                case 0x05: palette ??= ReadPlane(data, body); break;
            }
            position += size;
        }
    }

    private static Plane ReadPlane(byte[] data, int body)
    {
        ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + at));
        int U32(int at) => (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(body + at));
        return new Plane((Format)U16(4), U16(6) == 1, U16(8), U16(10), U16(12), Math.Max((ushort)1, U16(14)),
            body + U32(28), body + U32(32));
    }

    private static uint ReadColor(byte[] data, Plane plane, int x, int y)
    {
        var at = plane.PixelStart + plane.ByteOffset(x, y);
        static byte Scale5(int v) => (byte)((v * 0x21) >> 2);
        switch (plane.Format)
        {
            case Format.Rgba8888:
                return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at));
            case Format.Rgba4444:
            {
                var c = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
                return Pack((byte)((c & 0xF) * 17), (byte)((c >> 4 & 0xF) * 17), (byte)((c >> 8 & 0xF) * 17), (byte)((c >> 12) * 17));
            }
            case Format.Rgba5551:
            {
                var c = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
                return Pack(Scale5(c & 0x1F), Scale5(c >> 5 & 0x1F), Scale5(c >> 10 & 0x1F), (byte)((c >> 15) * 255));
            }
            case Format.Rgb5650:
            {
                var c = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
                return Pack(Scale5(c & 0x1F), (byte)(((c >> 5 & 0x3F) * 0x41) >> 4), Scale5(c >> 11), 255);
            }
            default:
                throw new NotSupportedException($"Paleta GIM no formato {plane.Format} não suportada.");
        }
    }

    private static uint Pack(byte r, byte g, byte b, byte a) => (uint)(r | g << 8 | b << 16 | a << 24);

    private int Index(int x, int y)
    {
        var value = _data[_image.PixelStart + _image.ByteOffset(x, y)];
        return _image.Format == Format.Index8 ? value : (x & 1) == 0 ? value & 0xF : value >> 4;
    }

    public RgbaImage ToRgba()
    {
        var image = new RgbaImage(Width, Height);
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var color = _image.Format is Format.Index4 or Format.Index8
                ? (Index(x, y) is var index && index < _palette.Length ? _palette[index] : 0)
                : ReadColor(_data, _image, x, y);
            BinaryPrimitives.WriteUInt32LittleEndian(image.Pixels.AsSpan(image.Offset(x, y)), color);
        }
        return image;
    }

    /// <summary>
    /// A copy of the GIM file with the pixels of <paramref name="image"/>, each
    /// mapped to the closest color of the original palette.
    /// </summary>
    public byte[] WithPixels(RgbaImage image)
    {
        if (image.Width != Width || image.Height != Height)
            throw new InvalidDataException($"Imagem {image.Width}x{image.Height}; o GIM é {Width}x{Height}.");
        var data = (byte[])_data.Clone();
        var cache = new Dictionary<uint, int>();
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var color = BinaryPrimitives.ReadUInt32LittleEndian(image.Pixels.AsSpan(image.Offset(x, y)));
            var at = _image.PixelStart + _image.ByteOffset(x, y);
            if (_image.Format is Format.Index4 or Format.Index8)
            {
                if (!cache.TryGetValue(color, out var index)) cache[color] = index = Closest(color);
                if (_image.Format == Format.Index8) data[at] = (byte)index;
                else data[at] = (x & 1) == 0 ? (byte)(data[at] & 0xF0 | index) : (byte)(data[at] & 0x0F | index << 4);
            }
            else
            {
                WriteColor(data, _image, x, y, color);
            }
        }
        return data;
    }

    private static void WriteColor(byte[] data, Plane plane, int x, int y, uint color)
    {
        var at = plane.PixelStart + plane.ByteOffset(x, y);
        var r = (byte)color;
        var g = (byte)(color >> 8);
        var b = (byte)(color >> 16);
        var a = (byte)(color >> 24);
        switch (plane.Format)
        {
            case Format.Rgb5650:
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at),
                    (ushort)(r >> 3 | (g >> 2) << 5 | (b >> 3) << 11));
                break;
            case Format.Rgba5551:
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at),
                    (ushort)(r >> 3 | (g >> 3) << 5 | (b >> 3) << 10 | (a >= 128 ? 1 : 0) << 15));
                break;
            case Format.Rgba4444:
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at),
                    (ushort)(r >> 4 | (g >> 4) << 4 | (b >> 4) << 8 | (a >> 4) << 12));
                break;
            case Format.Rgba8888:
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), color);
                break;
        }
    }

    private int Closest(uint color)
    {
        var best = 0;
        var bestDistance = long.MaxValue;
        for (var i = 0; i < _palette.Length; i++)
        {
            var other = _palette[i];
            // Fully transparent pixels match a transparent entry whatever their color.
            long distance = 0;
            for (var shift = 0; shift < 32; shift += 8)
            {
                var a = (int)(color >> shift & 0xFF);
                var b = (int)(other >> shift & 0xFF);
                if (shift < 24 && (color >> 24) == 0 && (other >> 24) == 0) continue;
                distance += (long)(a - b) * (a - b);
            }
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
                if (distance == 0) break;
            }
        }
        return best;
    }
}
