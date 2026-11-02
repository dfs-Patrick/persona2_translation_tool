using System.Buffers.Binary;

namespace GameTranslationEngine.Core.Iso;

/// <summary>
/// EDC and ECC of CD-ROM XA sectors (ECMA-130, Mode 2): what a raw sector needs
/// after its user data changes. Form 1 has EDC + P/Q parity, Form 2 only EDC.
/// </summary>
public static class CdSectorCodec
{
    private static readonly uint[] EdcTable = BuildEdcTable();
    private static readonly byte[] Forward = new byte[256];
    private static readonly byte[] Backward = new byte[256];

    static CdSectorCodec()
    {
        for (var i = 0; i < 256; i++)
        {
            var j = (i << 1) ^ ((i & 0x80) != 0 ? 0x11D : 0);
            Forward[i] = (byte)j;
            Backward[i ^ j] = (byte)i;
        }
    }

    private static uint[] BuildEdcTable()
    {
        var table = new uint[256];
        for (var i = 0; i < 256; i++)
        {
            var edc = (uint)i;
            for (var bit = 0; bit < 8; bit++) edc = (edc >> 1) ^ ((edc & 1) != 0 ? 0xD8018001u : 0);
            table[i] = edc;
        }
        return table;
    }

    public static uint Edc(ReadOnlySpan<byte> data)
    {
        uint edc = 0;
        foreach (var b in data) edc = (edc >> 8) ^ EdcTable[(edc ^ b) & 0xFF];
        return edc;
    }

    /// <summary>True for a Mode 2 sector whose submode flags Form 2 (bit 5 of the subheader).</summary>
    public static bool IsForm2(ReadOnlySpan<byte> sector) => (sector[18] & 0x20) != 0;

    /// <summary>Recomputes EDC (and ECC for Form 1) of a raw 2352-byte Mode 2 sector, in place.</summary>
    public static void Fix(Span<byte> sector)
    {
        if (sector.Length != 2352) throw new ArgumentException("Setor bruto deve ter 2352 bytes.", nameof(sector));
        if (IsForm2(sector))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(sector[2348..], Edc(sector[16..2348]));
            return;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(sector[2072..], Edc(sector[16..2072]));

        // The header address is excluded from the parity of Mode 2.
        Span<byte> address = stackalloc byte[4];
        sector.Slice(12, 4).CopyTo(address);
        sector.Slice(12, 4).Clear();
        Parity(sector[12..], 86, 24, 2, 86, sector.Slice(0x81C, 172));
        Parity(sector[12..], 52, 43, 86, 88, sector.Slice(0x8C8, 104));
        address.CopyTo(sector.Slice(12, 4));
    }

    /// <summary>True when the stored EDC (and ECC) match the sector's contents.</summary>
    public static bool IsValid(ReadOnlySpan<byte> sector)
    {
        var copy = sector.ToArray();
        Fix(copy);
        return copy.AsSpan().SequenceEqual(sector);
    }

    private static void Parity(ReadOnlySpan<byte> source, int majorCount, int minorCount, int majorMultiple,
        int minorIncrement, Span<byte> destination)
    {
        var size = majorCount * minorCount;
        for (var major = 0; major < majorCount; major++)
        {
            var index = (major >> 1) * majorMultiple + (major & 1);
            byte a = 0, b = 0;
            for (var minor = 0; minor < minorCount; minor++)
            {
                var value = source[index];
                index += minorIncrement;
                if (index >= size) index -= size;
                a ^= value;
                b ^= value;
                a = Forward[a];
            }
            a = Backward[Forward[a] ^ b];
            destination[major] = a;
            destination[major + majorCount] = (byte)(a ^ b);
        }
    }
}
