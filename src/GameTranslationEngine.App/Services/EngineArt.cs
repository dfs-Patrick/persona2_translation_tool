using Avalonia.Media.Imaging;
using GameTranslationEngine.Core.Engines;

namespace GameTranslationEngine.App.Services;

/// <summary>Logo and background of each engine, decoded once.</summary>
public static class EngineArt
{
    private static readonly Dictionary<(string, Artwork), Bitmap?> Cache = new();

    public static Bitmap? Logo(IGameEngine engine) => Get(engine, Artwork.Logo);
    public static Bitmap? Background(IGameEngine engine) => Get(engine, Artwork.Background);

    private static Bitmap? Get(IGameEngine engine, Artwork kind)
    {
        if (Cache.TryGetValue((engine.Id, kind), out var bitmap)) return bitmap;
        try
        {
            using var stream = engine.OpenArtwork(kind);
            bitmap = stream is null ? null : new Bitmap(stream);
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
        {
            bitmap = null; // Artwork is decoration: the interface works without it.
        }
        Cache[(engine.Id, kind)] = bitmap;
        return bitmap;
    }
}
