using Microsoft.Win32.SafeHandles;

namespace GameTranslationEngine.Core.IO;

/// <summary>
/// Read-only access to a sequence of bytes by position. Lets a file inside
/// the ISO (or inside a CPK inside the ISO) be read without extracting it.
/// </summary>
public interface IByteSource
{
    long Length { get; }

    /// <summary>Fills <paramref name="buffer"/> starting at <paramref name="offset"/>.</summary>
    void Read(long offset, Span<byte> buffer);
}

public static class ByteSourceExtensions
{
    public static byte[] ReadBytes(this IByteSource source, long offset, int length)
    {
        var buffer = new byte[length];
        source.Read(offset, buffer);
        return buffer;
    }

    public static byte[] ReadAll(this IByteSource source)
    {
        if (source.Length > int.MaxValue)
            throw new InvalidOperationException("Arquivo grande demais para carregar em memória.");
        return source.ReadBytes(0, (int)source.Length);
    }

    /// <summary>A window over <paramref name="length"/> bytes starting at <paramref name="offset"/>.</summary>
    public static IByteSource Slice(this IByteSource source, long offset, long length)
    {
        if (offset < 0 || length < 0 || offset + length > source.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Intervalo fora dos limites da origem.");
        return source is SliceByteSource slice
            ? new SliceByteSource(slice.Inner, slice.Start + offset, length)
            : new SliceByteSource(source, offset, length);
    }
}

/// <summary>A file on disk, read with positional reads (thread-safe, no shared cursor).</summary>
public sealed class FileByteSource : IByteSource, IDisposable
{
    private readonly SafeFileHandle _handle;

    public FileByteSource(string path)
    {
        Path = path;
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            FileOptions.RandomAccess);
        Length = RandomAccess.GetLength(_handle);
    }

    public string Path { get; }
    public long Length { get; }

    public void Read(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Leitura fora dos limites do arquivo.");
        var done = 0;
        while (done < buffer.Length)
        {
            var read = RandomAccess.Read(_handle, buffer[done..], offset + done);
            if (read == 0) throw new EndOfStreamException($"Fim inesperado de {Path}.");
            done += read;
        }
    }

    public void Dispose() => _handle.Dispose();
}

public sealed class MemoryByteSource(ReadOnlyMemory<byte> data) : IByteSource
{
    public long Length => data.Length;

    public void Read(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Leitura fora dos limites do buffer.");
        data.Span.Slice((int)offset, buffer.Length).CopyTo(buffer);
    }
}

internal sealed class SliceByteSource(IByteSource inner, long start, long length) : IByteSource
{
    public IByteSource Inner { get; } = inner;
    public long Start { get; } = start;
    public long Length { get; } = length;

    public void Read(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Leitura fora dos limites do trecho.");
        Inner.Read(Start + offset, buffer);
    }
}
