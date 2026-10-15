using System.Security.Cryptography;
using LunaArc.Core.Contracts;
using LunaArc.Core.Pipeline;

namespace LunaArc.Core.Security;

public sealed record SourceCleanupResult(int DeletedFiles, int KeptFiles, string Message);

/// <summary>Deletes captured sources only after comparing a verified archive with their contents.</summary>
public sealed class VerifiedSourceCleanup
{
    private sealed record SourceFile(string Path, string Key, byte[] Hash, long Length, DateTime Modified);
    private readonly List<SourceFile> _files = new();
    private readonly List<(string Path, string Key)> _directories = new();

    public static async Task<VerifiedSourceCleanup> CaptureAsync(IReadOnlyList<string> sources, CancellationToken ct)
    {
        var result = new VerifiedSourceCleanup();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = new HashSet<string>(comparer);
        var keys = new HashSet<string>(comparer);
        foreach (string source in sources)
        {
            ct.ThrowIfCancellationRequested();
            string full = Path.GetFullPath(source);
            RejectLinks(full);
            if (File.Exists(full)) { await AddFile(full, Path.GetFileName(full)); continue; }
            if (!Directory.Exists(full)) throw new FileNotFoundException("A source no longer exists.", full);
            if (Path.TrimEndingDirectorySeparator(full) == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!))
                throw new IOException("Deleting a filesystem root after archiving is not supported.");
            string parent = Path.GetDirectoryName(full)!;
            var pending = new Stack<string>();
            pending.Push(full);
            while (pending.TryPop(out string? directory))
            {
                ct.ThrowIfCancellationRequested();
                RejectLinks(directory);
                if (!paths.Add(directory)) throw new IOException("Select each source only once when deleting after archiving.");
                result._directories.Add((directory, Path.GetRelativePath(parent, directory).Replace('\\', '/')));
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    RejectLinks(entry);
                    if (Directory.Exists(entry)) pending.Push(entry);
                    else await AddFile(entry, Path.GetRelativePath(parent, entry).Replace('\\', '/'));
                }
            }
        }
        return result;

        async Task AddFile(string path, string key)
        {
            if (!paths.Add(path) || !keys.Add(key)) throw new IOException("Overlapping sources or duplicate archive names cannot be deleted automatically.");
            var info = new FileInfo(path);
            long length = info.Length;
            DateTime modified = info.LastWriteTimeUtc;
            byte[] hash = await HashAsync(path, ct);
            info.Refresh();
            if (info.Length != length || info.LastWriteTimeUtc != modified) throw new IOException("A source changed while preparing the archive. Sources were kept.");
            result._files.Add(new(path, key, hash, length, modified));
        }
    }

    public async Task<SourceCleanupResult> DeleteArchivedSourcesAsync(IArchiveBackend backend, string archive,
        string? password, LunaJobController job)
    {
        job.Token.ThrowIfCancellationRequested();
        var verification = await backend.TestArchiveAsync(archive, password, job);
        if (!verification.IsHealthy) throw new InvalidDataException("Archive verification failed; source files were kept.");
        string temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        string temporary = Directory.CreateTempSubdirectory("LunaArc_DeleteVerify_").FullName;
        try
        {
            await backend.ExtractAsync(archive, new ExtractOptions
            { DestinationDirectory = temporary, Password = password, SmartFolderCreation = false }, job);
            foreach (var file in _files)
            {
                job.Token.ThrowIfCancellationRequested();
                string copy = ZipSlipGuard.ValidateAndResolvePath(temporary, file.Key);
                if (!File.Exists(copy) || !file.Hash.SequenceEqual(await HashAsync(copy, job.Token)) || !await UnchangedAsync(file, job.Token))
                    return new(0, _files.Count, "Source cleanup skipped: a source changed or did not match the archive.");
            }
            int deleted = 0, kept = 0;
            foreach (var file in _files)
            {
                job.WaitIfPaused();
                job.Token.ThrowIfCancellationRequested();
                if (!await UnchangedAsync(file, job.Token)) { kept++; continue; }
                try { if (await DeleteOneAsync(file, job.Token)) deleted++; else kept++; }
                catch (UnauthorizedAccessException) { kept++; }
            }
            foreach (var directory in _directories.OrderByDescending(d => d.Path.Length))
            {
                job.Token.ThrowIfCancellationRequested();
                // Only remove represented, empty directories; never recursively delete user data.
                if (!Directory.Exists(ZipSlipGuard.ValidateAndResolvePath(temporary, directory.Key))) continue;
                try { RejectLinks(directory.Path); Directory.Delete(directory.Path, recursive: false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return new(deleted, kept, $"Removed {deleted} archived source file(s); {kept} captured file(s) kept. New or unarchived files/folders remain.");
        }
        finally
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string full = Path.GetFullPath(temporary);
            if (!string.Equals(Path.GetDirectoryName(full), temporaryRoot, comparison)
                || !Path.GetFileName(full).StartsWith("LunaArc_DeleteVerify_", StringComparison.Ordinal))
                throw new IOException("Refusing to remove an unexpected verification directory.");
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing to follow a replaced verification directory.");
            Directory.Delete(full, recursive: true);
        }
    }

    private static async Task<bool> UnchangedAsync(SourceFile file, CancellationToken ct)
    {
        try
        {
            RejectLinks(file.Path);
            var info = new FileInfo(file.Path);
            return info.Exists && info.Length == file.Length && info.LastWriteTimeUtc == file.Modified
                && file.Hash.SequenceEqual(await HashAsync(file.Path, ct));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static async Task<bool> DeleteOneAsync(SourceFile file, CancellationToken ct)
    {
        string staged = Path.Combine(Path.GetDirectoryName(file.Path)!, ".lunaarc-source-" + Guid.NewGuid().ToString("N"));
        bool removed = false;
        // Move the exact source into a private name, so a replacement at the original path is never deleted.
        File.Move(file.Path, staged, overwrite: false);
        try
        {
            RejectLinks(staged);
            await using var input = new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (input.Length != file.Length || File.GetLastWriteTimeUtc(staged) != file.Modified
                || !file.Hash.SequenceEqual(await SHA256.HashDataAsync(input, ct))) return false;
            ct.ThrowIfCancellationRequested();
            File.Delete(staged);
            removed = true;
            return true;
        }
        finally
        {
            if (!removed && File.Exists(staged))
            {
                try { File.Move(staged, file.Path, overwrite: false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { throw new IOException($"A source was preserved at '{staged}'; its original path could not be restored.", ex); }
            }
        }
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        return await SHA256.HashDataAsync(input, ct);
    }
    private static void RejectLinks(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Automatic source deletion does not support symbolic links or reparse points.");
    }
}
