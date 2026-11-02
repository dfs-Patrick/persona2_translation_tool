using GameTranslationEngine.Core.Build;
using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.Images;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Projects;

namespace GameTranslationEngine.Core.Engines;

/// <summary>
/// Everything that belongs to one game: how to recognize its ISO, where its
/// text is, how to compile a translation back and which commands its messages
/// use. Projects, the editor, the glossary and the spell checker are shared by
/// every engine.
/// </summary>
public interface IGameEngine
{
    /// <summary>Stable id, written to the project file.</summary>
    string Id { get; }

    string Name { get; }

    /// <summary>One or two sentences for the engine list on the start page.</summary>
    string Description { get; }

    /// <summary>The ISO the engine works with, for messages to the user.</summary>
    string ExpectedIso { get; }

    bool Supports(IsoIdentity identity);

    /// <summary>Readable name of a game id (supported or not).</summary>
    string DescribeGame(string gameId);

    /// <summary>A2–A8 · Reads the text from the ISO and writes the TBF files still missing.</summary>
    TranslationAreaReport GenerateTranslationArea(IsoIndex iso, string messagesDirectory,
        IProgress<double>? progress, CancellationToken cancellation);

    /// <summary>Images the game contains in formats currently supported by the viewer.</summary>
    IReadOnlyList<GameImageSource> DiscoverImages(IsoIndex iso);

    /// <summary>C1–C9 · Compiler of the project's translation into an ISO.</summary>
    IProjectCompiler CreateCompiler(TranslationProject project);

    /// <summary>Group and name shown in the file browser for a TBF.</summary>
    (string Group, string Label) DescribeTbf(string originalDirectory, string originalFilename);

    /// <summary>Command names accepted in the messages (for highlighting).</summary>
    IReadOnlyCollection<string> CommandNames { get; }

    /// <summary>Font tools, when the engine has them.</summary>
    IFontTools? Fonts { get; }

    /// <summary>The game's logo or background for the interface (PNG or JPEG), or null when there is none.</summary>
    Stream? OpenArtwork(Artwork kind);
}

public enum Artwork
{
    /// <summary>Light logo on a transparent background, shown in the header.</summary>
    Logo,
    /// <summary>Picture shown faded behind the whole window.</summary>
    Background,
}

public interface IProjectCompiler
{
    BuildReport Validate(IProgress<(string Step, double Fraction)>? progress = null, CancellationToken cancellation = default);

    BuildReport Build(string output, IProgress<(string Step, double Fraction)>? progress = null,
        CancellationToken cancellation = default);
}

public interface IFontTools
{
    /// <summary>Value of "font" in a new project.</summary>
    string DefaultProfile { get; }

    /// <summary>Exports the game's fonts into the project and describes what was written.</summary>
    string Export(TranslationProject project);
}

/// <summary>The engines the tool knows.</summary>
public static class GameEngines
{
    public static readonly IReadOnlyList<IGameEngine> All = [new InnocentSinEngine(), new EternalPunishmentEngine(), new ShinMegamiTenseiEngine()];

    /// <summary>Engine of projects created before engines existed.</summary>
    public static IGameEngine Default => All[0];

    public static IGameEngine Get(string? id) =>
        string.IsNullOrEmpty(id)
            ? Default
            : All.FirstOrDefault(e => e.Id == id)
              ?? throw new InvalidDataException($"Motor \"{id}\" desconhecido. Motores disponíveis: {string.Join(", ", All.Select(e => e.Id))}.");
}
