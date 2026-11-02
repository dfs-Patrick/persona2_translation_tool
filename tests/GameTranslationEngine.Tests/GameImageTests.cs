using GameTranslationEngine.Core.Build;
using GameTranslationEngine.Core.Archives;
using GameTranslationEngine.Core.Cpk;
using GameTranslationEngine.Core.Engines;
using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.IO;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Projects;

namespace GameTranslationEngine.Tests;

public sealed class GameImageTests
{
    [IsoFact]
    public void Image_catalog_discovers_renderable_GIM_textures()
    {
        var catalog = TextSourceCatalog.LoadInnocentSin();
        var images = GameImageCatalog.Discover(TestIso.Index!, catalog.CpkPath);

        Assert.NotEmpty(images);
        Assert.All(images, image =>
        {
            Assert.True(image.Image.Width > 0);
            Assert.True(image.Image.Height > 0);
            Assert.Equal(image.Image.Width * image.Image.Height * 4, image.Pixels.Pixels.Length);
            Assert.NotEmpty(image.Compression);
        });
    }

    [EternalPunishmentIsoFact]
    public void Eternal_Punishment_catalog_discovers_renderable_GIM_textures()
    {
        using var iso = IsoIndex.Read(new FileByteSource(TestIso.EternalPunishmentPath!));
        var catalog = TextSourceCatalog.Load("ep");
        var images = GameImageCatalog.Discover(iso, catalog.CpkPath);

        Assert.NotEmpty(images);
        Assert.All(images, image => Assert.True(image.Pixels.Pixels.Length > 0));
    }

    [IsoFact]
    public void Innocent_Sin_image_replacements_are_compiled_into_the_ISO()
    {
        BuildAndAssertImageReplacement(TestIso.Path!, TextSourceCatalog.LoadInnocentSin().CpkPath,
            GameBuildSpec.InnocentSin(null));
    }

    [EternalPunishmentIsoFact]
    public void Eternal_Punishment_image_replacements_are_compiled_into_the_ISO()
    {
        BuildAndAssertImageReplacement(TestIso.EternalPunishmentPath!, TextSourceCatalog.Load("ep").CpkPath,
            EternalPunishmentEngine.BuildSpec("original"));
    }

    private static void BuildAndAssertImageReplacement(string isoPath, string cpkPath, GameBuildSpec buildSpec)
    {
        var root = Path.Combine(Path.GetTempPath(), "p2tool-image-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "translation");
            Directory.CreateDirectory(project);
            var messages = Path.Combine(project, "messages");
            Directory.CreateDirectory(messages);

            using var iso = IsoIndex.Read(new FileByteSource(isoPath));
            var source = GameImageCatalog.Discover(iso, cpkPath).First();
            var replacementPixels = source.Pixels.Clone();
            for (var i = 0; i < replacementPixels.Pixels.Length; i += 4)
            {
                replacementPixels.Pixels[i] = 0xFF;
                replacementPixels.Pixels[i + 1] = 0;
                replacementPixels.Pixels[i + 2] = 0xFF;
                replacementPixels.Pixels[i + 3] = 0xFF;
            }

            var replacementPath = Path.Combine(root, "replacement.png");
            File.WriteAllBytes(replacementPath, Png.Encode(replacementPixels));
            var control = new ImageTranslationControl();
            control.Images[source.Path] = new ImageTranslationEntry { Replacement = "replacement.png" };
            var controlPath = Path.Combine(project, "image-translation-control.json");
            ProjectIndexes.Save(controlPath, control);

            var output = Path.Combine(root, "compiled.iso");
            var report = new TranslationBuilder(isoPath, messages, buildSpec, controlPath, root).Build(output);
            Assert.True(report.Succeeded, string.Join("\n", report.Issues));
            Assert.Contains(source.EntryName, report.RebuiltEntries);

            using var builtIso = IsoIndex.Read(new FileByteSource(output));
            var rebuiltCpk = CpkIndex.Read(builtIso.Open(cpkPath));
            ReadOnlyMemory<byte> gimData = rebuiltCpk.ReadEntry(rebuiltCpk.FindById(source.CpkId)!);
            foreach (var step in source.Chain)
            {
                gimData = step.Type switch
                {
                    "par" => Par.Split(gimData)[step.Index].Data,
                    "bnp" => BnpEntryCodec.Decode(Bnp.Split(gimData)[step.Index].Data.Span).Data,
                    _ => throw new InvalidDataException($"Container de imagem desconhecido: {step.Type}."),
                };
            }
            var rebuiltImage = GimImage.Read(gimData.Span).ToRgba();
            var expected = GimImage.Read(source.Gim.WithPixels(replacementPixels)).ToRgba();
            Assert.Equal(expected.Pixels, rebuiltImage.Pixels);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
