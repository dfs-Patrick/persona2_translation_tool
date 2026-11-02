using GameTranslationEngine.Core.Archives;
using GameTranslationEngine.Core.Build;
using GameTranslationEngine.Core.Cpk;
using GameTranslationEngine.Core.Eboot;
using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.Fonts;
using GameTranslationEngine.Core.GameData;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.IO;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Scripts;
using GameTranslationEngine.Core.Tbf;
using GameTranslationEngine.Core.Text;

namespace GameTranslationEngine.Tests;

public class BuildTests
{
    private static readonly MessageContext Context =
        new(EventEncoding.LoadInnocentSinEnglish(), ConstantTables.LoadInnocentSin());

    public static TheoryData<string> Samples => ["random", "runs", "text", "empty-ish"];

    private static byte[] Sample(string kind)
    {
        var random = new Random(42);
        return kind switch
        {
            "random" => Enumerable.Range(0, 5000).Select(_ => (byte)random.Next(256)).ToArray(),
            "runs" => Enumerable.Range(0, 5000).Select(i => (byte)(i / 300)).ToArray(),
            "text" => System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("Tatsuya looked at the sea. ", 200))),
            _ => [7],
        };
    }

    [Theory, MemberData(nameof(Samples))]
    public void Compressors_round_trip(string kind)
    {
        var data = Sample(kind);
        Assert.Equal(data, Lzss.Decompress(Lzss.Compress(data), data.Length));
        var rle = Rle.Compress(data);
        if (rle.Length <= data.Length) Assert.Equal(data, Rle.Decompress(rle, data.Length));
        foreach (var compression in new[] { BnpCompression.None, BnpCompression.Rle, BnpCompression.Lzss })
        {
            var entry = BnpEntryCodec.Encode(BnpEntryType.Other, compression, 0x1234, data);
            var decoded = BnpEntryCodec.Decode(entry);
            Assert.Equal(data, decoded.Data);
            Assert.Equal(0x1234, decoded.Tag);
        }
    }

    [Fact]
    public void Gzip_keeps_the_original_header()
    {
        var data = System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("PSPCHECK ", 500)));
        // Header with FNAME, as in event.bin: 1F 8B 08 08 mtime(4) 00 0B "e0000.bin\0"
        byte[] header = [0x1F, 0x8B, 8, 8, 0x82, 0xB0, 0x0A, 0x4E, 0, 0x0B, .. "e0000.bin"u8, 0];
        var original = header.Concat(new byte[16]).ToArray();
        var packed = Gzip.Compress(data, original);
        Assert.Equal(header, packed[..header.Length]);
        Assert.Equal(data, Gzip.Decompress(packed));
    }

    [Fact]
    public void Lines_ending_with_colon_stay_inside_their_message()
    {
        // v2's split turns "> Notice:" into a key; compiling must join it back.
        var tbf = MessageFile.ToTbf("dir", "script.msg", "\nmsg_0:\n> Notice:\nNo running.[wait][end]\n\nmsg_1:\nBye[end]\n");
        Assert.Equal(["msg_0", "> Notice", "msg_1"], tbf.Entries.Select(e => e.Key));
        tbf.Entries[1].Translation = "Proibido correr.";
        var messages = MessageFile.ParseMessages(MessageFile.Render(tbf));
        Assert.Equal([("msg_0", "> Notice:\nProibido correr.[wait][end]"), ("msg_1", "Bye[end]")], messages);
    }

    [Fact]
    public void Compiler_keeps_untranslated_bytes_restores_items_and_reports_errors()
    {
        var item = Message.Parse("Got [item_with_type(item_5)]![wait]");
        var plain = Message.Parse("Hello[wait]");
        var source = new ExtractedText(
            new TextSource("efb", [new SourceStep("x.bin", "par", null, 1), new SourceStep("0", "efb", 0, null)]),
            "dir", "script.msg",
            [("msg_0", plain), ("msg_1", item), ("msg_2", plain)],
            [plain.EncodeBytes(Context), item.EncodeBytes(Context), plain.EncodeBytes(Context)]);
        var tbf = MessageFile.ToTbf("dir", "script.msg", source.MessageFileText);
        Assert.Contains("item_5", tbf.Entries[1].Original);
        tbf.Entries[1].Translation = "Recebeu [item_with_type(item_0)]!";   // as v2 exported it
        tbf.Entries[2].Translation = "Olá ☃";                               // no glyph for ☃

        var issues = new List<BuildIssue>();
        var compiled = new MessageCompiler(Context).Compile(source, tbf, issues);

        Assert.Same(source.OriginalBytes[0], compiled.Messages[0]);
        Assert.Equal("Recebeu [item_with_type(item_5)]![wait]", Message.Decode(compiled.Messages[1], Context, out _).ToText());
        Assert.Contains(issues, i => i is { Level: IssueLevel.Info, Message: "msg_1" });
        Assert.Contains(issues, i => i is { Level: IssueLevel.Error, Message: "msg_2" });
        Assert.Equal(2, compiled.Translated);
    }

    [IsoFact]
    public void Every_tbf_compiles_back_to_the_original_messages()
    {
        var texts = new TextExtractor(TestIso.Index!).ExtractAll();
        var different = new List<string>();
        foreach (var text in texts)
        {
            var tbf = MessageFile.ToTbf(text.OriginalDirectory, text.OriginalFilename, text.MessageFileText);
            var parsed = MessageFile.ParseMessages(MessageFile.Render(tbf, useTranslation: false));
            Assert.Equal(text.Messages.Select(m => m.Name), parsed.Select(p => p.Name));
            for (var i = 0; i < parsed.Count; i++)
            {
                var again = Message.Decode(Message.Parse(parsed[i].Text).EncodeBytes(Context), Context, out _);
                if (again.ToText() != text.Messages[i].Message.ToText()) different.Add($"{text.Source.TbfPath} {parsed[i].Name}");
            }
        }
        Assert.Empty(different);
    }

    [IsoFact]
    public void Containers_rebuilt_from_their_files_are_identical()
    {
        var iso = TestIso.Index!;
        var catalog = TextSourceCatalog.LoadInnocentSin();
        var cpk = CpkIndex.Read(iso.Open(catalog.CpkPath));
        foreach (var step in catalog.Sources.Select(s => s.Entry).DistinctBy(s => s.Name))
        {
            var data = cpk.ReadEntry(cpk.FindById(step.CpkId!.Value)!);
            switch (step.Type)
            {
                case "ear":
                    var ear = Ear.Split(data);
                    Assert.Equal(data, Ear.Build(ear.Select(e => e.Data).ToList(), ear[0].Offset).Data);
                    foreach (var child in ear)
                    {
                        var par = Gzip.Decompress(child.Data);
                        Assert.Equal(par, Par.Build(Par.Split(par).Select(e => e.Data).ToList()).Data);
                    }
                    break;
                case "par":
                    Assert.Equal(data, Par.Build(Par.Split(data).Select(e => e.Data).ToList()).Data);
                    break;
                default:
                    var bnp = Bnp.Split(data);
                    Assert.Equal(data, Bnp.Build(bnp.Select(e => e.Data).ToList(), bnp.Select(e => e.Offset % 2048 == 0).ToList()).Data);
                    break;
            }
        }
    }

    [IsoFact]
    public void Scripts_rebuilt_with_their_own_messages_are_identical()
    {
        var iso = TestIso.Index!;
        var catalog = TextSourceCatalog.LoadInnocentSin();
        var cpk = CpkIndex.Read(iso.Open(catalog.CpkPath));
        var ear = Ear.Split(cpk.ReadEntry(cpk.FindById(6000)!));
        foreach (var source in catalog.Sources.Where(s => s.Entry.Name == "event.bin"))
        {
            var par = Par.Split(Gzip.Decompress(ear[source.Chain[1].Index!.Value].Data));
            var efb = par[source.Chain[2].Index!.Value].Data.ToArray();
            var script = EventScript.Read(efb, Context);
            var section = efb[script.Layout.MessageSection..];
            var messages = script.Messages.Select(m => section[m.Offset..(m.Offset + m.Length)]).ToList();
            Assert.Equal(efb, script.WithMessages(efb, messages));
        }
    }

    [IsoFact]
    public void Eboot_decrypts_and_its_tables_describe_the_original_containers()
    {
        var iso = TestIso.Index!;
        var elf = ElfImage.Parse(PrxDecrypter.Decrypt(iso.Open(TranslationBuilder.EbootPath).ReadAll()));
        var patcher = new EbootPatcher(elf);
        var cpk = CpkIndex.Read(iso.Open(TextSourceCatalog.LoadInnocentSin().CpkPath));
        foreach (var table in EbootTable.LoadInnocentSin())
        {
            var data = cpk.ReadEntry(cpk.FindByName(table.File)!);
            var layout = (table.Format == "ear" ? Ear.Split(data) : Bnp.Split(data))
                .Select(e => new EntryLayout(e.Offset, e.Data.Length)).ToList();
            Assert.True(patcher.Matches(table, layout), table.File);
        }
        var cpkFile = iso.Get("PSP_GAME/USRDIR/pack/P2PT_ALL.cpk");
        Assert.Single(elf.FindInSections(System.Text.Encoding.ASCII.GetBytes(EbootPatcher.DiscPath(cpkFile.Lba, cpkFile.Size) + "\0")));
    }

    [IsoFact]
    public void Translated_project_builds_a_verified_iso()
    {
        var root = Path.Combine(Path.GetTempPath(), "p2tool-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            var messages = Path.Combine(root, "messages");
            TranslationArea.Generate(messages, new TextExtractor(TestIso.Index!).ExtractAll());
            // One file of each kind; e0000 grows a lot, pushing every later event forward.
            string[] files = ["event.bin/e0000.tbf", "event.bin/e0001.tbf", "DNGSCN.BNP/0_0000.tbf", "F_BE.BNP/0_0000.tbf",
                "MMAP01.BNP/25_0800.tbf", "CD_SHOP.BIN/0.tbf", "P_DEMO.BNP/pdemo.tbf"];
            foreach (var file in files)
            {
                var path = Path.Combine(messages, file);
                var tbf = TbfDocument.Load(path);
                foreach (var entry in tbf.Entries.Take(file.EndsWith("e0000.tbf") ? 1000 : 2))
                    entry.Translation = (file.EndsWith("e0000.tbf") ? string.Concat(Enumerable.Repeat("Texto bem mais longo. ", 8)) : "Ação é ótima! ") + entry.Original;
                tbf.Save(path);
            }

            var output = Path.Combine(root, "traduzido.iso");
            var font = FontProfile.LoadPortuguese();
            var report = new TranslationBuilder(TestIso.Path!, messages, font).Build(output);

            Assert.True(report.Succeeded, string.Join("\n", report.Issues));
            Assert.Equal(output, report.OutputPath);
            Assert.Equal(["CD_SHOP.BIN", "DNGSCN.BNP", "event.bin", "F_BE.BNP", "MMAP01.BNP", "P_DEMO.BNP"], report.RebuiltEntries);
            using var built = IsoIndex.Read(new FileByteSource(output));
            var withFont = new MessageContext(font.Encoding, ConstantTables.LoadInnocentSin());
            var text = new TextExtractor(built, context: withFont).ExtractAll().First(t => t.Source.TbfPath == "event.bin/e0001.tbf");
            Assert.StartsWith("Ação é ótima! ", text.Messages[0].Message.ToText());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Portuguese_profile_adds_the_accented_letters()
    {
        var font = FontProfile.LoadPortuguese();
        // 30 accented letters, plus " _ ° (glyphs the game has without a code) and º ª (drawn for PT-BR).
        Assert.Equal(35, font.Mappings.Count);
        Assert.All("áàâãçéêíóôõúÁÀÂÃÇÉÊÍÓÔÕÚ\"_°ºª", c => Assert.True(font.Encoding.CanEncode(c.ToString()), c.ToString()));
        Assert.Equal([7, 8, 12, 13], font.Pages.Keys.Order());
        Assert.All(font.Mappings, m => Assert.NotNull(Glyph.Bounds(font.Pages[7 + (m.FontPosition >> 8)], m.FontPosition)));
        Assert.All(font.Mappings.Where(m => m.FontPosition < 0x11F),
            m => Assert.NotNull(Glyph.Bounds(font.Pages[12 + (m.FontPosition >> 8)], m.FontPosition)));
    }

    [Fact]
    public void Png_round_trips()
    {
        var image = new RgbaImage(3, 2);
        for (var i = 0; i < image.Pixels.Length; i++) image.Pixels[i] = (byte)(i * 11);
        Assert.Equal(image.Pixels, Png.Decode(Png.Encode(image)).Pixels);
    }

    [IsoFact]
    public void Font_pages_keep_the_size_of_syscg_and_the_eboot_gets_the_glyphs()
    {
        var iso = TestIso.Index!;
        var cpk = CpkIndex.Read(iso.Open(TextSourceCatalog.LoadInnocentSin().CpkPath));
        var tables = FontInstaller.Tables.LoadInnocentSin();
        var syscg = cpk.ReadEntry(cpk.FindByName(tables.PagesFile)!);
        var files = Par.Split(syscg);
        var page = GimImage.Read(files[7].Data.Span);
        Assert.Equal(files[7].Data.ToArray(), page.WithPixels(page.ToRgba()));

        var font = FontProfile.LoadPortuguese();
        var (data, pages) = FontInstaller.BuildPages(syscg, font);
        Assert.Equal(syscg.Length, data.Length);

        var elf = ElfImage.Parse(PrxDecrypter.Decrypt(iso.Open(TranslationBuilder.EbootPath).ReadAll()));
        Assert.False(FontInstaller.IsInstalled(elf, font, tables));
        FontInstaller.PatchEboot(elf, font, pages, tables);
        Assert.True(FontInstaller.IsInstalled(elf, font, tables));
    }

    /// <summary>
    /// The v2 pages had every glyph moved (narrow capitals 3 px right), which with the
    /// game's width table cut the first letter of each word in the location banner.
    /// </summary>
    [IsoFact]
    public void Font_pages_keep_the_game_glyphs_and_align_the_added_ones()
    {
        var iso = TestIso.Index!;
        var cpk = CpkIndex.Read(iso.Open(TextSourceCatalog.LoadInnocentSin().CpkPath));
        var syscg = cpk.ReadEntry(cpk.FindByName(FontInstaller.Tables.LoadInnocentSin().PagesFile)!);
        var files = Par.Split(syscg);
        var font = FontProfile.LoadPortuguese();
        var (_, pages) = FontInstaller.BuildPages(syscg, font);
        var mapped = font.Mappings.Select(m => m.FontPosition).ToHashSet();

        foreach (var number in new[] { 7, 8, 12, 13 })
        {
            var original = GimImage.Read(files[number].Data.Span).ToRgba();
            var first = number >= 12 ? 12 : 7;
            for (var cell = 0; cell < 256; cell++)
            {
                if (mapped.Contains(((number - first) << 8) + cell)) continue;
                int x = cell % 16 * 16, y = cell / 16 * 16;
                for (var row = 0; row < 16; row++)
                    Assert.True(original.Pixels.AsSpan(original.Offset(x, y + row), 64)
                        .SequenceEqual(pages[number].Pixels.AsSpan(pages[number].Offset(x, y + row), 64)),
                        $"página {number}, célula {cell:X2}");
            }
        }

        // An accented letter stands on the same line as its base letter, in both fonts.
        int Bottom(RgbaImage page, int cell)
        {
            for (var y = 15; y >= 0; y--)
                for (var x = 0; x < 16; x++)
                    if (page.Alpha(cell % 16 * 16 + x, cell / 16 * 16 + y) > 0) return y;
            return -1;
        }
        int Position(string c) => font.Mappings.FirstOrDefault(m => m.Character == c)?.FontPosition ?? -1;
        var a = Position("á");
        var capital = Position("Á");
        Assert.True(a >= 0 && capital >= 0);
        foreach (var first in new[] { 7, 12 })
        {
            // Base letters: "a" at 0x101 and "A" at 0x0E0 in the game's layout.
            Assert.Equal(Bottom(pages[first + 1], 0x01), Bottom(pages[first + (a >> 8)], a & 0xFF));
            Assert.Equal(Bottom(pages[first], 0xE0), Bottom(pages[first + (capital >> 8)], capital & 0xFF));
        }
    }
}
