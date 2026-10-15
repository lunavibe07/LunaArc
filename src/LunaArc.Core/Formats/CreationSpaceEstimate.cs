using LunaArc.Core.Contracts;
using LunaArc.Core.Pipeline;

namespace LunaArc.Core.Formats;

internal static class CreationSpaceEstimate
{
    public static void Check(IReadOnlyList<string> sources, CompressOptions options, bool sfx, bool recovery, bool verify, LunaJobController job)
    {
        job.Report("Checking free space");
        long bytes = 0, entries = 0;
        void Add(string path)
        {
            job.WaitIfPaused();
            job.Token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Archive sources cannot traverse symbolic links or reparse points.");
            entries++;
            if (Directory.Exists(path)) foreach (string child in Directory.EnumerateFileSystemEntries(path)) Add(child);
            else bytes = checked(bytes + new FileInfo(path).Length);
        }
        foreach (string path in sources) Add(path);
        long estimate = checked(bytes + entries * 1024 + 1024 * 1024);
        int tempCopies = options.Format switch
        {
            OutputArchiveFormat.EncryptedTarGz => 4,
            OutputArchiveFormat.SevenZip => 2,
            OutputArchiveFormat.LarcNative when !string.IsNullOrEmpty(options.Password) => 2,
            OutputArchiveFormat.LarcNative => 1,
            OutputArchiveFormat.Zip when !string.IsNullOrEmpty(options.Password) => 1,
            _ => verify ? 1 : 0
        };
        long destination = checked(estimate * (sfx || options.SplitVolumeSizeBytes.HasValue ? 2 : 1));
        if (sfx) tempCopies++;
        if (sfx && File.Exists(SfxArchive.GuiStubPath)) destination = checked(destination + new FileInfo(SfxArchive.GuiStubPath).Length);
        if (recovery) destination = checked(destination + estimate / 8 + 1024 * 1024);
        DiskSpaceGuard.Ensure(new[] { new DiskSpaceRequirement(Path.GetTempPath(), checked(estimate * tempCopies), "temporary archive staging"),
            new DiskSpaceRequirement(options.OutputArchivePath, destination, "archive output") }, job.FreeSpaceProbe);
    }
}
