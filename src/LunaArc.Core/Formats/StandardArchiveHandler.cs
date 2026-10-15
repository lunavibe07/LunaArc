using System.IO.Compression;
using LunaArc.Core.Contracts;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;
using LunaArc.Core.VfsIndex;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace LunaArc.Core.Formats;

/// <summary>
/// Shared archive backend for catalog, extraction, compression and testing.
/// </summary>
public sealed class StandardArchiveHandler : IArchiveBackend
{
    private readonly LarcNativeHandler _larcHandler = new();

    public async Task<FlatVfsArena> ReadCatalogAsync(string archivePath, string? password = null, CancellationToken ct = default)
    {
        if (archivePath.EndsWith(".larc", StringComparison.OrdinalIgnoreCase) || LarcNativeHandler.IsLarcFile(archivePath))
        {
            return await _larcHandler.ReadCatalogAsync(archivePath, password);
        }

        if (TarArchiveHandler.Handles(archivePath))
            return await TarArchiveHandler.ReadCatalogAsync(archivePath, ct);

        return await Task.Run(() =>
        {
            var readerOptions = new ReaderOptions { Password = password, LeaveStreamOpen = false };
            using var archive = ArchiveFactory.OpenArchive(archivePath, readerOptions);
            var arena = new FlatVfsArena();

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                arena.AddEntry(
                    entry.Key ?? string.Empty,
                    entry.Size,
                    entry.CompressedSize,
                    (uint)entry.Crc,
                    entry.LastModifiedTime,
                    entry.IsDirectory,
                    entry.IsEncrypted);
            }

            return arena;
        }, ct);
    }

    public async Task ExtractAsync(
        string archivePath,
        ExtractOptions options,
        LunaJobController controller,
        Action<long, long, string>? onProgress = null)
    {
        string targetRoot = options.DestinationDirectory;
        if (!Enum.IsDefined(options.UpdateMode)) throw new ArgumentOutOfRangeException(nameof(options.UpdateMode));

        if (options.SmartFolderCreation)
        {
            var catalog = await ReadCatalogAsync(archivePath, options.Password, controller.Token);
            if (!catalog.HasSingleRootFolder(out _) && options.UpdateMode != ExtractionUpdateMode.FreshenExisting)
            {
                Directory.CreateDirectory(targetRoot);
            }
        }
        else if (options.UpdateMode != ExtractionUpdateMode.FreshenExisting)
        {
            Directory.CreateDirectory(targetRoot);
        }

        if (archivePath.EndsWith(".larc", StringComparison.OrdinalIgnoreCase) || LarcNativeHandler.IsLarcFile(archivePath))
        {
            await _larcHandler.ExtractLarcArchiveAsync(archivePath, options, controller, onProgress);
            return;
        }

        if (TarArchiveHandler.Handles(archivePath))
        {
            await TarArchiveHandler.ExtractAsync(archivePath, options, controller, onProgress);
            return;
        }

        var readerOptions = new ReaderOptions { Password = options.Password, LeaveStreamOpen = false };
        using var archive = ArchiveFactory.OpenArchive(archivePath, readerOptions);

        var noCrc = archive.Type == ArchiveType.Zip
            ? ZipAesIntegrity.Validate(archivePath, options.Password, archive.Entries.ToArray(), controller)
            : new HashSet<IArchiveEntry>();

        long totalBytes = archive.Entries.Where(e => !e.IsDirectory).Sum(e => e.Size);
        long processedBytes = 0;

        foreach (var entry in archive.Entries)
        {
            controller.WaitIfPaused();
            controller.Token.ThrowIfCancellationRequested();

            string entryKey = (entry.Key ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(entryKey)) continue;

            if (options.SelectedEntries != null &&
                options.SelectedEntries.Count > 0 &&
                !options.SelectedEntries.Contains(entryKey))
            {
                continue;
            }

            string safeTargetPath = ZipSlipGuard.ValidateAndResolvePath(targetRoot, entryKey);

            if (entry.IsDirectory)
            {
                if (options.UpdateMode != ExtractionUpdateMode.FreshenExisting) Directory.CreateDirectory(safeTargetPath);
                continue;
            }

            if (!ExtractionUpdatePolicy.ShouldExtract(safeTargetPath, entry.LastModifiedTime, options.UpdateMode)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(safeTargetPath)!);

            if (File.Exists(safeTargetPath))
            {
                OverwriteMode mode = options.OverwriteMode == OverwriteMode.Ask && options.OnFileConflictCallback != null
                    ? options.OnFileConflictCallback(entryKey)
                    : options.OverwriteMode;
                if (mode == OverwriteMode.Ask) throw new IOException($"Overwrite confirmation required: {entryKey}");

                if (mode == OverwriteMode.SkipExisting) continue;
                if (mode == OverwriteMode.UpdateNewer &&
                    entry.LastModifiedTime.HasValue &&
                    File.GetLastWriteTimeUtc(safeTargetPath) >= entry.LastModifiedTime.Value.ToUniversalTime())
                {
                    continue;
                }
                if (mode == OverwriteMode.AutoRename)
                {
                    safeTargetPath = ZipSlipGuard.GetAutoRenamedPath(safeTargetPath);
                }
            }

            uint expectedCrc = (uint)entry.Crc;
            await AtomicExtractFile.WriteAsync(safeTargetPath, options.KeepBrokenFiles, controller.Token, async outFile =>
            {
                await using var entryStream = entry.OpenEntryStream();

                long entryBase = processedBytes;
                uint crc = await LunaStreamPipeline.CopyAndHashChunkedAsync(
                    entryStream,
                    outFile,
                    entry.Size,
                    controller,
                    (curr, _) => onProgress?.Invoke(entryBase + curr, Math.Max(1, totalBytes), entryKey));
                if (archive.Type == ArchiveType.Zip && !noCrc.Contains(entry) && crc != expectedCrc)
                    throw new InvalidDataException($"CRC32 mismatch in '{entryKey}'.");
            });
            if (entry.LastModifiedTime.HasValue)
                File.SetLastWriteTimeUtc(safeTargetPath, entry.LastModifiedTime.Value.ToUniversalTime());
            processedBytes += entry.Size;
        }
    }

    public async Task CompressAsync(
        IReadOnlyList<string> sourcePaths,
        CompressOptions options,
        LunaJobController controller,
        Action<long, long, string>? onProgress = null)
    {
        controller.Token.ThrowIfCancellationRequested();
        if (sourcePaths.Count == 0 || sourcePaths.Any(p => !File.Exists(p) && !Directory.Exists(p)))
            throw new ArgumentException("Every source must be an existing file or directory.");
        if (options.Format == OutputArchiveFormat.TarZst)
            throw new NotSupportedException("TAR Zstandard output is not implemented. Use .tar.gz or .larc.");
        if (options.Format != OutputArchiveFormat.LarcNative && !string.IsNullOrEmpty(options.Password))
            throw new NotSupportedException("Password-protected output is supported for .larc only.");
        if (!Enum.IsDefined(options.Format)) throw new ArgumentOutOfRangeException(nameof(options.Format));
        string output = Path.GetFullPath(options.OutputArchivePath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (string source in sourcePaths)
        {
            string fullSource = Path.GetFullPath(source);
            if (output.Equals(fullSource, comparison) || (Directory.Exists(fullSource) &&
                output.StartsWith(Path.TrimEndingDirectorySeparator(fullSource) + Path.DirectorySeparatorChar, comparison)))
                throw new ArgumentException("The output archive must be outside the source files and directories.");
        }
        if (options.Format == OutputArchiveFormat.LarcNative)
        {
            await _larcHandler.CreateLarcArchiveAsync(sourcePaths, options, controller, onProgress);
        }
        else if (options.Format == OutputArchiveFormat.Zip)
        {
            await CreateZipArchiveAsync(sourcePaths, options, controller, onProgress);
        }
        else
        {
            await CreateTarCompressedArchiveAsync(sourcePaths, options, controller, onProgress);
        }

        if (options.DeleteSourceAfterArchiving && !controller.IsCancelled)
        {
            var verification = await TestArchiveAsync(output, options.Password, controller);
            if (!verification.IsHealthy) throw new InvalidDataException("Archive verification failed; source files were preserved.");
            foreach (string src in sourcePaths)
            {
                if (File.Exists(src)) File.Delete(src);
                else if (Directory.Exists(src)) Directory.Delete(src, recursive: true);
            }
        }
    }

    private static async Task CreateZipArchiveAsync(
        IReadOnlyList<string> sourcePaths,
        CompressOptions options,
        LunaJobController controller,
        Action<long, long, string>? onProgress)
    {
        CompressionLevel netLevel = options.Level switch
        {
            LunaCompressionLevel.Store => CompressionLevel.NoCompression,
            LunaCompressionLevel.Fastest or LunaCompressionLevel.Fast => CompressionLevel.Fastest,
            LunaCompressionLevel.Best => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal
        };

        await using Stream baseOut = options.SplitVolumeSizeBytes.HasValue
            ? new MultiVolumeOutputStream(options.OutputArchivePath, options.SplitVolumeSizeBytes.Value)
            : new FileStream(options.OutputArchivePath, FileMode.Create, FileAccess.Write, FileShare.None);

        using var zip = new ZipArchive(baseOut, ZipArchiveMode.Create, leaveOpen: false);
        if (!string.IsNullOrEmpty(options.ArchiveComment))
            zip.Comment = options.ArchiveComment;

        var files = new List<(string FullPath, string RelPath)>();
        foreach (string p in sourcePaths)
        {
            string full = Path.GetFullPath(p);
            string baseDir = Path.GetDirectoryName(full) ?? full;
            if (File.Exists(full))
                files.Add((full, Path.GetFileName(full)));
            else if (Directory.Exists(full))
            {
                foreach (string f in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                    files.Add((f, Path.GetRelativePath(baseDir, f).Replace('\\', '/')));
            }
        }

        long totalBytes = files.Sum(f => new FileInfo(f.FullPath).Length);
        long processed = 0;

        foreach (var (fullPath, relPath) in files)
        {
            controller.WaitIfPaused();
            controller.Token.ThrowIfCancellationRequested();

            var entry = zip.CreateEntry(relPath, netLevel);
            // ZIP stores local DOS timestamps with two-second resolution.
            DateTime modified = File.GetLastWriteTime(fullPath);
            DateTime earliest = new(1980, 1, 1, 0, 0, 0, DateTimeKind.Local);
            DateTime latest = new(2107, 12, 31, 23, 59, 58, DateTimeKind.Local);
            entry.LastWriteTime = new DateTimeOffset(modified < earliest ? earliest : modified > latest ? latest : modified);
            await using var entryOut = entry.Open();
            await using var fileIn = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            long baseDone = processed;
            await LunaStreamPipeline.CopyAndHashChunkedAsync(
                fileIn,
                entryOut,
                fileIn.Length,
                controller,
                (curr, _) => onProgress?.Invoke(baseDone + curr, Math.Max(1, totalBytes), relPath));

            processed += fileIn.Length;
        }
    }

    private static async Task CreateTarCompressedArchiveAsync(
        IReadOnlyList<string> sourcePaths,
        CompressOptions options,
        LunaJobController controller,
        Action<long, long, string>? onProgress)
    {
        await Task.Run(() =>
        {
            using var outStream = new FileStream(options.OutputArchivePath, FileMode.Create, FileAccess.Write);
            using var gzip = new GZipStream(outStream, CompressionLevel.Optimal);
            using var writer = new System.Formats.Tar.TarWriter(gzip, System.Formats.Tar.TarEntryFormat.Pax);
            var entries = new List<(string FullPath, string RelativePath)>();
            foreach (string source in sourcePaths)
            {
                string fullPath = Path.GetFullPath(source);
                string parent = Path.GetDirectoryName(fullPath) ?? fullPath;
                entries.Add((fullPath, Path.GetFileName(fullPath)));
                if (Directory.Exists(fullPath))
                {
                    foreach (string item in Directory.EnumerateFileSystemEntries(fullPath, "*", SearchOption.AllDirectories))
                        entries.Add((item, Path.GetRelativePath(parent, item).Replace('\\', '/')));
                }
            }
            for (int i = 0; i < entries.Count; i++)
            {
                controller.WaitIfPaused();
                controller.Token.ThrowIfCancellationRequested();
                writer.WriteEntry(entries[i].FullPath, entries[i].RelativePath);
                onProgress?.Invoke(i + 1, entries.Count, entries[i].RelativePath);
            }
        }, controller.Token);
    }

    public async Task<ArchiveTestResult> TestArchiveAsync(
        string archivePath,
        string? password,
        LunaJobController controller,
        Action<int, int, string>? onProgress = null)
    {
        int total = 0;
        int passed = 0;
        var broken = new List<string>();

        if (archivePath.EndsWith(".larc", StringComparison.OrdinalIgnoreCase) || LarcNativeHandler.IsLarcFile(archivePath))
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "LunaArc_Test_" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                var catalog = await _larcHandler.ReadCatalogAsync(archivePath, password);
                total = Enumerable.Range(0, catalog.Count).Count(i => !catalog.GetNode(i).IsDirectory);
                await _larcHandler.ExtractLarcArchiveAsync(
                    archivePath,
                    new ExtractOptions { DestinationDirectory = tempDir, Password = password },
                    controller,
                    (_, _, file) => onProgress?.Invoke(++passed, Math.Max(1, total), file));
                return new ArchiveTestResult(total, total, 0, broken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                broken.Add(ex.Message);
                return new ArchiveTestResult(Math.Max(1, total), passed, 1, broken);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        }

        if (TarArchiveHandler.Handles(archivePath))
            return await TarArchiveHandler.TestAsync(archivePath, controller, onProgress);

        return await Task.Run(() =>
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions { Password = password, LeaveStreamOpen = false });
            var noCrc = archive.Type == ArchiveType.Zip
                ? ZipAesIntegrity.Validate(archivePath, password, archive.Entries.ToArray(), controller)
                : new HashSet<IArchiveEntry>();
            var fileEntries = archive.Entries.Where(e => !e.IsDirectory).ToList();
            total = fileEntries.Count;

            for (int i = 0; i < fileEntries.Count; i++)
            {
                controller.WaitIfPaused();
                controller.Token.ThrowIfCancellationRequested();

                var entry = fileEntries[i];
                string key = entry.Key ?? $"Entry_{i}";
                onProgress?.Invoke(i + 1, total, key);

                try
                {
                    long expectedLength = entry.Size;
                    uint expectedCrc = (uint)entry.Crc;
                    using var output = new VerificationWriteStream(controller, expectedLength);
                    entry.WriteTo(output, new SharpCompress.Common.ExtractionOptions { CheckCrc = true });
                    if (output.Written != expectedLength)
                        throw new InvalidDataException($"Size mismatch in '{key}'.");
                    if (archive.Type == ArchiveType.Zip && !noCrc.Contains(entry) && output.Crc != expectedCrc)
                        throw new InvalidDataException($"CRC32 mismatch in '{key}'.");
                    passed++;
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    broken.Add(key);
                }
            }

            return new ArchiveTestResult(total, passed, broken.Count, broken);
        }, controller.Token);
    }
}
