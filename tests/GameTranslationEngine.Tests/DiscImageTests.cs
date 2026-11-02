using GameTranslationEngine.Core.Engines;
using GameTranslationEngine.Core.IO;
using GameTranslationEngine.Core.Iso;

namespace GameTranslationEngine.Tests;

public class DiscImageTests
{
    private static string? FindSmtCue()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "lab", "Shin Megami Tensei (Japan).cue");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    [Fact]
    public void RawSectorSource_ExposesOnlyTheUserData()
    {
        var raw = new byte[3 * DiscImage.RawSectorSize];
        for (var s = 0; s < 3; s++)
            for (var i = 0; i < DiscImage.RawSectorSize; i++)
                raw[s * DiscImage.RawSectorSize + i] = i is >= 24 and < 24 + 2048 ? (byte)(s + 1) : (byte)0xEE;

        var source = new RawSectorSource(new MemoryByteSource(raw), 24);

        Assert.Equal(3 * 2048, source.Length);
        var window = source.ReadBytes(2048 - 2, 4);
        Assert.Equal(new byte[] { 1, 1, 2, 2 }, window);
    }

    [Fact]
    public void Cue_ResolvesTheBinNextToIt()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "jogo (x).bin"), new byte[DiscImage.RawSectorSize * 2]);
            var cue = Path.Combine(folder.FullName, "jogo.cue");
            File.WriteAllText(cue, "FILE \"jogo (x).bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n");

            Assert.Equal(Path.Combine(folder.FullName, "jogo (x).bin"), DiscImage.ResolveDataFile(cue));
            Assert.Equal(DiscImage.RawSectorSize * 2, DiscImage.PhysicalSize(cue));
            using var source = (IDisposable)DiscImage.Open(cue);
            Assert.Equal(2 * 2048, ((IByteSource)source).Length);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public void ShinMegamiTensei_IsIdentifiedFromTheCue()
    {
        var cue = FindSmtCue();
        if (cue is null) return;

        using var iso = IsoIndex.Read(DiscImage.Open(cue));
        var identity = IsoVerifier.Identify(iso);

        Assert.Equal(SupportedGames.ShinMegamiTenseiJp, identity.GameId);
        Assert.True(new ShinMegamiTenseiEngine().Supports(identity));
        Assert.NotNull(iso.Find("DATA.BIN"));
        Assert.Equal(33153024u, iso.Get("DATA.BIN").Size);
    }
}
