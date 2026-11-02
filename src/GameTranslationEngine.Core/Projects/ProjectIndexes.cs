using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameTranslationEngine.Core.Projects;

public sealed class DirectoryActiveIndex
{
    public Dictionary<string, DirectoryActiveEntry> Directories { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DirectoryActiveEntry
{
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class ImageTranslationControl
{
    public Dictionary<string, ImageTranslationEntry> Images { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ImageTranslationEntry
{
    /// <summary>Replacement file path, relative to the project root.</summary>
    public string? Replacement { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>Reads and writes the two editable indices kept in translation/.</summary>
public static class ProjectIndexes
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static DirectoryActiveIndex LoadDirectories(string path) =>
        Load(path, static () => new DirectoryActiveIndex());

    public static ImageTranslationControl LoadImages(string path) =>
        Load(path, static () => new ImageTranslationControl());

    public static void Save(string path, DirectoryActiveIndex index) => SaveJson(path, index);
    public static void Save(string path, ImageTranslationControl index) => SaveJson(path, index);

    private static T Load<T>(string path, Func<T> create) where T : class
    {
        if (!File.Exists(path))
        {
            var initial = create();
            SaveJson(path, initial);
            return initial;
        }

        var index = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
                    ?? throw new InvalidDataException($"Índice vazio ou inválido: {path}");
        if (index is DirectoryActiveIndex directories)
            directories.Directories = new Dictionary<string, DirectoryActiveEntry>(
                directories.Directories ?? [], StringComparer.OrdinalIgnoreCase);
        if (index is ImageTranslationControl images)
            images.Images = new Dictionary<string, ImageTranslationEntry>(
                images.Images ?? [], StringComparer.OrdinalIgnoreCase);
        return index;
    }

    private static void SaveJson<T>(string path, T index)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(index, Options));
    }
}
