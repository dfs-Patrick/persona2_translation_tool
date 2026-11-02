using System.Buffers.Binary;

namespace GameTranslationEngine.Core.Smt;

/// <summary>
/// Name-entry keyboard in Portuguese. DATA.BIN entry 24 holds three modes
/// (hiragana, katakana, alphanumeric), each with two screens of 5 × 10 tokens
/// (two blocks of five per row, scrolled with L1/R1) ended by a zero; entry 6, the keyboard overlay, starts with the zero-terminated
/// labels of the modes and buttons. Both are rewritten in place.
/// </summary>
public static class SmtKeyboard
{
    public const int PagesEntry = 24;
    public const int OverlayEntry = 6;

    private const int FirstScreen = 0x0e;
    private const int ScreenStride = 102;  // 50 tokens + terminator
    private const int ScreenSize = 50;
    private const int Blank = 0x2f;

    private static readonly string[][] Pages =
    [
        ["ABCDEFGHIJ", "KLMNOPQRST", "UVWXYZÇ.-'", "ÁÀÂÃÉÊÍÓÔÕ", "Ú&!?"],
        ["abcdefghij", "klmnopqrst", "uvwxyzç.-'", "áàâãéêíóôõ", "úü!?"],
        ["0123456789", ".,!?:;-'\"(", ")/%+…&*", "♂♀〜＋―＠"],
    ];

    /// <summary>(byte offset in entry 6, room in words including the terminator, label).</summary>
    private static readonly (int Offset, int Room, string Text)[] Labels =
    [
        (0x2c, 6, "ABC"),       // ひらがな
        (0x38, 6, "abc"),       // カタカナ
        (0x44, 4, "123"),       // 英数字
        (0x4c, 4, "＜"),        // 戻る
        (0x54, 4, "＞"),        // 進む
        (0x5c, 8, "―Fim―"),     // ―終了―
    ];

    public static byte[] TranslatePages(byte[] entry, SmtCharMap map)
    {
        var result = entry.ToArray();
        if (Word(result, FirstScreen) != Token(map, "あ") || Word(result, FirstScreen + 2 * ScreenStride) != Token(map, "ア")
            || Word(result, FirstScreen + 2 * ScreenSize) != 0)
            throw new InvalidDataException("A tabela do teclado (DATA.BIN 24) não é a esperada.");
        for (var p = 0; p < Pages.Length; p++)
            for (var i = 0; i < 2 * ScreenSize; i++)
            {
                var row = i / 10 < Pages[p].Length ? Pages[p][i / 10] : "";
                var token = i % 10 < row.Length ? Token(map, row[i % 10].ToString()) : Blank;
                var screen = FirstScreen + (2 * p + i / ScreenSize) * ScreenStride;
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(screen + 2 * (i % ScreenSize)), (ushort)token);
            }
        return result;
    }

    public static byte[] TranslateLabels(byte[] entry, SmtCharMap map)
    {
        var result = entry.ToArray();
        if (Word(result, 0x2c) != Token(map, "ひ") || Word(result, 0x38) != Token(map, "カ"))
            throw new InvalidDataException("Os rótulos do teclado (DATA.BIN 6) não são os esperados.");
        foreach (var (offset, room, text) in Labels)
        {
            if (text.Length >= room) throw new InvalidOperationException($"Rótulo do teclado longo demais: {text}");
            for (var i = 0; i < room; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset + 2 * i),
                    (ushort)(i < text.Length ? Token(map, text[i].ToString()) : 0));
        }
        return result;
    }

    private static int Token(SmtCharMap map, string character) =>
        map.TryEncode(character, out var token) ? token : throw new InvalidDataException($"\"{character}\" não existe na fonte.");

    private static int Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
}
