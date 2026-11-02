using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameTranslationEngine.Core.Tbf;

/// <summary>
/// Writes JSON exactly like JavaScript's JSON.stringify(value, null, 2), which
/// produced the v2 TBF files. System.Text.Json escapes characters outside the
/// basic plane (e.g. the musical note in song lyrics); JSON.stringify does not.
/// </summary>
public static class JsJson
{
    public static string Stringify(JsonNode? node)
    {
        var builder = new StringBuilder();
        Write(builder, node, 0);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, JsonNode? node, int depth)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;
            case JsonObject obj:
                if (obj.Count == 0) { builder.Append("{}"); break; }
                builder.Append("{\n");
                var first = true;
                foreach (var (key, value) in obj)
                {
                    if (!first) builder.Append(",\n");
                    first = false;
                    Indent(builder, depth + 1);
                    WriteString(builder, key);
                    builder.Append(": ");
                    Write(builder, value, depth + 1);
                }
                builder.Append('\n');
                Indent(builder, depth);
                builder.Append('}');
                break;
            case JsonArray array:
                if (array.Count == 0) { builder.Append("[]"); break; }
                builder.Append("[\n");
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0) builder.Append(",\n");
                    Indent(builder, depth + 1);
                    Write(builder, array[i], depth + 1);
                }
                builder.Append('\n');
                Indent(builder, depth);
                builder.Append(']');
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                WriteString(builder, value.GetValue<string>());
                break;
            default:
                builder.Append(node.ToJsonString());
                break;
        }
    }

    private static void Indent(StringBuilder builder, int depth) => builder.Append(' ', depth * 2);

    private static void WriteString(StringBuilder builder, string text)
    {
        builder.Append('"');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        builder.Append("\\u").Append(((int)c).ToString("x4"));
                    else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                        builder.Append(c).Append(text[++i]);
                    else if (char.IsSurrogate(c))
                        builder.Append("\\u").Append(((int)c).ToString("x4")); // lone surrogate
                    else
                        builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }
}
