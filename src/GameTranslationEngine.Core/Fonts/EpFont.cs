using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using GameTranslationEngine.Core.Archives;
using GameTranslationEngine.Core.Eboot;
using GameTranslationEngine.Core.GameData;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.Text;

namespace GameTranslationEngine.Core.Fonts;

/// <summary>A cell of the EP font that a profile redraws: position, character and width.</summary>
public sealed record EpGlyphCell(int Position, string Character, int Left, int Width);

/// <summary>
/// Font profile of Eternal Punishment (data/ep/fonts/&lt;name&gt;): the Japanese
/// font is fixed width (14 px); the profile puts narrow Latin glyphs in cells
/// below <see cref="SpacedCharacters"/>, each with its own width, which
/// <see cref="EpTextPatch"/> makes the game use.
/// </summary>
public sealed class EpFontProfile
{
    /// <summary>Cells that get their own width (as in v1's p2ep_template); kana and kanji keep 14 px.</summary>
    public const int SpacedCharacters = 0x120;
    /// <summary>syscg.bin file of font page 0.</summary>
    public const int FirstPageFile = 5;
    public const string PagesFile = "syscg.bin";

    private EpFontProfile(EventEncoding encoding, IReadOnlyList<EpGlyphCell> cells, IReadOnlyDictionary<int, int> eventToFont,
        IReadOnlyDictionary<int, RgbaImage> pages)
    {
        Encoding = encoding;
        Cells = cells;
        EventToFont = eventToFont;
        Pages = pages;
    }

    public EventEncoding Encoding { get; }
    public IReadOnlyList<EpGlyphCell> Cells { get; }
    /// <summary>Event codes pointed to other font cells (new characters).</summary>
    public IReadOnlyDictionary<int, int> EventToFont { get; }
    /// <summary>Font pages (0 = positions 0x000–0x0FF…) with the profile's glyphs.</summary>
    public IReadOnlyDictionary<int, RgbaImage> Pages { get; }

    public static EpFontProfile LoadPortuguese()
    {
        Stream Open(string file) => GameResources.Open($"ep/fonts/pt-br/{file}");
        EventEncoding encoding;
        using (var stream = Open("event.json")) encoding = EventEncoding.Load(stream);
        using var font = JsonDocument.Parse(Open("font.json"));
        var root = font.RootElement;
        static int Hex(string value) => int.Parse(value.Replace("0x", ""), NumberStyles.HexNumber);
        var cells = root.GetProperty("cells").EnumerateArray().Select(c => new EpGlyphCell(
            Hex(c.GetProperty("position").GetString()!), c.GetProperty("char").GetString()!,
            c.GetProperty("left").GetInt32(), c.GetProperty("width").GetInt32())).ToList();
        var map = root.GetProperty("eventToFont").EnumerateObject().ToDictionary(p => Hex(p.Name), p => Hex(p.Value.GetString()!));
        var pages = new Dictionary<int, RgbaImage>();
        foreach (var page in root.GetProperty("pages").EnumerateArray().Select(p => p.GetInt32()))
        {
            using var png = Open($"{page}.png");
            using var memory = new MemoryStream();
            png.CopyTo(memory);
            pages[page] = Png.Decode(memory.ToArray());
        }
        foreach (var cell in cells)
        {
            if (cell.Position >= SpacedCharacters) throw new InvalidDataException($"Célula 0x{cell.Position:x} fora das larguras próprias.");
            if (cell.Left is < 0 or > 15 || cell.Width is < 1 or > 15) throw new InvalidDataException($"Largura inválida em 0x{cell.Position:x}.");
            if (!pages.ContainsKey(cell.Position >> 8)) throw new InvalidDataException($"Página da célula 0x{cell.Position:x} ausente.");
        }
        return new EpFontProfile(encoding, cells, map, pages);
    }

    /// <summary>One byte per cell below 0x120: left column &lt;&lt; 4 | width. Other cells keep the game's 1 and 14.</summary>
    public byte[] WidthTable()
    {
        var table = Enumerable.Repeat((byte)(1 << 4 | 14), SpacedCharacters).ToArray();
        foreach (var cell in Cells) table[cell.Position] = (byte)(cell.Left << 4 | cell.Width);
        return table;
    }
}

/// <summary>C4 for Eternal Punishment: the font pages in syscg.bin and the EBOOT changes.</summary>
public static class EpFontInstaller
{
    /// <summary>syscg.bin with the profile's cells, same size, and the pages as the game will show them.</summary>
    public static (byte[] Data, IReadOnlyDictionary<int, RgbaImage> Pages) BuildPages(ReadOnlyMemory<byte> syscg, EpFontProfile profile)
    {
        var files = Par.Split(syscg);
        var raws = files.Select(f => f.Data).ToList();
        var shown = new Dictionary<int, RgbaImage>();
        foreach (var (page, image) in profile.Pages)
        {
            var file = EpFontProfile.FirstPageFile + page;
            var gim = GimImage.Read(files[file].Data.Span);
            var cells = profile.Cells.Where(c => c.Position >> 8 == page).Select(c => c.Position & 0xFF).ToHashSet();
            var data = gim.WithPixels(FontPageMerge.Merge(gim.ToRgba(), image, cells));
            raws[file] = data;
            shown[page] = GimImage.Read(data).ToRgba();
        }
        var built = Par.Build(raws).Data;
        if (built.Length != syscg.Length)
            throw new InvalidDataException($"syscg.bin mudou de tamanho ({syscg.Length} → {built.Length}).");
        return (built, shown);
    }

    /// <summary>New characters in the event→font table, then the width routines (the EBOOT grows).</summary>
    public static ElfImage PatchEboot(ElfImage elf, EpFontProfile profile)
    {
        foreach (var (code, position) in profile.EventToFont)
        {
            if (code >= EpTextPatch.EventToFontEntries) throw new InvalidDataException($"Código 0x{code:x4} fora da tabela event→fonte.");
            BinaryPrimitives.WriteUInt16LittleEndian(elf.Data.AsSpan(elf.ToOffset(EpTextPatch.EventToFont + (uint)code * 2)), (ushort)position);
        }
        return EpTextPatch.Apply(elf, profile.WidthTable());
    }

    public static bool IsInstalled(ElfImage elf, EpFontProfile profile) =>
        EpTextPatch.IsApplied(elf)
        && profile.EventToFont.All(p => BinaryPrimitives.ReadUInt16LittleEndian(
            elf.Data.AsSpan(elf.ToOffset(EpTextPatch.EventToFont + (uint)p.Key * 2))) == p.Value);
}
