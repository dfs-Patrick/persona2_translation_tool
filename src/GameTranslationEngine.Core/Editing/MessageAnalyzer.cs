namespace GameTranslationEngine.Core.Editing;

public enum SpanKind
{
    /// <summary>A known game command, e.g. [color(name_green)].</summary>
    Command,
    /// <summary>A command the engine does not know, or a "[" without "]".</summary>
    InvalidCommand,
    /// <summary>\[, \\, "\ " and \xHHHH.</summary>
    Escape,
    Glossary,
    Misspelled,
    /// <summary>An occurrence of the text of the global search (B5).</summary>
    Search,
}

/// <summary>A marked piece of a message; <see cref="Term"/> is set for glossary spans.</summary>
public sealed record TextSpan(int Start, int Length, SpanKind Kind, GlossaryTerm? Term = null)
{
    public int End => Start + Length;
    public bool Contains(int offset) => offset >= Start && offset < End;
}

/// <summary>
/// B4 · Splits a message into what the editor marks: commands (from the
/// engine's list), glossary terms and, in the translation, misspelled words.
/// Follows the text form read by Message.Parse: [name(args)] and the escapes.
/// </summary>
public sealed class MessageAnalyzer(IReadOnlyCollection<string> commandNames, Glossary? glossary, SpellChecker? speller)
{
    public Glossary? Glossary { get; } = glossary;
    public SpellChecker? Speller { get; } = speller;

    public IReadOnlyList<TextSpan> Analyze(string text, bool isTranslation)
    {
        var spans = Commands(text);
        var code = spans.ToList();
        bool InCode(int start, int length) => code.Any(c => start < c.End && c.Start < start + length);

        if (Glossary is not null)
            foreach (var match in isTranslation ? Glossary.FindTranslation(text) : Glossary.FindOriginal(text))
                if (!InCode(match.Start, match.Length))
                    spans.Add(new TextSpan(match.Start, match.Length, SpanKind.Glossary, match.Term));

        if (isTranslation && Speller is not null)
        {
            var terms = spans.Where(s => s.Kind == SpanKind.Glossary).ToList();
            foreach (System.Text.RegularExpressions.Match word in SpellChecker.WordPattern().Matches(text))
            {
                if (InCode(word.Index, word.Length)
                    || terms.Any(t => word.Index < t.End && t.Start < word.Index + word.Length)
                    || Speller.Check(word.Value))
                    continue;
                spans.Add(new TextSpan(word.Index, word.Length, SpanKind.Misspelled));
            }
        }
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return spans;
    }

    private List<TextSpan> Commands(string text)
    {
        var spans = new List<TextSpan>();
        var index = 0;
        while (index < text.Length)
        {
            var c = text[index];
            if (c == '\\' && index + 1 < text.Length)
            {
                var length = text[index + 1] == 'x' ? Math.Min(6, text.Length - index) : 2;
                spans.Add(new TextSpan(index, length, SpanKind.Escape));
                index += length;
            }
            else if (c == '[')
            {
                var close = text.IndexOf(']', index + 1);
                var lineEnd = text.IndexOf('\n', index + 1);
                if (close < 0 || (lineEnd >= 0 && lineEnd < close))
                {
                    // Message.Parse would fail here: mark up to the end of the line.
                    var end = lineEnd < 0 ? text.Length : lineEnd;
                    spans.Add(new TextSpan(index, end - index, SpanKind.InvalidCommand));
                    index = end;
                    continue;
                }
                var body = text.AsSpan(index + 1, close - index - 1);
                var paren = body.IndexOf('(');
                var name = (paren < 0 ? body : body[..paren]).Trim().ToString();
                var valid = commandNames.Contains(name) && (paren < 0 || body.EndsWith(")"));
                spans.Add(new TextSpan(index, close - index + 1, valid ? SpanKind.Command : SpanKind.InvalidCommand));
                index = close + 1;
            }
            else
            {
                index++;
            }
        }
        return spans;
    }
}
