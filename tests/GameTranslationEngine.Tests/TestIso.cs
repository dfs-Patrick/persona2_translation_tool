using GameTranslationEngine.Core.IO;
using GameTranslationEngine.Core.Iso;

namespace GameTranslationEngine.Tests;

/// <summary>
/// Locates the user's original ISO (lab/p2is.iso). The ISO is not part of
/// the repository, so tests that need it are skipped when it is absent.
/// </summary>
public static class TestIso
{
    private static readonly Lazy<string?> PathLazy = new(() =>
    {
        var env = Environment.GetEnvironmentVariable("P2_ISO");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "lab", "p2is.iso");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    });

    private static readonly Lazy<IsoIndex?> IndexLazy = new(() =>
        PathLazy.Value is { } path ? IsoIndex.Read(new FileByteSource(path)) : null);

    public static string? Path => PathLazy.Value;
    public static IsoIndex? Index => IndexLazy.Value;

    private static readonly Lazy<string?> EternalPunishmentPathLazy = new(() =>
    {
        var env = Environment.GetEnvironmentVariable("P2_EP_ISO");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "lab", "p2ep.iso");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    });

    public static string? EternalPunishmentPath => EternalPunishmentPathLazy.Value;
}

public sealed class IsoFactAttribute : FactAttribute
{
    public IsoFactAttribute()
    {
        if (TestIso.Path is null) Skip = "ISO original não encontrada em lab/p2is.iso (ou na variável P2_ISO).";
    }
}

public sealed class EternalPunishmentIsoFactAttribute : FactAttribute
{
    public EternalPunishmentIsoFactAttribute()
    {
        if (TestIso.EternalPunishmentPath is null)
            Skip = "ISO original do Eternal Punishment não encontrada em lab/p2ep.iso (ou na variável P2_EP_ISO).";
    }
}
