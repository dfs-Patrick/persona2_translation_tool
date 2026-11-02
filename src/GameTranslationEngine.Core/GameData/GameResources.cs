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

    /// <summary>Eternal Punishment: event bits and item names, as { "0x0001": "name" } tables from v2.</summary>
    public static ConstantTables LoadEternalPunishment()
    {
        var tables = new ConstantTables();
        foreach (var kind in new[] { "bit", "item" })
        {
            using var json = GameResources.ReadJson($"ep/context/{kind}.json");
            tables.Add(kind, json.RootElement.EnumerateObject().ToDictionary(
                p => Convert.ToInt32(p.Name, 16), p => p.Value.GetString()!));
        }
        return tables;
    }

    private void Add(string kind, IReadOnlyList<string> names) =>
        Add(kind, names.Select((name, value) => (value, name)).ToDictionary(p => p.value, p => p.name));

    private void Add(string kind, IReadOnlyDictionary<int, string> names)
    {
        var from = new Dictionary<int, string>();
        var to = new Dictionary<string, int>();
        foreach (var (value, name) in names)
        {
            if (!to.TryAdd(name, value)) throw new InvalidDataException($"Constante duplicada em {kind}: {name}");
            from[value] = name;
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
