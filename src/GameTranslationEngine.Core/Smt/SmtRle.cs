using System.Buffers.Binary;

namespace GameTranslationEngine.Core.Smt;

/// <summary>
/// RLE of DATA.BIN: u32 decoded size, one escape byte, then a stream where
/// "esc value count" repeats value count times. The escape varies per entry
/// and an entry can be compressed more than once.
/// </summary>
public static class SmtRle
{
    private const int MaxSize = 16 * 1024 * 1024;

    /// <summary>Decodes one layer; false when the data is not a valid RLE entry.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, out byte[] result)
    {
        result = [];
        if (data.Length < 8) return false;
        var size = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (size < 8 || size > MaxSize) return false;
        var escape = data[4];
        var output = new byte[size];
        var written = 0;
        var i = 5;
        while (written < size && i < data.Length)
        {
            var b = data[i];
            if (b == escape)
            {
                if (i + 2 >= data.Length) return false;
                var count = data[i + 2];
                if (written + count > size) return false;
                output.AsSpan(written, count).Fill(data[i + 1]);
                written += count;
                i += 3;
            }
            else
            {
                output[written++] = b;
                i++;
            }
        }
        if (written != size) return false;
        foreach (var rest in data[i..]) if (rest != 0) return false;
        result = output;
        return true;
    }

    /// <summary>Decodes until the data is no longer RLE; returns the number of layers removed.</summary>
    public static int DecodeAll(byte[] data, out byte[] result)
    {
        var layers = 0;
        result = data;
        while (layers < 4 && TryDecode(result, out var next))
        {
            result = next;
            layers++;
        }
        return layers;
    }

    /// <summary>
    /// Encodes one layer. Runs of 3 or more bytes (and every literal escape byte) become
    /// "esc value count"; with the escape the original used this reproduces its output.
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<byte> data, byte preferredEscape)
    {
        var counts = new int[256];
        foreach (var b in data) counts[b]++;
        var escape = preferredEscape;
        if (counts[escape] != 0)
            for (var b = 0; b < 256; b++)
                if (counts[b] < counts[escape]) escape = (byte)b;

        var output = new List<byte>(data.Length / 2 + 16);
        output.AddRange(BitConverter.GetBytes((uint)data.Length));
        output.Add(escape);
        for (var i = 0; i < data.Length;)
        {
            var value = data[i];
            var j = i;
            while (j < data.Length && data[j] == value && j - i < 255) j++;
            var run = j - i;
            if (run >= 3 || value == escape)
            {
                output.Add(escape);
                output.Add(value);
                output.Add((byte)run);
                i = j;
            }
            else
            {
                output.Add(value);
                i++;
            }
        }
        return [.. output];
    }
}
