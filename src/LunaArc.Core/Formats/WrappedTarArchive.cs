using LunaArc.Core.Pipeline;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace LunaArc.Core.Formats;

/// <summary>Only explicitly named .tar.gz.7z containers with one root TAR GZip are expanded.</summary>
internal static class WrappedTarArchive
{
    public static async Task<TemporaryArchive?> OpenAsync(string path, string? password, LunaJobController job)
    {
        if (!path.EndsWith(".tar.gz.7z", StringComparison.OrdinalIgnoreCase)) return null;
        job.Report("Reading wrapper metadata");
        using var archive = ArchiveFactory.OpenArchive(path, new ReaderOptions { Password = password });
        var entries = archive.Entries.ToArray();
        if (archive.Type != SharpCompress.Common.ArchiveType.SevenZip || entries.Length != 1 || entries[0].IsDirectory ||
            entries[0].Key is not { } name || name.Contains('/') || name.Contains('\\') || name.Contains(':') ||
            !name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || entries[0].Size <= 0)
            throw new InvalidDataException("A TAR GZip wrapper must contain exactly one root .tar.gz file.");
        var entry = entries[0];
        job.Report("Checking free space");
        DiskSpaceGuard.Ensure(new[] { new DiskSpaceRequirement(Path.GetTempPath(), entry.Size, "TAR GZip wrapper staging") }, job.FreeSpaceProbe);
        var result = new TemporaryArchive(".tar.gz", entry.IsEncrypted);
        try
        {
            await using var output = new FileStream(result.Path, FileMode.CreateNew, FileAccess.Write);
            using var input = entry.OpenEntryStream();
            using var verification = new VerificationWriteStream(job, entry.Size);
            byte[] buffer = new byte[65536];
            job.Report("Unwrapping TAR GZip", 0, entry.Size, name);
            while (true)
            {
                job.WaitIfPaused(); job.Token.ThrowIfCancellationRequested();
                int read = await input.ReadAsync(buffer, job.Token); if (read == 0) break;
                verification.Write(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), job.Token);
                job.Report("Unwrapping TAR GZip", verification.Written, entry.Size, name);
            }
            if (verification.Written != entry.Size || verification.Crc != (uint)entry.Crc)
                throw new InvalidDataException("TAR GZip wrapper checksum or length mismatch.");
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
