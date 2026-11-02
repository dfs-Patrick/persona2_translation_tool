using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.GameData;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Projects;
using GameTranslationEngine.Core.Smt;
using GameTranslationEngine.Core.Tbf;

namespace GameTranslationEngine.Core.Engines;

/// <summary>
/// Shin Megami Tensei, PlayStation 1, JP (SLPS-03170). The disc is read as a
/// raw .cue/.bin; text extraction and the disc compiler are still being built.
/// </summary>
public sealed class ShinMegamiTenseiEngine : IGameEngine
{
    public string Id => "smt-ps1-jp";
    public string Name => "Shin Megami Tensei (PS1, JP)";

    public string Description =>
        "Disco PS1 (.cue/.bin): extração dos textos dos scripts de mapa e compilação no próprio disco (setores com EDC/ECC refeitos). O disco precisa ser o japonês (SLPS-03170).";

    public string ExpectedIso => SupportedGames.Describe(SupportedGames.ShinMegamiTenseiJp);

    public bool Supports(IsoIdentity identity) => identity.GameId == SupportedGames.ShinMegamiTenseiJp;

    public string DescribeGame(string gameId) => SupportedGames.Describe(gameId);

    public TranslationAreaReport GenerateTranslationArea(IsoIndex iso, string messagesDirectory,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(messagesDirectory);
        var archive = SmtDataArchive.Open(iso);
        var map = SmtCharMap.Load();
        int created = 0, kept = 0, total = 0, done = 0;
        var differences = new List<string>();

        foreach (var entry in archive.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report((double)++done / archive.Entries.Count);
            if (entry.Length < 0x28) continue;
            var script = SmtScript.Parse(archive.Read(entry));
            if (script is null || script.Messages.Count == 0) continue;

            var messages = script.Messages.Select(m => (
                Key: $"msg_{m.Start:d5}",
                Before: "",
                Original: SmtMessageCodec.Decode(script.Words.Skip(m.Start).Take(m.Length).ToArray(), map),
                After: "")).ToList();
            total += messages.Count;

            var name = $"map_{entry.Index:d4}";
            var target = Path.Combine(messagesDirectory, "smt", name + ".tbf");
            if (File.Exists(target)) { kept++; continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            TbfDocument.Create(SmtDataArchive.DataPath, $"{name}.sc02", messages).Save(target);
            created++;
        }
        foreach (var location in SmtStringBank.Locations)
        {
            var bank = SmtStringBank.Parse(archive.Read(archive.Entries[location.Entry]), location);
            if (bank is null) continue;
            var strings = bank.Strings.Select(s => (
                Key: $"str_{s.Start:d5}",
                Before: "",
                Original: SmtMessageCodec.Decode(bank.Words.Skip(s.Start).Take(s.Length).ToArray(), map),
                After: "")).ToList();
            total += strings.Count;

            var name = SmtCompiler.BankName(location);
            var target = Path.Combine(messagesDirectory, "smt", name + ".tbf");
            if (File.Exists(target)) { kept++; continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            TbfDocument.Create(SmtDataArchive.DataPath, $"{name}.bank", strings).Save(target);
            created++;
        }
        return new TranslationAreaReport(created, kept, total, differences);
    }

    public IReadOnlyList<GameImageSource> DiscoverImages(IsoIndex iso) => SmtImageCatalog.Discover(iso);

    public IProjectCompiler CreateCompiler(TranslationProject project) => new SmtCompiler(project);

    public (string Group, string Label) DescribeTbf(string originalDirectory, string originalFilename) =>
        originalFilename.EndsWith(".bank", StringComparison.OrdinalIgnoreCase)
            ? ("Menus, itens e nomes", originalFilename.Replace(".bank", "", StringComparison.OrdinalIgnoreCase))
            : ("Scripts de mapa", originalFilename.Replace(".sc02", "", StringComparison.OrdinalIgnoreCase));

    public IReadOnlyCollection<string> CommandNames { get; } = [];

    public IFontTools? Fonts => null;

    public Stream? OpenArtwork(Artwork kind) => GameResources.Open(kind switch
    {
        Artwork.Logo => "smt/art/logo.png",
        _ => "smt/art/background.png",
    });
}
