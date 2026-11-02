using System.Buffers.Binary;

namespace GameTranslationEngine.Core.Eboot;

/// <summary>
/// Minimal MIPS (Allegrex) assembler for the few routines the tool adds to an
/// EBOOT. Code is placed at a fixed game address; branches use labels, which
/// may be defined after they are used. Only the instructions the patches need.
/// </summary>
public sealed class MipsAssembler(uint origin)
{
    public const int Zero = 0, At = 1, V0 = 2, V1 = 3, A0 = 4, A1 = 5, A2 = 6, A3 = 7;
    public const int T0 = 8, T1 = 9, T2 = 10, T3 = 11, T4 = 12, T5 = 13, T6 = 14, T7 = 15;
    public const int S0 = 16, S1 = 17, S2 = 18, S3 = 19, S4 = 20, S5 = 21, Sp = 29, Fp = 30, Ra = 31;
    public const int F0 = 0, F1 = 1, F2 = 2, F3 = 3;

    private readonly List<uint> _words = [];
    private readonly Dictionary<string, uint> _labels = new();
    private readonly List<(int Index, string Label)> _branches = [];

    public uint Origin { get; } = origin;
    public uint Here => Origin + (uint)_words.Count * 4;

    public void Label(string name)
    {
        if (!_labels.TryAdd(name, Here)) throw new InvalidOperationException($"Rótulo repetido: {name}");
    }

    public uint AddressOf(string label) => _labels[label];

    public byte[] ToBytes()
    {
        foreach (var (index, label) in _branches)
        {
            if (!_labels.TryGetValue(label, out var target)) throw new InvalidOperationException($"Rótulo indefinido: {label}");
            var distance = ((long)target - (Origin + index * 4 + 4)) / 4;
            if (distance is < short.MinValue or > short.MaxValue) throw new InvalidOperationException($"Desvio longe demais: {label}");
            _words[index] = (_words[index] & 0xFFFF0000) | (ushort)(short)distance;
        }
        var bytes = new byte[_words.Count * 4];
        for (var i = 0; i < _words.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), _words[i]);
        return bytes;
    }

    public void Word(uint value) => _words.Add(value);
    public void Byte4(ReadOnlySpan<byte> data)
    {
        if (data.Length % 4 != 0) throw new ArgumentException("Dados precisam ter múltiplos de 4 bytes.");
        for (var i = 0; i < data.Length; i += 4) _words.Add(BinaryPrimitives.ReadUInt32LittleEndian(data[i..]));
    }

    // R-type and I-type encodings.
    private void R(int rs, int rt, int rd, int shamt, int funct) =>
        _words.Add((uint)((rs << 21) | (rt << 16) | (rd << 11) | (shamt << 6) | funct));
    private void I(int op, int rs, int rt, int immediate) =>
        _words.Add((uint)((op << 26) | (rs << 21) | (rt << 16)) | (ushort)immediate);
    private void Branch(int op, int rs, int rt, string label)
    {
        _branches.Add((_words.Count, label));
        I(op, rs, rt, 0);
    }

    public void Nop() => _words.Add(0);
    public void Sll(int rd, int rt, int shift) => R(0, rt, rd, shift, 0x00);
    public void Srl(int rd, int rt, int shift) => R(0, rt, rd, shift, 0x02);
    public void Jr(int rs) => R(rs, 0, 0, 0, 0x08);
    public void Addu(int rd, int rs, int rt) => R(rs, rt, rd, 0, 0x21);
    public void Subu(int rd, int rs, int rt) => R(rs, rt, rd, 0, 0x23);
    public void Move(int rd, int rs) => Addu(rd, rs, Zero);
    public void Sltu(int rd, int rs, int rt) => R(rs, rt, rd, 0, 0x2B);
    public void And(int rd, int rs, int rt) => R(rs, rt, rd, 0, 0x24);
    public void Or(int rd, int rs, int rt) => R(rs, rt, rd, 0, 0x25);
    public void Sh(int rt, int offset, int rs) => I(0x29, rs, rt, Check16(offset));
    public void Mtlo(int rs) => R(rs, 0, 0, 0, 0x13);
    public void Addiu(int rt, int rs, int immediate) => I(0x09, rs, rt, Check16(immediate));
    public void Sltiu(int rt, int rs, int immediate) => I(0x0B, rs, rt, Check16(immediate));
    public void Andi(int rt, int rs, int immediate) => I(0x0C, rs, rt, CheckU16(immediate));
    public void Ori(int rt, int rs, int immediate) => I(0x0D, rs, rt, CheckU16(immediate));
    public void Lui(int rt, int immediate) => I(0x0F, 0, rt, CheckU16(immediate));
    public void Lh(int rt, int offset, int rs) => I(0x21, rs, rt, Check16(offset));
    public void Lbu(int rt, int offset, int rs) => I(0x24, rs, rt, Check16(offset));
    public void Lhu(int rt, int offset, int rs) => I(0x25, rs, rt, Check16(offset));
    public void Lw(int rt, int offset, int rs) => I(0x23, rs, rt, Check16(offset));
    public void Sw(int rt, int offset, int rs) => I(0x2B, rs, rt, Check16(offset));
    public void Swc1(int ft, int offset, int rs) => I(0x39, rs, ft, Check16(offset));
    public void Mtc1(int rt, int fs) => _words.Add((uint)((0x11 << 26) | (0x04 << 21) | (rt << 16) | (fs << 11)));
    /// <summary>cvt.s.w fd, fs</summary>
    public void CvtSW(int fd, int fs) => _words.Add((uint)((0x11 << 26) | (0x14 << 21) | (fs << 11) | (fd << 6) | 0x20));
    public void Beq(int rs, int rt, string label) => Branch(0x04, rs, rt, label);
    public void Bne(int rs, int rt, string label) => Branch(0x05, rs, rt, label);
    public void Blez(int rs, string label) => Branch(0x06, rs, 0, label);
    public void Bltz(int rs, string label) { _branches.Add((_words.Count, label)); I(0x01, rs, 0, 0); }
    public void B(string label) => Beq(Zero, Zero, label);

    /// <summary>Loads a 32-bit constant in two instructions (lui + ori).</summary>
    public void Li32(int rt, uint value)
    {
        Lui(rt, (int)(value >> 16));
        Ori(rt, rt, (int)(value & 0xFFFF));
    }

    public void Jal(uint target) => _words.Add(JalWord(target));
    public void J(uint target) => _words.Add(JWord(target));

    public static uint JWord(uint target) => (0x02u << 26) | ((target >> 2) & 0x03FFFFFF);
    public static uint JalWord(uint target) => (0x03u << 26) | ((target >> 2) & 0x03FFFFFF);
    public static uint NopWord => 0;
    /// <summary>addiu rt, zero, value — "li" for small constants.</summary>
    public static uint LiWord(int rt, short value) => (0x09u << 26) | ((uint)rt << 16) | (ushort)value;

    private static int Check16(int value) =>
        value is >= short.MinValue and <= short.MaxValue ? value : throw new ArgumentOutOfRangeException(nameof(value));
    private static int CheckU16(int value) =>
        value is >= 0 and <= ushort.MaxValue ? value : throw new ArgumentOutOfRangeException(nameof(value));
}
