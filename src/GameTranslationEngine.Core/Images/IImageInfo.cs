namespace GameTranslationEngine.Core.Images;

/// <summary>What the image viewer shows about a game image, whatever its format.</summary>
public interface IImageInfo
{
    int Width { get; }
    int Height { get; }
    int ColorCount { get; }
    int BitsPerPixel { get; }
    string PixelFormat { get; }
    string PaletteFormat { get; }
    bool IsSwizzled { get; }

    /// <summary>Name of the colour profile, or null when the format cannot embed one.</summary>
    string ColorProfile { get; }

    /// <summary>"Swizzled…"/"Linear…" text for the viewer's grid line.</summary>
    string GridDescription { get; }
}
