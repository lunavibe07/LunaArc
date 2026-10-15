using ICSharpCode.SharpZipLib.Zip;
using LunaArc.Core.Contracts;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;

namespace LunaArc.Core.Formats;

/// <summary>Standard WinZip AES-256 output, read and authenticated by the independent backend.</summary>
internal static class EncryptedZipEncoder
{
    public static async Task CreateAsync(IReadOnlyList<string> sources, CompressOptions options, LunaJobController job,
        Action<long, long, string>? progress)
    {
        if (options.SplitVolumeSizeBytes.HasValue) throw new NotSupportedException("Encrypted split ZIP creation is unsupported.");
        string stage = Directory.CreateTempSubdirectory("LunaArc_AesZip_").FullName;
        try
        {
            var entries = new List<(string Source, string Name, bool Directory)>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            void Collect(string source, string name)
            {
                job.WaitIfPaused(); job.Token.ThrowIfCancellationRequested();
                for (string? ancestor = source; ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
                    if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Encrypted ZIP sources cannot traverse symbolic links or reparse points.");
                ZipSlipGuard.ValidateAndResolvePath(stage, name);
                if (!names.Add(name)) throw new IOException("Encrypted ZIP source names overlap.");
                bool directory = System.IO.Directory.Exists(source);
                entries.Add((source, name, directory));
                if (directory)
                    foreach (string child in System.IO.Directory.EnumerateFileSystemEntries(source))
                        Collect(child, name + "/" + Path.GetFileName(child));
            }
            foreach (string source in sources)
            {
                string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
                string name = Path.GetFileName(full);
                if (string.IsNullOrEmpty(name)) throw new ArgumentException("Filesystem roots cannot be encrypted ZIP sources.");
                Collect(full, name);
            }
            long total = entries.Where(e => !e.Directory).Sum(e => new FileInfo(e.Source).Length);
            string temporary = Path.Combine(stage, "result.zip");
            using (var zip = new ZipOutputStream(File.Create(temporary)))
            {
                zip.UseZip64 = UseZip64.Dynamic;
                zip.Password = options.Password;
                zip.SetLevel(new[] { 0, 1, 3, 5, 7, 9 }[(int)options.Level]);
                if (!string.IsNullOrEmpty(options.ArchiveComment)) zip.SetComment(options.ArchiveComment);
                long done = 0;
                foreach (var entry in entries)
                {
                    job.WaitIfPaused(); job.Token.ThrowIfCancellationRequested();
                    zip.Password = entry.Directory ? null : options.Password;
                    var item = new ZipEntry(entry.Name + (entry.Directory ? "/" : ""))
                    {
                        IsUnicodeText = true, DateTime = File.GetLastWriteTime(entry.Source),
                        AESKeySize = entry.Directory ? 0 : 256,
                        Size = entry.Directory ? 0 : new FileInfo(entry.Source).Length
                    };
                    zip.PutNextEntry(item);
                    if (!entry.Directory)
                    {
                        await using var input = File.OpenRead(entry.Source);
                        long start = done;
                        await LunaStreamPipeline.CopyAndHashChunkedAsync(input, zip, input.Length, job,
                            (current, _) => progress?.Invoke(start + current, Math.Max(1, total), entry.Name));
                        done += input.Length;
                    }
                    zip.CloseEntry();
                }
                zip.Finish();
            }
            job.Token.ThrowIfCancellationRequested();
            File.Move(temporary, options.OutputArchivePath, overwrite: false);
        }
        finally { Directory.Delete(stage, recursive: true); }
    }
}
