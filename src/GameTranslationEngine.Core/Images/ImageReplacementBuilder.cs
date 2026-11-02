using GameTranslationEngine.Core.Archives;
using GameTranslationEngine.Core.Build;
using GameTranslationEngine.Core.Cpk;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Projects;

namespace GameTranslationEngine.Core.Images;

public sealed record RebuiltImageEntry(
    int CpkId,
    string Name,
    string ContainerType,
    byte[] OriginalData,
    byte[] Data,
    IReadOnlyList<EntryLayout>? OriginalLayout,
    IReadOnlyList<EntryLayout>? NewLayout);

public sealed record ImageReplacementPlan(
    IReadOnlyDictionary<int, RebuiltImageEntry> Entries,
    IReadOnlyList<string> ReplacedImages);

public static class ImageReplacementBuilder
{
    public static ImageReplacementPlan Build(
        IsoIndex iso,
        CpkIndex cpk,
        string cpkPath,
        string projectRoot,
        ImageTranslationControl control,
        CancellationToken cancellation = default)
    {
        var sources = GameImageCatalog.Discover(iso, cpkPath)
            .ToDictionary(image => image.Path, StringComparer.OrdinalIgnoreCase);
        var replacements = new Dictionary<int, RebuiltImageEntry>();
        var changedImages = new List<string>();
        projectRoot = Path.GetFullPath(projectRoot);

        foreach (var (path, entry) in control.Images)
        {
            cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.Replacement)) continue;
            if (!sources.TryGetValue(path, out var source))
                throw new InvalidDataException($"Imagem do índice não existe mais na ISO: {path}");

            var replacementPath = ResolveProjectFile(projectRoot, entry.Replacement);
            var replacement = File.ReadAllBytes(replacementPath);
            var pixels = Path.GetExtension(replacementPath).ToLowerInvariant() switch
            {
                ".png" => Png.Decode(replacement),
                ".gim" => GimImage.Read(replacement).ToRgba(),
                _ => throw new InvalidDataException($"Formato de substituição não suportado: {replacementPath}"),
            };
            var gim = source.Gim.WithPixels(pixels);

            if (!replacements.TryGetValue(source.CpkId, out var rebuilt))
            {
                var cpkEntry = cpk.FindById(source.CpkId)
                               ?? throw new InvalidDataException($"Entrada {source.EntryName} não existe no CPK.");
                var original = cpk.ReadEntry(cpkEntry);
                rebuilt = new RebuiltImageEntry(source.CpkId, source.EntryName, source.ContainerType,
                    original, original, Layout(source.ContainerType, original), null);
            }

            var updated = Replace(rebuilt.Data, source.Chain, 0, gim);
            var newLayout = Layout(source.ContainerType, updated);
            replacements[source.CpkId] = rebuilt with { Data = updated, NewLayout = newLayout };
            changedImages.Add(path);
        }

        return new ImageReplacementPlan(replacements, changedImages);
    }

    private static string ResolveProjectFile(string projectRoot, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"O caminho de substituição deve ser relativo ao projeto: {relativePath}");

        var fullPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        var rootPrefix = projectRoot.EndsWith(Path.DirectorySeparatorChar)
            ? projectRoot
            : projectRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"O caminho de substituição sai da pasta do projeto: {relativePath}");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Imagem de substituição não encontrada: {relativePath}", fullPath);
        return fullPath;
    }

    private static byte[] Replace(
        ReadOnlyMemory<byte> data,
        IReadOnlyList<ImageArchiveStep> chain,
        int depth,
        byte[] replacement)
    {
        if (depth == chain.Count) return replacement;

        var step = chain[depth];
        switch (step.Type)
        {
            case "par":
            {
                var entries = Par.Split(data);
                if ((uint)step.Index >= (uint)entries.Count)
                    throw new InvalidDataException($"Índice PAR {step.Index} fora do intervalo.");
                var files = entries.Select(e => e.Data).ToList();
                files[step.Index] = Replace(files[step.Index], chain, depth + 1, replacement);
                return Par.Build(files).Data;
            }
            case "bnp":
            {
                var entries = Bnp.Split(data);
                if ((uint)step.Index >= (uint)entries.Count)
                    throw new InvalidDataException($"Índice BNP {step.Index} fora do intervalo.");
                var files = entries.Select(e => e.Data).ToList();
                var entry = BnpEntryCodec.Decode(files[step.Index].Span);
                var inner = Replace(entry.Data, chain, depth + 1, replacement);
                files[step.Index] = BnpEntryCodec.Encode(entry.Type, entry.Compression, entry.Tag, inner);
                return Bnp.Build(files, entries.Select(e => e.Offset % 2048 == 0).ToList()).Data;
            }
            default:
                throw new InvalidDataException($"Container de imagem não suportado: {step.Type}.");
        }
    }

    private static IReadOnlyList<EntryLayout>? Layout(string type, ReadOnlyMemory<byte> data) => type switch
    {
        "par" => Par.Split(data).Select(e => new EntryLayout(e.Offset, e.Data.Length)).ToList(),
        "bnp" => Bnp.Split(data).Select(e => new EntryLayout(e.Offset, e.Data.Length)).ToList(),
        _ => null,
    };
}
