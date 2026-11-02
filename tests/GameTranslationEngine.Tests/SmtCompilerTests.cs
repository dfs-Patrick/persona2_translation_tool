using GameTranslationEngine.Core.Engines;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Projects;
using GameTranslationEngine.Core.Smt;
using GameTranslationEngine.Core.Tbf;

namespace GameTranslationEngine.Tests;

public class SmtCompilerTests
{
    private static string? FindCue()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "lab", "Shin Megami Tensei (Japan).cue");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    [Fact]
    public async Task RealDisc_CompiledDiscHoldsTheTranslationAndOnlyValidSectorsChange()
    {
        var cue = FindCue();
        if (cue is null) return;
        var root = Path.Combine(Path.GetTempPath(), "smt-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = await TranslationProject.CreateAsync(root, "teste", cue, new ShinMegamiTenseiEngine());
            var tbfPath = Directory.GetFiles(Path.Combine(project.MessagesDirectory, "smt"), "map_*.tbf").Order().First();
            var tbf = TbfDocument.Load(tbfPath);
            var target = tbf.Entries[0];
            target.Translation = "Olá, mundo!";
            tbf.Save(tbfPath);

            var compiler = project.CreateBuilder();
            Assert.True(compiler.Validate().Succeeded);
            var output = Path.Combine(root, "build", "smt-ptbr.cue");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var report = compiler.Build(output);
            Assert.True(report.Succeeded, string.Join("; ", report.Issues));
            Assert.Equal(1, report.ChangedScripts);

            var bin = Path.ChangeExtension(output, ".bin");
            Assert.True(File.Exists(output));
            Assert.Equal(new FileInfo(DiscImage.ResolveDataFile(cue)).Length, new FileInfo(bin).Length);

            using var built = IsoIndex.Read(DiscImage.Open(output));
            var archive = SmtDataArchive.Open(built);
            var index = int.Parse(Path.GetFileNameWithoutExtension(tbfPath)[4..]);
            var script = SmtScript.Parse(archive.Read(archive.Entries[index]))!;
            var map = SmtCharMap.Load();
            var texts = script.Messages.Select(m =>
                SmtMessageCodec.Decode(script.Words.Skip(m.Start).Take(m.Length).ToArray(), map)).ToList();
            Assert.Contains("Olá, mundo!", texts.Select(t => t.Replace('\u3000', ' ')));

            // Every sector that differs from the original must still carry a valid EDC/ECC.
            using var original = new FileStream(DiscImage.ResolveDataFile(cue), FileMode.Open, FileAccess.Read);
            using var patched = new FileStream(bin, FileMode.Open, FileAccess.Read);
            var a = new byte[DiscImage.RawSectorSize];
            var b = new byte[DiscImage.RawSectorSize];
            var different = 0;
            while (original.Read(a) == a.Length && patched.Read(b) == b.Length)
            {
                if (a.AsSpan().SequenceEqual(b)) continue;
                different++;
                Assert.True(CdSectorCodec.IsValid(b));
            }
            Assert.InRange(different, 1, 80);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RealDisc_ScriptLargerThanItsSpaceMovesTheNextEntriesAndKeepsTheDiscSize()
    {
        var cue = FindCue();
        if (cue is null) return;
        var root = Path.Combine(Path.GetTempPath(), "smt-grow-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = await TranslationProject.CreateAsync(root, "teste", cue, new ShinMegamiTenseiEngine());
            var tbfPath = Path.Combine(project.MessagesDirectory, "smt", "map_0049.tbf");
            var tbf = TbfDocument.Load(tbfPath);
            for (var i = 0; i < 12; i++)
                tbf.Entries[i].Translation = string.Join("\n", Enumerable.Repeat(new string('a', 20), 4)) + "\n" + new string('b', 20);
            tbf.Save(tbfPath);

            var output = Path.Combine(root, "build", "smt-ptbr.cue");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var report = project.CreateBuilder().Build(output);
            Assert.True(report.Succeeded, string.Join("; ", report.Issues));

            using var original = IsoIndex.Read(DiscImage.Open(cue));
            using var built = IsoIndex.Read(DiscImage.Open(output));
            var before = SmtDataArchive.Open(original);
            var after = SmtDataArchive.Open(built);
            Assert.Equal(before.Entries.Count, after.Entries.Count);
            Assert.True(after.Entries[49].Length > before.Entries[49].Length);
            Assert.Equal(before.Entries[^1].Offset + before.Entries[^1].Length, after.Entries[^1].Offset + after.Entries[^1].Length);
            for (var i = 50; i < before.Entries.Count; i++)
                Assert.True(after.Entries[i].Offset >= after.Entries[i - 1].Offset + after.Entries[i - 1].Length);
            Assert.Equal(before.Read(before.Entries[60]), after.Read(after.Entries[60]));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
