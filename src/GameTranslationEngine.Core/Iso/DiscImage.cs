using System.Text.RegularExpressions;
using GameTranslationEngine.Core.IO;

namespace GameTranslationEngine.Core.Iso;

/// <summary>
/// Opens a disc image as a logical ISO: a plain .iso as is, and a raw CD
/// image (.cue/.bin in MODE2/2352 or MODE1/2352) as the 2048-byte user data
/// of each sector, so the ISO 9660 reader works on both.
/// </summary>
public static class DiscImage
{
    public const int RawSectorSize = 2352;
    public const int UserDataSize = 2048;

    private static readonly Regex CueFile = new("""^\s*FILE\s+(?:"(?<q>[^"]+)"|(?<p>\S+))""",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex CueTrack = new(@"^\s*TRACK\s+\d+\s+(?<mode>\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static bool IsCue(string path) => path.EndsWith(".cue", StringComparison.OrdinalIgnoreCase);

    /// <summary>The file that really holds the data: the .bin a .cue points to, or the path itself.</summary>
    public static string ResolveDataFile(string path)
    {
        if (!IsCue(path)) return path;
        var cue = File.ReadAllText(path);
        var match = CueFile.Match(cue);
        if (!match.Success) throw new InvalidDataException("O .cue não tem uma linha FILE.");
        var name = match.Groups["q"].Success ? match.Groups["q"].Value : match.Groups["p"].Value;
        var bin = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, name);
        if (!File.Exists(bin)) throw new FileNotFoundException($"Arquivo de dados do .cue não encontrado: {bin}");
        return bin;
    }

    /// <summary>Size in bytes of the file on disk that holds the data (what the project records).</summary>
    public static long PhysicalSize(string path) => new FileInfo(ResolveDataFile(path)).Length;

    public static IByteSource Open(string path)
    {
        if (IsCue(path))
        {
            var tracks = CueTrack.Matches(File.ReadAllText(path));
            if (tracks.Count != 1)
                throw new InvalidDataException("Só há suporte a discos com uma única trilha de dados.");
            var mode = tracks[0].Groups["mode"].Value.ToUpperInvariant();
            if (mode is not ("MODE2/2352" or "MODE1/2352"))
                throw new InvalidDataException($"Modo de trilha não suportado: {mode}.");
            return new RawSectorSource(new FileByteSource(ResolveDataFile(path)), mode == "MODE2/2352" ? 24 : 16);
        }

        var file = new FileByteSource(path);
        // A .bin given directly: raw sectors start with the 00 FF×10 00 sync pattern.
        if (file.Length % RawSectorSize == 0 && file.Length >= RawSectorSize && IsSync(file.ReadBytes(0, 12)))
        {
            var header = file.ReadBytes(15, 1)[0];
            return new RawSectorSource(file, header == 2 ? 24 : 16);
        }
        return file;
    }

    private static bool IsSync(byte[] head)
    {
        if (head[0] != 0 || head[11] != 0) return false;
        for (var i = 1; i < 11; i++) if (head[i] != 0xFF) return false;
        return true;
    }
}

/// <summary>The 2048 user bytes of every raw sector, as one contiguous stream.</summary>
public sealed class RawSectorSource : IByteSource, IDisposable
{
    private readonly IByteSource _raw;
    private readonly int _dataOffset;

    public RawSectorSource(IByteSource raw, int dataOffset)
    {
        _raw = raw;
        _dataOffset = dataOffset;
        Length = raw.Length / DiscImage.RawSectorSize * DiscImage.UserDataSize;
    }

    public long Length { get; }

    /// <summary>The raw image underneath, for code that rewrites whole sectors.</summary>
    public IByteSource Raw => _raw;

    public int DataOffset => _dataOffset;

    public void Read(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Leitura fora dos limites do disco.");
        var done = 0;
        while (done < buffer.Length)
        {
            var position = offset + done;
            var sector = position / DiscImage.UserDataSize;
            var inSector = (int)(position % DiscImage.UserDataSize);
            var count = Math.Min(buffer.Length - done, DiscImage.UserDataSize - inSector);
            _raw.Read(sector * DiscImage.RawSectorSize + _dataOffset + inSector, buffer.Slice(done, count));
            done += count;
        }
    }

    public void Dispose() => (_raw as IDisposable)?.Dispose();
}
