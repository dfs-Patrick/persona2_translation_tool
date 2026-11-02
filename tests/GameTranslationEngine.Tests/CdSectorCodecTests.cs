using GameTranslationEngine.Core.Iso;

namespace GameTranslationEngine.Tests;

public class CdSectorCodecTests
{
    private static string? FindSmtBin()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "lab", "Shin Megami Tensei (Japan).bin");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    [Fact]
    public void Codec_ReproducesTheEdcAndEccOfRealSectors()
    {
        var bin = FindSmtBin();
        if (bin is null) return;
        using var stream = File.OpenRead(bin);
        var sector = new byte[2352];
        var form1 = 0;
        var form2 = 0;
        // System area, executable, DATA.BIN and STR streams, all Form 1 on this disc.
        foreach (var lba in new long[] { 16, 17, 23, 24, 100, 360, 361, 5000, 16547, 17198, 17300, 19486 })
        {
            stream.Seek(lba * 2352, SeekOrigin.Begin);
            stream.ReadExactly(sector);
            if (sector[15] != 2) continue;
            Assert.True(CdSectorCodec.IsValid(sector), $"setor {lba}");
            if (CdSectorCodec.IsForm2(sector)) form2++; else form1++;
        }
        Assert.True(form1 >= 10);
        Assert.Equal(0, form2);
    }

    [Fact]
    public void Patcher_ChangesOnlyTheTouchedSectorsAndKeepsThemValid()
    {
        var bin = FindSmtBin();
        if (bin is null) return;
        var cue = Path.ChangeExtension(bin, ".cue");
        using var source = (RawSectorSource)DiscImage.Open(cue);
        var output = Path.Combine(Path.GetTempPath(), $"smt-patch-{Guid.NewGuid():N}.bin");
        try
        {
            const long offset = 360L * 2048 + 2040;
            RawDiscPatcher.Write(source, output, [(offset, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16])]);

            using var original = File.OpenRead(bin);
            using var patched = File.OpenRead(output);
            Assert.Equal(original.Length, patched.Length);
            var a = new byte[2352];
            var b = new byte[2352];
            foreach (var lba in new long[] { 359, 360, 361, 362 })
            {
                original.Seek(lba * 2352, SeekOrigin.Begin); original.ReadExactly(a);
                patched.Seek(lba * 2352, SeekOrigin.Begin); patched.ReadExactly(b);
                Assert.Equal(lba is 360 or 361, !a.AsSpan().SequenceEqual(b));
                Assert.True(CdSectorCodec.IsValid(b), $"setor {lba}");
            }
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, b.Length > 0 ? ReadUser(patched, 360, 2040, 8) : []);
            Assert.Equal(new byte[] { 9, 10, 11, 12, 13, 14, 15, 16 }, ReadUser(patched, 361, 0, 8));
        }
        finally
        {
            File.Delete(output);
        }
    }

    private static byte[] ReadUser(Stream stream, long lba, int offset, int length)
    {
        var data = new byte[length];
        stream.Seek(lba * 2352 + 24 + offset, SeekOrigin.Begin);
        stream.ReadExactly(data);
        return data;
    }
}
