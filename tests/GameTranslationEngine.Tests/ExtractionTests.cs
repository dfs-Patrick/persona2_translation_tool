using System.Diagnostics;
using GameTranslationEngine.Core.Extraction;
using GameTranslationEngine.Core.GameData;
using GameTranslationEngine.Core.Scripts;
using GameTranslationEngine.Core.Text;
using Xunit.Abstractions;

namespace GameTranslationEngine.Tests;

public partial class ExtractionTests(ITestOutputHelper output)
{
    private static readonly Lazy<IReadOnlyList<ExtractedText>> Extracted = new(() =>
        new TextExtractor(TestIso.Index!).ExtractAll());

    [IsoFact]
    public void A4_A6_extracts_all_617_text_sources()
    {
        var watch = Stopwatch.StartNew();
        var texts = new TextExtractor(TestIso.Index!).ExtractAll();
        output.WriteLine($"Extração: {watch.ElapsedMilliseconds} ms, {texts.Sum(t => t.Messages.Count)} mensagens");
        Assert.Equal(617, texts.Count);
        Assert.True(texts.Sum(t => t.Messages.Count) > 10000);
    }

    [IsoFact]
    public void Every_message_reencodes_to_the_original_bytes()
    {
        var context = new MessageContext(EventEncoding.LoadInnocentSinEnglish(), ConstantTables.LoadInnocentSin());
        var failures = new List<string>();
        foreach (var text in Extracted.Value)
        {
            for (var i = 0; i < text.Messages.Count; i++)
            {
                var (name, message) = text.Messages[i];
                var fromText = Message.Parse(message.ToText() + "[end]");
                var encoded = fromText.EncodeBytes(context);
                if (!encoded.AsSpan().SequenceEqual(text.OriginalBytes[i]))
                {
                    // Two codes can share a character (the game has two spaces); the
                    // text must still read back the same.
                    if (Message.Decode(encoded, context, out _).ToText() == message.ToText())
                    {
                        output.WriteLine($"Equivalente (bytes diferentes, mesmo texto): {text.OriginalDirectory} {name}");
                        continue;
                    }
                    var original = text.OriginalBytes[i];
                    var at = Enumerable.Range(0, Math.Min(encoded.Length, original.Length)).FirstOrDefault(k => encoded[k] != original[k]) & ~1;
                    string Hex(byte[] b) => Convert.ToHexString(b.AsSpan(Math.Max(0, at - 6), Math.Min(16, b.Length - Math.Max(0, at - 6))));
                    var shown = message.ToText().Replace("\n", "\\n");
                    failures.Add($"{text.OriginalDirectory} {name} @{at}: original {Hex(original)} | v3 {Hex(encoded)} | «{shown[..Math.Min(120, shown.Length)]}»");
                }
            }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} mensagens diferentes:\n" + string.Join("\n", failures.Take(20)));
    }

    /// <summary>
    /// Compares with the TBF files v2 generated from the same ISO. Set
    /// P2_V2_REFERENCE to v2's "translation/en/new/messages" folder.
    /// </summary>
    [IsoFact]
    public void A8_tbf_files_are_identical_to_v2()
    {
        var reference = Environment.GetEnvironmentVariable("P2_V2_REFERENCE");
        if (string.IsNullOrEmpty(reference) || !Directory.Exists(reference))
        {
            output.WriteLine("P2_V2_REFERENCE não definido; comparação com a v2 pulada.");
            return;
        }
        var expected = new Dictionary<string, string>();
        foreach (var path in Directory.EnumerateFiles(reference, "*.msg.tbf"))
        {
            var text = File.ReadAllText(path);
            var doc = GameTranslationEngine.Core.Tbf.TbfDocument.Parse(text);
            expected[$"{doc.OriginalDirectory}/{doc.OriginalFilename}"] = text;
        }

        var mismatches = new List<string>();
        var itemFixes = 0;
        foreach (var text in Extracted.Value)
        {
            var key = $"{text.OriginalDirectory}/{text.OriginalFilename}";
            if (!expected.Remove(key, out var v2))
            {
                mismatches.Add($"ausente na v2: {key}");
                continue;
            }
            var v3 = MessageFile.ToTbf(text.OriginalDirectory, text.OriginalFilename, text.MessageFileText).Serialize();
            // v3 reads the real item of [item_with_type(...)]; v2 always wrote item_0.
            if (v3 != v2 && ItemArgument().Replace(v3, "item_with_type(item)") == ItemArgument().Replace(v2, "item_with_type(item)"))
            {
                itemFixes++;
                continue;
            }
            if (v3 != v2)
            {
                var at = Enumerable.Range(0, Math.Min(v2.Length, v3.Length)).FirstOrDefault(i => v2[i] != v3[i]);
                mismatches.Add($"{key} difere em {at}: v2=«{Snippet(v2, at)}» v3=«{Snippet(v3, at)}»");
            }
        }
        mismatches.AddRange(expected.Keys.Select(k => $"ausente na v3: {k}"));
        output.WriteLine($"{itemFixes} arquivo(s) diferem só pela correção de item_with_type.");
        Assert.True(mismatches.Count == 0, $"{mismatches.Count} diferenças:\n" + string.Join("\n", mismatches.Take(15)));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"item_with_type\(item_[0-9a-f]+\)")]
    private static partial System.Text.RegularExpressions.Regex ItemArgument();

    private static string Snippet(string text, int at) =>
        text.Substring(Math.Max(0, at - 30), Math.Min(80, text.Length - Math.Max(0, at - 30))).Replace("\n", "\n");
}
