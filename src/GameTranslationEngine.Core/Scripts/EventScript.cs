using System.Buffers.Binary;
using GameTranslationEngine.Core.Text;

namespace GameTranslationEngine.Core.Scripts;

/// <summary>A message used by a script: its name, where it is and its content.</summary>
public sealed record ScriptMessage(string Name, int Offset, int Length, Message Message);

/// <summary>
/// EFB event script. Layout (little-endian): a header of six offsets
/// (start function, functions, function count, instructions, arguments,
/// messages), 72-byte function records, 8-byte instructions (opcode +
/// argument offset), 32-bit arguments and the message section.
/// </summary>
public sealed class EventScript
{
    /// <summary>"msgShow": the only Innocent Sin instruction that points to a message.</summary>
    public const int MessageShowOpcode = 0x13;

    private EventScript(Header header, List<Instruction> instructions, List<ScriptMessage> messages)
    {
        Layout = header;
        Instructions = instructions;
        Messages = messages;
    }

    public Header Layout { get; }
    public IReadOnlyList<Instruction> Instructions { get; }

    /// <summary>Messages ordered by offset; names follow first use (msg_0, msg_1…), like v2.</summary>
    public IReadOnlyList<ScriptMessage> Messages { get; }

    public readonly record struct Header(
        int StartFunction, int FunctionSection, int FunctionCount,
        int InstructionSection, int ArgumentSection, int MessageSection);

    /// <summary>An instruction with the position of its arguments in the file.</summary>
    public sealed record Instruction(int Index, int Opcode, int ArgumentOffset, int[] Arguments);

    public static EventScript Read(ReadOnlySpan<byte> data, MessageContext context)
    {
        if (data.Length < 24) throw new InvalidDataException($"Script EFB curto demais em {context.File}.");
        var header = new Header(
            I32(data, 0), I32(data, 4), I32(data, 8), I32(data, 12), I32(data, 16), I32(data, 20));
        if (header.InstructionSection > header.ArgumentSection || header.ArgumentSection > header.MessageSection
            || header.MessageSection > data.Length)
            throw new InvalidDataException($"Cabeçalho EFB inconsistente em {context.File}.");

        var count = (header.ArgumentSection - header.InstructionSection) / 8;
        var raw = new (int Opcode, int Arguments)[count];
        for (var i = 0; i < count; i++)
        {
            var at = header.InstructionSection + i * 8;
            raw[i] = (BinaryPrimitives.ReadUInt16LittleEndian(data[at..]), I32(data, at + 4));
        }

        var instructions = new List<Instruction>(count);
        for (var i = 0; i < count; i++)
        {
            var end = i + 1 < count ? raw[i + 1].Arguments : header.MessageSection;
            var arguments = new int[Math.Max(0, (end - raw[i].Arguments) / 4)];
            for (var a = 0; a < arguments.Length; a++) arguments[a] = I32(data, raw[i].Arguments + a * 4);
            instructions.Add(new Instruction(i, raw[i].Opcode, raw[i].Arguments, arguments));
        }

        // Names in order of first use; listing in offset order (JS sorts integer keys).
        var names = new Dictionary<int, string>();
        foreach (var instruction in instructions.Where(i => i.Opcode == MessageShowOpcode))
        {
            if (instruction.Arguments.Length != 1)
                throw new InvalidDataException(
                    $"msgShow com {instruction.Arguments.Length} argumentos na instrução {instruction.Index} de {context.File}.");
            names.TryAdd(instruction.Arguments[0], $"msg_{names.Count}");
        }

        var messageSection = data[header.MessageSection..];
        var messages = new List<ScriptMessage>(names.Count);
        foreach (var (offset, name) in names.OrderBy(n => (uint)n.Key))
        {
            if (offset < 0 || offset >= messageSection.Length)
                throw new InvalidDataException($"{name} aponta para fora da seção de mensagens em {context.File}.");
            var message = Message.Decode(messageSection[offset..], context.At(context.File, offset), out var length);
            messages.Add(new ScriptMessage(name, offset, length, message));
        }
        return new EventScript(header, instructions, messages);
    }

    /// <summary>
    /// C3 · The script with new message bytes (one per entry of
    /// <see cref="Messages"/>, terminator included). The code is not recompiled:
    /// everything before the message section stays as it is, except the
    /// argument of each msgShow, which gets the new position of its message.
    /// Bytes of the section that belong to no message are kept in place.
    /// </summary>
    public byte[] WithMessages(ReadOnlySpan<byte> original, IReadOnlyList<byte[]> messages)
    {
        if (messages.Count != Messages.Count)
            throw new ArgumentException($"Esperadas {Messages.Count} mensagens, recebidas {messages.Count}.", nameof(messages));

        var section = original[Layout.MessageSection..];
        using var output = new MemoryStream(original.Length + 1024);
        output.Write(original[..Layout.MessageSection]);
        var moved = new Dictionary<int, int>(Messages.Count);
        var cursor = 0;
        for (var i = 0; i < Messages.Count; i++)
        {
            var message = Messages[i];
            if (message.Offset < cursor)
                throw new InvalidDataException($"{message.Name} se sobrepõe à mensagem anterior.");
            output.Write(section[cursor..message.Offset]);
            moved[message.Offset] = (int)output.Length - Layout.MessageSection;
            output.Write(messages[i]);
            cursor = message.Offset + message.Length;
        }
        output.Write(section[cursor..]);

        var data = output.ToArray();
        foreach (var instruction in Instructions.Where(i => i.Opcode == MessageShowOpcode))
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(instruction.ArgumentOffset), moved[instruction.Arguments[0]]);
        return data;
    }

    private static int I32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
}
