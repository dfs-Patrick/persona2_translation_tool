using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Smt;

namespace GameTranslationEngine.Tests;

public class SmtTextTests
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
    public void RealDisc_VariableWidthPatchFitsAndRedirectsTheDrawers()
    {
        var cue = FindSmtCue();
        if (cue is null) return;
        using var iso = IsoIndex.Read(DiscImage.Open(cue));
        var entry = iso.Find(SmtDataArchive.ExecutablePath)!;
        var exe = GameTranslationEngine.Core.IO.ByteSourceExtensions.ReadBytes(iso.Open(SmtDataArchive.ExecutablePath), 0, (int)entry.Size);
        Assert.False(SmtTextPatch.IsApplied(exe));
        var (offset, bytes) = SmtTextPatch.Patch(exe, SmtLatinFont.Load().Advances);
        var patched = exe.ToArray();
        bytes.CopyTo(patched, offset);
        Assert.True(SmtTextPatch.IsApplied(patched));
        int Word(byte[] data, uint address) => BitConverter.ToInt32(data, (int)(address - 0x80010000) + 0x800);
        // The message renderer and both menu drawers now call into the patch.
        Assert.NotEqual(Word(exe, 0x80047bbc), Word(patched, 0x80047bbc));
        Assert.NotEqual(Word(exe, 0x80021fc8), Word(patched, 0x80021fc8));
        Assert.NotEqual(Word(exe, 0x80022264), Word(patched, 0x80022264));
        // Applying twice is refused: the original words are checked first.
        Assert.Throws<InvalidDataException>(() => SmtTextPatch.Apply(patched, SmtLatinFont.Load().Advances));
    }

    [Fact]
    public void RealDisc_KeyboardBecomesLatin()
    {
        var cue = FindSmtCue();
        if (cue is null) return;
        using var iso = IsoIndex.Read(DiscImage.Open(cue));
        var archive = SmtDataArchive.Open(iso);
        var map = SmtCharMap.Load();
        var pages = SmtKeyboard.TranslatePages(archive.Read(archive.Entries[SmtKeyboard.PagesEntry]), map);
        map.TryEncode("A", out var a);
        Assert.Equal(a, BitConverter.ToUInt16(pages, 0x0e));
        Assert.Equal(0, BitConverter.ToUInt16(pages, 0x0e + 100));   // screen terminator kept
        var labels = SmtKeyboard.TranslateLabels(archive.Read(archive.Entries[SmtKeyboard.OverlayEntry]), map);
        Assert.Equal(a, BitConverter.ToUInt16(labels, 0x2c));
    }

    [Fact]
    public void CharMap_DecodesKana()
    {
        var map = SmtCharMap.Load();
        Assert.True(map.TokenCount > 1400);
        Assert.Equal("あ", map.Decode(1));
        Assert.True(map.TryEncode("あ", out var token));
        Assert.Equal(1, token);
    }

    [Fact]
    public void Codec_RoundTripsGlyphsControlCodesAndUnknownTokens()
    {
        var map = SmtCharMap.Load();
        ushort[] words = [1, 2, 0x800, 0x816, 3, 0x58a, 0x2f];
        var text = SmtMessageCodec.Decode(words, map);

        Assert.Contains("[c816]", text);
        Assert.Contains("\n", text);
        Assert.Equal(words, SmtMessageCodec.Encode(text, map));
    }

    [Fact]
    public void Codec_RejectsCharactersMissingFromTheFont()
    {
        Assert.Throws<InvalidDataException>(() => SmtMessageCodec.Encode("Ω", SmtCharMap.Load()));
    }

    [Fact]
    public void RealDisc_FontEncodesIdenticallyAndLatinGlyphsFit()
    {
        var cue = FindSmtCue();
        if (cue is null) return;
        using var iso = IsoIndex.Read(DiscImage.Open(cue));
        var archive = SmtDataArchive.Open(iso);
        var entry = archive.Entries[SmtLatinFont.FontEntryIndex];
        var original = archive.Read(entry);
        Assert.True(SmtRle.TryDecode(original, out var container));
        Assert.Equal(original.Where((_, i) => i < 85163), SmtRle.Encode(container, original[4]));

        var font = SmtLatinFont.Load();
        var changed = (byte[])container.Clone();
        font.Install(changed);
        var packed = SmtRle.Encode(changed, original[4]);
        Assert.True(packed.Length <= entry.Length);
        Assert.True(SmtRle.TryDecode(packed, out var again));
        Assert.Equal(changed, again);
        Assert.NotEqual(container, changed);
        Assert.Equal(container.Length, changed.Length);
        var token = font.Chars["A"];
        var page = (token - 1) / 441;
        var timOffset = (int)BitConverter.ToUInt32(changed, 4 + 4 * (page / 2 + 1));
        var image = timOffset + 8 + (int)BitConverter.ToUInt32(changed, timOffset + 8);
        var width = BitConverter.ToUInt16(changed, image + 8) * 4;
        var cell = (token - 1) % 441;
        for (var y = 0; y < 12; y++)
        for (var x = 0; x < 12; x++)
        {
            var position = (cell / 21 * 12 + y) * width + cell % 21 * 12 + x;
            var nibble = changed[image + 12 + position / 2] >> ((position & 1) * 4);
            Assert.Equal(font.Glyphs[token][y][x] - '0', (nibble >> (page % 2 * 2)) & 3);
        }
        Assert.True(SmtCharMap.Load().TryEncode("ç", out _));
        Assert.True(SmtCharMap.Load().TryEncode(" ", out var space));
        Assert.Equal(SmtLatinFont.SpaceToken, space);   // narrow Latin space, not the full-width 0x2f
        Assert.True(font.Advances[font.Chars["i"]] < font.Advances[font.Chars["M"]]);
    }

    [Fact]
    public void RealDisc_RebuildWithoutChangesIsIdentical_AndTableOffsetsFollowEdits()
    {
        var cue = FindSmtCue();
        if (cue is null) return;
        using var iso = IsoIndex.Read(DiscImage.Open(cue));
        var archive = SmtDataArchive.Open(iso);
        var rebuilt = 0;
        var grown = 0;
        foreach (var entry in archive.Entries)
        {
            var bytes = archive.Read(entry);
            var script = SmtScript.Parse(bytes);
            if (script is null || script.Messages.Count == 0) continue;
            Assert.Equal(bytes, script.Rebuild(bytes, new Dictionary<int, ushort[]>()));
            rebuilt++;

            // Making the first message 3 words longer moves every later start by 3.
            var first = script.Messages[0];
            byte[] edited;
            try
            {
                edited = script.Rebuild(bytes, new Dictionary<int, ushort[]>
                {
                    [first.Start] = [.. script.Words.Skip(first.Start).Take(first.Length), (ushort)1, (ushort)1, (ushort)1],
                });
            }
            catch (InvalidOperationException)
            {
                continue; // script with no spare room
            }
            grown++;
            var again = SmtScript.Parse(edited)!;
            Assert.Equal(script.Messages.Count, again.Messages.Count);
            Assert.Equal(first.Length + 3, again.Messages[0].Length);
        }
        Assert.True(rebuilt >= 30);
        Assert.True(grown > 10, $"grown={grown}");
    }

    [Fact]
    public void RealDisc_AllScriptMessagesRoundTrip()
    {
        var cue = FindSmtCue();
        if (cue is null) return;
        using var iso = IsoIndex.Read(DiscImage.Open(cue));
        var archive = SmtDataArchive.Open(iso);
        var map = SmtCharMap.Load();
        var scripts = 0;
        var messages = 0;
        foreach (var entry in archive.Entries)
        {
            var script = SmtScript.Parse(archive.Read(entry));
            if (script is null) continue;
            scripts++;
            foreach (var (start, length) in script.Messages)
            {
                var words = script.Words.Skip(start).Take(length).ToArray();
                Assert.Equal(words, SmtMessageCodec.Encode(SmtMessageCodec.Decode(words, map), map));
                messages++;
            }
        }
        Assert.True(archive.Entries.Count > 1200);
        Assert.True(scripts >= 30, $"scripts={scripts}, messages={messages}");
        Assert.True(messages > 1500);
    }

    [Fact]
    public void RealDisc_StringBanksParseRebuildIdenticallyAndRoundTrip()
    {
        var cue = FindSmtCue();
        if (cue is null) return;
        using var iso = IsoIndex.Read(DiscImage.Open(cue));
        var archive = SmtDataArchive.Open(iso);
        var map = SmtCharMap.Load();
        var strings = 0;
        foreach (var location in SmtStringBank.Locations)
        {
            var bytes = archive.Read(archive.Entries[location.Entry]);
            var bank = SmtStringBank.Parse(bytes, location);
            Assert.NotNull(bank);
            Assert.Equal(bytes, bank.Rebuild(bytes, location, new Dictionary<int, ushort[]>()));
            foreach (var (start, length) in bank.Strings)
            {
                var words = bank.Words.Skip(start).Take(length).ToArray();
                Assert.Equal(words, SmtMessageCodec.Encode(SmtMessageCodec.Decode(words, map), map));
                strings++;
            }

            var first = bank.Strings[0];
            byte[] grown;
            try
            {
                grown = bank.Rebuild(bytes, location, new Dictionary<int, ushort[]>
                {
                    [first.Start] = [.. bank.Words.Skip(first.Start).Take(first.Length), (ushort)1, (ushort)1],
                });
            }
            catch (InvalidOperationException) { continue; }
            var again = SmtStringBank.Parse(grown, location)!;
            Assert.Equal(bank.Strings.Count, again.Strings.Count);
            Assert.Equal(first.Length + 2, again.Strings[0].Length);
        }
        Assert.True(strings > 3000, $"strings={strings}");
    }
}
