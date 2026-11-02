using System.Buffers.Binary;
using System.Text.Json;
using GameTranslationEngine.Core.GameData;

namespace GameTranslationEngine.Core.Smt;

/// <summary>
/// Latin glyphs (data/smt/latin.json) drawn over the font's blank tokens, so that
/// Portuguese text can be written with the game's 12×12 font.
/// </summary>
public sealed class SmtLatinFont
{
    /// <summary>DATA.BIN entry that holds the font TIMs (RLE container).</summary>
    public const int FontEntryIndex = 10;
    private const int GlyphSize = 12;
    private const int GlyphsPerRow = 21;
    private const int GlyphsPerPage = GlyphsPerRow * GlyphsPerRow;

    /// <summary>Blank token drawn as the narrow Latin space.</summary>
    public const int SpaceToken = 1555;
    private const int SpaceWidth = 4;

    private SmtLatinFont(IReadOnlyDictionary<string, int> chars, IReadOnlyDictionary<int, string[]> glyphs)
    {
        var withSpace = chars.ToDictionary();
        withSpace[" "] = SpaceToken;
        Chars = withSpace;
        // Glyphs are drawn left-aligned in their cell; the advance is the ink width plus one pixel.
        var aligned = new Dictionary<int, string[]>();
        var advances = new Dictionary<int, int> { [SpaceToken] = SpaceWidth };
        foreach (var (token, rows) in glyphs)
        {
            var left = GlyphSize;
            var right = -1;
            foreach (var row in rows)
                for (var x = 0; x < GlyphSize; x++)
                    if (row[x] != '0') { left = Math.Min(left, x); right = Math.Max(right, x); }
            if (right < 0) { aligned[token] = rows; advances[token] = SpaceWidth; continue; }
            aligned[token] = [.. rows.Select(r => r[left..].PadRight(GlyphSize, '0'))];
            advances[token] = Math.Min(GlyphSize, right - left + 2);
        }
        aligned[SpaceToken] = [.. Enumerable.Repeat(new string('0', GlyphSize), GlyphSize)];
        Glyphs = aligned;
        Advances = advances;
    }

    /// <summary>Token → horizontal advance in pixels (the variable-width patch reads it).</summary>
    public IReadOnlyDictionary<int, int> Advances { get; }

    /// <summary>Character → token that draws it.</summary>
    public IReadOnlyDictionary<string, int> Chars { get; }

    /// <summary>Token → 12 rows of 12 digits (0 transparent, 1 shadow, 2 stroke).</summary>
    public IReadOnlyDictionary<int, string[]> Glyphs { get; }

    public static SmtLatinFont Load()
    {
        using var json = GameResources.ReadJson("smt/latin.json");
        var chars = json.RootElement.GetProperty("chars").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetInt32());
        var glyphs = json.RootElement.GetProperty("glyphs").EnumerateObject()
            .ToDictionary(p => int.Parse(p.Name), p => p.Value.EnumerateArray().Select(r => r.GetString()!).ToArray());
        return new SmtLatinFont(chars, glyphs);
    }

    /// <summary>
    /// Draws the glyphs into the decoded font container (u32 n, n+1 offsets; the
    /// font pages are TIMs 1 and 2, each holding two overlapped 2-bit pages).
    /// </summary>
    public void Install(byte[] container)
    {
        var count = BinaryPrimitives.ReadUInt32LittleEndian(container);
        if (count < 3) throw new InvalidDataException("O contêiner da fonte não tem as páginas esperadas.");
        var pages = new (int Data, int Width)[2];
        for (var i = 0; i < 2; i++)
        {
            var tim = (int)BinaryPrimitives.ReadUInt32LittleEndian(container.AsSpan(4 + 4 * (i + 1)));
            var image = tim + 8 + (int)BinaryPrimitives.ReadUInt32LittleEndian(container.AsSpan(tim + 8));
            pages[i] = (image + 12, BinaryPrimitives.ReadUInt16LittleEndian(container.AsSpan(image + 8)) * 4);
        }

        foreach (var (token, rows) in Glyphs)
        {
            var page = (token - 1) / GlyphsPerPage;
            var index = (token - 1) % GlyphsPerPage;
            var (data, width) = pages[page / 2];
            var layer = page % 2;
            var originX = index % GlyphsPerRow * GlyphSize;
            var originY = index / GlyphsPerRow * GlyphSize;
            for (var y = 0; y < GlyphSize; y++)
            for (var x = 0; x < GlyphSize; x++)
            {
                var position = (originY + y) * width + originX + x;
                var shift = (position & 1) * 4 + layer * 2;
                ref var cell = ref container[data + position / 2];
                cell = (byte)((cell & ~(3 << shift)) | ((rows[y][x] - '0') << shift));
            }
        }
        InstallSmall(container);
    }

    /// <summary>First Latin token: the 10-pixel menu font keeps glyph k = token − FirstToken.</summary>
    public int FirstToken => Glyphs.Keys.Min();

    /// <summary>
    /// The menus' 10 × 10 font (TIM 0, glyphs from y = 130, 21 per row) has no lowercase
    /// or accents; its second 2-bit layer is empty, so the Latin glyphs go there, in
    /// cell token − <see cref="FirstToken"/>. Rows 1–10 of each 12-pixel glyph are kept.
    /// </summary>
    private void InstallSmall(byte[] container)
    {
        const int Small = 10, Top = 130;
        var tim = (int)BinaryPrimitives.ReadUInt32LittleEndian(container.AsSpan(4));
        var image = tim + 8 + (int)BinaryPrimitives.ReadUInt32LittleEndian(container.AsSpan(tim + 8));
        var data = image + 12;
        var width = BinaryPrimitives.ReadUInt16LittleEndian(container.AsSpan(image + 8)) * 4;
        var height = BinaryPrimitives.ReadUInt16LittleEndian(container.AsSpan(image + 10));
        var first = FirstToken;
        foreach (var (token, rows) in Glyphs)
        {
            var index = token - first;
            var originX = index % GlyphsPerRow * Small;
            var originY = Top + index / GlyphsPerRow * Small;
            if (originY + Small > height) throw new InvalidDataException("Os glifos latinos não cabem na fonte dos menus.");
            for (var y = 0; y < Small; y++)
            for (var x = 0; x < Small; x++)
            {
                var position = (originY + y) * width + originX + x;
                var shift = (position & 1) * 4 + 2;
                ref var cell = ref container[data + position / 2];
                cell = (byte)((cell & ~(3 << shift)) | ((rows[y + 1][x] - '0') << shift));
            }
        }
    }
}
