using System.Text.Json;
using GameTranslationEngine.Core.GameData;

namespace GameTranslationEngine.Core.Text;

/// <summary>
/// Character table of the dialogue font ("event" encoding): 16-bit code ↔ character.
/// </summary>
public sealed class EventEncoding
{
    private readonly Dictionary<int, string> _toText;
    private readonly Dictionary<string, int> _toCode;

    private EventEncoding(Dictionary<int, string> toText, Dictionary<string, int> toCode)
    {
        _toText = toText;
        _toCode = toCode;
    }

    public static EventEncoding LoadInnocentSinEnglish()
    {
        using var stream = GameResources.Open("is/encoding/en/event.json");
        return Load(stream);
    }

    /// <summary>Loads a { "hex code": "character" } table.</summary>
    public static EventEncoding Load(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        var properties = document.RootElement.EnumerateObject().ToList();
        // When a character appears twice, v2 (JavaScript) keeps the first one in
        // object order, and JS lists integer-like keys ("1120") before the others.
        var ordered = properties.Where(p => IsCanonicalInteger(p.Name))
            .OrderBy(p => long.Parse(p.Name))
            .Concat(properties.Where(p => !IsCanonicalInteger(p.Name)));

        var toText = new Dictionary<int, string>();
        var toCode = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in ordered)
        {
            var character = property.Value.GetString() ?? "";
            if (character.Length == 0) continue;
            var code = Convert.ToInt32(property.Name, 16);
            toText[code] = character;
            toCode.TryAdd(character, code);
        }
        return new EventEncoding(toText, toCode);
    }

    /// <summary>Every code with its character.</summary>
    public IReadOnlyDictionary<int, string> Codes => _toText;

    public bool TryDecode(int code, out string character) => _toText.TryGetValue(code, out character!);
    public bool TryEncode(string character, out int code) => _toCode.TryGetValue(character, out code);
    public bool CanEncode(string character) => _toCode.ContainsKey(character);

    private static bool IsCanonicalInteger(string key) =>
        key.Length > 0 && key.All(char.IsAsciiDigit) && (key == "0" || key[0] != '0') && key.Length <= 10
        && long.Parse(key) < uint.MaxValue;
}
