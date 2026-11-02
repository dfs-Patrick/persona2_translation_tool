using GameTranslationEngine.Core.Cpk;
using GameTranslationEngine.Core.Iso;

namespace GameTranslationEngine.Tests;

public class IsoTests
{
    [IsoFact]
    public void A1_identifies_innocent_sin_us()
    {
        var identity = IsoVerifier.Identify(TestIso.Index!);
        Assert.Equal(SupportedGames.InnocentSinUs, identity.GameId);
        Assert.True(new GameTranslationEngine.Core.Engines.InnocentSinEngine().Supports(identity));
    }

    [IsoFact]
    public void A2_indexes_the_known_layout()
    {
        var iso = TestIso.Index!;
        Assert.NotNull(iso.Find("UMD_DATA.BIN"));
        Assert.NotNull(iso.Find("PSP_GAME/SYSDIR/EBOOT.BIN"));
        var cpk = iso.Get("PSP_GAME/USRDIR/pack/P2PT_ALL.cpk");
        Assert.False(cpk.IsDirectory);
        Assert.True(cpk.Offset + cpk.Size <= iso.Source.Length);
        Assert.Equal(19, iso.Get("PSP_GAME/USRDIR/pack/movie").Children.Count);
    }

    [IsoFact]
    public void A3_indexes_the_135_cpk_entries()
    {
        var iso = TestIso.Index!;
        var cpk = CpkIndex.Read(iso.Open("PSP_GAME/USRDIR/pack/P2PT_ALL.cpk"));
        Assert.Equal(135, cpk.Entries.Count);
        foreach (var name in new[] { "event.bin", "DNGSCN.BNP", "syscg.bin", "TM_EVE.BNP" })
            Assert.NotNull(cpk.FindByName(name));
        Assert.All(cpk.Entries, e => Assert.True(e.Offset + e.StoredSize <= cpk.Source.Length, e.FullName));
    }

    [IsoFact]
    public void A3_reads_every_cpk_entry_with_the_expected_size()
    {
        var iso = TestIso.Index!;
        var cpk = CpkIndex.Read(iso.Open("PSP_GAME/USRDIR/pack/P2PT_ALL.cpk"));
        foreach (var entry in cpk.Entries)
            Assert.Equal(entry.ExtractedSize, cpk.ReadEntry(entry).Length);
    }
}
