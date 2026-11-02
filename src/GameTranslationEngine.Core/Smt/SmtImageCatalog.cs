using System.Buffers.Binary;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.Iso;

namespace GameTranslationEngine.Core.Smt;

/// <summary>Finds the TIM textures of DATA.BIN: raw, RLE-compressed and inside containers.</summary>
public static class SmtImageCatalog
{
    public static IReadOnlyList<GameImageSource> Discover(IsoIndex iso)
    {
        var archive = SmtDataArchive.Open(iso);
        var images = new List<GameImageSource>();
        foreach (var entry in archive.Entries)
        {
            var data = archive.Read(entry);
            Scan(data, $"DATA.BIN/{entry.Index:d4}", "Sem compressão", entry.Index, images, 0);
        }
        return images;
    }

    private static void Scan(byte[] data, string path, string compression, int index,
        List<GameImageSource> images, int depth)
    {
        if (depth > 3 || data.Length < 16) return;

        if (TimImage.LooksLikeTim(data))
        {
            try
            {
                var tim = TimImage.Read(data);
                images.Add(new GameImageSource(
                    Group: $"Imagens {tim.Width}×{tim.Height}",
                    Name: Path.GetFileName(path.Replace('/', '\\')) + ".tim",
                    Path: path,
                    Compression: compression,
                    Image: tim,
                    Pixels: tim.ToRgba(),
                    CpkId: index,
                    EntryName: SmtDataArchive.DataPath,
                    ContainerType: "smt",
                    Chain: []));
            }
            catch (InvalidDataException)
            {
                // A TIM-like header with a broken body is not an image.
            }
            return;
        }

        if (SmtRle.TryDecode(data, out var decoded))
        {
            var layers = 1 + SmtRle.DecodeAll(decoded, out var inner);
            Scan(inner, path, layers == 1 ? "RLE" : $"RLE × {layers}", index, images, depth + 1);
            return;
        }

        if (TryContainer(data, out var parts))
        {
            for (var i = 0; i < parts.Count; i++)
                Scan(parts[i], $"{path}/{i}", compression + " · contêiner", index, images, depth + 1);
        }
    }

    /// <summary>Segments of a catalog path after the entry index (container parts).</summary>
    public static bool TryParsePath(string path, out int entry, out int[] route)
    {
        entry = 0; route = [];
        var parts = path.Split('/');
        if (parts.Length < 2 || parts[0] != SmtDataArchive.DataPath || !int.TryParse(parts[1], out entry)) return false;
        route = new int[parts.Length - 2];
        for (var i = 2; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out route[i - 2])) return false;
        return true;
    }

    /// <summary>
    /// Rewrites the TIM at <paramref name="route"/> inside an entry, recompressing and
    /// re-packing every layer in place. Throws if the result does not fit the original space.
    /// </summary>
    public static byte[] Replace(byte[] data, ReadOnlySpan<int> route, Func<byte[], byte[]> edit)
    {
        if (TimImage.LooksLikeTim(data))
        {
            if (route.Length != 0) throw new InvalidDataException("O caminho da imagem não confere com o disco.");
            return edit(data);
        }
        if (SmtRle.TryDecode(data, out var decoded))
        {
            var updated = Replace(decoded, route, edit);
            var packed = SmtRle.Encode(updated, data[4]);
            if (packed.Length > data.Length)
                throw new InvalidOperationException($"a imagem compactada passou o espaço em {packed.Length - data.Length} bytes.");
            var padded = new byte[data.Length];
            packed.CopyTo(padded, 0);
            return padded;
        }
        if (route.Length > 0 && TryContainer(data, out var parts) && route[0] < parts.Count)
        {
            var start = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4 + route[0] * 4));
            var end = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8 + route[0] * 4));
            var part = Replace(parts[route[0]], route[1..], edit);
            if (part.Length > end - start)
                throw new InvalidOperationException($"a imagem passou o espaço da parte {route[0]} em {part.Length - (end - start)} bytes.");
            var result = (byte[])data.Clone();
            result.AsSpan(start, end - start).Clear();
            part.CopyTo(result, start);
            return result;
        }
        throw new InvalidDataException("O caminho da imagem não confere com o disco.");
    }

    /// <summary>u32 n followed by n+1 ascending offsets, the first right after the table.</summary>
    private static bool TryContainer(byte[] data, out List<byte[]> parts)
    {
        parts = [];
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (count is < 1 or > 256 || 4 + (count + 1) * 4 > data.Length) return false;
        var offsets = new int[count + 1];
        for (var i = 0; i <= count; i++)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4 + i * 4));
            if (value > data.Length || (i > 0 && value < offsets[i - 1])) return false;
            offsets[i] = (int)value;
        }
        if (offsets[0] != 4 + (count + 1) * 4) return false;
        for (var i = 0; i < count; i++)
            parts.Add(data.AsSpan(offsets[i], offsets[i + 1] - offsets[i]).ToArray());
        return true;
    }
}
