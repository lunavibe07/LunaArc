using System.Formats.Tar;
using System.IO.Compression;
using LunaArc.Core.Contracts;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;
using LunaArc.Core.VfsIndex;

namespace LunaArc.Core.Formats;

/// <summary>
/// Streams standard TAR and TAR GZip archives through the .NET TAR reader.
/// </summary>
internal static class TarArchiveHandler
{
    public static bool Handles(string path) => path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    private static Stream Open(string path)
    {
        var file = File.OpenRead(path);
        return path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)
            ? file : new GZipStream(file, CompressionMode.Decompress);
    }

    public static Task<FlatVfsArena> ReadCatalogAsync(string path, CancellationToken ct) => Task.Run(() =>
    {
        using var stream = Open(path);
        using var reader = new TarReader(stream);
        var arena = new FlatVfsArena();
        while (reader.GetNextEntry() is { } entry)
        {
            ct.ThrowIfCancellationRequested();
            ValidateEntryType(entry);
            arena.AddEntry(entry.Name, entry.Length, entry.Length, 0, entry.ModificationTime.UtcDateTime,
                entry.EntryType == TarEntryType.Directory, false);
        }
        return arena;
    }, ct);

    public static async Task ExtractAsync(string path, ExtractOptions options, LunaJobController job,
        Action<long, long, string>? progress)
    {
        using var stream = Open(path);
        using var reader = new TarReader(stream);
        long processed = 0;
        long total = (await ReadCatalogAsync(path, job.Token)).TotalUncompressedBytes;
        while (reader.GetNextEntry() is { } entry)
        {
            job.WaitIfPaused();
            job.Token.ThrowIfCancellationRequested();
            ValidateEntryType(entry);
            string key = entry.Name.Replace('\\', '/').TrimEnd('/');
            if (options.SelectedEntries is { Count: > 0 } selected && !selected.Contains(key)) continue;
            // Conventional TAR root entries do not represent a file to extract.
            if (entry.EntryType == TarEntryType.Directory && key is "." or "") continue;
            string target = ZipSlipGuard.ValidateAndResolvePath(options.DestinationDirectory, key);
            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            if (File.Exists(target))
            {
                var mode = options.OverwriteMode;
                if (mode == OverwriteMode.Ask)
                    mode = options.OnFileConflictCallback?.Invoke(key) ?? throw new IOException($"Overwrite confirmation required: {key}");
                if (mode == OverwriteMode.SkipExisting) continue;
                if (mode == OverwriteMode.UpdateNewer && File.GetLastWriteTimeUtc(target) >= entry.ModificationTime.UtcDateTime) continue;
                if (mode == OverwriteMode.AutoRename) target = ZipSlipGuard.GetAutoRenamedPath(target);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".lunaarc-" + Guid.NewGuid().ToString("N"));
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                {
                    if (entry.DataStream != null)
                        await LunaStreamPipeline.CopyAndHashChunkedAsync(entry.DataStream, output, entry.Length, job,
                            (current, _) => progress?.Invoke(processed + current, Math.Max(1, total), key));
                }
                job.Token.ThrowIfCancellationRequested();
                File.Move(temporary, target, overwrite: true);
                File.SetLastWriteTimeUtc(target, entry.ModificationTime.UtcDateTime);
                processed += entry.Length;
                progress?.Invoke(processed, Math.Max(1, total), key);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    public static Task<ArchiveTestResult> TestAsync(string path, LunaJobController job, Action<int, int, string>? progress) => Task.Run(() =>
    {
        int passed = 0;
        try
        {
            using var stream = Open(path);
            using var reader = new TarReader(stream);
            byte[] buffer = new byte[LunaStreamPipeline.ChunkSize];
            while (reader.GetNextEntry() is { } entry)
            {
                job.WaitIfPaused();
                job.Token.ThrowIfCancellationRequested();
                ValidateEntryType(entry);
                if (entry.EntryType == TarEntryType.Directory) continue;
                if (entry.DataStream != null)
                    while (entry.DataStream.Read(buffer) > 0)
                    {
                        job.WaitIfPaused();
                        job.Token.ThrowIfCancellationRequested();
                    }
                passed++;
                progress?.Invoke(passed, passed, entry.Name);
            }
            return new ArchiveTestResult(passed, passed, 0, new List<string>());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ArchiveTestResult(passed + 1, passed, 1, new List<string> { ex.Message });
        }
    }, job.Token);

    private static void ValidateEntryType(TarEntry entry)
    {
        if (entry.EntryType is not (TarEntryType.Directory or TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            throw new NotSupportedException($"Unsupported TAR entry type '{entry.EntryType}' in '{entry.Name}'. Links and device entries are not extracted.");
    }
}
