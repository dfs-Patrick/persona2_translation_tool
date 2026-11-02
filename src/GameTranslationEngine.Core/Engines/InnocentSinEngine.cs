using GameTranslationEngine.Core.Build;
using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.Fonts;
using GameTranslationEngine.Core.GameData;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Projects;
using GameTranslationEngine.Core.Text;

namespace GameTranslationEngine.Core.Engines;

/// <summary>Persona 2: Innocent Sin, PSP, US (ULUS-10584): stages A and C of MAPA.md.</summary>
public sealed class InnocentSinEngine : IGameEngine
{
    public string Id => "p2is-psp-us";
    public string Name => "Persona 2: Innocent Sin (PSP, US)";

    public string Description =>
        "Eventos, dungeons, mapas, demos de persona e loja de CDs, com a fonte PT-BR. A ISO precisa ser a americana (ULUS-10584).";

    public string ExpectedIso => SupportedGames.Describe(SupportedGames.InnocentSinUs);

    public bool Supports(IsoIdentity identity) => identity.GameId == SupportedGames.InnocentSinUs;

    public string DescribeGame(string gameId) => SupportedGames.Describe(gameId);

    public TranslationAreaReport GenerateTranslationArea(IsoIndex iso, string messagesDirectory,
        IProgress<double>? progress, CancellationToken cancellation) =>
        TranslationArea.Generate(messagesDirectory, new TextExtractor(iso).ExtractAll(progress, cancellation));

    public IProjectCompiler CreateCompiler(TranslationProject project) =>
        new TranslationBuilder(project.IsoPath, project.MessagesDirectory, LoadFont(project));

    public (string Group, string Label) DescribeTbf(string originalDirectory, string originalFilename) =>
        TbfLocation.Describe(originalDirectory, originalFilename);

    public IReadOnlyCollection<string> CommandNames { get; } = EventCommands.KnownNames.ToHashSet();

    public IFontTools? Fonts { get; } = new FontTools();

    public Stream? OpenArtwork(Artwork kind) => GameResources.Open(kind switch
    {
        Artwork.Logo => "is/art/logo.png",
        _ => "is/art/background.png",
    });

    /// <summary>"pt-br" (shipped), "original" (the game's font) or a folder relative to the project.</summary>
    public static FontProfile? LoadFont(TranslationProject project) => project.File.Font switch
    {
        "" or "original" => null,
        "pt-br" => FontProfile.LoadPortuguese(),
        var folder => FontProfile.LoadFolder(Path.GetFullPath(Path.Combine(project.Root, folder))),
    };

    private sealed class FontTools : IFontTools
    {
        public string DefaultProfile => "pt-br";

        /// <summary>
        /// A7 · The game's font pages go to fonts/original and an editable copy
        /// of the PT-BR profile to fonts/pt-br (existing files are kept).
        /// </summary>
        public string Export(TranslationProject project)
        {
            using var iso = project.OpenIso();
            var original = Path.Combine(project.FontsDirectory, "original");
            FontPages.ExportOriginal(iso, original);
            var profile = Path.Combine(project.FontsDirectory, "pt-br");
            var copied = FontPages.CopyPortugueseProfile(profile);
            return $"Páginas originais da fonte (PNG): {original}\n\n" +
                   $"Cópia editável do perfil PT-BR: {profile} ({copied} arquivo(s) novo(s); os existentes foram mantidos).\n\n" +
                   "Para compilar com a cópia editada, troque \"font\" para \"fonts/pt-br\" no arquivo do projeto (translation-project.json).";
        }
    }
}

/// <summary>Turns the original location of an Innocent Sin script into a readable group and name.</summary>
public static class TbfLocation
{
    private static readonly Dictionary<string, string> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["event.bin"] = "Eventos",
        ["DNGSCN.BNP"] = "Dungeons",
        ["F_BE.BNP"] = "F_BE",
        ["P_DEMO.BNP"] = "Personas (demo)",
        ["CD_SHOP.BIN"] = "Loja de CDs",
        ["TM_EVE.BNP"] = "Contatos",
        ["EVTUNIT2.BNP"] = "Contatos",
    };

    public static (string Group, string Label) Describe(string originalDirectory, string originalFilename)
    {
        var parts = originalDirectory.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.TrimEnd('$'))
            .ToList();
        var cpk = parts.FindIndex(p => p.Equals("P2PT_ALL.cpk", StringComparison.OrdinalIgnoreCase));
        var inside = cpk >= 0 ? parts.Skip(cpk + 1).ToList() : parts;
        if (inside.Count == 0) return ("Outros", originalFilename);

        var container = inside[0];
        var group = Groups.GetValueOrDefault(container)
                    ?? (container.StartsWith("MMAP", StringComparison.OrdinalIgnoreCase) ? "Mapas" : container);
        // event.bin/e0000.bin/8.efb → "e0000"; DNGSCN.BNP/0_0000.efb → "0_0000"; MMAP01.BNP/… → "MMAP01".
        var label = inside.Count > 1 ? Path.GetFileNameWithoutExtension(inside[1]) : Path.GetFileNameWithoutExtension(originalFilename);
        if (group == "Mapas") label = Path.GetFileNameWithoutExtension(container);
        // Only messages are translated in v3; scripts imported from v2 are marked.
        return originalFilename.EndsWith(".msg", StringComparison.OrdinalIgnoreCase)
            ? (group, label)
            : (group, $"{label} · {Path.GetExtension(originalFilename).TrimStart('.')} (v2)");
    }
}
