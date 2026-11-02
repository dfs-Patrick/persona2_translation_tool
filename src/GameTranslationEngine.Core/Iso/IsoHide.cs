using System.Buffers.Binary;
using System.Text;
using GameTranslationEngine.Core.IO;

namespace GameTranslationEngine.Core.Iso;

/// <summary>
/// Hides a directory of a written ISO from the game by renaming it, keeping
/// the name's length: its record in the parent directory and its entries in
/// both path tables. EP: PSP_GAME/INSDIR holds the copy of the data pack the
/// game installs to the Memory Stick; while it exists, an installed (Japanese)
/// copy would be read instead of the translated pack (v1 deleted the folder).
/// </summary>
public static class IsoHide
{
    public static void RenameDirectory(string isoPath, string path, string newName)
    {
        string oldName;
        long recordOffset;
        using (var iso = IsoIndex.Read(new FileByteSource(isoPath)))
        {
            var entry = iso.Find(path) ?? throw new InvalidDataException($"{path} não existe na ISO.");
            if (!entry.IsDirectory) throw new InvalidDataException($"{path} não é uma pasta.");
            oldName = entry.Name;
            recordOffset = entry.RecordOffset;
        }
        if (newName.Length != oldName.Length) throw new ArgumentException("O novo nome precisa ter o mesmo tamanho.");
        var oldBytes = Encoding.ASCII.GetBytes(oldName);
        var newBytes = Encoding.ASCII.GetBytes(newName);

        using var stream = new FileStream(isoPath, FileMode.Open, FileAccess.ReadWrite);
        // Directory record: name length at +32, name at +33.
        Replace(stream, recordOffset + 33, oldBytes, newBytes);

        // Path tables (little- and big-endian copies), sizes and places in the volume descriptor.
        var descriptor = new byte[IsoIndex.SectorSize];
        stream.Position = 16L * IsoIndex.SectorSize;
        stream.ReadExactly(descriptor);
        var tableSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(132));
        foreach (var lba in new[] { BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(140)), BinaryPrimitives.ReadUInt32BigEndian(descriptor.AsSpan(148)) })
        {
            var table = new byte[tableSize];
            stream.Position = (long)lba * IsoIndex.SectorSize;
            stream.ReadExactly(table);
            var found = false;
            for (var at = 0; at < table.Length;)
            {
                int length = table[at];
                if (length == 0) break;
                if (table.AsSpan(at + 8, length).SequenceEqual(oldBytes))
                {
                    newBytes.CopyTo(table.AsSpan(at + 8));
                    found = true;
                }
                at += 8 + length + (length & 1);
            }
            if (!found) throw new InvalidDataException($"{oldName} não encontrado na tabela de caminhos.");
            stream.Position = (long)lba * IsoIndex.SectorSize;
            stream.Write(table);
        }
    }

    private static void Replace(FileStream stream, long offset, byte[] expected, byte[] replacement)
    {
        var current = new byte[expected.Length];
        stream.Position = offset;
        stream.ReadExactly(current);
        if (!current.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException("O registro de diretório não tem o nome esperado.");
        stream.Position = offset;
        stream.Write(replacement);
    }
}
