using System.Buffers.Binary;
using LunaArc.Core.Formats;

namespace LunaArc.Core.Security;

/// <summary>Detects encryption from metadata, without testing passwords or extracting file contents.</summary>
public static class ArchivePasswordDetection
{
    public static async Task<bool> RequiresPasswordAsync(string archivePath, CancellationToken ct = default)
    {
        using var job = new LunaArc.Core.Pipeline.LunaJobController(); using var cancellation = ct.Register(job.Cancel);
        using var split = await SplitArchive.OpenAsync(archivePath, job);
        if (split != null) return await RequiresPasswordAsync(split.Path, ct);
        if (AuthenticatedLarc.IsFile(archivePath)) { await AuthenticatedLarc.CheckHeaderAsync(archivePath, ct); return true; }
        using var sfx = await SfxArchive.OpenPayloadAsync(archivePath, ct);
        if (sfx != null) return await RequiresPasswordAsync(sfx.Path, ct);
        if (LarcNativeHandler.IsLarcFile(archivePath))
        {
            await using var input = File.OpenRead(archivePath);
            byte[] header = new byte[13];
            await input.ReadExactlyAsync(header, ct);
            int flags = header[8];
            int size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(9));
            if ((flags & ~3) != 0 || size <= 0 || size > 64 * 1024 * 1024 || size > input.Length - header.Length)
                throw new InvalidDataException("Invalid LARC header.");
            // Encrypted empty archives still require a password, even with no file entries.
            if ((flags & 1) != 0) return true;
        }
        try
        {
            var catalog = await new StandardArchiveHandler().ReadCatalogAsync(archivePath, null, ct);
            return Enumerable.Range(0, catalog.Count).Any(i => catalog.GetNode(i).IsEncrypted);
        }
        catch (SharpCompress.Common.CryptographicException)
        {
            // The decoder reached encrypted metadata with no password (RAR / 7z headers).
            // I/O, malformed-format and access errors remain errors, not password prompts.
            return true;
        }
    }
}
