using System.Security.Cryptography;

namespace LunaArc.Core.Security;

/// <summary>Advisory LunaArc lock, not encryption or an operating-system access boundary.</summary>
public static class ArchiveLock
{
    public static string RecordPath(string archive) => archive + ".lunalock";
    public static bool IsLocked(string archive) => File.Exists(RecordPath(archive));
    public static void EnsureWritable(string archive)
    {
        if (IsLocked(archive)) throw new IOException("This archive is locked by LunaArc. Unlock it explicitly before replacing it.");
    }
    public static async Task LockAsync(string archive, CancellationToken ct = default)
    {
        if ((File.GetAttributes(archive) & FileAttributes.ReparsePoint) != 0) throw new IOException("Cannot lock a link.");
        await using var input = File.OpenRead(archive);
        byte[] hash = await SHA256.HashDataAsync(input, ct);
        string temporary = RecordPath(archive) + "." + Guid.NewGuid().ToString("N");
        bool moved = false;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                await output.WriteAsync(System.Text.Encoding.ASCII.GetBytes("LUNAARC-LOCK-1\n" + Convert.ToHexString(hash) + "\n"), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, RecordPath(archive), overwrite: false); moved = true;
            // The companion record is authoritative; read-only is an additional Explorer hint.
            File.SetAttributes(archive, File.GetAttributes(archive) | FileAttributes.ReadOnly);
        }
        catch { if (moved) File.Delete(RecordPath(archive)); throw; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Unlock(string archive)
    {
        if (File.Exists(archive))
        {
            if ((File.GetAttributes(archive) & FileAttributes.ReparsePoint) != 0) throw new IOException("Cannot unlock a link.");
            File.SetAttributes(archive, File.GetAttributes(archive) & ~FileAttributes.ReadOnly);
        }
        File.Delete(RecordPath(archive));
    }
}
