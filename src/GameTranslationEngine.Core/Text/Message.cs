using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using GameTranslationEngine.Core.GameData;

namespace GameTranslationEngine.Core.Text;

public abstract record MessagePart;
public sealed record TextPart(string Text) : MessagePart;
public sealed record CommandPart(string Name, IReadOnlyList<string> Arguments) : MessagePart
{
    public static CommandPart Of(string name, params string[] arguments) => new(name, arguments);
}
/// <summary>A raw 16-bit value written as \xNNNN in the text.</summary>
public sealed record RawPart(int Value) : MessagePart;

/// <summary>
/// A dialogue message. Text form (as in the TBF): characters, line breaks,
/// and control codes in brackets, e.g. "[color(yellow)]Tatsuya[wait]".
/// </summary>
public sealed class Message(IReadOnlyList<MessagePart> parts)
{
    public const int Terminator = 0x1103;

    public IReadOnlyList<MessagePart> Parts { get; } = parts;

    /// <summary>Decodes a message from the game's bytes, stopping at the terminator.</summary>
    public static Message Decode(ReadOnlySpan<byte> data, MessageContext context, out int length)
    {
        var parts = new List<MessagePart>();
        var position = 0;
        while (true)
        {
            var value = Next(data, ref position, context);
            if (value == Terminator) break;
            if ((value & 0xF000) != 0)
            {
                var size = (value >> 8) & 0xF;
                if (size == 0) throw new InvalidDataException($"Comando inválido 0x{value:x} em {context.Location(position - 2)}.");
                var arguments = new int[size - 1];
                for (var i = 0; i < arguments.Length; i++) arguments[i] = Next(data, ref position, context);
                parts.Add(EventCommands.Decode(value & 0xFF, arguments, context));
            }
            else if (context.Encoding.TryDecode(value, out var character))
            {
                parts.Add(new TextPart(character));
            }
            else
            {
                throw new InvalidDataException($"Caractere desconhecido 0x{value:x} em {context.Location(position - 2)}.");
            }
        }
        length = position;
        return new Message(Merge(parts));
    }

    private static int Next(ReadOnlySpan<byte> data, ref int position, MessageContext context)
    {
        if (position + 2 > data.Length)
            throw new InvalidDataException($"Fim inesperado da mensagem em {context.Location(position)}.");
        var value = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
        position += 2;
        return value;
    }

    /// <summary>Encodes to 16-bit values, terminator included.</summary>
    public List<int> Encode(MessageContext context)
    {
        var output = new List<int>();
        foreach (var part in Parts)
        {
            switch (part)
            {
                case RawPart raw:
                    output.Add(raw.Value);
                    break;
                case TextPart text:
                    foreach (var rune in text.Text.EnumerateRunes())
                    {
                        var character = rune.ToString();
                        if (!context.Encoding.TryEncode(character, out var code))
                            throw new InvalidDataException(
                                $"Caractere {character} (U+{rune.Value:X4}) não existe na fonte, em {context.File}.");
                        output.Add(code);
                    }
                    break;
                case CommandPart command:
                    output.AddRange(EventCommands.Encode(command, context));
                    break;
            }
        }
        output.Add(Terminator);
        return output;
    }

    public byte[] EncodeBytes(MessageContext context)
    {
        var values = Encode(context);
        var bytes = new byte[values.Count * 2];
        for (var i = 0; i < values.Count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), (ushort)values[i]);
        return bytes;
    }

    /// <summary>Text form, identical to v2's messageToString.</summary>
    public string ToText()
    {
        var builder = new StringBuilder();
        foreach (var part in Parts)
        {
            switch (part)
            {
                case TextPart text: builder.Append(text.Text); break;
                case RawPart raw: builder.Append("\\x").Append(raw.Value.ToString("x4")); break;
                case CommandPart { Name: "nl" }: builder.Append('\n'); break;
                case CommandPart { Name: "space" }: builder.Append(' '); break;
                case CommandPart command: builder.Append('[').Append(EventCommands.ToText(command)).Append(']'); break;
            }
        }
        return builder.ToString();
    }

    /// <summary>Parses the text form (port of v2's parseMessage).</summary>
    public static Message Parse(string text, string file = "")
    {
        var characters = new List<string>();
        foreach (var rune in text.EnumerateRunes()) characters.Add(rune.ToString());

        var parts = new List<MessagePart>();
        var current = new StringBuilder();
        void Flush()
        {
            if (current.Length > 0) parts.Add(new TextPart(current.ToString()));
            current.Clear();
        }

        var index = 0;
        while (index < characters.Count)
        {
            var c = characters[index++];
            switch (c)
            {
                case "\\":
                    if (index >= characters.Count) throw new InvalidDataException($"Fim inesperado da mensagem em {file}.");
                    var escaped = characters[index];
                    if (escaped is "[" or "\\")
                    {
                        current.Append(escaped);
                        index++;
                    }
                    else if (escaped == " ")
                    {
                        Flush();
                        parts.Add(CommandPart.Of("pxl"));
                        index++;
                    }
                    else if (escaped == "x")
                    {
                        var hex = string.Concat(characters.Skip(index + 1).Take(4));
                        parts.Add(new RawPart(int.Parse(hex, NumberStyles.HexNumber)));
                        index += 5;
                    }
                    else
                    {
                        Flush();
                        parts.Add(new TextPart(escaped));
                        index++;
                    }
                    break;
                case "\r":
                    break;
                case "\n":
                    Flush();
                    parts.Add(CommandPart.Of("nl"));
                    break;
                case "[":
                    Flush();
                    var close = characters.IndexOf("]", index);
                    if (close < 0) throw new InvalidDataException($"Comando sem ']' em {file}: {text}");
                    var body = string.Concat(characters.Skip(index).Take(close - index));
                    index = close + 1;
                    var command = ParseCommand(body, text, file);
                    if (command.Name == "end")
                    {
                        index = characters.Count; // anything after [end] is ignored
                        break;
                    }
                    parts.Add(command);
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }
        Flush();
        return new Message(parts);
    }

    private static CommandPart ParseCommand(string body, string text, string file)
    {
        var open = body.IndexOf('(');
        if (open < 0) return new CommandPart(body.Trim(), []);
        var close = body.IndexOf(')');
        if (close != body.Length - 1) throw new InvalidDataException($"Comando inválido em {file}: [{body}]");
        var arguments = body[(open + 1)..close].Split(',').Select(a => a.Trim()).ToList();
        return new CommandPart(body[..open].Trim(), arguments);
    }

    private static List<MessagePart> Merge(List<MessagePart> parts)
    {
        var merged = new List<MessagePart>(parts.Count);
        foreach (var part in parts)
        {
            if (part is TextPart text && merged.Count > 0 && merged[^1] is TextPart previous)
                merged[^1] = new TextPart(previous.Text + text.Text);
            else
                merged.Add(part);
        }
        return merged;
    }
}

/// <summary>What message encoding/decoding needs: the character table, constants and where we are.</summary>
public sealed class MessageContext(EventEncoding encoding, ConstantTables constants, string file = "", int baseOffset = 0)
{
    public EventEncoding Encoding { get; } = encoding;
    public ConstantTables Constants { get; } = constants;
    public string File { get; } = file;
    public int BaseOffset { get; } = baseOffset;

    public MessageContext At(string file, int baseOffset) => new(Encoding, Constants, file, baseOffset);
    public string Location(int position) => $"{File} em 0x{BaseOffset + position:x}";
}
