using System.Runtime.InteropServices;

namespace LunaArc.Core.VfsIndex;

/// <summary>
/// Packed archive entry metadata; strings are stored separately.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct LunaVfsNode
{
    public int NameOffset;
    public int FullPathOffset;
    public int ParentIndex;
    public long UncompressedSize;
    public long CompressedSize;
    public uint Crc32;
    public int LastWriteUnixTime;
    public byte Attributes;

    public readonly bool IsDirectory => (Attributes & 0b0001) != 0;
    public readonly bool IsEncrypted => (Attributes & 0b0010) != 0;

    public readonly double CompressionRatio =>
        UncompressedSize <= 0 ? 0.0 : Math.Max(0.0, Math.Round((1.0 - (double)CompressedSize / UncompressedSize) * 100.0, 1));
}
