using GameTranslationEngine.Core.Images;

namespace GameTranslationEngine.Core.Fonts;

/// <summary>
/// C4 · Puts the glyphs a profile adds into a page of the game, and nothing else.
///
/// The game cuts each glyph out of its 16x16 cell with the width table of the
/// EBOOT (first column and width), which is only rewritten for the glyphs the
/// profile adds. The PT-BR pages inherited from v2 have every glyph moved
/// (regular font 1 px up, narrow font 3 px right): with the original widths,
/// the first columns of the capitals fell outside the cut, which is why the
/// location banner showed "{even" for "Seven". So the page is the game's own,
/// and only the cells of the added characters come from the profile, moved
/// back by the offset of the profile page.
/// </summary>
public static class FontPageMerge
{
    private const int Cell = 16;
    private const int MaxOffset = 4;

    /// <summary>The game's page with the profile's cells <paramref name="cells"/> (0–255) copied in.</summary>
    public static RgbaImage Merge(RgbaImage original, RgbaImage profile, IReadOnlyCollection<int> cells)
    {
        var (dx, dy) = DetectOffset(original, profile, cells);
        var merged = original.Clone();
        foreach (var cell in cells)
        {
            int cx = cell % 16 * Cell, cy = cell / 16 * Cell;
            for (var y = 0; y < Cell; y++)
            for (var x = 0; x < Cell; x++)
            {
                int sx = x + dx, sy = y + dy;
                var target = merged.Offset(cx + x, cy + y);
                if (sx is >= 0 and < Cell && sy is >= 0 and < Cell)
                    Array.Copy(profile.Pixels, profile.Offset(cx + sx, cy + sy), merged.Pixels, target, 4);
                else
                    Array.Clear(merged.Pixels, target, 4);
            }
        }
        return merged;
    }

    /// <summary>
    /// How far the glyphs of <paramref name="profile"/> sit from the game's: the
    /// most common move of the glyph box over the cells both pages share.
    /// </summary>
    public static (int Dx, int Dy) DetectOffset(RgbaImage original, RgbaImage profile, IReadOnlyCollection<int> skip)
    {
        var votes = new Dictionary<(int, int), int>();
        for (var cell = 0; cell < 256; cell++)
        {
            if (skip.Contains(cell)) continue;
            if (Box(original, cell) is not { } a || Box(profile, cell) is not { } b) continue;
            // Only glyphs that keep their size: a redrawn glyph says nothing about the page.
            if (a.Right - a.Left != b.Right - b.Left || a.Bottom - a.Top != b.Bottom - b.Top) continue;
            var move = (b.Left - a.Left, b.Top - a.Top);
            votes[move] = votes.GetValueOrDefault(move) + 1;
        }
        if (votes.Count == 0) return (0, 0);
        var (dx, dy) = votes.MaxBy(v => v.Value).Key;
        return Math.Abs(dx) <= MaxOffset && Math.Abs(dy) <= MaxOffset ? (dx, dy) : (0, 0);
    }

    private static (int Left, int Right, int Top, int Bottom)? Box(RgbaImage image, int cell)
    {
        int cx = cell % 16 * Cell, cy = cell / 16 * Cell;
        int left = Cell, right = -1, top = Cell, bottom = -1;
        for (var y = 0; y < Cell; y++)
        for (var x = 0; x < Cell; x++)
        {
            if (image.Alpha(cx + x, cy + y) == 0) continue;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }
        return right < 0 ? null : (left, right, top, bottom);
    }
}
