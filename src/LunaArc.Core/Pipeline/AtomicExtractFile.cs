namespace LunaArc.Core.Pipeline;

/// <summary>
/// Replaces an extraction destination only after its new contents have been verified.
/// </summary>
internal static class AtomicExtractFile
{
    public static async Task WriteAsync(string target, bool keepBroken, CancellationToken ct, Func<Stream, Task> write)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".lunaarc-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await write(output);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            if (keepBroken && !ct.IsCancellationRequested && File.Exists(temporary))
                File.Move(temporary, target + ".broken-" + Guid.NewGuid().ToString("N"));
            throw;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
