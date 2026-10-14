namespace LunaArc.Core.Pipeline;

/// <summary>Discards verified output while enforcing cancellation, pause and the declared size.</summary>
internal sealed class VerificationWriteStream(LunaJobController job, long expected) : Stream
{
    public long Written { get; private set; }
    private uint _runningCrc = 0xffffffff;
    public uint Crc => ~_runningCrc;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Written;
    public override long Position { get => Written; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        job.WaitIfPaused();
        job.Token.ThrowIfCancellationRequested();
        if (expected < 0 || buffer.Length > expected - Written)
            throw new InvalidDataException("Archive output exceeds its declared size.");
        Written += buffer.Length;
        _runningCrc = LunaArc.Core.Security.Crc32Verifier.UpdateCrc32(_runningCrc, buffer);
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
