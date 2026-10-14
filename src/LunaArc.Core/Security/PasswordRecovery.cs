using System.Security.Cryptography;
using System.Text.Json;
using LunaArc.Core.Contracts;
using LunaArc.Core.Pipeline;

namespace LunaArc.Core.Security;

public sealed record RecoveryProgress(long Attempted, long Total);
public sealed record RecoveryResult(bool Verified, string? Password);

/// <summary>Bounded candidate search; this does not bypass archive encryption.</summary>
public static class PasswordRecovery
{
    public const long MaximumCandidates = 1_000_000;

    private static string[] ParseMask(string mask)
    {
        if (string.IsNullOrEmpty(mask) || mask.Length > 128)
            throw new ArgumentException("Enter a mask between 1 and 128 characters.");
        var sets = new List<string>();
        for (int i = 0; i < mask.Length; i++)
        {
            char c = mask[i];
            if (c == '\\')
            {
                if (++i == mask.Length) throw new ArgumentException("A mask cannot end with an escape character.");
                sets.Add(mask[i].ToString());
            }
            else sets.Add(c switch { '?' => "0123456789", '#' => "abcdefghijklmnopqrstuvwxyz", _ => c.ToString() });
        }
        return sets.ToArray();
    }

    public static long CountMask(string mask)
    {
        long count = 1;
        foreach (string set in ParseMask(mask))
        {
            if (count > MaximumCandidates / set.Length)
                throw new ArgumentException($"The mask exceeds the {MaximumCandidates:N0}-candidate limit. Narrow the pattern.");
            count *= set.Length;
        }
        return count;
    }

    public static IEnumerable<string> EnumerateMask(string mask)
    {
        long count = CountMask(mask);
        string[] sets = ParseMask(mask);
        char[] buffer = new char[sets.Length];
        for (long index = 0; index < count; index++)
        {
            long value = index;
            for (int position = sets.Length - 1; position >= 0; position--)
            {
                buffer[position] = sets[position][(int)(value % sets[position].Length)];
                value /= sets[position].Length;
            }
            yield return new string(buffer);
        }
    }

    public static async Task<RecoveryResult> SearchAsync(IEnumerable<string> candidates, long total,
        Func<string, CancellationToken, Task<bool>> verify, LunaJobController controller,
        IProgress<RecoveryProgress>? progress = null)
    {
        if (total < 1 || total > MaximumCandidates) throw new ArgumentOutOfRangeException(nameof(total));
        string? found = null;
        long attempted = 0;
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(controller.Token);
        try
        {
            await Parallel.ForEachAsync(candidates.Take((int)total), new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(2, Environment.ProcessorCount), CancellationToken = stopped.Token
            }, async (candidate, ct) =>
            {
                controller.WaitIfPaused(ct);
                ct.ThrowIfCancellationRequested();
                if (await verify(candidate, ct) && Interlocked.CompareExchange(ref found, candidate, null) == null)
                    stopped.Cancel();
                progress?.Report(new(Interlocked.Increment(ref attempted), total));
            });
        }
        catch (OperationCanceledException) when (found != null && !controller.Token.IsCancellationRequested) { }
        controller.Token.ThrowIfCancellationRequested();
        return new(found != null, found);
    }

    public static async Task<bool> VerifyArchiveAsync(IArchiveBackend backend, string path, string? password,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var catalog = await backend.ReadCatalogAsync(path, password, ct);
            ct.ThrowIfCancellationRequested();
            // Metadata limits are not comprehensive archive-bomb protection.
            if (catalog.Count > 10000)
                throw new NotSupportedException("Recovery verification is limited to 10,000 entries and 256 MiB of declared output.");
            bool encrypted = false;
            long declared = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                var node = catalog.GetNode(i);
                encrypted |= node.IsEncrypted;
                if (node.IsDirectory) continue;
                if (node.UncompressedSize < 0 || node.UncompressedSize > 256L * 1024 * 1024 - declared)
                    throw new NotSupportedException("Recovery verification is limited to 256 MiB of declared output.");
                declared += node.UncompressedSize;
            }
            // An encrypted empty LARC has no entry carrying the encrypted flag.
            if (LunaArc.Core.Formats.LarcNativeHandler.IsLarcFile(path))
            {
                using var stream = File.OpenRead(path);
                stream.Position = 8;
                encrypted |= (stream.ReadByte() & 1) != 0;
            }
            if (password == null && encrypted) return false;
            if (password != null && !encrypted) return false;
            using var job = new LunaJobController();
            using var registration = ct.Register(job.Cancel);
            var result = await backend.TestArchiveAsync(path, password, job);
            ct.ThrowIfCancellationRequested();
            return result.IsHealthy;
        }
        catch (Exception ex) when (IsCandidateFailure(ex)) { ct.ThrowIfCancellationRequested(); return false; }
    }

    private static bool IsCandidateFailure(Exception ex) =>
        ex is CryptographicException or InvalidDataException or JsonException or UnauthorizedAccessException ||
        (ex.GetType().Namespace?.StartsWith("SharpCompress", StringComparison.Ordinal) == true && ex is not NotSupportedException) ||
        ex.GetType().Namespace?.StartsWith("ZstdSharp", StringComparison.Ordinal) == true;
}
