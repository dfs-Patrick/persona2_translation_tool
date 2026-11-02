using System.Buffers.Binary;
using System.Text;

namespace GameTranslationEngine.Core.Cpk;

/// <summary>
/// CRILAYLA decompression (CRI's LZ variant used inside CPK files).
/// The stream is read backwards, bit by bit, from the end of the data;
/// the first 0x100 bytes of the output are stored uncompressed after it.
/// </summary>
public static class CriLayla
{
    public static bool IsCompressed(ReadOnlySpan<byte> data) =>
        data.Length >= 16 && Encoding.ASCII.GetString(data[..8]) == "CRILAYLA";

    public static byte[] Decompress(ReadOnlySpan<byte> input)
    {
        if (!IsCompressed(input)) throw new InvalidDataException("Dados não estão em CRILAYLA.");

        var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(input[8..]);
        var headerOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(input[12..]);
        const int prefix = 0x100;
        var output = new byte[size + prefix];
        input.Slice(headerOffset + 0x10, prefix).CopyTo(output);

        var bits = new BackwardBitReader(input, input.Length - prefix - 1);
        var outputEnd = prefix + size - 1;
        var written = 0;
        while (written < size)
        {
            if (bits.Read(1) == 0)
            {
                output[outputEnd - written++] = (byte)bits.Read(8);
                continue;
            }

            var source = bits.Read(13) + (outputEnd - written + 3);
            var length = 3;
            foreach (var width in VariableLengths())
            {
                var part = bits.Read(width);
                length += part;
                if (part != (1 << width) - 1) break;
            }
            for (var i = 0; i < length; i++) output[outputEnd - written++] = output[source--];
        }
        return output;
    }

    private static IEnumerable<int> VariableLengths()
    {
        yield return 2;
        yield return 3;
        yield return 5;
        while (true) yield return 8;
    }

    private ref struct BackwardBitReader(ReadOnlySpan<byte> data, int start)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position = start;
        private int _remaining;
        private int _current;

        public int Read(int count)
        {
            var value = 0;
            while (count > 0)
            {
                if (_remaining == 0)
                {
                    _current = _data[_position--];
                    _remaining = 8;
                }
                var take = Math.Min(_remaining, count);
                value = (value << take) | ((_current >> (_remaining - take)) & ((1 << take) - 1));
                _remaining -= take;
                count -= take;
            }
            return value;
        }
    }
}
