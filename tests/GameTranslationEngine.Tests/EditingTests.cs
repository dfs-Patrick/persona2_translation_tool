using GameTranslationEngine.Core.Editing;
using GameTranslationEngine.Core.Engines;

namespace GameTranslationEngine.Tests;

public class EditingTests
{
    private static Glossary Sample() => new()
    {
        Terms =
        [
            new GlossaryTerm { Original = "Exalted One", Translation = "Exaltado", Description = "Líder do Círculo Mascarado" },
            new GlossaryTerm { Original = "Masked Circle", Translation = "Círculo Mascarado" },
            new GlossaryTerm { Original = "Masked Circle member", Translation = "membro do Círculo Mascarado" },
            new GlossaryTerm { Original = "Joker", Translation = "Coringa", MatchCase = true },
        ],
    };

    [Fact]
    public void GlossaryFindsWholeWordsLongestFirstAcrossLineBreaks()
    {
        var glossary = Sample();
        var found = glossary.FindOriginal("[color(name_green)]Masked Circle member\nIs the Exalted\nOne up there? exalted ones, joker, Joker");
        Assert.Equal(["Masked Circle member", "Exalted One", "Joker"], found.Select(f => f.Term.Original));
        Assert.Equal("Exalted\nOne", "[color(name_green)]Masked Circle member\nIs the Exalted\nOne up there?".Substring(found[1].Start, found[1].Length));
    }

    [Fact]
    public void GlossaryReportsMissingTranslations()
    {
        var glossary = Sample();
        Assert.Equal(["Exalted One"], glossary.Missing("Is the Exalted One up there?", "O Líder está lá em cima?").Select(t => t.Original));
        Assert.Empty(glossary.Missing("Is the Exalted One up there?", "O exaltado está lá em cima?"));
        Assert.Empty(glossary.Missing("Is the Exalted One up there?", ""));
    }

    [Fact]
    public void GlossarySavesAndLoads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"p2tool-{Guid.NewGuid():N}", "glossary.json");
        try
        {
            var glossary = Sample();
            glossary.Terms.Add(new GlossaryTerm());
            glossary.Save(path);
            var loaded = Glossary.Load(path);
            Assert.Equal(4, loaded.Terms.Count);
            Assert.Equal("Líder do Círculo Mascarado", loaded.Terms[0].Description);
            Assert.True(loaded.Terms[3].MatchCase);
            Assert.Contains("\"Círculo Mascarado\"", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void AnalyzerMarksCommandsEscapesAndGlossaryOutsideCommands()
    {
        var analyzer = new MessageAnalyzer(new InnocentSinEngine().CommandNames, Sample(), null);
        var text = "[color(name_green)]Joker\n[bogus]\\[x] [delay(30)]Joker[color(Joker)] [choice(2)sem fim";
        var spans = analyzer.Analyze(text, isTranslation: false);
        string Show(TextSpan s) => $"{s.Kind}:{text.Substring(s.Start, s.Length)}";
        Assert.Equal(
        [
            "Command:[color(name_green)]", "Glossary:Joker", "InvalidCommand:[bogus]", "Escape:\\[",
            "Command:[delay(30)]", "Glossary:Joker", "Command:[color(Joker)]", "InvalidCommand:[choice(2)sem fim",
        ], spans.Select(Show));
    }

    [Fact]
    public async Task SpellCheckerUsesDictionaryProjectWordsAndGlossary()
    {
        var file = Path.Combine(Path.GetTempPath(), $"p2tool-{Guid.NewGuid():N}.txt");
        try
        {
            var speller = await SpellChecker.LoadPortugueseAsync(file);
            Assert.True(speller.Check("você"));
            Assert.False(speller.Check("voce"));
            Assert.Contains("horrível", speller.Suggest("horrivel"));
            Assert.False(speller.Check("Tatsuya"));
            speller.AddWord("Tatsuya");
            Assert.True(speller.Check("Tatsuya"));
            Assert.Equal("Tatsuya\n", File.ReadAllText(file));
            Assert.True((await SpellChecker.LoadPortugueseAsync(file)).Check("Tatsuya"));

            speller.SetGlossary(new Glossary { Terms = [new GlossaryTerm { Original = "Sumaru", Translation = "Sumaru" }] });
            var analyzer = new MessageAnalyzer(new InnocentSinEngine().CommandNames, null, speller);
            var spans = analyzer.Analyze("[color(name_green)]Voce está em Sumaru, Tatsuya?", isTranslation: true);
            Assert.Equal(["Voce"], spans.Where(s => s.Kind == SpanKind.Misspelled).Select(s => "[color(name_green)]Voce está em Sumaru, Tatsuya?".Substring(s.Start, s.Length)));
            Assert.DoesNotContain(analyzer.Analyze("Voce", isTranslation: false), s => s.Kind == SpanKind.Misspelled);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void EnginesResolveOldAndUnknownIds()
    {
        Assert.Equal("p2is-psp-us", GameEngines.Get("").Id);
        Assert.Equal("p2is-psp-us", GameEngines.Get("p2is-psp-us").Id);
        Assert.Throws<InvalidDataException>(() => GameEngines.Get("p5"));
    }
}

public class SearchTests
{
    [Fact]
    public void SearchFindsTextInTheChosenSide()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"p2tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var files = new List<GameTranslationEngine.Core.Projects.TbfFileInfo>();
            for (var n = 0; n < 2; n++)
            {
                var doc = GameTranslationEngine.Core.Tbf.TbfDocument.Create("event.bin$/e000" + n, "script.msg",
                [
                    ("msg_0", "msg_0:", "Is the Exalted One\r\nup there?", ""),
                    ("msg_1", "msg_1:", "Nothing here.", ""),
                ]);
                doc.Entries[0].Translation = n == 0 ? "O Exaltado está lá? Exaltado!" : "";
                doc.Entries[1].Translation = "Exalted";
                var path = Path.Combine(folder, $"e000{n}.tbf");
                doc.Save(path);
                files.Add(new GameTranslationEngine.Core.Projects.TbfFileInfo(path, $"e000{n}.tbf", "Eventos", $"e000{n}"));
            }
            using var _ = new StreamWriter(Path.Combine(folder, "lixo.tbf"));
            files.Add(new GameTranslationEngine.Core.Projects.TbfFileInfo(Path.Combine(folder, "lixo.tbf"), "lixo.tbf", "Outros", "lixo"));

            var both = GameTranslationEngine.Core.Projects.TranslationSearch.Search(files, "exalt", GameTranslationEngine.Core.Projects.SearchScope.Both, matchCase: false);
            Assert.Equal(5, both.TotalHits);
            Assert.Equal(2, both.Files);
            var first = both.Hits[0];
            Assert.Equal(("msg_0", false, 7), (first.Key, first.InTranslation, first.Start));
            Assert.Equal(2, both.Hits.Single(h => h.File.Label == "e0000" && h.InTranslation && h.Key == "msg_0").Occurrences);

            Assert.Equal(2, GameTranslationEngine.Core.Projects.TranslationSearch.Search(files, "Exalted", GameTranslationEngine.Core.Projects.SearchScope.Original, true).TotalHits);
            Assert.Equal(3, GameTranslationEngine.Core.Projects.TranslationSearch.Search(files, "Exalt", GameTranslationEngine.Core.Projects.SearchScope.Translation, true).TotalHits);
            Assert.Equal(0, GameTranslationEngine.Core.Projects.TranslationSearch.Search(files, "exalted", GameTranslationEngine.Core.Projects.SearchScope.Translation, true).TotalHits);
            Assert.Equal(2, GameTranslationEngine.Core.Projects.TranslationSearch.Search(files, "One\nup", GameTranslationEngine.Core.Projects.SearchScope.Original, false).TotalHits);
            Assert.True(GameTranslationEngine.Core.Projects.TranslationSearch.Search(files, "e", GameTranslationEngine.Core.Projects.SearchScope.Both, false, limit: 3).Truncated);
            // A space also matches the hand-made line breaks of the game; other characters are literal.
            Assert.Equal(2, GameTranslationEngine.Core.Projects.TranslationSearch.Search(files, "one up", GameTranslationEngine.Core.Projects.SearchScope.Original, false).TotalHits);
            Assert.Equal(0, GameTranslationEngine.Core.Projects.TranslationSearch.Search(files, "one.up", GameTranslationEngine.Core.Projects.SearchScope.Original, false).TotalHits);
            Assert.Equal(1, GameTranslationEngine.Core.Projects.TranslationSearch.Search(files, "lá?", GameTranslationEngine.Core.Projects.SearchScope.Translation, false).TotalHits);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
