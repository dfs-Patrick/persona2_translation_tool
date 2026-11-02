namespace GameTranslationEngine.Core.Text;

/// <summary>
/// Control codes of Innocent Sin dialogue messages. Binary form: one 16-bit
/// word 0x1L00|op (L = number of words including itself) followed by L-1
/// arguments. Names and text form follow v2, so existing TBF files stay valid.
/// </summary>
public static class EventCommands
{
    private enum Kind { Basic, Number, Lookup, Lookup3, Color, Color2, Symbol }

    private sealed record Definition(string Name, int Opcode, Kind Kind, string? Table = null);

    private static readonly Definition[] Definitions =
    [
        new("nl", 0x01, Kind.Basic),
        new("clear", 0x02, Kind.Basic),
        new("end", 0x03, Kind.Basic),
        new("delay", 0x05, Kind.Number),
        new("wait", 0x06, Kind.Basic),
        new("sync", 0x07, Kind.Basic),
        new("choice", 0x08, Kind.Number),
        new("end_choice", 0x09, Kind.Basic),
        new("var", 0x0E, Kind.Lookup, "msg_var"),
        new("if", 0x10, Kind.Lookup, "bit"),
        new("if_not", 0x11, Kind.Lookup, "bit"),
        new("tatsu", 0x12, Kind.Basic),
        new("tatsuya", 0x13, Kind.Basic),
        new("suou", 0x14, Kind.Basic),
        new("color2", 0x1D, Kind.Color2),
        new("dbl_tab", 0x1F, Kind.Basic),
        new("space", 0x20, Kind.Basic),
        new("half_tab", 0x21, Kind.Basic),
        new("color", 0x31, Kind.Color),
        new("sym", 0x32, Kind.Symbol),
        new("item_with_type", 0x33, Kind.Lookup3, "item"),
    ];

    private static readonly Dictionary<int, Definition> ByOpcode = Definitions.ToDictionary(d => d.Opcode);
    private static readonly Dictionary<string, Definition> ByName = Definitions.ToDictionary(d => d.Name);

    /// <summary>Color names → palette value (the binary stores value - 0x10).</summary>
    public static readonly IReadOnlyDictionary<string, int> Colors = new Dictionary<string, int>
    {
        ["light_blue"] = 7, ["lime"] = 0xB, ["orange"] = 0xD, ["yellow"] = 0x14, ["teal"] = 0x15,
        ["fuschia"] = 0x16, ["light_gray"] = 0x17, ["green"] = 0x18, ["black"] = 0x19, ["blue"] = 0x20,
        ["pink"] = 0x21, ["name_green"] = 0x32, ["rank"] = 0x33, ["lime2"] = 0x13, ["white1"] = 0x11,
        ["white"] = 0x12, ["default"] = 0x02, ["white_5"] = 5, ["white_1"] = 1,
    };

    public static readonly IReadOnlyDictionary<string, int> Symbols = new Dictionary<string, int>
    {
        ["unk1"] = 1, ["unk2"] = 2, ["unk3"] = 3, ["unk4"] = 4, ["unk5"] = 5, ["unk6"] = 6, ["heart"] = 7,
        ["unk8"] = 8, ["unk9"] = 9, ["unka"] = 0xA, ["unkb"] = 0xB, ["unkc"] = 0xC, ["unkd"] = 0xD,
        ["unke"] = 0xE, ["unkf"] = 0xF, ["unk10"] = 0x10, ["unk11"] = 0x11, ["unk12"] = 0x12,
        ["lquote"] = 0x13, ["rqoute"] = 0x14, ["unk15"] = 0x15,
    };

    private static readonly Dictionary<int, string> ColorNames = Invert(Colors);
    private static readonly Dictionary<int, string> SymbolNames = Invert(Symbols);

    /// <summary>Names accepted in the text form (for validation and highlighting).</summary>
    public static IEnumerable<string> KnownNames => ByName.Keys.Append("theater_rank");

    public static CommandPart Decode(int opcode, int[] arguments, MessageContext context)
    {
        if (!ByOpcode.TryGetValue(opcode, out var definition))
            throw new InvalidDataException($"Comando de mensagem desconhecido 0x{opcode:x} em {context.File}.");

        void Expect(int count)
        {
            if (arguments.Length != count)
                throw new InvalidDataException(
                    $"Comando {definition.Name} com {arguments.Length} argumento(s), esperado {count}, em {context.File}.");
        }
        void ExpectZeroPrefix()
        {
            Expect(3);
            if (arguments[0] != 0 || arguments[1] != 0)
                throw new InvalidDataException($"Argumentos inesperados em {definition.Name}, em {context.File}.");
        }

        switch (definition.Kind)
        {
            case Kind.Basic:
                Expect(0);
                return CommandPart.Of(definition.Name);
            case Kind.Number:
                Expect(1);
                return CommandPart.Of(definition.Name, arguments[0].ToString());
            case Kind.Lookup:
                Expect(1);
                return CommandPart.Of(definition.Name, context.Constants.Name(definition.Table!, arguments[0]));
            case Kind.Lookup3:
                ExpectZeroPrefix();
                // The value is the third word. v2 read the first one (always 0) and
                // wrote "item_0", which rebuilt every one of these messages with item 0.
                return CommandPart.Of(definition.Name, context.Constants.Name(definition.Table!, arguments[2]));
            case Kind.Color:
                ExpectZeroPrefix();
                var color = arguments[2] + 0x10;
                if (color == 0x33) return CommandPart.Of("theater_rank");
                return ColorNames.TryGetValue(color, out var colorName)
                    ? CommandPart.Of("color", colorName)
                    : throw new InvalidDataException($"Cor desconhecida 0x{color:x} em {context.File}.");
            case Kind.Color2:
                Expect(1);
                return ColorNames.TryGetValue(arguments[0] & 0xF, out var color2Name)
                    ? CommandPart.Of("color", color2Name)
                    : throw new InvalidDataException($"Cor desconhecida 0x{arguments[0] & 0xF:x} em {context.File}.");
            case Kind.Symbol:
                ExpectZeroPrefix();
                return SymbolNames.TryGetValue(arguments[2], out var symbol)
                    ? CommandPart.Of("sym", symbol)
                    : throw new InvalidDataException($"Símbolo desconhecido 0x{arguments[2]:x} em {context.File}.");
            default:
                throw new InvalidOperationException();
        }
    }

    public static string ToText(CommandPart command) => command.Arguments.Count == 0
        ? command.Name
        : $"{command.Name}({string.Join(", ", command.Arguments)})";

    public static IEnumerable<int> Encode(CommandPart command, MessageContext context)
    {
        if (command.Name == "theater_rank")
        {
            RequireArguments(command, 0);
            return [0x1431, 0, 0, 0x33 - 0x10];
        }
        if (!ByName.TryGetValue(command.Name, out var definition))
            throw new InvalidDataException($"Comando desconhecido [{command.Name}] em {context.File}.");

        switch (definition.Kind)
        {
            case Kind.Basic:
                RequireArguments(command, 0);
                return [0x1100 | definition.Opcode];
            case Kind.Number:
                RequireArguments(command, 1);
                if (!int.TryParse(command.Arguments[0], out var number))
                    throw new InvalidDataException($"[{ToText(command)}] espera um número, em {context.File}.");
                return [0x1200 | definition.Opcode, number];
            case Kind.Lookup:
                RequireArguments(command, 1);
                return [0x1200 | definition.Opcode, context.Constants.Value(definition.Table!, command.Arguments[0])];
            case Kind.Lookup3:
                RequireArguments(command, 1);
                return [0x1400 | definition.Opcode, 0, 0, context.Constants.Value(definition.Table!, command.Arguments[0])];
            case Kind.Color:
                RequireArguments(command, 1);
                // Both color opcodes read back as [color(...)]. Values below 0x10 only
                // exist in the short form (0x1D); v2 always wrote the long one, which
                // turned e.g. white_1 into an invalid 0xFFF1.
                var color = ColorValue(command, context);
                return color >= 0x10 ? [0x1431, 0, 0, color - 0x10] : [0x121D, color];
            case Kind.Color2:
                RequireArguments(command, 1);
                return [0x121D, ColorValue(command, context)];
            case Kind.Symbol:
                RequireArguments(command, 1);
                return Symbols.TryGetValue(command.Arguments[0], out var symbol)
                    ? [0x1432, 0, 0, symbol]
                    : throw new InvalidDataException($"Símbolo desconhecido [{ToText(command)}] em {context.File}.");
            default:
                throw new InvalidOperationException();
        }
    }

    private static int ColorValue(CommandPart command, MessageContext context) =>
        Colors.TryGetValue(command.Arguments[0], out var value)
            ? value
            : throw new InvalidDataException($"Cor desconhecida [{ToText(command)}] em {context.File}.");

    private static void RequireArguments(CommandPart command, int count)
    {
        if (command.Arguments.Count != count)
            throw new InvalidDataException(count == 0
                ? $"[{ToText(command)}] não aceita argumentos."
                : $"[{ToText(command)}] espera {count} argumento(s).");
    }

    private static Dictionary<int, string> Invert(IReadOnlyDictionary<string, int> map)
    {
        var inverted = new Dictionary<int, string>();
        foreach (var (name, value) in map) inverted[value] = name;
        return inverted;
    }
}
