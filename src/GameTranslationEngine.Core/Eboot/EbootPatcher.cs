using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using GameTranslationEngine.Core.Archives;
using GameTranslationEngine.Core.GameData;

namespace GameTranslationEngine.Core.Eboot;

/// <summary>A table in the EBOOT with the position of each file of a container.</summary>
/// <param name="Format">"ear": (start, end) pairs; "bnp_sector": (offset in sector, sector, size).</param>
public sealed record EbootTable(string File, string Format, uint Address, int SizeAdjust)
{
    public static IReadOnlyList<EbootTable> LoadInnocentSin()
    {
        using var document = GameResources.ReadJson("is/eboot_tables.json");
        return document.RootElement.GetProperty("tables").EnumerateArray().Select(t => new EbootTable(
            t.GetProperty("file").GetString()!,
            t.GetProperty("format").GetString()!,
            uint.Parse(t.GetProperty("address").GetString()!.Replace("0x", ""), NumberStyles.HexNumber),
            t.TryGetProperty("sizeAdjust", out var adjust) ? adjust.GetInt32() : 0)).ToList();
    }

    public int EntrySize => 8;
}

/// <summary>
/// C7 · Changes to the decrypted EBOOT: the tables that say where each file
/// is inside event.bin and the BNP containers, and the fixed disc locations
/// ("disc0:/sce_lbn0x…_size0x…") the game uses to open the CPK.
/// Every change first checks that the EBOOT still holds the original value.
/// </summary>
public sealed class EbootPatcher(ElfImage elf)
{
    public ElfImage Elf { get; } = elf;

    /// <summary>True when the table at <paramref name="table"/> describes <paramref name="layout"/>.</summary>
    public bool Matches(EbootTable table, IReadOnlyList<EntryLayout> layout)
    {
        if (!Elf.TryToOffset(table.Address, out var start) || start + layout.Count * table.EntrySize > Elf.Data.Length) return false;
        Span<byte> expected = stackalloc byte[8];
        for (var i = 0; i < layout.Count; i++)
        {
            Encode(table, layout[i], expected);
            if (!Elf.Data.AsSpan(start + i * table.EntrySize, table.EntrySize).SequenceEqual(expected)) return false;
        }
        return true;
    }

    /// <summary>Rewrites a table after checking it describes the original container.</summary>
    public void UpdateTable(EbootTable table, IReadOnlyList<EntryLayout> original, IReadOnlyList<EntryLayout> updated)
    {
        if (original.Count != updated.Count)
            throw new InvalidDataException($"{table.File}: o número de arquivos mudou ({original.Count} → {updated.Count}).");
        if (!Matches(table, original))
            throw new InvalidDataException(
                $"A tabela de {table.File} em 0x{table.Address:X8} não corresponde ao arquivo original; o EBOOT não é o esperado.");
        var start = Elf.ToOffset(table.Address);
        for (var i = 0; i < updated.Count; i++)
            Encode(table, updated[i], Elf.Data.AsSpan(start + i * table.EntrySize, table.EntrySize));
    }

    private static void Encode(EbootTable table, EntryLayout entry, Span<byte> output)
    {
        switch (table.Format)
        {
            case "ear":
                BinaryPrimitives.WriteInt32LittleEndian(output, entry.Offset);
                BinaryPrimitives.WriteInt32LittleEndian(output[4..], entry.Offset + entry.Size);
                break;
            case "bnp_sector":
                var size = entry.Size + table.SizeAdjust;
                if (entry.Offset >> 11 > ushort.MaxValue || size < 0)
                    throw new InvalidDataException($"{table.File}: posição 0x{entry.Offset:X} fora do alcance da tabela.");
                BinaryPrimitives.WriteUInt16LittleEndian(output, (ushort)(entry.Offset & 0x7FF));
                BinaryPrimitives.WriteUInt16LittleEndian(output[2..], (ushort)(entry.Offset >> 11));
                BinaryPrimitives.WriteInt32LittleEndian(output[4..], size);
                break;
            default:
                throw new InvalidDataException($"Formato de tabela desconhecido: {table.Format}.");
        }
    }

    public static string DiscPath(uint lba, uint size) => $"disc0:/sce_lbn0x{lba:x}_size0x{size:x}";

    /// <summary>
    /// Points the game's fixed disc location of a file to its new sector and
    /// size. The new text has to fit where the old one was.
    /// </summary>
    public void UpdateDiscLocation(uint oldLba, uint oldSize, uint newLba, uint newSize)
    {
        var old = Encoding.ASCII.GetBytes(DiscPath(oldLba, oldSize) + "\0");
        var found = Elf.FindInSections(old);
        if (found.Count == 0)
            throw new InvalidDataException($"Localização {DiscPath(oldLba, oldSize)} não encontrada no EBOOT.");
        var replacement = Encoding.ASCII.GetBytes(DiscPath(newLba, newSize));
        foreach (var offset in found)
        {
            // Room: the old text plus the zero bytes that follow it.
            var room = old.Length - 1;
            while (offset + room < Elf.Data.Length && Elf.Data[offset + room] == 0) room++;
            if (replacement.Length + 1 > room)
                throw new InvalidDataException(
                    $"{DiscPath(newLba, newSize)} não cabe no lugar de {DiscPath(oldLba, oldSize)} no EBOOT.");
            var target = Elf.Data.AsSpan(offset, old.Length - 1);
            target.Clear();
            replacement.CopyTo(Elf.Data.AsSpan(offset));
        }
    }
}
