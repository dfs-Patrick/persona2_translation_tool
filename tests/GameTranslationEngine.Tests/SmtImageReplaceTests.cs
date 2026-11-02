using GameTranslationEngine.Core.Engines;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Projects;
using GameTranslationEngine.Core.Smt;

namespace GameTranslationEngine.Tests;

public class SmtImageReplaceTests
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
    public void RealDisc_ReplacingAnImageWithItsOwnPixelsKeepsTheSizeAndFitsMostImages()
    {
        var cue = FindCue();
        if (cue is null) return;
        using var iso = IsoIndex.Read(DiscImage.Open(cue));
        var archive = SmtDataArchive.Open(iso);
        var images = SmtImageCatalog.Discover(iso);
        var fitted = 0;
        var tooBig = 0;
        foreach (var source in images)
        {
            Assert.True(SmtImageCatalog.TryParsePath(source.Path, out var index, out var route));
            var tim = (TimImage)source.Image;
            var data = archive.Read(archive.Entries[index]);
            byte[] updated;
            try { updated = SmtImageCatalog.Replace(data, route, original => tim.WithPixels(original, source.Pixels)); }
            catch (InvalidOperationException) { tooBig++; continue; }
            Assert.Equal(data.Length, updated.Length);
            fitted++;
        }
        Assert.True(fitted > images.Count / 2, $"fitted={fitted}, tooBig={tooBig}, total={images.Count}");
    }

    [Fact]
    public async Task RealDisc_CompiledDiscShowsTheReplacementImage()
    {
        var cue = FindCue();
        if (cue is null) return;
        var root = Path.Combine(Path.GetTempPath(), "smt-img-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = await TranslationProject.CreateAsync(root, "teste", cue, new ShinMegamiTenseiEngine());
            using var iso = project.OpenIso();
            var archive = SmtDataArchive.Open(iso);

            GameImageSource? chosen = null;
            RgbaImage? edited = null;
            foreach (var source in SmtImageCatalog.Discover(iso).Where(s => ((TimImage)s.Image).Width >= 16))
            {
                SmtImageCatalog.TryParsePath(source.Path, out var index, out var route);
                var tim = (TimImage)source.Image;
                var candidate = source.Pixels.Clone();
                var o = candidate.Offset(3, 2);
                candidate.Pixels[o] = 255; candidate.Pixels[o + 1] = 255; candidate.Pixels[o + 2] = 255; candidate.Pixels[o + 3] = 255;
                try { SmtImageCatalog.Replace(archive.Read(archive.Entries[index]), route, d => tim.WithPixels(d, candidate)); }
                catch (InvalidOperationException) { continue; }
                chosen = source;
                edited = candidate;
                break;
            }
            Assert.NotNull(chosen);

            var png = Path.Combine(project.Root, "images", "novo.png");
            Directory.CreateDirectory(Path.GetDirectoryName(png)!);
            File.WriteAllBytes(png, Png.Encode(edited!));
            var control = project.LoadImageTranslationControl();
            control.Images[chosen.Path] = new ImageTranslationEntry { Replacement = Path.Combine("images", "novo.png") };
            ProjectIndexes.Save(project.ImageTranslationControlPath, control);

            var output = Path.Combine(root, "build", "img.cue");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var report = project.CreateBuilder().Build(output);
            Assert.True(report.Succeeded, string.Join("; ", report.Issues));

            using var built = IsoIndex.Read(DiscImage.Open(output));
            var after = SmtImageCatalog.Discover(built).First(s => s.Path == chosen.Path);
            var changedPixels = 0;
            for (var i = 0; i < after.Pixels.Pixels.Length; i++)
                if (after.Pixels.Pixels[i] != chosen.Pixels.Pixels[i]) changedPixels++;
            Assert.True(changedPixels > 0, "a imagem não mudou no disco gerado");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
