using System.Buffers.Binary;
using System.Text;

namespace GameTranslationEngine.Core.Eboot;

/// <summary>
/// The decrypted EBOOT (a 32-bit little-endian MIPS ELF) with what C7 needs:
/// its program and section headers and the conversion between game addresses
/// (as the code sees them, loaded at <see cref="LoadAddress"/>) and file offsets.
/// </summary>
public sealed class ElfImage
{
    /// <summary>Where the PSP loads the game's executable.</summary>
    public const uint LoadAddress = 0x08804000;

    public static ReadOnlySpan<byte> Magic => [0x7F, (byte)'E', (byte)'L', (byte)'F'];

    public sealed record ProgramHeader(uint Type, uint Offset, uint VirtualAddress, uint FileSize, uint MemorySize);

    public sealed record SectionHeader(string Name, uint Type, uint Flags, uint Address, uint Offset, uint Size);

    private ElfImage(byte[] data, List<ProgramHeader> programs, List<SectionHeader> sections)
    {
        Data = data;
        Programs = programs;
        Sections = sections;
    }

    public byte[] Data { get; }
    public IReadOnlyList<ProgramHeader> Programs { get; }
    public IReadOnlyList<SectionHeader> Sections { get; }

    public static ElfImage Parse(byte[] data)
    {
        if (data.Length < 0x34 || !data.AsSpan(0, 4).SequenceEqual(ElfImage.Magic) || data[4] != 1 || data[5] != 1)
            throw new InvalidDataException("EBOOT não é um ELF de 32 bits little-endian.");
        var span = data.AsSpan();
        var phOffset = U32(span, 0x1C);
        var shOffset = U32(span, 0x20);
        var phSize = U16(span, 0x2A);
        var phCount = U16(span, 0x2C);
        var shSize = U16(span, 0x2E);
        var shCount = U16(span, 0x30);
        var shStrings = U16(span, 0x32);

        var programs = new List<ProgramHeader>(phCount);
        for (var i = 0; i < phCount; i++)
        {
            var at = (int)phOffset + i * phSize;
            programs.Add(new ProgramHeader(U32(span, at), U32(span, at + 4), U32(span, at + 8), U32(span, at + 16), U32(span, at + 20)));
        }

        var raw = new List<(uint Name, uint Type, uint Flags, uint Address, uint Offset, uint Size)>(shCount);
        for (var i = 0; i < shCount; i++)
        {
            var at = (int)shOffset + i * shSize;
            raw.Add((U32(span, at), U32(span, at + 4), U32(span, at + 8), U32(span, at + 12), U32(span, at + 16), U32(span, at + 20)));
        }
        var names = shStrings < raw.Count ? raw[shStrings].Offset : 0;
        var sections = raw.Select(s => new SectionHeader(CString(data, (int)(names + s.Name)), s.Type, s.Flags, s.Address, s.Offset, s.Size)).ToList();
        return new ElfImage(data, programs, sections);
    }

    /// <summary>Size covered by the sections (what the ~PSP header records as the ELF size).</summary>
    public int ComputeSize() => (int)Sections.Select(s => s.Offset + s.Size).DefaultIfEmpty(0u).Max();

    /// <summary>File offset of a game address, or throws when the address is not in a loaded segment.</summary>
    public int ToOffset(uint address)
    {
        var relative = address - LoadAddress;
        foreach (var program in Programs)
        {
            if (program.Type == 1 && relative >= program.VirtualAddress && relative < program.VirtualAddress + program.FileSize)
                return (int)(relative - program.VirtualAddress + program.Offset);
        }
        throw new InvalidDataException($"Endereço 0x{address:X8} fora dos segmentos carregados do EBOOT.");
    }

    /// <summary>Game address of a file offset inside a loaded segment.</summary>
    public uint ToAddress(int offset)
    {
        foreach (var program in Programs)
        {
            if (program.Type == 1 && offset >= program.Offset && offset < program.Offset + program.FileSize)
                return (uint)(LoadAddress + program.VirtualAddress + offset - program.Offset);
        }
        throw new InvalidDataException($"Posição 0x{offset:X} fora dos segmentos carregados do EBOOT.");
    }

    public bool TryToOffset(uint address, out int offset)
    {
        var relative = address - LoadAddress;
        foreach (var program in Programs)
        {
            if (program.Type == 1 && relative >= program.VirtualAddress && relative < program.VirtualAddress + program.FileSize)
            {
                offset = (int)(relative - program.VirtualAddress + program.Offset);
                return true;
            }
        }
        offset = -1;
        return false;
    }

    /// <summary>Every file offset where <paramref name="pattern"/> occurs inside a PROGBITS section.</summary>
    public List<int> FindInSections(ReadOnlySpan<byte> pattern)
    {
        var found = new List<int>();
        foreach (var section in Sections.Where(s => s.Type == 1 && s.Size > 0))
        {
            var data = Data.AsSpan((int)section.Offset, (int)section.Size);
            var start = 0;
            while (true)
            {
                var index = data[start..].IndexOf(pattern);
                if (index < 0) break;
                found.Add((int)section.Offset + start + index);
                start += index + 1;
            }
        }
        return found;
    }

    public uint ReadU32(uint address) => U32(Data, ToOffset(address));

    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    private static string CString(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset >= data.Length) return "";
        var end = data[offset..].IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? data[offset..] : data.Slice(offset, end));
    }
}
