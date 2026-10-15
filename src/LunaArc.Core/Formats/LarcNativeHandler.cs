using System.Text.Json;
using LunaArc.Core.Contracts;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;
using LunaArc.Core.VfsIndex;
using ZstdSharp;

namespace LunaArc.Core.Formats;

/// <summary>
/// LunaArc format: JSON metadata and per-entry Zstandard payloads, with optional AES encryption.
/// </summary>
public sealed class LarcNativeHandler
{
    public static ReadOnlySpan<byte> MagicBytes => "LUNAARC"u8;

    private sealed class LarcHeaderDto
    {
        public string Comment { get; set; } = string.Empty;
        public List<LarcEntryDto> Entries { get; set; } = new();
    }

    private sealed class LarcEntryDto
    {
        public string Path { get; set; } = string.Empty;
        public bool IsDir { get; set; }
        public long OrigSize { get; set; }
        public long CompSize { get; set; }
        public long DataOffset { get; set; }
        public uint Crc32 { get; set; }
        public long LastModifiedUtcTicks { get; set; }
    }

    public static bool IsLarcFile(string archivePath)
    {
        if (!File.Exists(archivePath)) return false;
        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fs.Length < 13) return false;
        Span<byte> header = stackalloc byte[8];
        fs.ReadExactly(header);
        return header.SequenceEqual(MagicBytes);
    }

    public async Task<FlatVfsArena> ReadCatalogAsync(string archivePath, string? password = null)
    {
        await using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var (header, isEncrypted, _) = await ReadHeaderInternalAsync(fs, password);

        var arena = new FlatVfsArena(Math.Max(16, header.Entries.Count))
        {
            ArchiveComment = header.Comment
        };

        foreach (var e in header.Entries)
        {
            arena.AddEntry(
                e.Path,
                e.OrigSize,
                e.CompSize,
                e.Crc32,
                new DateTime(e.LastModifiedUtcTicks, DateTimeKind.Utc),
                e.IsDir,
                isEncrypted);
        }

        return arena;
    }

    public async Task CreateLarcArchiveAsync(
        IReadOnlyList<string> sourcePaths,
        CompressOptions options,
        LunaJobController controller,
        Action<long, long, string>? onProgress = null)
    {
        var filesToPack = CollectFiles(sourcePaths);
        long totalBytes = filesToPack.Sum(f => f.IsDir ? 0 : new FileInfo(f.FullPath).Length);
        long processedBytes = 0;

        int zstdLevel = options.Level switch
        {
            LunaCompressionLevel.Store => 1,
            LunaCompressionLevel.Fastest => 1,
            LunaCompressionLevel.Fast => 3,
            LunaCompressionLevel.Normal => 6,
            LunaCompressionLevel.Good => 12,
            LunaCompressionLevel.Best => 19,
            _ => 6
        };

        bool isEncrypted = !string.IsNullOrEmpty(options.Password);
        var headerDto = new LarcHeaderDto { Comment = options.ArchiveComment ?? string.Empty };

        string tempPayloadPath = Path.GetTempFileName();
        try
        {
            await using (var payloadOut = new FileStream(tempPayloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                foreach (var item in filesToPack)
                {
                    controller.WaitIfPaused();
                    controller.Token.ThrowIfCancellationRequested();

                    if (item.IsDir)
                    {
                        headerDto.Entries.Add(new LarcEntryDto
                        {
                            Path = item.RelativePath,
                            IsDir = true,
                            LastModifiedUtcTicks = Directory.GetLastWriteTimeUtc(item.FullPath).Ticks
                        });
                        continue;
                    }

                    byte[] rawBytes = await File.ReadAllBytesAsync(item.FullPath, controller.Token);
                    uint crc = Crc32Verifier.Compute(rawBytes);

                    byte[] compressedBytes;
                    if (options.Level == LunaCompressionLevel.Store)
                    {
                        compressedBytes = rawBytes;
                    }
                    else
                    {
                        using var compressor = new Compressor(zstdLevel);
                        compressedBytes = compressor.Wrap(rawBytes).ToArray();
                    }

                    if (isEncrypted)
                    {
                        compressedBytes = LunaAes256Cipher.EncryptBytes(compressedBytes, options.Password!);
                    }

                    long offset = payloadOut.Position;
                    await payloadOut.WriteAsync(compressedBytes, controller.Token);

                    headerDto.Entries.Add(new LarcEntryDto
                    {
                        Path = item.RelativePath,
                        IsDir = false,
                        OrigSize = rawBytes.LongLength,
                        CompSize = compressedBytes.LongLength,
                        DataOffset = offset,
                        Crc32 = crc,
                        LastModifiedUtcTicks = File.GetLastWriteTimeUtc(item.FullPath).Ticks
                    });

                    processedBytes += rawBytes.LongLength;
                    onProgress?.Invoke(processedBytes, Math.Max(1, totalBytes), item.RelativePath);
                }
            }

            byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(headerDto);
            using (var headerComp = new Compressor(6))
            {
                headerBytes = headerComp.Wrap(headerBytes).ToArray();
            }

            if (isEncrypted)
            {
                headerBytes = LunaAes256Cipher.EncryptBytes(headerBytes, options.Password!);
            }

            await using Stream finalOut = options.SplitVolumeSizeBytes.HasValue
                ? new MultiVolumeOutputStream(options.OutputArchivePath, options.SplitVolumeSizeBytes.Value)
                : new FileStream(options.OutputArchivePath, FileMode.Create, FileAccess.Write, FileShare.None);

            await finalOut.WriteAsync(MagicBytes.ToArray(), controller.Token);
            byte flags = 0;
            if (isEncrypted) flags |= 0b0001;
            if (options.Level == LunaCompressionLevel.Store) flags |= 0b0010;
            finalOut.WriteByte(flags);

            await finalOut.WriteAsync(BitConverter.GetBytes(headerBytes.Length), controller.Token);
            await finalOut.WriteAsync(headerBytes, controller.Token);

            await using var payloadIn = new FileStream(tempPayloadPath, FileMode.Open, FileAccess.Read);
            await payloadIn.CopyToAsync(finalOut, controller.Token);
        }
        finally
        {
            if (File.Exists(tempPayloadPath))
                File.Delete(tempPayloadPath);
        }
    }

    public async Task ExtractLarcArchiveAsync(
        string archivePath,
        ExtractOptions options,
        LunaJobController controller,
        Action<long, long, string>? onProgress = null)
    {
        if (!Enum.IsDefined(options.UpdateMode)) throw new ArgumentOutOfRangeException(nameof(options.UpdateMode));
        await using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var (header, isEncrypted, flags) = await ReadHeaderInternalAsync(fs, options.Password);
        bool isStoreMode = (flags & 0b0010) != 0;
        long payloadBaseOffset = fs.Position;

        long totalBytes = header.Entries.Where(e => !e.IsDir).Sum(e => e.OrigSize);
        long extractedBytes = 0;

        foreach (var entry in header.Entries)
        {
            controller.WaitIfPaused();
            controller.Token.ThrowIfCancellationRequested();

            if (options.SelectedEntries != null &&
                options.SelectedEntries.Count > 0 &&
                !options.SelectedEntries.Contains(entry.Path))
            {
                continue;
            }

            string targetPath = ZipSlipGuard.ValidateAndResolvePath(options.DestinationDirectory, entry.Path);

            if (entry.IsDir)
            {
                if (options.UpdateMode != ExtractionUpdateMode.FreshenExisting) Directory.CreateDirectory(targetPath);
                continue;
            }

            if (!ExtractionUpdatePolicy.ShouldExtract(targetPath, new DateTime(entry.LastModifiedUtcTicks, DateTimeKind.Utc), options.UpdateMode)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            if (File.Exists(targetPath))
            {
                OverwriteMode mode = options.OverwriteMode == OverwriteMode.Ask && options.OnFileConflictCallback != null
                    ? options.OnFileConflictCallback(entry.Path)
                    : options.OverwriteMode;
                if (mode == OverwriteMode.Ask) throw new IOException($"Overwrite confirmation required: {entry.Path}");

                if (mode == OverwriteMode.SkipExisting) continue;
                if (mode == OverwriteMode.UpdateNewer &&
                    File.GetLastWriteTimeUtc(targetPath).Ticks >= entry.LastModifiedUtcTicks) continue;
                if (mode == OverwriteMode.AutoRename)
                    targetPath = ZipSlipGuard.GetAutoRenamedPath(targetPath);
            }

            fs.Seek(payloadBaseOffset + entry.DataOffset, SeekOrigin.Begin);
            byte[] block = new byte[entry.CompSize];
            await fs.ReadExactlyAsync(block, controller.Token);

            if (isEncrypted)
            {
                block = LunaAes256Cipher.DecryptBytes(block, options.Password ?? string.Empty);
            }

            byte[] plainBytes;
            if (isStoreMode)
            {
                plainBytes = block;
            }
            else
            {
                using var decompressor = new Decompressor();
                plainBytes = decompressor.Unwrap(block).ToArray();
            }

            uint computedCrc = Crc32Verifier.Compute(plainBytes);
            await AtomicExtractFile.WriteAsync(targetPath, options.KeepBrokenFiles, controller.Token, async output =>
            {
                await output.WriteAsync(plainBytes, controller.Token);
                if (computedCrc != entry.Crc32)
                    throw new InvalidDataException($"CRC32 mismatch in '{entry.Path}' (the file may be damaged).");
            });
            File.SetLastWriteTimeUtc(targetPath, new DateTime(entry.LastModifiedUtcTicks, DateTimeKind.Utc));

            extractedBytes += plainBytes.LongLength;
            onProgress?.Invoke(extractedBytes, Math.Max(1, totalBytes), entry.Path);
        }
    }

    private static async Task<(LarcHeaderDto Header, bool IsEncrypted, byte Flags)> ReadHeaderInternalAsync(Stream fs, string? password)
    {
        byte[] magic = new byte[8];
        fs.ReadExactly(magic);
        if (!magic.AsSpan().SequenceEqual(MagicBytes))
            throw new InvalidDataException("Invalid LunaArc .larc file.");

        int flagByte = fs.ReadByte();
        if (flagByte < 0 || (flagByte & ~0b0011) != 0)
            throw new InvalidDataException("Unsupported or truncated .larc flags.");
        bool isEncrypted = (flagByte & 0b0001) != 0;

        byte[] lenBytes = new byte[4];
        await fs.ReadExactlyAsync(lenBytes);
        int headerLen = BitConverter.ToInt32(lenBytes);
        if (headerLen <= 0 || headerLen > 64 * 1024 * 1024 || headerLen > fs.Length - fs.Position)
            throw new InvalidDataException("Invalid .larc header length.");

        byte[] headerData = new byte[headerLen];
        await fs.ReadExactlyAsync(headerData);

        if (isEncrypted)
        {
            if (string.IsNullOrEmpty(password))
                throw new UnauthorizedAccessException("This .larc archive requires a password.");
            headerData = LunaAes256Cipher.DecryptBytes(headerData, password);
        }

        using var decompressor = new Decompressor();
        byte[] jsonBytes = decompressor.Unwrap(headerData).ToArray();
        var header = JsonSerializer.Deserialize<LarcHeaderDto>(jsonBytes) ?? new LarcHeaderDto();
        long payloadLength = fs.Length - fs.Position;
        foreach (var entry in header.Entries)
        {
            if (entry.OrigSize < 0 || entry.CompSize < 0 || entry.CompSize > int.MaxValue || entry.DataOffset < 0 ||
                entry.DataOffset > payloadLength || entry.CompSize > payloadLength - entry.DataOffset ||
                entry.LastModifiedUtcTicks < DateTime.MinValue.Ticks || entry.LastModifiedUtcTicks > DateTime.MaxValue.Ticks)
                throw new InvalidDataException("Invalid .larc entry metadata.");
        }
        return (header, isEncrypted, (byte)flagByte);
    }

    private static List<(string FullPath, string RelativePath, bool IsDir)> CollectFiles(IReadOnlyList<string> sourcePaths)
    {
        var list = new List<(string, string, bool)>();
        foreach (string rawPath in sourcePaths)
        {
            string full = Path.GetFullPath(rawPath);
            string baseDir = Path.GetDirectoryName(full) ?? full;

            if (File.Exists(full))
            {
                list.Add((full, Path.GetFileName(full), false));
            }
            else if (Directory.Exists(full))
            {
                list.Add((full, Path.GetRelativePath(baseDir, full).Replace('\\', '/'), true));
                foreach (string entry in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories))
                {
                    bool isDir = Directory.Exists(entry);
                    string rel = Path.GetRelativePath(baseDir, entry).Replace('\\', '/');
                    list.Add((entry, rel, isDir));
                }
            }
        }
        return list;
    }
}
