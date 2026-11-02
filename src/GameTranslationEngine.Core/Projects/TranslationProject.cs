using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameTranslationEngine.Core.Engines;
using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.IO;
using GameTranslationEngine.Core.Iso;
using GameTranslationEngine.Core.Tbf;

namespace GameTranslationEngine.Core.Projects;

/// <summary>Contents of translation-project.json.</summary>
public sealed class ProjectFile
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public SourceIso Iso { get; set; } = new();

    /// <summary>Id of the game engine (see <see cref="GameEngines"/>). Empty in projects made before engines existed.</summary>
    public string Engine { get; set; } = "";

    /// <summary>
    /// Font profile installed by the build, as the engine reads it. Innocent Sin:
    /// "pt-br" (shipped with the tool), "original" (the game's font) or a folder,
    /// relative to the project.
    /// </summary>
    public string Font { get; set; } = "pt-br";

    public sealed class SourceIso
    {
        /// <summary>Absolute path, or relative to the project folder.</summary>
        public string Path { get; set; } = "";
        public string GameId { get; set; } = "";
        public string Title { get; set; } = "";
        public long Size { get; set; }
        public string Sha256 { get; set; } = "";
    }
}

/// <summary>A translation file of the project, with the data the browser shows.</summary>
public sealed record TbfFileInfo(
    string FullPath,
    string RelativePath,
    string Group,
    string Label,
    string DirectoryKey = "");

/// <summary>
/// A translation project: a folder holding translation-project.json, the TBF
/// files and the cache. The original ISO stays where it is and is only read.
/// </summary>
public sealed class TranslationProject
{
    public const string FileName = "translation-project.json";

    /// <summary>Name used while the tool was P2Tool; such projects open and keep their file.</summary>
    public const string LegacyFileName = "p2project.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private TranslationProject(string root, ProjectFile file, string fileName = FileName)
    {
        Root = root;
        File = file;
        Engine = GameEngines.Get(file.Engine);
        ProjectFilePath = Path.Combine(root, fileName);
    }

    public string Root { get; }
    public ProjectFile File { get; }
    public IGameEngine Engine { get; }
    public string Name => File.Name;
    public string ProjectFilePath { get; }
    public string TranslationDirectory => Path.Combine(Root, "translation");
    public string MessagesDirectory => Path.Combine(TranslationDirectory, "messages");
    public string CacheDirectory => Path.Combine(Root, "cache");
    public string BuildDirectory => Path.Combine(Root, "build");

    /// <summary>Suggested name for the translated ISO.</summary>
    public string DefaultOutputIso => Path.Combine(BuildDirectory,
        string.Concat(Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim() is { Length: > 0 } safe
            ? $"{safe}.iso" : "traducao.iso");

    public string IsoPath => Path.IsPathRooted(File.Iso.Path)
        ? File.Iso.Path
        : Path.GetFullPath(Path.Combine(Root, File.Iso.Path));

    /// <summary>
    /// Creates a project for <paramref name="isoPath"/> after the engine checks it (A1).
    /// </summary>
    public static async Task<TranslationProject> CreateAsync(string root, string name, string isoPath, IGameEngine? engine = null,
        IProgress<(string Step, double Fraction)>? progress = null, CancellationToken cancellation = default)
    {
        engine ??= GameEngines.Default;
        root = Path.GetFullPath(root);
        if (FindProjectFile(root) is not null)
            throw new InvalidOperationException($"Já existe um projeto em {root}.");

        progress?.Report(("Verificando a ISO", 0));
        IsoIdentity identity;
        using (var index = IsoIndex.Read(DiscImage.Open(isoPath)))
            identity = IsoVerifier.Identify(index);
        if (!engine.Supports(identity))
            throw new InvalidDataException(
                $"ISO não suportada pelo motor {engine.Name}: {engine.DescribeGame(identity.GameId)}. Use a ISO original de {engine.ExpectedIso}.");

        var sha = await IsoVerifier.ComputeSha256Async(DiscImage.ResolveDataFile(isoPath),
            new Progress<double>(f => progress?.Report(("Calculando o hash da ISO", f))), cancellation);

        Directory.CreateDirectory(root);
        var file = new ProjectFile
        {
            Name = name,
            Engine = engine.Id,
            Font = engine.Fonts?.DefaultProfile ?? "",
            Iso = new ProjectFile.SourceIso
            {
                Path = RelativeIfInside(root, Path.GetFullPath(isoPath)),
                GameId = identity.GameId,
                Title = identity.Title,
                Size = DiscImage.PhysicalSize(isoPath),
                Sha256 = sha,
            },
        };
        var project = new TranslationProject(root, file);
        Directory.CreateDirectory(project.MessagesDirectory);
        Directory.CreateDirectory(project.CacheDirectory);
        project.EnsureTranslationIndexes();
        project.Save();

        progress?.Report(("Gerando os arquivos de tradução", 0));
        project.GenerateTranslationArea(
            new Progress<double>(f => progress?.Report(("Gerando os arquivos de tradução", f))), cancellation);
        return project;
    }

    public static TranslationProject Open(string path)
    {
        var file = Directory.Exists(path) ? FindProjectFile(path) ?? Path.Combine(path, FileName) : path;
        if (!System.IO.File.Exists(file)) throw new FileNotFoundException($"Projeto não encontrado: {file}");
        var data = JsonSerializer.Deserialize<ProjectFile>(System.IO.File.ReadAllText(file), JsonOptions)
                   ?? throw new InvalidDataException($"{Path.GetFileName(file)} vazio ou inválido.");
        var full = Path.GetFullPath(file);
        var project = new TranslationProject(Path.GetDirectoryName(full)!, data, Path.GetFileName(full));
        Directory.CreateDirectory(project.MessagesDirectory);
        project.EnsureTranslationIndexes();
        return project;
    }

    /// <summary>The project file of a folder: the current name, or the one from the P2Tool days.</summary>
    public static string? FindProjectFile(string folder) =>
        new[] { FileName, LegacyFileName }.Select(n => Path.Combine(folder, n)).FirstOrDefault(System.IO.File.Exists);

    public void Save() =>
        System.IO.File.WriteAllText(ProjectFilePath, JsonSerializer.Serialize(File, JsonOptions));

    /// <summary>
    /// Quick check that the ISO is still the one the project was created with
    /// (existence and size; the full hash is only computed on creation).
    /// </summary>
    public string? CheckIso()
    {
        if (!System.IO.File.Exists(IsoPath)) return $"ISO não encontrada: {IsoPath}";
        var size = DiscImage.PhysicalSize(IsoPath);
        return size == File.Iso.Size
            ? null
            : $"A ISO em {IsoPath} tem {size} bytes, mas o projeto foi criado com uma de {File.Iso.Size} bytes.";
    }

    public IsoIndex OpenIso() => IsoIndex.Read(DiscImage.Open(IsoPath));

    /// <summary>
    /// A2–A8 · Reads the text of the game straight from the ISO and writes the
    /// TBF files that are still missing. Existing TBF files are never touched.
    /// </summary>
    public TranslationAreaReport GenerateTranslationArea(IProgress<double>? progress = null,
        CancellationToken cancellation = default)
    {
        using var iso = OpenIso();
        return Engine.GenerateTranslationArea(iso, MessagesDirectory, progress, cancellation);
    }

    /// <summary>C1–C9 · Compiler of this project's translation into an ISO.</summary>
    public IProjectCompiler CreateBuilder() => Engine.CreateCompiler(this);

    public string FontsDirectory => Path.Combine(Root, "fonts");

    /// <summary>B2 · Reference terms of the translation.</summary>
    public string GlossaryPath => Path.Combine(TranslationDirectory, "glossary.json");

    /// <summary>B3 · Words the spell checker accepts in this project, one per line.</summary>
    public string DictionaryPath => Path.Combine(TranslationDirectory, "dictionary.txt");
    public string DirectoryActiveIndexPath => Path.Combine(TranslationDirectory, "directory-active-index.json");
    public string ImageTranslationControlPath => Path.Combine(TranslationDirectory, "image-translation-control.json");

    public DirectoryActiveIndex LoadDirectoryActiveIndex() =>
        ProjectIndexes.LoadDirectories(DirectoryActiveIndexPath);

    public ImageTranslationControl LoadImageTranslationControl() =>
        ProjectIndexes.LoadImages(ImageTranslationControlPath);

    private void EnsureTranslationIndexes()
    {
        _ = LoadDirectoryActiveIndex();
        _ = LoadImageTranslationControl();
    }

    public IReadOnlyList<TbfFileInfo> ListTranslationFiles()
    {
        if (!Directory.Exists(MessagesDirectory)) return [];
        return Directory.EnumerateFiles(MessagesDirectory, "*.tbf", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => Describe(path))
            .ToList();
    }

    /// <summary>
    /// Copies TBF files from another folder (e.g. a v2 workspace) into the
    /// project. Existing files are never overwritten.
    /// </summary>
    public (int Copied, int Skipped, IReadOnlyList<string> Invalid) ImportTbf(string sourceDirectory)
    {
        var copied = 0;
        var skipped = 0;
        var invalid = new List<string>();
        foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*.tbf", SearchOption.AllDirectories))
        {
            try
            {
                TbfDocument.Load(source);
            }
            catch (InvalidDataException error)
            {
                invalid.Add($"{source}: {error.Message}");
                continue;
            }
            var target = Path.Combine(MessagesDirectory, Path.GetRelativePath(sourceDirectory, source));
            if (System.IO.File.Exists(target))
            {
                skipped++;
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            System.IO.File.Copy(source, target);
            copied++;
        }
        return (copied, skipped, invalid);
    }

    private TbfFileInfo Describe(string path)
    {
        var relative = Path.GetRelativePath(MessagesDirectory, path).Replace('\\', '/');
        string group = "Outros", label = Path.GetFileNameWithoutExtension(path);
        string originalDirectory = "";
        try
        {
            using var stream = System.IO.File.OpenRead(path);
            using var json = JsonDocument.Parse(stream);
            var header = json.RootElement.GetProperty("header");
            originalDirectory = header.GetProperty("originalDirectory").GetString() ?? "";
            (group, label) = Engine.DescribeTbf(
                originalDirectory,
                header.GetProperty("originalFilename").GetString() ?? "");
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Invalid files still show up; opening them reports the problem.
        }
        return new TbfFileInfo(path, relative, group, label, DirectoryKey(originalDirectory));
    }

    private static string DirectoryKey(string? headerDirectory)
    {
        if (string.IsNullOrWhiteSpace(headerDirectory)) return "";
        var parts = headerDirectory.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.TrimEnd('$')).ToList();
        var cpk = parts.FindIndex(part => part.EndsWith(".cpk", StringComparison.OrdinalIgnoreCase));
        return cpk >= 0 && cpk + 1 < parts.Count ? parts[cpk + 1] : parts.FirstOrDefault() ?? "";
    }

    private static string RelativeIfInside(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.StartsWith("..") || Path.IsPathRooted(relative) ? path : relative;
    }
}
