using System.Buffers.Binary;
using System.Text;

namespace GameTranslationEngine.Core.Cpk;

/// <summary>
/// CRI "@UTF" table: the column/row structure CPK uses for its header and
/// tables of contents. All values are big-endian; offsets are relative to
/// byte 8 of the table (right after the magic and the size).
/// </summary>
public sealed class UtfTable
{
    private enum Storage : byte { Zero = 1, Constant = 3, PerRow = 5, Constant2 = 7 }

    private sealed record Column(string Name, byte Type, Storage Storage, object? Constant);

    private readonly List<Column> _columns;
    private readonly List<object?[]> _rows;
    private readonly List<int[]> _cells;

    private UtfTable(string name, List<Column> columns, List<object?[]> rows, List<int[]> cells)
    {
        Name = name;
        _columns = columns;
        _rows = rows;
        _cells = cells;
    }

    public string Name { get; }
    public int RowCount => _rows.Count;
    public IEnumerable<string> ColumnNames => _columns.Select(c => c.Name);

    public bool HasColumn(string column) => _columns.Any(c => c.Name == column);

    public object? Get(int row, string column)
    {
        var index = _columns.FindIndex(c => c.Name == column);
        if (index < 0) throw new KeyNotFoundException($"Coluna {column} ausente na tabela {Name}.");
        return _rows[row][index];
    }

    public ulong GetNumber(int row, string column) => Get(row, column) switch
    {
        null => 0,
        byte v => v, sbyte v => (ulong)v, ushort v => v, short v => (ulong)v,
        uint v => v, int v => (ulong)v, ulong v => v, long v => (ulong)v,
        var other => throw new InvalidDataException($"Coluna {column} não é numérica ({other.GetType().Name})."),
    };

    public string? GetString(int row, string column) => Get(row, column) as string;

    /// <summary>
    /// Writes a number into the raw table <paramref name="utf"/> (the same bytes
    /// given to <see cref="Read"/>), in place. Only per-row numeric cells can be
    /// changed this way; the table keeps its size and every other byte.
    /// </summary>
    public void SetNumber(Span<byte> utf, int row, string column, ulong value)
    {
        var index = _columns.FindIndex(c => c.Name == column);
        if (index < 0) throw new KeyNotFoundException($"Coluna {column} ausente na tabela {Name}.");
        var offset = _cells[row][index];
        if (offset < 0) throw new InvalidOperationException($"A coluna {column} de {Name} não é gravada por linha.");
        var cell = utf[offset..];
        switch (_columns[index].Type)
        {
            case 0 or 1 when value <= byte.MaxValue: cell[0] = (byte)value; break;
            case 2 or 3 when value <= ushort.MaxValue: BinaryPrimitives.WriteUInt16BigEndian(cell, (ushort)value); break;
            case 4 or 5 when value <= uint.MaxValue: BinaryPrimitives.WriteUInt32BigEndian(cell, (uint)value); break;
            case 6 or 7: BinaryPrimitives.WriteUInt64BigEndian(cell, value); break;
            default: throw new InvalidDataException($"O valor {value} não cabe na coluna {column} de {Name}.");
        }
        _rows[row][index] = _columns[index].Type switch
        {
            0 => (byte)value, 1 => (sbyte)value, 2 => (ushort)value, 3 => (short)value,
            4 => (uint)value, 5 => (int)value, 6 => value, _ => (object)(long)value,
        };
    }

    public static UtfTable Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 32 || Encoding.ASCII.GetString(data[..4]) != "@UTF")
            throw new InvalidDataException("Tabela @UTF não encontrada (a tabela pode estar criptografada).");

        var tableSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        var body = data.Slice(8, Math.Min(tableSize, data.Length - 8));
        var rowsOffset = BinaryPrimitives.ReadUInt16BigEndian(body[2..]);
        var stringsOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
        var dataOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(body[8..]);
        var nameOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(body[12..]);
        var columnCount = BinaryPrimitives.ReadUInt16BigEndian(body[16..]);
        var rowLength = BinaryPrimitives.ReadUInt16BigEndian(body[18..]);
        var rowCount = (int)BinaryPrimitives.ReadUInt32BigEndian(body[20..]);

        var strings = body[stringsOffset..].ToArray();
        var blobs = body[dataOffset..].ToArray();
        string Str(uint offset) => ReadCString(strings.AsSpan((int)offset));

        var columns = new List<Column>(columnCount);
        var position = 24;
        for (var i = 0; i < columnCount; i++)
        {
            var flags = body[position];
            var name = Str(BinaryPrimitives.ReadUInt32BigEndian(body[(position + 1)..]));
            position += 5;
            var storage = (Storage)(flags >> 4);
            var type = (byte)(flags & 0x0F);
            object? constant = null;
            if (storage is Storage.Constant or Storage.Constant2)
            {
                constant = ReadValue(body[position..], type, Str, blobs, out var used);
                position += used;
            }
            else if (storage is not (Storage.Zero or Storage.PerRow))
            {
                throw new InvalidDataException($"Armazenamento de coluna desconhecido 0x{flags:X2} em {name}.");
            }
            columns.Add(new Column(name, type, storage, constant));
        }

        var rows = new List<object?[]>(rowCount);
        var cells = new List<int[]>(rowCount);
        for (var r = 0; r < rowCount; r++)
        {
            var row = new object?[columns.Count];
            var cell = new int[columns.Count];
            Array.Fill(cell, -1);
            var cursor = rowsOffset + r * rowLength;
            for (var c = 0; c < columns.Count; c++)
            {
                var column = columns[c];
                switch (column.Storage)
                {
                    case Storage.Zero: row[c] = null; break;
                    case Storage.Constant or Storage.Constant2: row[c] = column.Constant; break;
                    default:
                        cell[c] = 8 + cursor;
                        row[c] = ReadValue(body[cursor..], column.Type, Str, blobs, out var used);
                        cursor += used;
                        break;
                }
            }
            rows.Add(row);
            cells.Add(cell);
        }

        return new UtfTable(Str((uint)nameOffset), columns, rows, cells);
    }

    private static object? ReadValue(ReadOnlySpan<byte> data, byte type, Func<uint, string> str,
        ReadOnlySpan<byte> blobs, out int used)
    {
        switch (type)
        {
            case 0: used = 1; return data[0];
            case 1: used = 1; return (sbyte)data[0];
            case 2: used = 2; return BinaryPrimitives.ReadUInt16BigEndian(data);
            case 3: used = 2; return BinaryPrimitives.ReadInt16BigEndian(data);
            case 4: used = 4; return BinaryPrimitives.ReadUInt32BigEndian(data);
            case 5: used = 4; return BinaryPrimitives.ReadInt32BigEndian(data);
            case 6: used = 8; return BinaryPrimitives.ReadUInt64BigEndian(data);
            case 7: used = 8; return BinaryPrimitives.ReadInt64BigEndian(data);
            case 8: used = 4; return BinaryPrimitives.ReadSingleBigEndian(data);
            case 0xA: used = 4; return str(BinaryPrimitives.ReadUInt32BigEndian(data));
            case 0xB:
                used = 8;
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                return blobs.Slice(offset, size).ToArray();
            default:
                throw new InvalidDataException($"Tipo de coluna @UTF desconhecido: 0x{type:X}.");
        }
    }

    private static string ReadCString(ReadOnlySpan<byte> data)
    {
        var end = data.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? data : data[..end]);
    }
}
