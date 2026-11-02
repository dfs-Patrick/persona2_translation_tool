using GameTranslationEngine.Core.Archives;
using GameTranslationEngine.Core.Cpk;
using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.GameData;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.Iso;

namespace GameTranslationEngine.Core.Fonts;

/// <summary>A7 · The font pages of the game as PNG, and an editable copy of the shipped profile.</summary>
public static class FontPages
{
    public static readonly int[] Numbers = [7, 8, 12, 13];

    /// <summary>Writes the original pages of syscg.bin (7, 8, 12, 13) as PNG into <paramref name="folder"/>.</summary>
    public static IReadOnlyList<string> ExportOriginal(IsoIndex iso, string folder)
    {
        var cpk = CpkIndex.Read(iso.Open(TextSourceCatalog.LoadInnocentSin().CpkPath));
        var tables = FontInstaller.Tables.LoadInnocentSin();
        var files = Par.Split(cpk.ReadEntry(cpk.FindByName(tables.PagesFile)
                                            ?? throw new InvalidDataException($"{tables.PagesFile} não encontrado no CPK.")));
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        foreach (var number in Numbers)
        {
            var path = Path.Combine(folder, $"{number}.png");
            File.WriteAllBytes(path, Png.Encode(GimImage.Read(files[number].Data.Span).ToRgba()));
            written.Add(path);
        }
        return written;
    }

    /// <summary>Copies the shipped PT-BR profile into <paramref name="folder"/>; existing files are kept.</summary>
    public static int CopyPortugueseProfile(string folder)
    {
        Directory.CreateDirectory(folder);
        var copied = 0;
        foreach (var file in new[] { "event.json", "font.json", "glyphs.json" }.Concat(Numbers.Select(n => $"{n}.png")))
        {
            var target = Path.Combine(folder, file);
            if (File.Exists(target)) continue;
            using var source = GameResources.Open($"is/fonts/pt-br/{file}");
            using var output = File.Create(target);
            source.CopyTo(output);
            copied++;
        }
        return copied;
    }
}
