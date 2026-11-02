using static GameTranslationEngine.Core.Eboot.MipsAssembler;

namespace GameTranslationEngine.Core.Eboot;

/// <summary>
/// Variable-width text for Eternal Punishment (ULJM-06081). The Japanese game
/// draws every glyph 14 px wide and measures text as 14 px per character. These
/// changes give each font cell below 0x120 its own left column and width (a
/// table of one byte per cell, left &lt;&lt; 4 | width) and leave kana and kanji
/// as they were. Same idea as the "vwf" patch of v1's p2ep_template, written
/// again here: new routines go to memory added after .bss and the original
/// code jumps to them. Every original instruction is checked before it is
/// replaced, and its relocations are taken off the loader's list.
/// </summary>
public static class EpTextPatch
{
    /// <summary>Event code → font cell (u16 per code; same table as in IS).</summary>
    public const uint EventToFont = 0x08C7D56C;
    public const int EventToFontEntries = 0xB45;

    /// <summary>Inside the routine that maps a font cell to its texture rectangle (x, y, width, height).</summary>
    private const uint MapStart = 0x088226BC;
    private const uint MapEnd = 0x088226FC;
    /// <summary>
    /// "addiu v0, zero, 14" in the routine's epilogue. Kept: a special path of the
    /// routine (font cells drawn from another table) jumps to the epilogue too.
    /// </summary>
    private const uint MapReturn = 0x08822710;
    /// <summary>event_strwidth (width of a message line, 14/17 per character).</summary>
    private const uint StrWidth = 0x088238A4;
    /// <summary>event_strwidth2 (14 per character up to the first command).</summary>
    private const uint StrWidth2 = 0x088E7614;

    /// <summary>
    /// Event message box: "mtlo fp; addiu v0, v0, 14; madd v1, v0" puts each
    /// character at line start + index × (14 + extra spacing).
    /// </summary>
    private const uint EventBoxX = 0x088E818C;

    private const int SpacedCharacters = 0x120;

    // Original words, checked before anything is written.
    private static readonly (uint Address, uint Word)[] Expected =
    [
        (MapStart, 0x3062000F),      // andi v0, v1, 0xf
        (MapStart + 4, 0x00021100),  // sll v0, v0, 4
        (MapStart + 8, 0x24420001),  // addiu v0, v0, 1
        (MapReturn, 0x2402000E),     // addiu v0, zero, 0xe
        (MapEnd, 0x8FBF0010),        // lw ra, 0x10(sp): the epilogue map_glyph repeats
        (MapEnd + 4, 0x8FB3000C),    // lw s3, 0xc(sp)
        (MapEnd + 16, 0x8FB00000),   // lw s0, 0(sp)
        (MapReturn + 8, 0x27BD0020), // addiu sp, sp, 0x20
        (StrWidth, 0x00804021),      // move t0, a0
        (StrWidth2, 0x94820000),     // lhu v0, 0(a0)
        (EventBoxX, 0x03C00013),     // mtlo fp
        (EventBoxX + 4, 0x2442000E), // addiu v0, v0, 0xe
        (EventBoxX + 8, 0x0062001C), // madd v1, v0 (Allegrex)
    ];

    public static bool IsApplied(ElfImage elf) =>
        elf.ReadU32(MapStart) >> 26 == 0x02 && elf.ReadU32(StrWidth) >> 26 == 0x02 && elf.ReadU32(StrWidth2) >> 26 == 0x02;

    /// <summary>Returns the EBOOT with the routines added and the original code pointed to them.</summary>
    public static ElfImage Apply(ElfImage elf, byte[] widthTable)
    {
        if (widthTable.Length != SpacedCharacters) throw new ArgumentException($"A tabela de larguras precisa ter {SpacedCharacters} bytes.");
        foreach (var (address, word) in Expected)
            if (elf.ReadU32(address) != word)
                throw new InvalidDataException($"EBOOT inesperado em 0x{address:X8} (0x{elf.ReadU32(address):X8}); o patch de largura do EP não foi aplicado.");

        var code = new MipsAssembler(elf.AppendAddress);
        code.Label("widths");
        code.Byte4(widthTable);
        EmitMapGlyph(code);
        EmitStrWidth(code);
        EmitStrWidth2(code);
        EmitEventBoxX(code);
        var patched = elf.AppendToDataSegment(code.ToBytes());

        PspRelocations.Disable(patched, MapStart, MapEnd);
        PspRelocations.Disable(patched, StrWidth, StrWidth + 8);
        PspRelocations.Disable(patched, StrWidth2, StrWidth2 + 8);
        PspRelocations.Disable(patched, EventBoxX, EventBoxX + 12);

        // map_glyph ends the original routine itself (same epilogue, its own advance).
        patched.WriteU32(MapStart, JWord(code.AddressOf("map_glyph")));
        for (var address = MapStart + 4; address < MapEnd; address += 4) patched.WriteU32(address, NopWord);
        patched.WriteU32(StrWidth, JWord(code.AddressOf("str_width")));
        patched.WriteU32(StrWidth + 4, NopWord);
        patched.WriteU32(StrWidth2, JWord(code.AddressOf("str_width2")));
        patched.WriteU32(StrWidth2 + 4, NopWord);
        patched.WriteU32(EventBoxX, JalWord(code.AddressOf("event_box_x")));
        patched.WriteU32(EventBoxX + 4, NopWord);
        patched.WriteU32(EventBoxX + 8, NopWord);
        return patched;
    }

    /// <summary>
    /// Reached by a jump from the middle of the original routine. In: v1 = font
    /// cell, s0 = rectangle (floats x, y, width, height at +8..+0x14). Writes the
    /// rectangle, then returns from the original routine (its epilogue) with
    /// v0 = advance. Cells from 0x120 on get the game's x + 1 and 14.
    /// </summary>
    private static void EmitMapGlyph(MipsAssembler a)
    {
        a.Label("map_glyph");
        a.Andi(T0, V1, 0xF);
        a.Sll(T0, T0, 4);                 // x of the cell
        a.Andi(T1, V1, 0xF0);             // y of the cell
        a.Addiu(T2, Zero, 14);            // width
        a.Sltiu(At, V1, SpacedCharacters);
        a.Beq(At, Zero, "map_store");
        a.Addiu(T3, Zero, 1);             // (delay) left column
        a.Li32(T4, a.AddressOf("widths"));
        a.Addu(T4, T4, V1);
        a.Lbu(T4, 0, T4);
        a.Andi(T2, T4, 0xF);
        a.Srl(T3, T4, 4);
        a.Label("map_store");
        a.Addu(T0, T0, T3);
        a.Mtc1(T0, F0);
        a.CvtSW(F0, F0);
        a.Swc1(F0, 8, S0);
        a.Mtc1(T1, F1);
        a.CvtSW(F1, F1);
        a.Swc1(F1, 0xC, S0);
        a.Mtc1(T2, F2);
        a.CvtSW(F2, F2);
        a.Swc1(F2, 0x10, S0);
        a.Lui(T4, 0x4180);                // 16.0f
        a.Sw(T4, 0x14, S0);
        a.Move(V0, T2);
        // Epilogue of the original routine (0x088226FC…0x08822718), with v0 kept.
        a.Lw(Ra, 0x10, Sp);
        a.Lw(S3, 0xC, Sp);
        a.Lw(S2, 8, Sp);
        a.Lw(S1, 4, Sp);
        a.Lw(S0, 0, Sp);
        a.Jr(Ra);
        a.Addiu(Sp, Sp, 0x20);
    }

    /// <summary>
    /// Position of a character in the event message box (as v1's p2ep_template
    /// fixEventBoxX). In: v1 = index in the line, fp = line start, s2 = character
    /// code, v0 = the game's extra spacing. Out: LO = x. The x of the next
    /// character is kept in "box_x". Uses a1, t0, t3 and s5, which the original
    /// code sets again right after.
    /// </summary>
    private static void EmitEventBoxX(MipsAssembler a)
    {
        a.Label("box_x");
        a.Word(0);
        a.Label("event_box_x");
        a.Li32(T3, a.AddressOf("box_x"));
        a.Bne(V1, Zero, "box_measure");
        a.Nop();
        a.Sw(Fp, 0, T3);                  // first character: start of the line
        a.Label("box_measure");
        a.Li32(T0, EventToFont);
        a.Sll(A1, S2, 1);
        a.Addu(A1, A1, T0);
        a.Lh(A1, 0, A1);                  // font cell
        a.Sltiu(T0, A1, SpacedCharacters);
        a.Beq(T0, Zero, "box_advance");
        a.Addiu(S5, V0, 14);              // (delay) the game's advance
        a.Li32(T0, a.AddressOf("widths"));
        a.Addu(T0, T0, A1);
        a.Lbu(S5, 0, T0);
        a.Andi(S5, S5, 0xF);
        a.Label("box_advance");
        a.Lw(A1, 0, T3);
        a.Addu(T0, A1, S5);
        a.Sw(T0, 0, T3);
        a.Jr(Ra);
        a.Mtlo(A1);
    }

    /// <summary>Width of the character code in t2 → t4 (uses t3, t7; t5 = event→font, t6 = widths).</summary>
    private static void EmitCharWidth(MipsAssembler a, string prefix)
    {
        a.Sll(T3, T2, 1);
        a.Addu(T3, T3, T5);
        a.Lh(T3, 0, T3);                  // font cell (0x8140 = Japanese space, negative)
        a.Addiu(T4, Zero, 14);
        a.Addiu(T7, Zero, -0x7EC0);
        a.Beq(T3, T7, $"{prefix}_done");
        a.Addiu(T7, T3, 0xC0);            // (delay)
        a.Andi(T7, T7, 0xFFFF);
        a.Sltiu(T7, T7, 0x22);            // the game's 17 px cells
        a.Beq(T7, Zero, $"{prefix}_cell");
        a.Nop();
        a.B($"{prefix}_done");
        a.Addiu(T4, Zero, 0x11);          // (delay)
        a.Label($"{prefix}_cell");
        a.Sltiu(T7, T3, SpacedCharacters);
        a.Beq(T7, Zero, $"{prefix}_done");
        a.Addu(T7, T6, T3);               // (delay)
        a.Lbu(T7, 0, T7);
        a.Andi(T4, T7, 0xF);
        a.Label($"{prefix}_done");
    }

    /// <summary>event_strwidth(a0 = text, a1 = end word, a2 = maximum characters) → v0 = width.</summary>
    private static void EmitStrWidth(MipsAssembler a)
    {
        a.Label("str_width");
        a.Move(T0, A0);
        a.Move(T1, Zero);
        a.Andi(A1, A1, 0xFFFF);
        a.Li32(T5, EventToFont);
        a.Li32(T6, a.AddressOf("widths"));
        a.Blez(A2, "sw_return");
        a.Move(A3, Zero);                 // (delay) count
        a.Label("sw_loop");
        a.Lhu(T2, 0, T0);
        a.Beq(T2, A1, "sw_return");
        a.Andi(T3, T2, 0x1000);           // (delay) commands add nothing
        a.Bne(T3, Zero, "sw_next");
        a.Nop();
        EmitCharWidth(a, "sw");
        a.Addu(T1, T1, T4);
        a.Label("sw_next");
        a.Addiu(A3, A3, 1);
        a.Beq(A3, A2, "sw_return");
        a.Addiu(T0, T0, 2);               // (delay)
        a.B("sw_loop");
        a.Nop();
        a.Label("sw_return");
        a.Jr(Ra);
        a.Move(V0, T1);
    }

    /// <summary>event_strwidth2(a0 = text) → width of the characters before the first command.</summary>
    private static void EmitStrWidth2(MipsAssembler a)
    {
        a.Label("str_width2");
        a.Move(T0, A0);
        a.Move(T1, Zero);
        a.Li32(T5, EventToFont);
        a.Li32(T6, a.AddressOf("widths"));
        a.Label("sw2_loop");
        a.Lhu(T2, 0, T0);
        a.Andi(T3, T2, 0x1000);
        a.Bne(T3, Zero, "sw2_return");
        a.Nop();
        EmitCharWidth(a, "sw2");
        a.Addu(T1, T1, T4);
        a.B("sw2_loop");
        a.Addiu(T0, T0, 2);               // (delay)
        a.Label("sw2_return");
        a.Jr(Ra);
        a.Move(V0, T1);
    }
}
