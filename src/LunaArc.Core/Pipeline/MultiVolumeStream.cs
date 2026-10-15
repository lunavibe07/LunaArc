namespace LunaArc.Core.Pipeline;

/// <summary>
/// Splits output into numbered volume files.
/// </summary>
public sealed class MultiVolumeOutputStream : Stream
{
    private readonly string _baseFilePath;
    private readonly long _maxVolumeSize;
    private FileStream? _currentStream;
    private int _currentPartIndex = 0;
    private long _currentPartBytesWritten = 0;
    private readonly List<string> _createdParts = new();

    public IReadOnlyList<string> CreatedParts => _createdParts;

    public MultiVolumeOutputStream(string baseFilePath, long maxVolumeSize)
    {
        if (maxVolumeSize < 64 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxVolumeSize), "Each volume must be at least 64 KiB.");

        _baseFilePath = baseFilePath;
        _maxVolumeSize = maxVolumeSize;
        OpenNextVolume();
    }

    private void OpenNextVolume()
    {
        _currentStream?.Dispose();
        _currentPartIndex++;
        _currentPartBytesWritten = 0;

        string dir = Path.GetDirectoryName(_baseFilePath) ?? string.Empty;
        string nameNoExt = Path.GetFileNameWithoutExtension(_baseFilePath);
        string ext = Path.GetExtension(_baseFilePath);
        string partPath = Path.Combine(dir, $"{nameNoExt}.part{_currentPartIndex:D3}{ext}");

        _currentStream = new FileStream(partPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024);
        _createdParts.Add(partPath);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        int remaining = count;
        int currentOffset = offset;

        while (remaining > 0)
        {
            if (_currentPartBytesWritten >= _maxVolumeSize)
                OpenNextVolume();

            long spaceInCurrentVolume = _maxVolumeSize - _currentPartBytesWritten;
            int toWrite = (int)Math.Min(remaining, spaceInCurrentVolume);

            _currentStream!.Write(buffer, currentOffset, toWrite);
            _currentPartBytesWritten += toWrite;
            currentOffset += toWrite;
            remaining -= toWrite;
        }
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => (_currentPartIndex - 1) * _maxVolumeSize + _currentPartBytesWritten;
    public override long Position
    {
        get => Length;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _currentStream?.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _currentStream?.Dispose();
        base.Dispose(disposing);
    }
}
