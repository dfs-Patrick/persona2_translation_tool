namespace GameTranslationEngine.Core.Text;

/// <summary>
/// Control codes of the dialogue messages of a Persona 2 game. Binary form: one
/// 16-bit word 0x1L00|op (L = number of words including itself) followed by L-1
/// arguments. Both games share that form but not the opcodes, so each one has
/// its own set. Names and text form follow v2, so existing TBF files stay valid.
/// </summary>
public sealed class EventCommands
{
    private enum Kind
    {
        Basic,
        Number,
        /// <summary>One argument, a named constant (bit, item, variable…).</summary>
        Lookup,
        /// <summary>IS: three arguments, the constant is the third.</summary>
        Lookup3,
        /// <summary>IS 0x31: three arguments, palette value - 0x10 in the third.</summary>
        Color,
        /// <summary>IS 0x1D: one argument, palette value in the low nibble.</summary>
        Color2,
        /// <summary>IS 0x32: three arguments, symbol in the third.</summary>
        Symbol,
        /// <summary>EP 0x2E: one argument, palette index.</summary>
        PaletteColor,
        /// <summary>EP: n, n bits that must be set, m, m bits that must be clear ("!BIT" in text).</summary>
        Bits,
    }

    private sealed record Definition(string Name, int Opcode, Kind Kind, string? Table = null);

    /// <summary>Persona 2: Innocent Sin (US).</summary>
    public static readonly EventCommands InnocentSin = new(
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
    ],
    colors: new Dictionary<string, int>
    {
        ["light_blue"] = 7, ["lime"] = 0xB, ["orange"] = 0xD, ["yellow"] = 0x14, ["teal"] = 0x15,
        ["fuschia"] = 0x16, ["light_gray"] = 0x17, ["green"] = 0x18, ["black"] = 0x19, ["blue"] = 0x20,
        ["pink"] = 0x21, ["name_green"] = 0x32, ["rank"] = 0x33, ["lime2"] = 0x13, ["white1"] = 0x11,
        ["white"] = 0x12, ["default"] = 0x02, ["white_5"] = 5, ["white_1"] = 1,
    },
    symbols: new Dictionary<string, int>
    {
        ["unk1"] = 1, ["unk2"] = 2, ["unk3"] = 3, ["unk4"] = 4, ["unk5"] = 5, ["unk6"] = 6, ["heart"] = 7,
        ["unk8"] = 8, ["unk9"] = 9, ["unka"] = 0xA, ["unkb"] = 0xB, ["unkc"] = 0xC, ["unkd"] = 0xD,
        ["unke"] = 0xE, ["unkf"] = 0xF, ["unk10"] = 0x10, ["unk11"] = 0x11, ["unk12"] = 0x12,
        ["lquote"] = 0x13, ["rqoute"] = 0x14, ["unk15"] = 0x15,
    },
    extraNames: ["theater_rank"]);

    /// <summary>Persona 2: Eternal Punishment (PSP; table from v2 ep_event.ts).</summary>
    public static readonly EventCommands EternalPunishment = new(
    [
        new("nl", 0x01, Kind.Basic),
        new("clear", 0x02, Kind.Basic),
        new("end", 0x03, Kind.Basic),
        new("delay", 0x05, Kind.Number),
        new("wait", 0x06, Kind.Basic),
        new("sync", 0x07, Kind.Basic),
        new("choice", 0x08, Kind.Number),
        new("end_choice", 0x09, Kind.Basic),
        new("play_sound", 0x0B, Kind.Lookup, "sndeff"),
        new("load_sound", 0x0C, Kind.Lookup, "sndeff"),
        new("var", 0x0E, Kind.Lookup, "msg_var"),
        new("suou", 0x20, Kind.Basic),
        new("tatsuya", 0x21, Kind.Basic),
        new("tatsu", 0x22, Kind.Basic),
        new("pact_demon", 0x23, Kind.Number),
        new("yen", 0x24, Kind.Basic),
        new("coins", 0x25, Kind.Basic),
        new("if", 0x26, Kind.Lookup, "bit"),
        new("if_not", 0x27, Kind.Lookup, "bit"),
        new("if_all", 0x28, Kind.Bits),
        new("if_none", 0x29, Kind.Bits),
        new("if_any", 0x2A, Kind.Bits),
        new("if_not_all", 0x2B, Kind.Bits),
        new("line_height", 0x2D, Kind.Number),
        new("color", 0x2E, Kind.PaletteColor),
        new("unk2f", 0x2F, Kind.Number),
        new("dbl_tab", 0x30, Kind.Basic),
        new("tab", 0x31, Kind.Basic),
        new("half_tab", 0x32, Kind.Basic),
        new("set_x", 0x33, Kind.Number),
        new("set_y", 0x34, Kind.Number),
        new("item_with_type", 0x35, Kind.Lookup, "item"),
    ],
    colors: new Dictionary<string, int>
    {
        ["white"] = 1, ["white_inv"] = 2, ["gray"] = 3, ["gray_inv"] = 4, ["pink"] = 5, ["pink_inv"] = 6,
        ["blue"] = 7, ["blue_inv"] = 8, ["green"] = 9, ["green_inv"] = 10, ["yellow"] = 11,
        ["yellow_inv"] = 12, ["orange"] = 13, ["orange_inv"] = 14,
    },
    symbols: new Dictionary<string, int>(),
    extraNames: []);

    private readonly Dictionary<int, Definition> _byOpcode;
    private readonly Dictionary<string, Definition> _byName;
    private readonly Dictionary<int, string> _colorNames;
    private readonly Dictionary<int, string> _symbolNames;
    private readonly string[] _extraNames;

    private EventCommands(Definition[] definitions, Dictionary<string, int> colors, Dictionary<string, int> symbols,
        string[] extraNames)
    {
        _byOpcode = definitions.ToDictionary(d => d.Opcode);
        _byName = definitions.ToDictionary(d => d.Name);
        Colors = colors;
        Symbols = symbols;
        _colorNames = Invert(colors);
        _symbolNames = Invert(symbols);
        _extraNames = extraNames;
    }

    /// <summary>Color names → palette value.</summary>
    public IReadOnlyDictionary<string, int> Colors { get; }

    public IReadOnlyDictionary<string, int> Symbols { get; }

    /// <summary>Names accepted in the text form (for validation and highlighting).</summary>
    public IEnumerable<string> KnownNames => _byName.Keys.Concat(_extraNames);

    public CommandPart Decode(int opcode, int[] arguments, MessageContext context)
    {
        if (!_byOpcode.TryGetValue(opcode, out var definition))
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
                return _colorNames.TryGetValue(color, out var colorName)
                    ? CommandPart.Of("color", colorName)
                    : throw new InvalidDataException($"Cor desconhecida 0x{color:x} em {context.File}.");
            case Kind.Color2:
                Expect(1);
                return _colorNames.TryGetValue(arguments[0] & 0xF, out var color2Name)
                    ? CommandPart.Of("color", color2Name)
                    : throw new InvalidDataException($"Cor desconhecida 0x{arguments[0] & 0xF:x} em {context.File}.");
            case Kind.Symbol:
                ExpectZeroPrefix();
                return _symbolNames.TryGetValue(arguments[2], out var symbol)
                    ? CommandPart.Of("sym", symbol)
                    : throw new InvalidDataException($"Símbolo desconhecido 0x{arguments[2]:x} em {context.File}.");
            case Kind.PaletteColor:
                Expect(1);
                return _colorNames.TryGetValue(arguments[0], out var paletteName)
                    ? CommandPart.Of("color", paletteName)
                    : throw new InvalidDataException($"Cor desconhecida {arguments[0]} em {context.File}.");
            case Kind.Bits:
                return CommandPart.Of(definition.Name, DecodeBits(definition, arguments, context));
            default:
                throw new InvalidOperationException();
        }
    }

    private static string[] DecodeBits(Definition definition, int[] arguments, MessageContext context)
    {
        string Invalid() => $"Lista de bits inválida em {definition.Name}, em {context.File}.";
        if (arguments.Length < 2 || arguments[0] + 2 > arguments.Length) throw new InvalidDataException(Invalid());
        var set = arguments[0];
        var clear = arguments[1 + set];
        if (2 + set + clear != arguments.Length) throw new InvalidDataException(Invalid());
        return arguments.Skip(1).Take(set).Select(b => context.Constants.Name("bit", b))
            .Concat(arguments.Skip(2 + set).Select(b => "!" + context.Constants.Name("bit", b)))
            .ToArray();
    }

    public static string ToText(CommandPart command) => command.Arguments.Count == 0
        ? command.Name
        : $"{command.Name}({string.Join(", ", command.Arguments)})";

    public IEnumerable<int> Encode(CommandPart command, MessageContext context)
    {
        if (command.Name == "theater_rank" && _extraNames.Contains("theater_rank"))
        {
            RequireArguments(command, 0);
            return [0x1431, 0, 0, 0x33 - 0x10];
        }
        if (!_byName.TryGetValue(command.Name, out var definition))
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
            case Kind.PaletteColor:
                RequireArguments(command, 1);
                return [0x1200 | definition.Opcode, ColorValue(command, context)];
            case Kind.Bits:
                return EncodeBits(definition, command, context);
            default:
                throw new InvalidOperationException();
        }
    }

    private static int[] EncodeBits(Definition definition, CommandPart command, MessageContext context)
    {
        if (command.Arguments.Count == 0)
            throw new InvalidDataException($"[{ToText(command)}] espera ao menos um bit, em {context.File}.");
        var set = new List<int>();
        var clear = new List<int>();
        foreach (var argument in command.Arguments.Select(a => a.Trim()))
        {
            if (argument.StartsWith('!')) clear.Add(context.Constants.Value("bit", argument[1..].Trim()));
            else set.Add(context.Constants.Value("bit", argument));
        }
        var length = set.Count + clear.Count + 3;
        return [0x1000 | (length << 8) | definition.Opcode, set.Count, .. set, clear.Count, .. clear];
    }

    private int ColorValue(CommandPart command, MessageContext context) =>
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
