namespace LunaArc.Core.VfsIndex;

/// <summary>
/// Flat virtual directory index for archive entries.
/// </summary>
public sealed class FlatVfsArena
{
    private LunaVfsNode[] _nodes;
    private int _count;
    private readonly List<string> _stringPool;
    private readonly Dictionary<string, int> _folderPathToIndex;

    public string ArchiveComment { get; set; } = string.Empty;
    public long TotalUncompressedBytes { get; private set; }
    public long TotalCompressedBytes { get; private set; }

    public FlatVfsArena(int initialCapacity = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        _nodes = new LunaVfsNode[initialCapacity];
        _stringPool = new List<string>(initialCapacity * 2);
        _folderPathToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        _count = 0;
    }

    public int Count => _count;

    public int AddEntry(
        string fullRelativePath,
        long origSize,
        long compSize,
        uint crc,
        DateTime? lastModified,
        bool isDir,
        bool isEncrypted)
    {
        string normalized = fullRelativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(normalized))
            return -1;

        int lastSlash = normalized.LastIndexOf('/');
        string fileName = lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
        string parentPath = lastSlash >= 0 ? normalized[..lastSlash] : string.Empty;

        int parentIndex = EnsureParentFolderIndex(parentPath);

        if (isDir && _folderPathToIndex.TryGetValue(normalized, out int existingDirIdx))
            return existingDirIdx;

        int nodeIdx = InsertNodeInternal(fileName, normalized, parentIndex, origSize, compSize, crc, lastModified, isDir, isEncrypted);
        if (isDir)
        {
            _folderPathToIndex[normalized] = nodeIdx;
        }
        else
        {
            TotalUncompressedBytes += origSize;
            TotalCompressedBytes += compSize;
        }

        return nodeIdx;
    }

    private int EnsureParentFolderIndex(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
            return -1;

        if (_folderPathToIndex.TryGetValue(folderPath, out int idx))
            return idx;

        int lastSlash = folderPath.LastIndexOf('/');
        string folderName = lastSlash >= 0 ? folderPath[(lastSlash + 1)..] : folderPath;
        string grandParent = lastSlash >= 0 ? folderPath[..lastSlash] : string.Empty;

        int grandParentIdx = EnsureParentFolderIndex(grandParent);
        int createdIdx = InsertNodeInternal(folderName, folderPath, grandParentIdx, 0, 0, 0, DateTime.UtcNow, isDir: true, isEncrypted: false);
        _folderPathToIndex[folderPath] = createdIdx;
        return createdIdx;
    }

    private int InsertNodeInternal(
        string name,
        string fullPath,
        int parentIndex,
        long origSize,
        long compSize,
        uint crc,
        DateTime? lastModified,
        bool isDir,
        bool isEncrypted)
    {
        if (_count == _nodes.Length)
            Array.Resize(ref _nodes, _nodes.Length * 2);

        int nameIdx = _stringPool.Count;
        _stringPool.Add(name);
        int fullPathIdx = _stringPool.Count;
        _stringPool.Add(fullPath);

        byte attr = 0;
        if (isDir) attr |= 0b0001;
        if (isEncrypted) attr |= 0b0010;

        int unixTime = lastModified.HasValue
            ? (int)new DateTimeOffset(lastModified.Value.ToUniversalTime()).ToUnixTimeSeconds()
            : 0;

        _nodes[_count] = new LunaVfsNode
        {
            NameOffset = nameIdx,
            FullPathOffset = fullPathIdx,
            ParentIndex = parentIndex,
            UncompressedSize = origSize,
            CompressedSize = compSize,
            Crc32 = crc,
            LastWriteUnixTime = unixTime,
            Attributes = attr
        };

        return _count++;
    }

    public ref readonly LunaVfsNode GetNode(int index) => ref _nodes[index];
    public string GetName(int index) => _stringPool[_nodes[index].NameOffset];
    public string GetFullPath(int index) => _stringPool[_nodes[index].FullPathOffset];

    public List<int> GetChildrenIndices(int parentIndex)
    {
        var result = new List<int>();
        for (int i = 0; i < _count; i++)
        {
            if (_nodes[i].ParentIndex == parentIndex)
                result.Add(i);
        }
        result.Sort((a, b) =>
        {
            bool dirA = _nodes[a].IsDirectory;
            bool dirB = _nodes[b].IsDirectory;
            if (dirA != dirB) return dirB.CompareTo(dirA);
            return string.Compare(GetName(a), GetName(b), StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    public bool HasSingleRootFolder(out string rootFolderName)
    {
        rootFolderName = string.Empty;
        var rootChildren = GetChildrenIndices(-1);
        if (rootChildren.Count == 1 && _nodes[rootChildren[0]].IsDirectory)
        {
            rootFolderName = GetName(rootChildren[0]);
            return true;
        }
        return false;
    }
}
