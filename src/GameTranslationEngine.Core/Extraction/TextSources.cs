using System.Text.Json;
using GameTranslationEngine.Core.GameData;

namespace GameTranslationEngine.Core.Extraction;

/// <summary>One level of the path to a text file: a CPK entry, a container or the script itself.</summary>
/// <param name="Compressed">Children of this container are gzip-compressed (event.bin).</param>
public sealed record SourceStep(string Name, string Type, int? Index, int? CpkId, bool Compressed = false)
{
    /// <summary>File name as v2 extracted it: the type is appended when the name has no extension.</summary>
    public string FileName => Type == "folder" || Path.HasExtension(Name) ? Name : $"{Name}.{Type}";
}

/// <summary>A file of the game that holds dialogue (EFB script or the persona demo).</summary>
public sealed record TextSource(string Type, IReadOnlyList<SourceStep> Chain)
{
    public SourceStep Entry => Chain[0];

    /// <summary>Directory in v2's extracted tree, stored in the TBF header.</summary>
    public string OriginalDirectory(string cpkPath) =>
        cpkPath + "$/" + string.Join("/", Chain.Select(s => s.FileName + "$"));

    public string OriginalFilename => Type == "pdemo" ? "pdemo.msg" : "script.msg";

    /// <summary>Path of the TBF inside the project, e.g. "event.bin/e0000.tbf".</summary>
    public string TbfPath
    {
        get
        {
            if (Type == "pdemo") return $"{Entry.Name}/pdemo.tbf";
            // Name after the containers between the CPK entry and the script
            // (event.bin/e0000.bin/8 → e0000); the script itself when there are none.
            var inner = Chain.Count > 2 ? Chain.Skip(1).SkipLast(1) : Chain.Skip(1);
            return $"{Entry.Name}/{string.Join("_", inner.Select(s => Path.GetFileNameWithoutExtension(s.Name)))}.tbf";
        }
    }
}

/// <summary>Catalog of the text files of a game (data/&lt;game&gt;/text_sources.json).</summary>
public sealed class TextSourceCatalog
{
    private TextSourceCatalog(string cpkPath, IReadOnlyList<TextSource> sources)
    {
        CpkPath = cpkPath;
        Sources = sources;
    }

    public string CpkPath { get; }
    public IReadOnlyList<TextSource> Sources { get; }

    public static TextSourceCatalog LoadInnocentSin() => Load("is");

    /// <summary>Catalog shipped in data/<paramref name="game"/>/text_sources.json.</summary>
    public static TextSourceCatalog Load(string game)
    {
        using var document = GameResources.ReadJson($"{game}/text_sources.json");
        var root = document.RootElement;
        var sources = root.GetProperty("sources").EnumerateArray().Select(source => new TextSource(
            source.GetProperty("type").GetString()!,
            source.GetProperty("chain").EnumerateArray().Select(step => new SourceStep(
                step.GetProperty("name").GetString()!,
                step.GetProperty("type").GetString()!,
                step.TryGetProperty("index", out var index) ? index.GetInt32() : null,
                step.TryGetProperty("cpkId", out var id) ? id.GetInt32() : null,
                step.TryGetProperty("compressed", out var compressed) && compressed.GetBoolean())).ToList())).ToList();
        return new TextSourceCatalog(root.GetProperty("cpk").GetString()!, sources);
    }
}
