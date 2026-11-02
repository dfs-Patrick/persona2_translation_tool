using System.Buffers.Binary;
using GameTranslationEngine.Core.IO;

namespace GameTranslationEngine.Core.Iso;

/// <summary>A file of the ISO to replace: its path, new size and how to write it.</summary>
public sealed record IsoReplacement(string Path, long Length, Action<Stream> Write);

/// <summary>Where a replaced file goes in the new ISO.</summary>
public sealed record IsoPlacement(IsoEntry Original, uint Lba, long Length)
{
    public bool Moved => Lba != Original.Lba;
}

/// <summary>
/// C8 · Writes the new ISO as a copy of the original with some files
/// replaced. Every other file keeps its sector. A replaced file stays in its
/// place when it fits before the next used sector; otherwise it goes to the
/// end of the volume. Directory records and the volume size are updated in
/// place, so the directory tables keep their layout.
/// </summary>
public sealed class IsoRebuild
{
    private const int Sector = IsoIndex.SectorSize;

    private IsoRebuild(IsoIndex iso, IReadOnlyList<IsoPlacement> placements, uint volumeSectors)
    {
        Iso = iso;
        Placements = placements;
        VolumeSectors = volumeSectors;
    }

    public IsoIndex Iso { get; }
    public IReadOnlyList<IsoPlacement> Placements { get; }
    public uint VolumeSectors { get; }

    public IsoPlacement this[string path] =>
        Placements.First(p => string.Equals(p.Original.Path, path, StringComparison.OrdinalIgnoreCase));

    public static IsoRebuild Plan(IsoIndex iso, IEnumerable<(string Path, long Length)> files)
    {
        RequireOnlyPrimaryDescriptor(iso.Source);
        var starts = iso.Enumerate().Select(e => e.Lba).Append(iso.VolumeSectors).Order().ToList();
        var end = Math.Max(iso.VolumeSectors, (uint)((iso.Source.Length + Sector - 1) / Sector));
        var placements = new List<IsoPlacement>();
        foreach (var (path, length) in files)
        {
            var entry = iso.Get(path);
            if (length > uint.MaxValue) throw new InvalidDataException($"{path} grande demais para a ISO.");
            var next = starts.FirstOrDefault(s => s > entry.Lba, end);
            if (Sectors(length) <= next - entry.Lba)
            {
                placements.Add(new IsoPlacement(entry, entry.Lba, length));
            }
            else
            {
                placements.Add(new IsoPlacement(entry, end, length));
                end += (uint)Sectors(length);
            }
        }
        return new IsoRebuild(iso, placements, end);
    }

    public void Write(string output, IReadOnlyList<IsoReplacement> replacements,
        IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        var temporary = output + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20))
            {
                var total = (double)Iso.Source.Length + replacements.Sum(r => r.Length);
                CopyOriginal(stream, cancellation, done => progress?.Report(done / total));
                long written = Iso.Source.Length;
                foreach (var replacement in replacements)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var placement = this[replacement.Path];
                    if (placement.Length != replacement.Length)
                        throw new InvalidOperationException($"{replacement.Path}: tamanho diferente do planejado.");
                    // The old data goes away, so nothing stale is left behind.
                    Zero(stream, placement.Original.Offset, Sectors(placement.Original.Size) * Sector);
                    stream.Position = (long)placement.Lba * Sector;
                    replacement.Write(stream);
                    if (stream.Position != (long)placement.Lba * Sector + placement.Length)
                        throw new InvalidOperationException($"{replacement.Path}: foram gravados {stream.Position - (long)placement.Lba * Sector} bytes, esperados {placement.Length}.");
                    PatchRecord(stream, placement);
                    written += replacement.Length;
                    progress?.Report(written / total);
                }
                if (stream.Length < (long)VolumeSectors * Sector) stream.SetLength((long)VolumeSectors * Sector);
                if (VolumeSectors != Iso.VolumeSectors) PatchVolumeSize(stream, VolumeSectors);
            }
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void CopyOriginal(Stream output, CancellationToken cancellation, Action<long> progress)
    {
        var buffer = new byte[8 * 1024 * 1024];
        for (long done = 0; done < Iso.Source.Length;)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = (int)Math.Min(buffer.Length, Iso.Source.Length - done);
            Iso.Source.Read(done, buffer.AsSpan(0, count));
            output.Write(buffer, 0, count);
            done += count;
            progress(done);
        }
    }

    private static void PatchRecord(Stream stream, IsoPlacement placement)
    {
        Span<byte> record = stackalloc byte[18];
        stream.Position = placement.Original.RecordOffset;
        stream.ReadExactly(record);
        if (BinaryPrimitives.ReadUInt32LittleEndian(record[2..]) != placement.Original.Lba)
            throw new InvalidDataException($"Registro de diretório de {placement.Original.Path} não confere.");
        BinaryPrimitives.WriteUInt32LittleEndian(record[2..], placement.Lba);
        BinaryPrimitives.WriteUInt32BigEndian(record[6..], placement.Lba);
        BinaryPrimitives.WriteUInt32LittleEndian(record[10..], (uint)placement.Length);
        BinaryPrimitives.WriteUInt32BigEndian(record[14..], (uint)placement.Length);
        stream.Position = placement.Original.RecordOffset;
        stream.Write(record);
    }

    private static void PatchVolumeSize(Stream stream, uint sectors)
    {
        Span<byte> size = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(size, sectors);
        BinaryPrimitives.WriteUInt32BigEndian(size[4..], sectors);
        stream.Position = 16L * Sector + 80;
        stream.Write(size);
    }

    private static void Zero(Stream stream, long offset, long count)
    {
        var zeros = new byte[Math.Min(count, 1 << 20)];
        stream.Position = offset;
        while (count > 0)
        {
            var chunk = (int)Math.Min(zeros.Length, count);
            stream.Write(zeros, 0, chunk);
            count -= chunk;
        }
    }

    private static long Sectors(long length) => (length + Sector - 1) / Sector;

    /// <summary>Only the primary volume descriptor is updated, so a second directory tree (Joliet) is not supported.</summary>
    private static void RequireOnlyPrimaryDescriptor(IByteSource source)
    {
        for (var sector = 17; sector < 32; sector++)
        {
            var type = source.ReadBytes((long)sector * Sector, 6);
            if (type[0] == 255) return;
            if (type[0] == 2) throw new NotSupportedException("ISO com descritor de volume suplementar (Joliet) não é suportada.");
        }
    }
}
