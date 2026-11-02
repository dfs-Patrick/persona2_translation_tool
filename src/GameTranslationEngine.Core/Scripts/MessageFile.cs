using System.Text;
using System.Text.RegularExpressions;
using GameTranslationEngine.Core.Tbf;
using GameTranslationEngine.Core.Text;

namespace GameTranslationEngine.Core.Scripts;

/// <summary>
/// The ".msg" text form of a script's messages and its conversion to TBF.
/// Both follow v2 exactly, so TBF files from v2 and v3 are interchangeable.
/// </summary>
public static partial class MessageFile
{
    /// <summary>Port of v2's writeMessageFile (with empty comments).</summary>
    public static string Write(IEnumerable<(string Name, Message Message)> messages)
    {
        var lines = new List<string> { "" };
        foreach (var (name, message) in messages)
        {
            lines.Add($"{name}:");
            lines.Add(message.ToText() + "[end]");
            lines.Add("");
        }
        return string.Join("\n", lines);
    }

    public sealed record Split(string Key, string BeforeMessage, string Text, string AfterMessage);

    /// <summary>
    /// Port of v2's splitMessages: each "key:" line starts a message; trailing
    /// control codes ([wait][clear][end]…) are kept apart from the text.
    /// </summary>
    public static List<Split> SplitMessages(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var result = new List<Split>();
        var index = 0;
        while (index < lines.Length)
        {
            var match = KeyLine().Match(lines[index]);
            if (!match.Success)
            {
                index++;
                continue;
            }
            var key = match.Groups[1].Value.Trim();
            index++;
            var body = new List<string>();
            while (index < lines.Length && !KeyLine().IsMatch(lines[index])) body.Add(lines[index++]);

            var before = string.Join("\n", body);
            var after = "";
            var suffix = TrailingCodes().Match(before);
            if (suffix.Success)
            {
                after = suffix.Groups[1].Value;
                before = before[..^after.Length];
            }
            result.Add(new Split(key, $"{key}:", before, after));
        }
        return result;
    }

    /// <summary>
    /// Port of v2's renderMessages: rebuilds the .msg text, using the
    /// translation when present (or always the original, if asked).
    /// </summary>
    public static string Render(TbfDocument document, bool useTranslation = true)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < document.Entries.Count; i++)
        {
            var entry = document.Entries[i];
            if (i > 0) builder.Append('\n');
            var text = useTranslation && entry.IsTranslated ? entry.Translation : entry.Original;
            builder.Append(entry.BeforeMessage);
            // An entry with no text and no codes only exists when splitting cut a
            // message at its first line (e.g. "> Notice:"). v2 still wrote its empty
            // line, which put an extra line break at the start of the message.
            if (text.Length + entry.AfterMessage.Length > 0) builder.Append('\n').Append(text).Append(entry.AfterMessage);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Port of v2's parseMessageFile: a "key:" line starts a message that runs
    /// until a line ending with [end]. Unlike <see cref="SplitMessages"/>, a
    /// dialogue line ending with ":" stays inside its message, which is how
    /// v2 put back together the TBF entries split at such lines.
    /// Returns the text of each message, [end] included.
    /// </summary>
    public static List<(string Name, string Text)> ParseMessages(string text)
    {
        var lines = text.Split('\n');
        var messages = new List<(string, string)>();
        var index = 0;
        while (index < lines.Length)
        {
            var line = lines[index++];
            if (!line.Trim().EndsWith(':') || line.StartsWith('#')) continue;
            var name = ReplaceFirst(line.Trim(), ":", "");
            var body = new List<string>();
            while (!line.TrimEnd().EndsWith("[end]", StringComparison.Ordinal))
            {
                if (index >= lines.Length) throw new InvalidDataException($"Mensagem {name} sem [end].");
                line = lines[index++];
                body.Add(line);
            }
            messages.Add((name, string.Join("\n", body)));
        }
        return messages;
    }

    private static string ReplaceFirst(string text, string value, string replacement)
    {
        var at = text.IndexOf(value, StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + replacement + text[(at + value.Length)..];
    }

    public static TbfDocument ToTbf(string originalDirectory, string originalFilename, string messageFileText) =>
        TbfDocument.Create(originalDirectory, originalFilename,
            SplitMessages(messageFileText).Select(s => (s.Key, s.BeforeMessage, s.Text, s.AfterMessage)));

    // JS: /^([^:#][^:]*)\s*:\s*$/ — .NET "$" also matches before a final \n, so \z is used.
    [GeneratedRegex(@"^([^:#][^:]*)\s*:\s*\z")]
    private static partial Regex KeyLine();

    // JS: /((?:\[[^\]\r\n]+\])+\s*)$/
    [GeneratedRegex(@"((?:\[[^\]\r\n]+\])+\s*)\z")]
    private static partial Regex TrailingCodes();
}
