namespace LunaArc.Core.Pipeline;

/// <summary>A non-owning view of one payload; never reads bytes from the following entry.</summary>
internal sealed class BoundedReadStream(Stream source, long length) : Stream
{
    public long Remaining { get; private set; } = length >= 0 ? length : throw new ArgumentOutOfRangeException(nameof(length));
    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => length;
    public override long Position { get => length - Remaining; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        int read = source.Read(buffer[..(int)Math.Min(buffer.Length, Remaining)]);
        if (read == 0 && buffer.Length > 0 && Remaining > 0) throw new EndOfStreamException("Truncated archive payload.");
        Remaining -= read;
        return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        int read = await source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, Remaining)], ct);
        if (read == 0 && buffer.Length > 0 && Remaining > 0) throw new EndOfStreamException("Truncated archive payload.");
        Remaining -= read;
        return read;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override void Flush() { }
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
