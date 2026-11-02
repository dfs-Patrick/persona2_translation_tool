using System.Globalization;
using GameTranslationEngine.Core.Archives;
using GameTranslationEngine.Core.Build;
using GameTranslationEngine.Core.Cpk;
using GameTranslationEngine.Core.Eboot;
using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.Fonts;
using GameTranslationEngine.Core.GameData;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Projects;
using GameTranslationEngine.Core.Text;

namespace GameTranslationEngine.Core.Engines;

/// <summary>
/// Persona 2: Eternal Punishment, PSP, Japanese (ULJM-06081). Same formats as
/// Innocent Sin (the PSP IS was built on this engine), but Japanese text, its
/// own message commands, and a fixed-width Japanese font that gets narrow Latin
/// glyphs with their own widths (see <see cref="EpTextPatch"/>). v1's EP data
/// (the NPJH50581 digital release) and its p2ep_template were the study sources.
/// </summary>
public sealed class EternalPunishmentEngine : IGameEngine
{
    public const string GameId = "ULJM-06081";

    public string Id => "p2ep-psp-jp";
    public string Name => "Persona 2: Eternal Punishment (PSP, JP)";

    public string Description =>
        "Tradução a partir do japonês: eventos, dungeons, mapas, demos de persona e loja de CDs, com fonte latina de largura variável e acentos PT-BR. A ISO precisa ser a japonesa (ULJM-06081).";

    public string ExpectedIso => SupportedGames.Describe(GameId);

    public bool Supports(IsoIdentity identity) => identity.GameId == GameId;

    public string DescribeGame(string gameId) => SupportedGames.Describe(gameId);

    public static MessageContext JapaneseContext()
    {
        using var stream = GameResources.Open("ep/encoding/jp/event.json");
        return new MessageContext(EventEncoding.Load(stream), ConstantTables.LoadEternalPunishment(), commands: EventCommands.EternalPunishment);
    }

    public TranslationAreaReport GenerateTranslationArea(IsoIndex iso, string messagesDirectory,
        IProgress<double>? progress, CancellationToken cancellation) =>
        TranslationArea.Generate(messagesDirectory,
            new TextExtractor(iso, TextSourceCatalog.Load("ep"), JapaneseContext()).ExtractAll(progress, cancellation));

    public IReadOnlyList<GameImageSource> DiscoverImages(IsoIndex iso) =>
        GameImageCatalog.Discover(iso, TextSourceCatalog.Load("ep").CpkPath);

    public IProjectCompiler CreateCompiler(TranslationProject project) =>
        new TranslationBuilder(project.IsoPath, project.MessagesDirectory, BuildSpec(project.File.Font),
            project.ImageTranslationControlPath, project.Root);

    /// <summary>"pt-br" installs the PT-BR profile; "original" keeps the Japanese font.</summary>
    public static GameBuildSpec BuildSpec(string font)
    {
        var isoContext = JapaneseContext();
        var profile = font is "" or "original" ? null : font == "pt-br"
            ? EpFontProfile.LoadPortuguese()
            : throw new InvalidDataException($"Perfil de fonte \"{font}\" não existe para o EP (use \"pt-br\" ou \"original\").");
        using var tables = GameResources.ReadJson("ep/eboot_tables.json");
        var sizes = tables.RootElement.GetProperty("sizeInstructions").EnumerateArray().Select(s => (
            s.GetProperty("file").GetString()!,
            (IReadOnlyList<uint>)s.GetProperty("addresses").EnumerateArray()
                .Select(a => uint.Parse(a.GetString()!.Replace("0x", ""), NumberStyles.HexNumber)).ToList())).ToList();
        return new GameBuildSpec
        {
            Catalog = TextSourceCatalog.Load("ep"),
            IsoContext = isoContext,
            Context = profile is null ? isoContext
                : new MessageContext(profile.Encoding, isoContext.Constants, commands: EventCommands.EternalPunishment),
            Tables = EbootTable.Load("ep"),
            SizeInstructions = sizes,
            Font = profile is null ? null : new Font(profile),
            HiddenDirectories = [("PSP_GAME/INSDIR", "INSDIX")],
        };
    }

    public (string Group, string Label) DescribeTbf(string originalDirectory, string originalFilename) =>
        TbfLocation.Describe(originalDirectory, originalFilename);

    public IReadOnlyCollection<string> CommandNames { get; } = EventCommands.EternalPunishment.KnownNames.ToHashSet();

    public IFontTools? Fonts { get; } = new FontTools();

    public Stream? OpenArtwork(Artwork kind) => GameResources.Open(kind switch
    {
        Artwork.Logo => "ep/art/logo.png",
        _ => "ep/art/background.jpg",
    });

    private sealed class Font(EpFontProfile profile) : IBuildFont
    {
        public string PagesFile => EpFontProfile.PagesFile;
        public byte[] BuildPages(ReadOnlyMemory<byte> original) => EpFontInstaller.BuildPages(original, profile).Data;
        public ElfImage PatchExecutable(ElfImage elf) => EpFontInstaller.PatchEboot(elf, profile);
        public bool IsInstalled(ElfImage elf) => EpFontInstaller.IsInstalled(elf, profile);
    }

    private sealed class FontTools : IFontTools
    {
        public string DefaultProfile => "pt-br";

        /// <summary>The 32 font pages of the game (syscg.bin 5–36) as PNG.</summary>
        public string Export(TranslationProject project)
        {
            using var iso = project.OpenIso();
            var catalog = TextSourceCatalog.Load("ep");
            var cpk = CpkIndex.Read(iso.Open(catalog.CpkPath));
            var files = Par.Split(cpk.ReadEntry(cpk.FindByName(EpFontProfile.PagesFile)!));
            var folder = Path.Combine(project.FontsDirectory, "original");
            Directory.CreateDirectory(folder);
            for (var page = 0; page < 32; page++)
                File.WriteAllBytes(Path.Combine(folder, $"{page}.png"),
                    Png.Encode(GimImage.Read(files[EpFontProfile.FirstPageFile + page].Data.Span).ToRgba()));
            return $"As 32 páginas da fonte japonesa (PNG) estão em {folder}.\n\n" +
                   "O perfil PT-BR do EP vem embutido na ferramenta (data/ep/fonts/pt-br): letras latinas nas células abaixo de 0x120, cada uma com a sua largura.";
        }
    }
}
