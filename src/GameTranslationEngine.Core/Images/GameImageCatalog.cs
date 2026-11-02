using GameTranslationEngine.Core.Archives;
using GameTranslationEngine.Core.Cpk;
using GameTranslationEngine.Core.Iso;

namespace GameTranslationEngine.Core.Images;

/// <summary>A GIM texture discovered in a game's CPK or one of its image archives.</summary>
public sealed record GameImageSource(
    string Group,
    string Name,
    string Path,
    string Compression,
    IImageInfo Image,
    RgbaImage Pixels,
    int CpkId,
    string EntryName,
    string ContainerType,
    IReadOnlyList<ImageArchiveStep> Chain)
{
    /// <summary>The GIM behind a PSP image; only the PSP engines rebuild images.</summary>
    public GimImage Gim => Image as GimImage
        ?? throw new NotSupportedException("A substituição de imagens ainda só existe para jogos de PSP.");
}

public sealed record ImageArchiveStep(string Type, int Index);

/// <summary>Finds GIM images in CPK entries and the PAR/BNP archives used by the games.</summary>
public static class GameImageCatalog
{
    public static IReadOnlyList<GameImageSource> Discover(IsoIndex iso, string cpkPath)
    {
        var cpk = CpkIndex.Read(iso.Open(cpkPath));
        var images = new List<GameImageSource>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in cpk.Entries)
        {
            if (!MayContainImages(entry.FullName)) continue;
            var data = cpk.ReadEntry(entry);
            var compression = entry.IsCompressed
                ? $"CPK/CRILAYLA ({entry.StoredSize:N0} → {entry.ExtractedSize:N0} bytes)"
                : "Sem compressão no CPK";
            var containerType = data.AsSpan().StartsWith("MIG."u8) ? "gim" : ContainerType(entry.Name);
            Scan(data, entry.FullName, compression, entry.FullName, entry.Id, entry.Name,
                containerType, [], images, paths);
        }

        return images;
    }

    private static bool MayContainImages(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".gim", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".par", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".bnp", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".bin", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".dat", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".img", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".tex", StringComparison.OrdinalIgnoreCase);
    }

    private static void Scan(
        ReadOnlyMemory<byte> data,
        string group,
        string compression,
        string path,
        int cpkId,
        string entryName,
        string containerType,
        IReadOnlyList<ImageArchiveStep> chain,
        ICollection<GameImageSource> images,
        ISet<string> paths)
    {
        var bytes = data.Span;
        if (bytes.Length >= 4 && bytes[..4].SequenceEqual("MIG."u8))
        {
            try
            {
                var gim = GimImage.Read(bytes);
                if (paths.Add(path))
                {
                    var name = Path.GetFileName(path);
                    images.Add(new GameImageSource(group, name, path, compression, gim, gim.ToRgba(),
                        cpkId, entryName, containerType, chain));
                }
            }
            catch (InvalidDataException)
            {
                // A matching signature can still contain an invalid GIM block layout.
            }
            catch (NotSupportedException)
            {
                // Other GIM formats are not rendered by the current viewer.
            }
        }

        var extension = Path.GetExtension(path);
        if (extension.Equals(".par", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bin", StringComparison.OrdinalIgnoreCase))
        {
            List<ArchiveEntry> entries;
            try
            {
                entries = Par.Split(data);
            }
            catch (InvalidDataException)
            {
                // Most .bin entries are not PAR archives.
                return;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var childPath = $"{path}/{i}";
                Scan(entries[i].Data, group, $"{compression} · PAR sem compressão", childPath,
                    cpkId, entryName, containerType,
                    chain.Append(new ImageArchiveStep("par", i)).ToList(), images, paths);
            }
        }
        else if (extension.Equals(".bnp", StringComparison.OrdinalIgnoreCase))
        {
            var entries = Bnp.Split(data);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var decoded = BnpEntryCodec.Decode(entry.Data.Span);
                var childPath = $"{path}/{entry.Offset:X}";
                var childCompression = decoded.Compression switch
                {
                    BnpCompression.None => $"{compression} · BNP sem compressão",
                    BnpCompression.Rle => $"{compression} · BNP RLE",
                    BnpCompression.Lzss => $"{compression} · BNP LZSS",
                    _ => $"{compression} · BNP desconhecida",
                };
                Scan(decoded.Data, group, childCompression, childPath, cpkId, entryName,
                    containerType, chain.Append(new ImageArchiveStep("bnp", i)).ToList(), images, paths);
            }
        }
    }

    private static string ContainerType(string name)
    {
        var extension = Path.GetExtension(name);
        return extension.Equals(".bnp", StringComparison.OrdinalIgnoreCase) ? "bnp"
            : extension.Equals(".par", StringComparison.OrdinalIgnoreCase)
              || extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ? "par"
            : "gim";
    }
}
