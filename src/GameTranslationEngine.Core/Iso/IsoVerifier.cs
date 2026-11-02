using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using GameTranslationEngine.Core.IO;

namespace GameTranslationEngine.Core.Iso;

/// <summary>Identification of a source ISO.</summary>
public sealed record IsoIdentity(
    string GameId,
    string Title,
    string VolumeId,
    long Size,
    string? Sha256);

public static class SupportedGames
{
    public const string InnocentSinUs = "ULUS-10584";

    public static string Describe(string gameId) => gameId switch
    {
        InnocentSinUs => "Persona 2: Innocent Sin (PSP, US)",
        "ULES-01557" => "Persona 2: Innocent Sin (PSP, EU) — não suportada",
        "ULJM-05884" => "Persona 2: Tsumi (PSP, JP) — não suportada",
        _ => $"{gameId} — jogo não reconhecido",
    };
}

/// <summary>
/// A1 · Checks that the ISO is the one the tool supports, before anything
/// else touches it. The hash is the project's key to detect a different ISO.
/// </summary>
public static class IsoVerifier
{
    public static IsoIdentity Identify(IsoIndex iso, string? sha256 = null)
    {
        var gameId = ReadUmdGameId(iso);
        var sfo = iso.Find("PSP_GAME/PARAM.SFO") is { } sfoEntry
            ? ParamSfo.Read(iso.Open(sfoEntry).ReadAll())
            : new Dictionary<string, string>();

        if (gameId is null) sfo.TryGetValue("DISC_ID", out gameId);
        if (gameId is null)
            throw new InvalidDataException("Não foi possível identificar o jogo (UMD_DATA.BIN e PARAM.SFO ausentes).");
        if (gameId.Length == 9 && !gameId.Contains('-')) gameId = $"{gameId[..4]}-{gameId[4..]}";

        return new IsoIdentity(gameId, sfo.GetValueOrDefault("TITLE", ""), iso.VolumeId, iso.Source.Length, sha256);
    }

    /// <summary>SHA-256 of the whole ISO, reporting progress from 0 to 1.</summary>
    public static async Task<string> ComputeSha256Async(
        string path, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        const int chunk = 4 * 1024 * 1024;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            chunk, FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(chunk);
        try
        {
            long done = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, chunk), cancellation)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                done += read;
                progress?.Report((double)done / stream.Length);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string? ReadUmdGameId(IsoIndex iso)
    {
        if (iso.Find("UMD_DATA.BIN") is not { } entry) return null;
        var text = Encoding.ASCII.GetString(iso.Open(entry).ReadBytes(0, (int)Math.Min(entry.Size, 32u)));
        var separator = text.IndexOf('|');
        return separator > 0 ? text[..separator].Trim() : null;
    }
}

/// <summary>Reader for PSP PARAM.SFO (key/value table).</summary>
public static class ParamSfo
{
    public static Dictionary<string, string> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20 || data[0] != 0 || data[1] != (byte)'P' || data[2] != (byte)'S' || data[3] != (byte)'F')
            throw new InvalidDataException("PARAM.SFO inválido.");

        var keyTable = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var dataTable = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        var result = new Dictionary<string, string>();
        for (var i = 0; i < count; i++)
        {
            var entry = data.Slice(20 + i * 16, 16);
            var keyOffset = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            var format = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
            var valueOffset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);

            var key = ReadCString(data[(keyTable + keyOffset)..]);
            var value = data.Slice(dataTable + valueOffset, length);
            result[key] = format == 0x0404
                ? BinaryPrimitives.ReadUInt32LittleEndian(value).ToString()
                : Encoding.UTF8.GetString(value).TrimEnd('\0');
        }
        return result;
    }

    private static string ReadCString(ReadOnlySpan<byte> data)
    {
        var end = data.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? data : data[..end]);
    }
}
