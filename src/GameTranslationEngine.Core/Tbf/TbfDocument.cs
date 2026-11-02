using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameTranslationEngine.Core.Tbf;

/// <summary>One message of a TBF file.</summary>
public sealed class TbfEntry
{
    private readonly JsonObject _info;
    private readonly JsonObject _text;

    internal TbfEntry(int index, JsonObject info, JsonObject text)
    {
        Index = index;
        _info = info;
        _text = text;
    }

    public int Index { get; }
    public string Key => (string)_info["msg_key"]!;
    /// <summary>Script code / header that precedes the message (kept as-is).</summary>
    public string BeforeMessage => (string?)_info["before_msg"] ?? "";
    /// <summary>Control codes that end the message (kept as-is).</summary>
    public string AfterMessage => (string?)_info["after_msg"] ?? "";
    public string Original => (string)_text["before"]!;

    public string Translation
    {
        get => (string)_text["after"]!;
        set => _text["after"] = value;
    }

    public bool IsTranslated => Translation.Trim().Length > 0;
}

/// <summary>
/// A TBF translation file. Compatible with the files produced by v2:
/// the JSON is kept as a tree so unknown fields survive a round trip and
/// only the translation text is changed by the editor.
/// </summary>
public sealed class TbfDocument
{
    private readonly JsonObject _root;
    private readonly string _newLine;

    private TbfDocument(JsonObject root, string newLine, List<TbfEntry> entries)
    {
        _root = root;
        _newLine = newLine;
        Entries = entries;
    }

    public IReadOnlyList<TbfEntry> Entries { get; }

    private JsonObject Header => (JsonObject)_root["header"]!;

    /// <summary>Directory of the source file inside the extracted tree (e.g. ".../event.bin$/e101.bin$").</summary>
    public string OriginalDirectory => (string)Header["originalDirectory"]!;
    public string OriginalFilename => (string)Header["originalFilename"]!;
    public bool IsScript => !OriginalFilename.EndsWith(".msg", StringComparison.OrdinalIgnoreCase);

    /// <summary>B1 · Team note in header.context: where the scene happens.</summary>
    public string ContextLocation
    {
        get => ContextValue("location");
        set => SetContextValue("location", value);
    }

    /// <summary>B1 · Team note in header.context: what happens in the scene.</summary>
    public string ContextSummary
    {
        get => ContextValue("summary");
        set => SetContextValue("summary", value);
    }

    private string ContextValue(string key) =>
        Header["context"] is JsonObject context && context[key] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : "";

    // Other keys of the context (v2 allowed any) are kept as they are.
    private void SetContextValue(string key, string value)
    {
        if (Header["context"] is not JsonObject context) Header["context"] = context = new JsonObject();
        if (value.Length == 0) context.Remove(key);
        else context[key] = value;
    }

    public int TranslatedCount => Entries.Count(e => e.IsTranslated);

    public static TbfDocument Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"TBF inválido: JSON mal formado ({error.Message}).", error);
        }

        if (node is not JsonObject root
            || root["header"] is not JsonObject header
            || header["originalDirectory"]?.GetValueKind() != JsonValueKind.String
            || header["originalFilename"]?.GetValueKind() != JsonValueKind.String
            || root["translation"] is not JsonArray translation)
            throw new InvalidDataException("TBF inválido: cabeçalho ou lista translation ausente.");

        var entries = new List<TbfEntry>(translation.Count);
        foreach (var item in translation)
        {
            if (item is not JsonObject row
                || row["info"] is not JsonObject info
                || info["msg_key"]?.GetValueKind() != JsonValueKind.String
                || row["text"] is not JsonObject textNode
                || textNode["before"]?.GetValueKind() != JsonValueKind.String
                || textNode["after"]?.GetValueKind() != JsonValueKind.String)
                throw new InvalidDataException(
                    $"TBF inválido: mensagem {entries.Count} sem info.msg_key ou text.before/after.");
            entries.Add(new TbfEntry(entries.Count, info, textNode));
        }

        var newLine = text.Contains("\r\n") ? "\r\n" : "\n";
        return new TbfDocument(root, newLine, entries);
    }

    public static TbfDocument Load(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    /// <summary>Creates a new TBF (used by A8 when generating the translation area).</summary>
    public static TbfDocument Create(string originalDirectory, string originalFilename,
        IEnumerable<(string Key, string BeforeMessage, string Original, string AfterMessage)> messages)
    {
        var translation = new JsonArray();
        foreach (var message in messages)
        {
            translation.Add(new JsonObject
            {
                ["info"] = new JsonObject
                {
                    ["msg_key"] = message.Key,
                    ["before_msg"] = message.BeforeMessage,
                    ["after_msg"] = message.AfterMessage,
                },
                ["text"] = new JsonObject { ["before"] = message.Original, ["after"] = "" },
            });
        }
        var root = new JsonObject
        {
            ["header"] = new JsonObject
            {
                ["originalDirectory"] = originalDirectory,
                ["originalFilename"] = originalFilename,
                ["context"] = new JsonObject(),
            },
            ["translation"] = translation,
        };
        return Parse(JsJson.Stringify(root));
    }

    public string Serialize()
    {
        var json = JsJson.Stringify(_root);
        if (_newLine != "\n") json = json.Replace("\n", _newLine);
        return json + _newLine;
    }

    /// <summary>Saves atomically: a failure halfway never leaves a truncated TBF.</summary>
    public void Save(string path)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, Serialize(), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
