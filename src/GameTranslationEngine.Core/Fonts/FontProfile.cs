using System.Text.Json;
using GameTranslationEngine.Core.GameData;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.Text;

namespace GameTranslationEngine.Core.Fonts;

/// <summary>A character the profile adds: its code in the messages and its position in the font.</summary>
public sealed record FontMapping(string Character, int EventCode, int FontPosition);

/// <summary>
/// A font profile (like v2's fonts/pt-br): the character table of the
/// messages (event.json), the layout of the font pages (font.json), the
/// pages themselves (n.png) and optional recipes that build a glyph from the
/// pixels of two others (glyphs.json).
/// </summary>
public sealed class FontProfile
{
    /// <summary>Pages of the normal font start at syscg.bin file 7; the narrow ones at 12.</summary>
    public const int RegularFirstPage = 7;
    public const int NarrowFirstPage = 12;
    public const int MaxFontPosition = 0x500;

    private FontProfile(string name, EventEncoding encoding, IReadOnlyList<FontMapping> mappings, IReadOnlyDictionary<int, RgbaImage> pages)
    {
        Name = name;
        Encoding = encoding;
        Mappings = mappings;
        Pages = pages;
    }

    public string Name { get; }

    /// <summary>Character table used to encode the translation.</summary>
    public EventEncoding Encoding { get; }

    /// <summary>Characters that differ from the original game (the event→font table entries to change).</summary>
    public IReadOnlyList<FontMapping> Mappings { get; }

    /// <summary>The pages of syscg.bin that the profile replaces, recipes already applied.</summary>
    public IReadOnlyDictionary<int, RgbaImage> Pages { get; }

    /// <summary>The PT-BR profile shipped with the tool (tested with v2).</summary>
    public static FontProfile LoadPortuguese() => Load("pt-br", file => GameResources.Open($"is/fonts/pt-br/{file}"),
        Enumerable.Range(0, 32).Select(n => $"{n}.png").Where(Exists));

    /// <summary>A profile from a folder (e.g. one edited by the translation team).</summary>
    public static FontProfile LoadFolder(string directory) => Load(Path.GetFileName(directory.TrimEnd('/', '\\')),
        file => File.OpenRead(Path.Combine(directory, file)),
        Directory.EnumerateFiles(directory, "*.png").Select(Path.GetFileName).Where(n => int.TryParse(Path.GetFileNameWithoutExtension(n), out _))!);

    private static bool Exists(string file)
    {
        try
        {
            using var _ = GameResources.Open($"is/fonts/pt-br/{file}");
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static FontProfile Load(string name, Func<string, Stream> open, IEnumerable<string> pageFiles)
    {
        EventEncoding encoding;
        using (var stream = open("event.json")) encoding = EventEncoding.Load(stream);
        Dictionary<string, int> font;
        using (var stream = open("font.json")) font = LoadFontLayout(stream);

        var original = EventEncoding.LoadInnocentSinEnglish();
        Dictionary<string, int> originalFont;
        using (var stream = GameResources.Open("is/encoding/en/font.json")) originalFont = LoadFontLayout(stream);

        var mappings = new List<FontMapping>();
        foreach (var (code, character) in encoding.Codes.OrderBy(c => c.Key))
        {
            original.TryDecode(code, out var before);
            if (code >= 0x1000)
            {
                // Control codes (and the space command) keep their meaning.
                if (character != before)
                    throw new InvalidDataException($"Perfil {name}: \"{character}\" usa o código de comando 0x{code:X4}.");
                continue;
            }
            var hasPosition = font.TryGetValue(character, out var position);
            var hadPosition = originalFont.TryGetValue(character, out var originalPosition);
            // Unchanged character at the same place (or absent from both layouts, as v2 compares).
            if (character == before && hasPosition == hadPosition && position == originalPosition)
                continue;
            if (!hasPosition || position >= MaxFontPosition || code < 0x20)
                throw new InvalidDataException($"Perfil {name}: \"{character}\" não tem posição suportada na fonte (0000–04FF).");
            mappings.Add(new FontMapping(character, code, position));
        }

        var pages = new Dictionary<int, RgbaImage>();
        foreach (var file in pageFiles)
        {
            using var stream = open(file);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var page = Png.Decode(memory.ToArray());
            if (page.Width != 256 || page.Height != 256)
                throw new InvalidDataException($"Perfil {name}: {file} precisa ter 256x256 pixels.");
            pages[int.Parse(Path.GetFileNameWithoutExtension(file))] = page;
        }

        List<GlyphRecipe> recipes = [];
        try
        {
            using var stream = open("glyphs.json");
            recipes = JsonSerializer.Deserialize<List<GlyphRecipe>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (FileNotFoundException)
        {
        }
        foreach (var recipe in recipes) Apply(name, recipe, pages);

        foreach (var mapping in mappings)
        {
            var page = PageOf(name, pages, mapping.FontPosition, RegularFirstPage);
            if (Glyph.Bounds(page, mapping.FontPosition) is null)
                throw new InvalidDataException($"Perfil {name}: glifo vazio para \"{mapping.Character}\" na posição {mapping.FontPosition:X4}.");
        }
        return new FontProfile(name, encoding, mappings, pages);
    }

    /// <summary>font.json: { page: 16 rows of 16 characters }; the first position of a character wins.</summary>
    private static Dictionary<string, int> LoadFontLayout(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        var layout = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var page in document.RootElement.EnumerateObject().OrderBy(p => int.Parse(p.Name)))
        {
            var number = int.Parse(page.Name);
            var y = 0;
            foreach (var row in page.Value.EnumerateArray())
            {
                var x = 0;
                foreach (var cell in row.EnumerateArray())
                {
                    var character = cell.GetString() ?? "";
                    if (character.Length > 0) layout.TryAdd(character, number * 256 + y * 16 + x);
                    x++;
                }
                y++;
            }
        }
        return layout;
    }

    public sealed record GlyphRecipe(int Target, int Base, int Accent, int AccentRows);

    /// <summary>
    /// Builds a glyph: the top <c>AccentRows</c> rows come from the accent
    /// glyph and the rest from the base letter, in the normal and narrow pages.
    /// </summary>
    private static void Apply(string name, GlyphRecipe recipe, Dictionary<int, RgbaImage> pages)
    {
        if (new[] { recipe.Target, recipe.Base, recipe.Accent }.Any(n => n is < 0 or >= 0x200) || recipe.AccentRows is < 1 or > 5)
            throw new InvalidDataException($"Perfil {name}: receita de glifo inválida (posições 0000–01FF, 1–5 linhas de acento).");
        foreach (var first in new[] { RegularFirstPage, NarrowFirstPage })
        {
            var target = PageOf(name, pages, recipe.Target, first);
            var glyph = new byte[16 * 16 * 4];
            for (var y = 0; y < 16; y++)
            {
                var (code, source) = y < recipe.AccentRows
                    ? (recipe.Accent, PageOf(name, pages, recipe.Accent, first))
                    : (recipe.Base, PageOf(name, pages, recipe.Base, first));
                for (var x = 0; x < 16; x++)
                    source.Pixels.AsSpan(Glyph.Offset(source, code, x, y), 4).CopyTo(glyph.AsSpan((y * 16 + x) * 4));
            }
            for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                glyph.AsSpan((y * 16 + x) * 4, 4).CopyTo(target.Pixels.AsSpan(Glyph.Offset(target, recipe.Target, x, y)));
        }
    }

    public static RgbaImage PageOf(string name, IReadOnlyDictionary<int, RgbaImage> pages, int position, int firstPage) =>
        pages.TryGetValue(firstPage + (position >> 8), out var page)
            ? page
            : throw new InvalidDataException($"Perfil {name}: falta a página {firstPage + (position >> 8)}.png.");
}

/// <summary>A glyph is a 16x16 cell of a 256x256 page, 16 cells per row.</summary>
public static class Glyph
{
    public static int Offset(RgbaImage page, int position, int x, int y) =>
        page.Offset((position & 15) * 16 + x, (position >> 4 & 15) * 16 + y);

    /// <summary>First visible column and visible width of a glyph, or null when it is empty.</summary>
    public static (int Left, int Width)? Bounds(RgbaImage page, int position)
    {
        int left = 16, right = -1;
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
        {
            if (page.Pixels[Offset(page, position, x, y) + 3] == 0) continue;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
        }
        return right < left ? null : (left, right - left + 1);
    }
}
