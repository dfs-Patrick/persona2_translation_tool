using System.Text;
using System.Text.Json;
using GameTranslationEngine.Core.GameData;

namespace GameTranslationEngine.Core.Smt;

/// <summary>
/// Token ↔ character table of the game's 12×12 font (data/smt/charmap.json).
/// Tokens with no known character, and characters drawn by more than one
/// token, are written as {gXXX} so that decoding is always reversible.
/// </summary>
public sealed class SmtCharMap
{
    private readonly Dictionary<int, string> _toText = [];
    private readonly Dictionary<string, int> _toToken = [];

    public static SmtCharMap Load()
    {
        using var json = GameResources.ReadJson("smt/charmap.json");
        var map = new SmtCharMap();
        foreach (var pair in json.RootElement.GetProperty("tokens").EnumerateObject())
        {
            var token = int.Parse(pair.Name);
            var text = pair.Value.GetString();
            if (string.IsNullOrEmpty(text)) continue;
            map._toText[token] = text;
            if (!map._toToken.TryGetValue(text, out var known) || token < known) map._toToken[text] = token;
        }
        // The Latin glyphs are installed in the font by the compiler; a space is the blank token.
        map._toToken[" "] = 0x2f;
        foreach (var (character, token) in SmtLatinFont.Load().Chars)
        {
            map._toToken[character] = token;
            map._toText[token] = character;
        }
        return map;
    }

    public int TokenCount => _toText.Count;

    /// <summary>Text of a glyph token: the character, or {gXXX} when it cannot round-trip.</summary>
    public string Decode(int token)
    {
        if (_toText.TryGetValue(token, out var text)
            && _toToken[text] == token
            && text.Length == 1 && text[0] is not ('[' or '{' or '\n' or '\r'))
            return text;
        return $"{{g{token:x}}}";
    }

    public bool TryEncode(string character, out int token) => _toToken.TryGetValue(character, out token);
}

/// <summary>
/// Message text of the game's scripts (SC02) as a list of 16-bit words:
/// glyph tokens below 0x800, control codes from 0x800, 0x805 ends a message.
/// </summary>
public static class SmtMessageCodec
{
    public const int NewLine = 0x800;
    public const int End = 0x805;

    /// <summary>Words of one message, end marker excluded, as editable text.</summary>
    public static string Decode(ReadOnlySpan<ushort> words, SmtCharMap map)
    {
        var text = new StringBuilder();
        foreach (var word in words)
        {
            if (word == NewLine) text.Append('\n');
            else if (word >= 0x800) text.Append($"[c{word:x}]");
            else text.Append(map.Decode(word));
        }
        return text.ToString();
    }

    public static List<ushort> Encode(string text, SmtCharMap map)
    {
        var words = new List<ushort>();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r') continue;
            if (c == '\n') { words.Add(NewLine); continue; }
            if ((c == '[' || c == '{') && TryTag(text, i, out var value, out var length))
            {
                words.Add((ushort)value);
                i += length - 1;
                continue;
            }
            var unit = char.IsHighSurrogate(c) && i + 1 < text.Length ? text.Substring(i, 2) : c.ToString();
            if (!map.TryEncode(unit, out var token))
                throw new InvalidDataException($"O caractere \"{unit}\" não existe na fonte do jogo.");
            words.Add((ushort)token);
            i += unit.Length - 1;
        }
        return words;
    }

    private static bool TryTag(string text, int start, out int value, out int length)
    {
        value = 0; length = 0;
        var close = text[start] == '[' ? ']' : '}';
        var prefix = text[start] == '[' ? 'c' : 'g';
        var end = text.IndexOf(close, start + 1);
        if (end < 0 || end - start < 3 || text[start + 1] != prefix) return false;
        if (!int.TryParse(text.AsSpan(start + 2, end - start - 2), System.Globalization.NumberStyles.HexNumber, null, out value))
            return false;
        length = end - start + 1;
        return value is >= 0 and <= 0xFFFF;
    }
}
