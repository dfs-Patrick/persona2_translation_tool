using System.Buffers.Binary;
using GameTranslationEngine.Core.Eboot;
using static GameTranslationEngine.Core.Eboot.MipsAssembler;

namespace GameTranslationEngine.Core.Smt;

/// <summary>
/// Variable-width text in SLPS_031.70, for the message windows and the menus.
///
/// Message windows are grids of 28 cells per row; the cell routine (0x80048578)
/// puts a character at baseX + column × 12. The call made by the message
/// renderer (0x8004731c) goes through a stub that finds where the previous cell
/// of the same window ended and passes baseX' = x − column × 12.
///
/// Menus use two string drawers reached from 0x80021dbc: a 10-pixel one
/// (0x80021e80) and a 12-pixel one (0x800220d0), both placing the character n
/// at x + n × width. Their x becomes the sum of the widths of the characters
/// before it on the same line. The 10-pixel font only has kana, digits and
/// capitals, so the Latin tokens are drawn from the free second layer of its
/// texture (drawn by <see cref="SmtLatinFont"/>), with the palette of the same
/// colour for that layer.
///
/// The room comes from the message renderer itself: two places where six
/// identical blocks clear one flag bit each are folded into one block.
/// </summary>
public static class SmtTextPatch
{
    private const uint ExeBase = 0x80010000;
    private const int HeaderSize = 0x800;

    private const uint CallSite = 0x80047bbc;      // jal 0x80048578 (character cell)
    private const uint CellRoutine = 0x80048578;
    private const uint FreeStart = 0x80047bc4;     // six flag-clearing blocks (characters)
    private const uint FreeEnd = 0x80047d74;
    private const uint MenuStart = 0x80047464;     // six flag-clearing blocks (code 0x804)
    private const uint MenuEnd = 0x80047614;
    private const int CacheEntries = 4;            // (expected next cell, next x)
    private const int CellSize = 120;

    // 10-pixel drawer.
    private const uint SmallIndex = 0x80021f4c;    // addiu v0, s0, -1
    private const uint SmallIndexShift = 0x80021f50; // sll v0, v0, 16
    private const uint SmallPalette = 0x80021fbc;  // lhu v0, 0x38(sp)
    private const uint SmallX = 0x80021fc8;        // mflo t0 (column × 10)
    private const uint SmallNarrow = 0x80022038;   // sltiu s0, s0, 0x47 (digits one pixel closer)
    // 12-pixel drawer.
    private const uint LargeX = 0x80022264;        // mflo t2 (column × 12)

    private const int T8 = 24, T9 = 25;

    private static readonly (uint Address, uint Original)[] Expected =
    [
        (CallSite, JalWord(CellRoutine)),
        (FreeStart, 0x8622791c),                   // lh v0, 0x791c(s1)
        (FreeEnd - 4, 0xac820070),                 // sw v0, 0x70(a0)
        (MenuStart, 0x8622791c),
        (MenuEnd - 4, 0xac820070),
        (SmallIndex, 0x2602ffff),
        (SmallIndexShift, 0x00021400),
        (SmallPalette, 0x97a20038),
        (SmallX, 0x00004012),
        (SmallNarrow, 0x2e100047),
        (LargeX, 0x00005012),
    ];

    /// <summary>The patched bytes of the executable, as (file offset, bytes) covering every patched place.</summary>
    public static (int Offset, byte[] Bytes) Patch(byte[] exe, IReadOnlyDictionary<int, int> advances)
    {
        var copy = exe.ToArray();
        Apply(copy, advances);
        return (Offset(SmallIndex), copy[Offset(SmallIndex)..Offset(FreeEnd)]);
    }

    public static bool IsApplied(ReadOnlySpan<byte> exe) =>
        Read(exe, CallSite) != JalWord(CellRoutine);

    /// <summary>Patches the executable in place; <paramref name="advances"/> maps the Latin tokens to their widths.</summary>
    public static void Apply(byte[] exe, IReadOnlyDictionary<int, int> advances)
    {
        foreach (var (address, original) in Expected)
            if (Read(exe, address) != original)
                throw new InvalidDataException("O executável não é o esperado para o patch de largura variável.");

        var first = advances.Keys.Min();
        var count = advances.Keys.Max() - first + 1;

        var draft = BuildMessages(advances, first, count, new Dictionary<string, uint>());
        var labels = new[] { "vwf", "cache", "next", "table" }.ToDictionary(l => l, draft.AddressOf);
        var messages = BuildMessages(advances, first, count, labels);
        Place(exe, messages, FreeStart, FreeEnd);

        var menuDraft = BuildMenus(first, count, labels);
        foreach (var label in new[] { "index10", "sum10", "sum12" }) labels[label] = menuDraft.AddressOf(label);
        var menus = BuildMenus(first, count, labels);
        Place(exe, menus, MenuStart, MenuEnd);

        Write(exe, CallSite, JalWord(labels["vwf"]));
        Write(exe, SmallIndex, JalWord(labels["index10"]));
        Write(exe, SmallIndexShift, NopWord);
        Write(exe, SmallPalette, 0x97a2003c);       // lhu v0, 0x3c(sp): palette of this character
        Write(exe, SmallX, JalWord(labels["sum10"]));
        Write(exe, SmallNarrow, 0x24100000);        // addiu s0, zero, 0: widths already narrow the digits
        Write(exe, LargeX, JalWord(labels["sum12"]));
    }

    private static void Place(byte[] exe, MipsAssembler asm, uint start, uint end)
    {
        var code = asm.ToBytes();
        if (start + code.Length > end)
            throw new InvalidOperationException($"O patch de largura variável não cabe ({code.Length} bytes em {end - start}).");
        code.CopyTo(exe.AsSpan(Offset(start)));
    }

    /// <summary>The six flag-clearing blocks as one: clears bits 0–5 of the current cell, then resumes.</summary>
    private static void ClearCellFlags(MipsAssembler asm, uint resume)
    {
        asm.Lh(V0, 0x791c, S1);
        asm.Lh(V1, 0x791e, S1);
        asm.Sll(A0, V0, 4);
        asm.Subu(A0, A0, V0);
        asm.Sll(A0, A0, 3);                 // column × 120
        asm.Sll(V0, V1, 3);
        asm.Subu(V0, V0, V1);
        asm.Sll(V0, V0, 2);
        asm.Subu(V0, V0, V1);
        asm.Sll(V0, V0, 3);
        asm.Subu(V0, V0, V1);
        asm.Sll(V0, V0, 4);                 // row × 3440
        asm.Addu(V0, V0, S1);
        asm.Addu(A0, A0, V0);
        asm.Lw(V0, 0x70, A0);
        asm.Addiu(V1, Zero, -64);
        asm.And(V0, V0, V1);
        asm.J(resume);
        asm.Sw(V0, 0x70, A0);
    }

    private static MipsAssembler BuildMessages(IReadOnlyDictionary<int, int> advances, int first, int count,
        IReadOnlyDictionary<string, uint> labels)
    {
        var asm = new MipsAssembler(FreeStart);
        ClearCellFlags(asm, FreeEnd);

        // a0 token, a1 baseX; the caller's 0x1c(sp) holds the cell, 0x2c(sp) the column.
        asm.Label("vwf");
        asm.Lw(T1, 0x1c, Sp);
        asm.Lw(T2, 0x2c, Sp);
        asm.Addiu(T3, A0, -first);
        asm.Sltiu(T4, T3, count);
        asm.Beq(T4, Zero, "width_done");
        asm.Addiu(T5, Zero, 12);
        asm.Lui(T6, Hi(labels, "table"));
        asm.Addu(T6, T6, T3);
        asm.Lbu(T5, Lo(labels, "table"), T6);
        asm.Label("width_done");
        asm.Lui(T6, Hi(labels, "cache"));
        asm.Addiu(T6, T6, Lo(labels, "cache"));
        asm.Beq(T2, Zero, "new_line");
        asm.Addiu(T7, Zero, CacheEntries);
        asm.Label("search");
        asm.Lw(T8, 0, T6);
        asm.Addiu(T7, T7, -1);
        asm.Beq(T8, T1, "found");
        asm.Nop();
        asm.Bne(T7, Zero, "search");
        asm.Addiu(T6, T6, 8);
        asm.Sll(T8, T2, 3);                 // unknown window: keep the 12-pixel grid
        asm.Sll(T9, T2, 2);
        asm.Addu(T8, T8, T9);
        asm.B("allocate");
        asm.Addu(T8, A1, T8);
        asm.Label("new_line");
        asm.Move(T8, A1);
        asm.Label("allocate");
        asm.Lui(T6, Hi(labels, "next"));
        asm.Lw(T7, Lo(labels, "next"), T6);
        asm.Nop();
        asm.Addiu(T9, T7, 8);
        asm.Andi(T9, T9, CacheEntries * 8 - 1);
        asm.Sw(T9, Lo(labels, "next"), T6);
        asm.Lui(T6, Hi(labels, "cache"));
        asm.Addiu(T6, T6, Lo(labels, "cache"));
        asm.B("store");
        asm.Addu(T6, T6, T7);
        asm.Label("found");
        asm.Lw(T8, 4, T6);
        asm.Nop();
        asm.Label("store");
        asm.Addiu(T9, T1, CellSize);
        asm.Sw(T9, 0, T6);
        asm.Addu(T9, T8, T5);
        asm.Sw(T9, 4, T6);
        asm.Sll(T9, T2, 3);
        asm.Sll(T7, T2, 2);
        asm.Addu(T9, T9, T7);
        asm.J(CellRoutine);
        asm.Subu(A1, T8, T9);

        asm.Label("cache");
        for (var i = 0; i < CacheEntries * 2; i++) asm.Word(0);
        asm.Label("next");
        asm.Word(0);
        asm.Label("table");
        var table = new byte[(count + 3) & ~3];
        for (var i = 0; i < count; i++) table[i] = (byte)(advances.TryGetValue(first + i, out var w) ? w : 12);
        asm.Byte4(table);
        return asm;
    }

    private static MipsAssembler BuildMenus(int first, int count, IReadOnlyDictionary<string, uint> labels)
    {
        var asm = new MipsAssembler(MenuStart);
        ClearCellFlags(asm, MenuEnd);

        // 10-pixel drawer, per character: s0 token → v0 = (index in the font) << 16, and
        // 0x3c(sp) = palette. Latin tokens come from layer 1: index = token − first, and the
        // palette of the same colour for that layer (row 480 + c becomes 486 + 2c).
        asm.Label("index10");
        asm.Addiu(T4, S0, -first);
        asm.Sltiu(T5, T4, count);
        asm.Beq(T5, Zero, "index10_kana");
        asm.Lhu(T6, 0x38, Sp);
        asm.Move(V0, T4);
        asm.Srl(T5, T6, 6);
        asm.Sll(T5, T5, 1);
        asm.Addiu(T5, T5, -474);
        asm.Sll(T5, T5, 6);
        asm.Andi(T6, T6, 0x3f);
        asm.B("index10_done");
        asm.Or(T6, T6, T5);
        asm.Label("index10_kana");
        asm.Addiu(V0, S0, -1);
        asm.Label("index10_done");
        asm.Sh(T6, 0x3c, Sp);
        asm.Jr(Ra);
        asm.Sll(V0, V0, 16);

        // 10-pixel drawer: t0 = widths of the characters before this one on its line
        // (s2 = column + 1, s4 = this character). Digits and capitals are 9 pixels wide.
        asm.Label("sum10");
        asm.Addiu(T5, S2, -1);
        asm.Sll(T6, T5, 1);
        asm.Subu(T6, S4, T6);
        asm.Move(T0, Zero);
        asm.Label("sum10_loop");
        asm.Beq(T5, Zero, "sum10_end");
        asm.Nop();
        asm.Lhu(T7, 0, T6);
        asm.Addiu(T6, T6, 2);
        asm.Addiu(T5, T5, -1);
        asm.Addiu(T9, T7, -first);
        asm.Sltiu(T8, T9, count);
        asm.Beq(T8, Zero, "sum10_kana");
        asm.Lui(T8, Hi(labels, "table"));
        asm.Addu(T8, T8, T9);
        asm.Lbu(T8, Lo(labels, "table"), T8);
        asm.B("sum10_loop");
        asm.Addu(T0, T0, T8);
        asm.Label("sum10_kana");
        asm.Addiu(T9, T7, -0xa4);
        asm.Sltiu(T9, T9, 0x47);
        asm.Addiu(T8, Zero, 10);
        asm.Subu(T8, T8, T9);
        asm.B("sum10_loop");
        asm.Addu(T0, T0, T8);
        asm.Label("sum10_end");
        asm.Jr(Ra);
        asm.Nop();

        // 12-pixel drawer: t2 = widths of the characters before this one on its line
        // (s0 = column, s5 = this character).
        asm.Label("sum12");
        asm.Sll(T5, S0, 1);
        asm.Subu(T6, S5, T5);
        asm.Move(T5, S0);
        asm.Move(T2, Zero);
        asm.Label("sum12_loop");
        asm.Beq(T5, Zero, "sum12_end");
        asm.Nop();
        asm.Lhu(T7, 0, T6);
        asm.Addiu(T6, T6, 2);
        asm.Addiu(T5, T5, -1);
        asm.Addiu(T9, T7, -first);
        asm.Sltiu(T8, T9, count);
        asm.Beq(T8, Zero, "sum12_kana");
        asm.Lui(T8, Hi(labels, "table"));
        asm.Addu(T8, T8, T9);
        asm.Lbu(T8, Lo(labels, "table"), T8);
        asm.B("sum12_loop");
        asm.Addu(T2, T2, T8);
        asm.Label("sum12_kana");
        asm.B("sum12_loop");
        asm.Addiu(T2, T2, 12);
        asm.Label("sum12_end");
        asm.Jr(Ra);
        asm.Nop();
        return asm;
    }

    private static int Hi(IReadOnlyDictionary<string, uint> labels, string label) =>
        labels.TryGetValue(label, out var a) ? (int)((a + 0x8000) >> 16) : 0;
    private static int Lo(IReadOnlyDictionary<string, uint> labels, string label) =>
        labels.TryGetValue(label, out var a) ? (short)(a & 0xffff) : 0;

    private static int Offset(uint address) => (int)(address - ExeBase) + HeaderSize;
    private static uint Read(ReadOnlySpan<byte> exe, uint address) => BinaryPrimitives.ReadUInt32LittleEndian(exe[Offset(address)..]);
    private static void Write(Span<byte> exe, uint address, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(exe[Offset(address)..], value);
}
