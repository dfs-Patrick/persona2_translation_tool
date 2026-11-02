using GameTranslationEngine.Core.Engines;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Smt;

namespace GameTranslationEngine.Tests;

public class SmtImageTests
{
    [Fact]
    public void Rle_DecodesRepeatsWithAnyEscapeByte()
    {
        // size 6, escape 0x2c, "AB", 3×'C', "D"
        byte[] data = [6, 0, 0, 0, 0x2c, (byte)'A', (byte)'B', 0x2c, (byte)'C', 3, (byte)'D'];
        Assert.False(SmtRle.TryDecode(data, out _));
        byte[] ok = [8, 0, 0, 0, 0x2c, (byte)'A', (byte)'B', 0x2c, (byte)'C', 5, (byte)'D'];
        Assert.True(SmtRle.TryDecode(ok, out var result));
        Assert.Equal("ABCCCCCD"u8.ToArray(), result);
    }

    [Fact]
    public void Tim_Reads4BitImageWithPalette()
    {
        var tim = new List<byte> { 0x10, 0, 0, 0, 0x08, 0, 0, 0 };
        tim.AddRange([12 + 32, 0, 0, 0, 0, 0, 0, 0, 16, 0, 1, 0]);
        for (var i = 0; i < 16; i++) tim.AddRange(i == 1 ? [0x1F, 0x00] : [0, 0]);
        tim.AddRange([12 + 4, 0, 0, 0, 0, 0, 0, 0, 2, 0, 1, 0]);
        tim.AddRange([0x10, 0x01, 0x01, 0x10]);

        Assert.True(TimImage.LooksLikeTim(tim.ToArray()));
        var image = TimImage.Read(tim.ToArray());
        Assert.Equal((8, 1, 4), (image.Width, image.Height, image.BitsPerPixel));
        var rgba = image.ToRgba();
        Assert.Equal(0, rgba.Alpha(0, 0));
        Assert.Equal((255, 0, 0, 255), (rgba.Pixels[rgba.Offset(1, 0)], rgba.Pixels[rgba.Offset(1, 0) + 1],
            rgba.Pixels[rgba.Offset(1, 0) + 2], rgba.Pixels[rgba.Offset(1, 0) + 3]));
    }

    [Fact]
    public void RealDisc_FindsTheGameTextures()
    {
        string? cue = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null && cue is null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "lab", "Shin Megami Tensei (Japan).cue");
            if (File.Exists(candidate)) cue = candidate;
        }
        if (cue is null) return;

        using var iso = IsoIndex.Read(DiscImage.Open(cue));
        var images = new ShinMegamiTenseiEngine().DiscoverImages(iso);

        Assert.True(images.Count > 600, $"images={images.Count}");
        Assert.Contains(images, i => i.Image.Width == 256 && i.Image.Height == 256);
        Assert.All(images, i => Assert.Equal(i.Image.Width * i.Image.Height * 4, i.Pixels.Pixels.Length));
    }
}
