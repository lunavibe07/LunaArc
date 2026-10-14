using LunaArc.Core.Pipeline;
using LunaArc.Core.VfsIndex;

namespace LunaArc.Core.Contracts;

public interface IArchiveBackend
{
    Task<FlatVfsArena> ReadCatalogAsync(string archivePath, string? password = null, CancellationToken ct = default);

    Task ExtractAsync(
        string archivePath,
        ExtractOptions options,
        LunaJobController controller,
        Action<long, long, string>? onProgress = null);

    Task CompressAsync(
        IReadOnlyList<string> sourcePaths,
        CompressOptions options,
        LunaJobController controller,
        Action<long, long, string>? onProgress = null);

    Task<ArchiveTestResult> TestArchiveAsync(
        string archivePath,
        string? password,
        LunaJobController controller,
        Action<int, int, string>? onProgress = null);
}
