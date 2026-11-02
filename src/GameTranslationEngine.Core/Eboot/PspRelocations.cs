using System.Buffers.Binary;

namespace GameTranslationEngine.Core.Eboot;

/// <summary>
/// The relocations of a PSP module (sections of type 0x700000A0): when the
/// PSP loads the EBOOT it adds the load address to every word listed here.
/// Code that replaces original instructions must take those words off the
/// list, or the loader would "fix" the new instructions and break them.
/// </summary>
public static class PspRelocations
{
    public const uint SectionType = 0x700000A0;
    private const int EntrySize = 8;

    /// <summary>Game addresses (as loaded) of every relocated word.</summary>
    public static IEnumerable<uint> Targets(ElfImage elf)
    {
        foreach (var (offset, address) in Entries(elf))
            if ((elf.Data[offset + 4] & 0xFF) != 0) yield return address;
    }

    /// <summary>Turns into "no relocation" every entry that points into [<paramref name="from"/>, <paramref name="to"/>).</summary>
    public static int Disable(ElfImage elf, uint from, uint to)
    {
        var count = 0;
        foreach (var (offset, address) in Entries(elf))
        {
            if (address < from || address >= to || elf.Data[offset + 4] == 0) continue;
            elf.Data[offset + 4] = 0; // type R_MIPS_NONE, the rest of r_info kept
            count++;
        }
        return count;
    }

    private static IEnumerable<(int Offset, uint Address)> Entries(ElfImage elf)
    {
        var segments = elf.Programs;
        foreach (var section in elf.Sections.Where(s => s.Type == SectionType))
        {
            for (var at = (int)section.Offset; at + EntrySize <= section.Offset + section.Size; at += EntrySize)
            {
                var relative = BinaryPrimitives.ReadUInt32LittleEndian(elf.Data.AsSpan(at));
                var info = BinaryPrimitives.ReadUInt32LittleEndian(elf.Data.AsSpan(at + 4));
                var segment = (int)((info >> 8) & 0xFF);
                if (segment >= segments.Count) continue;
                yield return (at, ElfImage.LoadAddress + segments[segment].VirtualAddress + relative);
            }
        }
    }
}
