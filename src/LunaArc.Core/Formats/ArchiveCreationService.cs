using LunaArc.Core.Contracts;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;

namespace LunaArc.Core.Formats;

/// <summary>Shared create/verify/publish lifecycle for desktop and CLI.</summary>
public static class ArchiveCreationService
{
    public static async Task<string> CreateAsync(IReadOnlyList<string> sources, CompressOptions options,
        LunaJobController job, bool sfx = false, bool recovery = false, bool locked = false, bool test = false,
        string? sfxStub = null, Action<long, long, string>? progress = null)
    {
        string output = Path.GetFullPath(options.OutputArchivePath);
        if (options.SplitVolumeSizeBytes.HasValue) throw new NotSupportedException("This creation workflow does not support split volumes.");
        ArchiveLock.EnsureWritable(output);
        foreach (string path in new[] { output, ArchiveRecoveryRecord.RecordPath(output), ArchiveLock.RecordPath(output) })
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException("An output or companion file already exists. Choose a new archive name.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (string source in sources)
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            if (output.Equals(full, comparison) || Directory.Exists(full) && output.StartsWith(full + Path.DirectorySeparatorChar, comparison))
                throw new ArgumentException("Save the archive outside the selected sources.");
        }
        string directory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(directory);
        string extension = options.Format switch { OutputArchiveFormat.Zip => ".zip", OutputArchiveFormat.TarGz => ".tar.gz", OutputArchiveFormat.SevenZip => ".7z", _ => ".larc" };
        string temporary = Path.Combine(directory, ".lunaarc-" + Guid.NewGuid().ToString("N") + extension);
        string executable = temporary + ".sfx";
        string record = temporary + ".lrec";
        bool moved = false, movedRecord = false, committed = false;
        var backend = new StandardArchiveHandler();
        try
        {
            var cleanup = options.DeleteSourceAfterArchiving ? await VerifiedSourceCleanup.CaptureAsync(sources, job.Token) : null;
            await backend.CompressAsync(sources, new CompressOptions
            {
                OutputArchivePath = temporary, Format = options.Format, Level = options.Level, Password = options.Password,
                ArchiveComment = options.ArchiveComment, SolidArchive = options.SolidArchive
            }, job, progress);
            bool verify = test || cleanup != null || sfx || recovery;
            if (verify && !(await backend.TestArchiveAsync(temporary, options.Password, job)).IsHealthy)
                throw new InvalidDataException("Archive verification failed. Sources were preserved.");
            string finalStage = temporary;
            if (sfx)
            {
                await SfxArchive.CreateAsync(sfxStub ?? SfxArchive.GuiStubPath, temporary, executable, options.Format, job.Token);
                if (!(await backend.TestArchiveAsync(executable, options.Password, job)).IsHealthy) throw new InvalidDataException("SFX verification failed.");
                finalStage = executable;
            }
            if (recovery) await ArchiveRecoveryRecord.CreateAsync(finalStage, record, job);
            job.WaitIfPaused(); job.Token.ThrowIfCancellationRequested();
            ArchiveLock.EnsureWritable(output);
            File.Move(finalStage, output, overwrite: false); moved = true;
            if (recovery) { File.Move(record, ArchiveRecoveryRecord.RecordPath(output), overwrite: false); movedRecord = true; }
            if (locked) await ArchiveLock.LockAsync(output, job.Token);
            committed = true;
            string message = verify ? "Created and verified archive." : "Created archive.";
            if (cleanup != null)
            {
                try { message += " " + (await cleanup.DeleteArchivedSourcesAsync(backend, output, options.Password, job)).Message; }
                catch (Exception ex) { message += " Archive retained; remaining sources were kept. Cleanup: " + ex.Message; }
            }
            return message;
        }
        finally
        {
            foreach (string path in new[] { temporary, executable, record }) if (File.Exists(path)) File.Delete(path);
            if (!committed && moved)
            {
                File.Delete(output);
            }
            if (!committed && movedRecord) File.Delete(ArchiveRecoveryRecord.RecordPath(output));
        }
    }
}
