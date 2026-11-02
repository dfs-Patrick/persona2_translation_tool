using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GameTranslationEngine.Core.Eboot;

/// <summary>
/// A5 · Decrypts the game's EBOOT.BIN (~PSP container, tag 0xD91613F0, type 2)
/// into the plain ELF. Port of v2's pspDecryptType2: only the two keys that
/// this tag uses are needed.
/// </summary>
public static class PrxDecrypter
{
    public const uint SupportedTag = 0xD91613F0;

    private const int KirkCode = 0x5D;
    private static readonly byte[] KirkKey5D = Convert.FromHexString("115A5D20D53A8DD39CC5AF410F0F186F");
    private static readonly byte[] Kirk1Key = Convert.FromHexString("98C940975C1D10E87FE60EA3FD03A8BA");
    private static readonly byte[] TagKey = Convert.FromHexString("EBFF40D8B41AE166913B8F64B6FCB712");

    /// <summary>KIRK command 1 header size (keys, hashes, mode, sizes).</summary>
    private const int KirkHeaderSize = 0x90;

    public static bool IsEncrypted(ReadOnlySpan<byte> data) =>
        data.Length > 0x150 && data[..4].SequenceEqual("~PSP"u8);

    /// <summary>Returns the ELF inside <paramref name="prx"/>; throws if the result is not the expected ELF.</summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> prx)
    {
        if (!IsEncrypted(prx)) throw new InvalidDataException("EBOOT sem o cabeçalho ~PSP.");
        var elfSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(prx[0x28..]);
        var pspSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(prx[0x2C..]);
        var tag = BinaryPrimitives.ReadUInt32LittleEndian(prx[0xD0..]);
        if (tag != SupportedTag) throw new InvalidDataException($"Tag de criptografia não suportada: 0x{tag:X8}.");
        if (pspSize > prx.Length) throw new InvalidDataException("EBOOT truncado.");

        var xor = ExpandSeed();

        // Type-2 header, laid out as the SHA-1 expects it.
        var header = new byte[0x150];
        prx.Slice(0xD0, 4).CopyTo(header.AsSpan(0x00));           // tag
        prx.Slice(0x140, 0x10).CopyTo(header.AsSpan(0x5C));       // id
        prx.Slice(0x12C, 0x14).CopyTo(header.AsSpan(0x6C));       // sha1
        prx.Slice(0x80, 0x30).CopyTo(header.AsSpan(0x80));        // kirk header (first part)
        prx.Slice(0xC0, 0x10).CopyTo(header.AsSpan(0xB0));        // kirk header (rest)
        prx.Slice(0xB0, 0x10).CopyTo(header.AsSpan(0xC0));        // kirk metadata
        prx[..0x80].CopyTo(header.AsSpan(0xD0));                  // prx header
        Kirk7(header.AsSpan(0x5C, 0x60));

        var expectedSha = header.AsSpan(0x6C, 0x14).ToArray();
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
        {
            sha.AppendData(header.AsSpan(0x00, 4));
            sha.AppendData(xor.AsSpan(0, 0x10));
            sha.AppendData(header.AsSpan(0x04, 0x58));
            sha.AppendData(header.AsSpan(0x5C, 0x10));
            sha.AppendData(header.AsSpan(0x80, 0x40));
            sha.AppendData(header.AsSpan(0xC0, 0x10));
            sha.AppendData(header.AsSpan(0xD0, 0x80));
            if (!sha.GetHashAndReset().AsSpan().SequenceEqual(expectedSha))
                throw new InvalidDataException("EBOOT: SHA-1 do cabeçalho não confere (arquivo alterado ou de outra região).");
        }

        // Rebuild the KIRK command 1 block at 0x40 and decrypt it.
        var buffer = prx[..pspSize].ToArray();
        const int kirk = 0x40;
        buffer.AsSpan(kirk, KirkHeaderSize).Clear();
        header.AsSpan(0xC0, 0x10).CopyTo(buffer.AsSpan(kirk + 0x70));   // data size, data offset
        header.AsSpan(0xD0, 0x80).CopyTo(buffer.AsSpan(kirk + KirkHeaderSize));
        var keys = buffer.AsSpan(kirk, 0x40);
        for (var i = 0; i < 0x40; i++) keys[i] = (byte)(header[0x80 + i] ^ xor[0x10 + i]);
        Kirk7(keys);
        for (var i = 0; i < 0x40; i++) keys[i] ^= xor[0x50 + i];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(kirk + 0x60), 1); // mode: CMD1

        if (buffer[kirk + 0x64] != 0) throw new InvalidDataException("EBOOT com assinatura ECDSA não suportada.");
        var aesKey = AesCbcDecrypt(Kirk1Key, buffer.AsSpan(kirk, 16));
        var dataSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(kirk + 0x70));
        var dataOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(kirk + 0x74));
        var start = kirk + KirkHeaderSize + dataOffset;
        var length = (dataSize + 15) & ~15;
        if (start + length > buffer.Length || dataSize < elfSize)
            throw new InvalidDataException("EBOOT: tamanhos do bloco KIRK inconsistentes.");
        var elf = AesCbcDecrypt(aesKey, buffer.AsSpan(start, length));

        var result = elf.AsSpan(0, elfSize).ToArray();
        var computed = result.AsSpan(0, 4).SequenceEqual(ElfImage.Magic) ? ElfImage.Parse(result).ComputeSize() : -1;
        if (computed != elfSize)
            throw new InvalidDataException($"EBOOT: a decriptação não produziu um ELF válido ({Convert.ToHexString(result.AsSpan(0, 16))}, dataSize {dataSize}, elfSize {elfSize}, computed {computed}).");
        return result;
    }

    /// <summary>The 0x90-byte XOR pad derived from the tag key.</summary>
    private static byte[] ExpandSeed()
    {
        var expanded = new byte[0x90];
        for (var i = 0; i < 0x90; i += 0x10)
        {
            TagKey.CopyTo(expanded, i);
            expanded[i] = (byte)(i / 0x10);
        }
        Kirk7(expanded);
        return expanded;
    }

    /// <summary>KIRK command 7: AES-128-CBC decryption (zero IV) with key 0x5D, in place.</summary>
    private static void Kirk7(Span<byte> data) => AesCbcDecrypt(KirkKey5D, data).CopyTo(data);

    private static byte[] AesCbcDecrypt(byte[] key, ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(data, new byte[16], PaddingMode.None);
    }
}
