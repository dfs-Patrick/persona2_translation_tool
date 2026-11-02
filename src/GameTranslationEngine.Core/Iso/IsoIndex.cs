using System.Buffers.Binary;
using System.Text;
using GameTranslationEngine.Core.IO;

namespace GameTranslationEngine.Core.Iso;

/// <summary>A file or directory in the ISO 9660 tree.</summary>
public sealed class IsoEntry
{
    public required string Name { get; init; }
    /// <summary>Path from the root, separated by '/', without a leading slash.</summary>
    public required string Path { get; init; }
    public required bool IsDirectory { get; init; }
    /// <summary>First sector (2048 bytes) of the data.</summary>
    public required uint Lba { get; init; }
    public required uint Size { get; init; }
    public DateTime? Recorded { get; init; }
    /// <summary>Absolute position of this entry's directory record (C8 patches it); -1 for the root.</summary>
    public long RecordOffset { get; init; } = -1;
    public List<IsoEntry> Children { get; } = [];

    public long Offset => (long)Lba * IsoIndex.SectorSize;

    public override string ToString() => Path;
}

/// <summary>
/// A2 · ISO index: every file with its sector and size, read straight from
/// the directory records, without copying any data.
/// </summary>
public sealed class IsoIndex : IDisposable
{
    public const int SectorSize = 2048;
    private const int PvdSector = 16;

    private readonly Dictionary<string, IsoEntry> _byPath;

    private IsoIndex(IByteSource source, string volumeId, uint volumeSectors, IsoEntry root)
    {
        Source = source;
        VolumeId = volumeId;
        VolumeSectors = volumeSectors;
        Root = root;
        _byPath = new Dictionary<string, IsoEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Enumerate()) _byPath[entry.Path] = entry;
    }

    public IByteSource Source { get; }
    public string VolumeId { get; }
    public uint VolumeSectors { get; }
    public IsoEntry Root { get; }

    public static IsoIndex Read(IByteSource source)
    {
        if (source.Length < (PvdSector + 1) * SectorSize)
            throw new InvalidDataException("Arquivo pequeno demais para ser uma ISO.");

        var pvd = source.ReadBytes((long)PvdSector * SectorSize, SectorSize);
        if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001")
            throw new InvalidDataException("Descritor de volume ISO 9660 não encontrado no setor 16.");

        var volumeId = Encoding.ASCII.GetString(pvd, 40, 32).TrimEnd(' ', '\0');
        var volumeSectors = BinaryPrimitives.ReadUInt32LittleEndian(pvd.AsSpan(80));
        var rootRecord = ParseRecord(pvd.AsSpan(156), parentPath: null)
            ?? throw new InvalidDataException("Registro do diretório raiz inválido.");

        var root = new IsoEntry
        {
            Name = "", Path = "", IsDirectory = true,
            Lba = rootRecord.Lba, Size = rootRecord.Size, Recorded = rootRecord.Recorded,
        };
        var visited = new HashSet<uint>();
        ReadDirectory(source, root, visited);
        return new IsoIndex(source, volumeId, volumeSectors, root);
    }

    public IsoEntry? Find(string path) =>
        _byPath.GetValueOrDefault(path.Replace('\\', '/').Trim('/'));

    public IsoEntry Get(string path) =>
        Find(path) ?? throw new FileNotFoundException($"Arquivo não encontrado na ISO: {path}");

    /// <summary>Opens a file of the ISO for reading, with no copy.</summary>
    public IByteSource Open(IsoEntry entry)
    {
        if (entry.IsDirectory) throw new InvalidOperationException($"{entry.Path} é um diretório.");
        return Source.Slice(entry.Offset, entry.Size);
    }

    public IByteSource Open(string path) => Open(Get(path));

    /// <summary>Closes the ISO file when this index owns it.</summary>
    public void Dispose() => (Source as IDisposable)?.Dispose();

    /// <summary>All entries (directories first, depth-first), excluding the root.</summary>
    public IEnumerable<IsoEntry> Enumerate()
    {
        var stack = new Stack<IsoEntry>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current != Root) yield return current;
            for (var i = current.Children.Count - 1; i >= 0; i--) stack.Push(current.Children[i]);
        }
    }

    private static void ReadDirectory(IByteSource source, IsoEntry directory, HashSet<uint> visited)
    {
        if (!visited.Add(directory.Lba))
            throw new InvalidDataException($"Diretório repetido (ciclo) em {directory.Path}.");

        var data = source.ReadBytes(directory.Offset, (int)directory.Size);
        var position = 0;
        var index = 0;
        while (position < data.Length)
        {
            var length = data[position];
            if (length == 0)
            {
                // Records never cross a sector; zero length pads to the next one.
                position = (position / SectorSize + 1) * SectorSize;
                continue;
            }
            if (position + length > data.Length)
                throw new InvalidDataException($"Registro de diretório truncado em {directory.Path}.");

            var record = ParseRecord(data.AsSpan(position, length), directory.Path);
            position += length;
            // The first two records are "." and "..".
            if (index++ < 2 || record is null) continue;

            var entry = new IsoEntry
            {
                Name = record.Name,
                Path = directory.Path.Length == 0 ? record.Name : $"{directory.Path}/{record.Name}",
                IsDirectory = record.IsDirectory,
                Lba = record.Lba,
                Size = record.Size,
                Recorded = record.Recorded,
                RecordOffset = directory.Offset + position - length,
            };
            directory.Children.Add(entry);
            if (entry.IsDirectory) ReadDirectory(source, entry, visited);
        }
    }

    private sealed record Record(string Name, bool IsDirectory, uint Lba, uint Size, DateTime? Recorded);

    private static Record? ParseRecord(ReadOnlySpan<byte> record, string? parentPath)
    {
        if (record.Length < 34) return null;
        var lba = BinaryPrimitives.ReadUInt32LittleEndian(record[2..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(record[10..]);
        var flags = record[25];
        var nameLength = record[32];
        if (33 + nameLength > record.Length)
            throw new InvalidDataException($"Nome de arquivo truncado em {parentPath}.");

        var name = Encoding.ASCII.GetString(record.Slice(33, nameLength));
        var versionMark = name.IndexOf(';');
        if (versionMark >= 0) name = name[..versionMark];
        return new Record(name, (flags & 0x02) != 0, lba, size, ParseDate(record.Slice(18, 7)));
    }

    private static DateTime? ParseDate(ReadOnlySpan<byte> raw)
    {
        try
        {
            return new DateTime(1900 + raw[0], raw[1], raw[2], raw[3], raw[4], raw[5], DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
