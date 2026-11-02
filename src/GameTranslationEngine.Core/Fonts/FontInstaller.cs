using System.Buffers.Binary;
using System.Text.Json;
using GameTranslationEngine.Core.Archives;
using GameTranslationEngine.Core.Eboot;
using GameTranslationEngine.Core.GameData;
using GameTranslationEngine.Core.Images;

namespace GameTranslationEngine.Core.Fonts;

/// <summary>
/// C4 (and the font part of C7) · Puts a font profile into the game: the
/// pages go into syscg.bin (same GIM format and palette, so the file keeps
/// its size, which the EBOOT also records), and the EBOOT gets the new
/// event→font entries and the width of each new glyph.
/// </summary>
public static class FontInstaller
{
    public sealed record Table(byte[] Signature, int Entries, int EntrySize);

    public sealed record Tables(Table EventToFont, Table RegularMetrics, Table NarrowMetrics, string PagesFile)
    {
        public static Tables LoadInnocentSin()
        {
            using var document = GameResources.ReadJson("is/eboot_tables.json");
            var font = document.RootElement.GetProperty("font");
            Table Read(string name)
            {
                var table = font.GetProperty(name);
                return new Table(Convert.FromHexString(table.GetProperty("signature").GetString()!),
                    table.GetProperty("entries").GetInt32(), table.GetProperty("entrySize").GetInt32());
            }
            return new Tables(Read("eventToFont"), Read("regularMetrics"), Read("narrowMetrics"), font.GetProperty("pagesFile").GetString()!);
        }
    }

    /// <summary>syscg.bin with the profile's pages, and the pages as the game will show them.</summary>
    public static (byte[] Data, IReadOnlyDictionary<int, RgbaImage> Pages) BuildPages(ReadOnlyMemory<byte> syscg, FontProfile profile)
    {
        var files = Par.Split(syscg);
        var raws = files.Select(f => f.Data).ToList();
        var shown = new Dictionary<int, RgbaImage>();
        foreach (var (number, page) in profile.Pages)
        {
            if (number >= files.Count) throw new InvalidDataException($"syscg.bin não tem a página {number}.");
            var gim = GimImage.Read(files[number].Data.Span);
            // Only the cells of the characters the profile adds; the rest stays the game's.
            var first = number >= FontProfile.NarrowFirstPage ? FontProfile.NarrowFirstPage : FontProfile.RegularFirstPage;
            var cells = profile.Mappings.Select(m => m.FontPosition)
                .Where(p => p >> 8 == number - first)
                .Select(p => p & 0xFF)
                .ToHashSet();
            var data = gim.WithPixels(FontPageMerge.Merge(gim.ToRgba(), page, cells));
            raws[number] = data;
            shown[number] = GimImage.Read(data).ToRgba();
        }
        var built = Par.Build(raws).Data;
        if (built.Length != syscg.Length)
            throw new InvalidDataException($"syscg.bin mudou de tamanho ({syscg.Length} → {built.Length}); o EBOOT guarda o tamanho original.");
        return (built, shown);
    }

    /// <summary>Writes the event→font entries and the glyph widths into the EBOOT.</summary>
    public static void PatchEboot(ElfImage elf, FontProfile profile, IReadOnlyDictionary<int, RgbaImage> pages, Tables tables)
    {
        var eventTable = Locate(elf, tables.EventToFont, "event→fonte");
        var regular = Locate(elf, tables.RegularMetrics, "larguras da fonte");
        var narrow = Locate(elf, tables.NarrowMetrics, "larguras da fonte estreita");

        // Check everything first, then write.
        var writes = new List<Action>();
        foreach (var mapping in profile.Mappings)
        {
            if (mapping.EventCode >= tables.EventToFont.Entries)
                throw new InvalidDataException($"Código 0x{mapping.EventCode:X4} fora da tabela event→fonte.");
            var at = eventTable + mapping.EventCode * tables.EventToFont.EntrySize;
            writes.Add(() => BinaryPrimitives.WriteUInt16LittleEndian(elf.Data.AsSpan(at), (ushort)mapping.FontPosition));
        }
        foreach (var position in profile.Mappings.Select(m => m.FontPosition).Distinct())
        {
            if (position >= tables.RegularMetrics.Entries) throw new InvalidDataException($"Posição {position:X4} sem largura na tabela.");
            var (left, width) = Bounds(pages, profile, position, FontProfile.RegularFirstPage);
            var at = regular + position * tables.RegularMetrics.EntrySize;
            writes.Add(() =>
            {
                elf.Data[at] = (byte)left;
                elf.Data[at + 1] = (byte)width;
            });
            // The narrow font only has its own widths below 0x11F.
            if (position >= tables.NarrowMetrics.Entries) continue;
            var (narrowLeft, narrowWidth) = Bounds(pages, profile, position, FontProfile.NarrowFirstPage);
            var narrowAt = narrow + position * tables.NarrowMetrics.EntrySize;
            writes.Add(() =>
            {
                BinaryPrimitives.WriteInt32LittleEndian(elf.Data.AsSpan(narrowAt), narrowLeft);
                BinaryPrimitives.WriteInt32LittleEndian(elf.Data.AsSpan(narrowAt + 4), narrowWidth);
            });
        }
        foreach (var write in writes) write();
    }

    /// <summary>True when the EBOOT maps every character of the profile to its glyph.</summary>
    public static bool IsInstalled(ElfImage elf, FontProfile profile, Tables tables)
    {
        // The signature covers codes below 0x20, which a profile never changes.
        var found = elf.FindInSections(tables.EventToFont.Signature);
        return found.Count == 1 && found.All(at => profile.Mappings.All(m =>
            BinaryPrimitives.ReadUInt16LittleEndian(elf.Data.AsSpan(at + m.EventCode * 2)) == m.FontPosition));
    }

    private static (int Left, int Width) Bounds(IReadOnlyDictionary<int, RgbaImage> pages, FontProfile profile, int position, int firstPage) =>
        Glyph.Bounds(FontProfile.PageOf(profile.Name, pages, position, firstPage), position)
        ?? throw new InvalidDataException($"Glifo vazio na posição {position:X4} (página {firstPage + (position >> 8)}).");

    private static int Locate(ElfImage elf, Table table, string name)
    {
        var found = elf.FindInSections(table.Signature);
        if (found.Count != 1)
            throw new InvalidDataException($"Tabela de {name} não identificada no EBOOT ({found.Count} ocorrências).");
        if (found[0] + table.Entries * table.EntrySize > elf.Data.Length)
            throw new InvalidDataException($"Tabela de {name} ultrapassa o fim do EBOOT.");
        return found[0];
    }
}
