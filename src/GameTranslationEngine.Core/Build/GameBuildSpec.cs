using GameTranslationEngine.Core.Eboot;
using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.Fonts;
using GameTranslationEngine.Core.GameData;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.Text;

namespace GameTranslationEngine.Core.Build;

/// <summary>A font that a build installs: new pages in the CPK and changes to the EBOOT.</summary>
public interface IBuildFont
{
    /// <summary>CPK entry with the font pages.</summary>
    string PagesFile { get; }

    /// <summary>The pages file with the profile's glyphs (same size as the original).</summary>
    byte[] BuildPages(ReadOnlyMemory<byte> original);

    /// <summary>Applies the EBOOT side; may return a new image when the EBOOT grows.</summary>
    ElfImage PatchExecutable(ElfImage elf);

    bool IsInstalled(ElfImage elf);
}

/// <summary>
/// What the compiler needs to know about one game: where its text is, how it
/// is encoded, the EBOOT tables to keep in step with the containers and the
/// font to install. Built by each engine.
/// </summary>
public sealed class GameBuildSpec
{
    public const string DefaultEbootPath = "PSP_GAME/SYSDIR/EBOOT.BIN";

    public string EbootPath { get; init; } = DefaultEbootPath;
    public required TextSourceCatalog Catalog { get; init; }
    /// <summary>Reads the original ISO (the game's own character table).</summary>
    public required MessageContext IsoContext { get; init; }
    /// <summary>Encodes the translation (the font profile's character table).</summary>
    public required MessageContext Context { get; init; }
    public required IReadOnlyList<EbootTable> Tables { get; init; }
    /// <summary>Containers whose size the code loads as an immediate (EP: CD_SHOP.BIN).</summary>
    public IReadOnlyList<(string File, IReadOnlyList<uint> Addresses)> SizeInstructions { get; init; } = [];
    public IBuildFont? Font { get; init; }
    /// <summary>Directories renamed in the output so the game does not find them (EP: INSDIR).</summary>
    public IReadOnlyList<(string Path, string NewName)> HiddenDirectories { get; init; } = [];

    /// <summary>Persona 2: Innocent Sin (US), with an optional font profile (null keeps the game's font).</summary>
    public static GameBuildSpec InnocentSin(FontProfile? font)
    {
        var constants = ConstantTables.LoadInnocentSin();
        var isoContext = new MessageContext(EventEncoding.LoadInnocentSinEnglish(), constants);
        return new GameBuildSpec
        {
            Catalog = TextSourceCatalog.LoadInnocentSin(),
            IsoContext = isoContext,
            Context = font is null ? isoContext : new MessageContext(font.Encoding, constants),
            Tables = EbootTable.LoadInnocentSin(),
            Font = font is null ? null : new InnocentSinFont(font),
        };
    }

    private sealed class InnocentSinFont(FontProfile profile) : IBuildFont
    {
        private readonly FontInstaller.Tables _tables = FontInstaller.Tables.LoadInnocentSin();
        private IReadOnlyDictionary<int, RgbaImage>? _pages;

        public string PagesFile => _tables.PagesFile;

        public byte[] BuildPages(ReadOnlyMemory<byte> original)
        {
            (var data, _pages) = FontInstaller.BuildPages(original, profile);
            return data;
        }

        public ElfImage PatchExecutable(ElfImage elf)
        {
            FontInstaller.PatchEboot(elf, profile, _pages ?? throw new InvalidOperationException("Páginas da fonte ainda não geradas."), _tables);
            return elf;
        }

        public bool IsInstalled(ElfImage elf) => FontInstaller.IsInstalled(elf, profile, _tables);
    }
}
