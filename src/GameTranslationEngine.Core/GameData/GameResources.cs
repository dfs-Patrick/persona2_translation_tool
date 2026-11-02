using System.Reflection;
using System.Text.Json;

namespace GameTranslationEngine.Core.GameData;

/// <summary>
/// Game knowledge shipped inside the tool (from data/): character tables,
/// constant names and the catalog of text sources.
/// </summary>
public static class GameResources
{
    private static readonly Assembly Assembly = typeof(GameResources).Assembly;

    public static Stream Open(string path)
    {
        // RecursiveDir keeps the build machine's separator, so both forms are tried.
        var name = "data/" + path.Replace('\\', '/');
        return Assembly.GetManifestResourceStream(name)
               ?? Assembly.GetManifestResourceStream(name.Replace('/', '\\').Replace("data\\", "data/"))
               ?? throw new FileNotFoundException($"Recurso interno ausente: {name}");
    }

    public static JsonDocument ReadJson(string path)
    {
        using var stream = Open(path);
        return JsonDocument.Parse(stream);
    }
}

/// <summary>
/// Named constants used inside messages and scripts (event bits, items…).
/// Values without a known name become "&lt;kind&gt;_&lt;hex&gt;", like v2.
/// </summary>
public sealed class ConstantTables
{
    private readonly Dictionary<string, Dictionary<int, string>> _fromBin = new();
    private readonly Dictionary<string, Dictionary<string, int>> _toBin = new();

    public static ConstantTables LoadInnocentSin()
    {
        var tables = new ConstantTables();
        using var bits = GameResources.ReadJson("is/context/bit.json");
        tables.Add("bit", bits.RootElement.EnumerateArray().Select(e => e.GetString()!).ToList());
        return tables;
    }

    private void Add(string kind, IReadOnlyList<string> names)
    {
        var from = new Dictionary<int, string>();
        var to = new Dictionary<string, int>();
        for (var i = 0; i < names.Count; i++)
        {
            if (!to.TryAdd(names[i], i)) throw new InvalidDataException($"Constante duplicada em {kind}: {names[i]}");
            from[i] = names[i];
        }
        _fromBin[kind] = from;
        _toBin[kind] = to;
    }

    public string Name(string kind, int value) =>
        _fromBin.TryGetValue(kind, out var table) && table.TryGetValue(value, out var name)
            ? name
            : $"{kind}_{value:x}";

    public int Value(string kind, string name)
    {
        if (_toBin.TryGetValue(kind, out var table) && table.TryGetValue(name, out var value)) return value;
        var prefix = kind + "_";
        if (name.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(name.LastIndexOf('_') + 1), System.Globalization.NumberStyles.HexNumber, null, out value))
            return value;
        throw new InvalidDataException($"Constante desconhecida do tipo {kind}: {name}");
    }
}
