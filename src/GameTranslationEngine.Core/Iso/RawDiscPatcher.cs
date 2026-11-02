using GameTranslationEngine.Core.IO;

namespace GameTranslationEngine.Core.Iso;

/// <summary>
/// Writes a copy of a raw disc with some logical (2048 bytes per sector)
/// ranges replaced; every touched sector gets its EDC/ECC recomputed.
/// </summary>
public static class RawDiscPatcher
{
    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="outputBin"/> and applies the patches,
    /// given as logical byte offset → new data.
    /// </summary>
    public static void Write(RawSectorSource source, string outputBin,
        IReadOnlyList<(long LogicalOffset, byte[] Data)> patches,
        IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        var temporary = outputBin + ".tmp";
        var rawLength = source.Raw.Length;
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            var buffer = new byte[DiscImage.RawSectorSize * 512];
            for (long position = 0; position < rawLength; position += buffer.Length)
            {
                cancellation.ThrowIfCancellationRequested();
                var count = (int)Math.Min(buffer.Length, rawLength - position);
                source.Raw.Read(position, buffer.AsSpan(0, count));
                output.Write(buffer, 0, count);
                progress?.Report((double)(position + count) / rawLength * 0.9);
            }

            var touched = new SortedDictionary<long, byte[]>();
            foreach (var (offset, data) in patches)
            {
                for (var done = 0; done < data.Length;)
                {
                    var position = offset + done;
                    var sector = position / DiscImage.UserDataSize;
                    var inSector = (int)(position % DiscImage.UserDataSize);
                    var count = Math.Min(data.Length - done, DiscImage.UserDataSize - inSector);
                    if (!touched.TryGetValue(sector, out var raw))
                    {
                        raw = source.Raw.ReadBytes(sector * DiscImage.RawSectorSize, DiscImage.RawSectorSize);
                        touched[sector] = raw;
                    }
                    if (CdSectorCodec.IsForm2(raw))
                        throw new InvalidDataException($"O setor {sector} é Form 2 e não guarda 2048 bytes de dados.");
                    data.AsSpan(done, count).CopyTo(raw.AsSpan(source.DataOffset + inSector));
                    done += count;
                }
            }

            foreach (var (sector, raw) in touched)
            {
                CdSectorCodec.Fix(raw);
                output.Seek(sector * DiscImage.RawSectorSize, SeekOrigin.Begin);
                output.Write(raw);
            }
            progress?.Report(1);
        }
        File.Move(temporary, outputBin, overwrite: true);
    }
}
