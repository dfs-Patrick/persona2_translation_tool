using System.Text.Json;

namespace GameTranslationEngine.App.Services;

/// <summary>Per-user settings (recent projects), stored in the user's app data folder.</summary>
public sealed class AppSettings
{
    private const int MaxRecent = 8;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameTranslationEngine", "settings.json");

    /// <summary>Where the settings lived while the tool was P2Tool; read once if the new file does not exist yet.</summary>
    private static readonly string LegacyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "P2Tool", "settings.json");

    public List<string> RecentProjects { get; set; } = [];
    public string? LibreSpritePath { get; set; }
    public string? GimpPath { get; set; }

    public static AppSettings Load()
    {
        try
        {
            var path = File.Exists(FilePath) ? FilePath : LegacyFilePath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            // Corrupted settings are not worth blocking the app; start fresh.
        }
        return new AppSettings();
    }

    public void AddRecent(string projectFile)
    {
        RecentProjects.RemoveAll(p => string.Equals(p, projectFile, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, projectFile);
        if (RecentProjects.Count > MaxRecent) RecentProjects.RemoveRange(MaxRecent, RecentProjects.Count - MaxRecent);
        Save();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
