using System.Buffers;
using LunaArc.Core.Security;

namespace LunaArc.Core.Pipeline;

/// <summary>
/// Copies and hashes streams using a pooled 64 KiB buffer.
/// </summary>
public static class LunaStreamPipeline
{
    public const int ChunkSize = 64 * 1024;

    public static async Task<uint> CopyAndHashChunkedAsync(
        Stream source,
        Stream destination,
        long expectedTotalBytes,
        LunaJobController controller,
        Action<long, long>? onProgress = null)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        long processed = 0;
        uint runningCrc = 0xFFFFFFFF;

        try
        {
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, ChunkSize), controller.Token)) > 0)
            {
                controller.WaitIfPaused();

                runningCrc = Crc32Verifier.UpdateCrc32(runningCrc, buffer.AsSpan(0, bytesRead));
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), controller.Token);

                processed += bytesRead;
                onProgress?.Invoke(processed, expectedTotalBytes);
            }
            return ~runningCrc;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
