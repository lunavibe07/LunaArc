using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.Pipeline;
using System.Security.Cryptography;

internal static class OperationChecks
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        const long mib = 1024 * 1024;
        var requirements = new[] { new DiskSpaceRequirement("temp", 60 * mib, "staging"), new DiskSpaceRequirement("output", 60 * mib, "output") };
        check(await Reject(() => Task.Run(() => DiskSpaceGuard.Ensure(requirements, _ => new("same volume", 100 * mib)))), "Disk estimates combine temp and output on the same volume");
        check(DiskSpaceGuard.Ensure(requirements, path => new(path, 100 * mib)), "Separate volumes are checked independently");
        check(!DiskSpaceGuard.Ensure(requirements, _ => null), "Unknown free space is reported as unknown rather than invented");
        check(await Reject(() => Task.Run(() => DiskSpaceGuard.Ensure(new[] { new DiskSpaceRequirement("output", -1, "bad estimate") }))), "Negative disk estimates are rejected");
        var backend = new StandardArchiveHandler();
        string source = Path.Combine(root, "operation-source"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "first.txt"), "Synthetic first file");
        File.WriteAllBytes(Path.Combine(source, "second.bin"), RandomNumberGenerator.GetBytes(256 * 1024));
        const string password = "synthetic-wrapper-password";
        string wrapper = Path.Combine(root, "automatic.tar.gz.7z");
        using var job = new LunaJobController();
        var stages = new List<OperationProgress>(); job.ProgressChanged += stages.Add;
        await ArchiveCreationService.CreateAsync(new[] { source }, new CompressOptions { OutputArchivePath = wrapper,
            Format = OutputArchiveFormat.EncryptedTarGz, Password = password }, job, test: true);
        var catalog = await backend.ReadCatalogAsync(wrapper, password);
        check(Enumerable.Range(0, catalog.Count).Any(i => catalog.GetFullPath(i) == "operation-source/first.txt") && catalog.GetNode(catalog.Count - 1).IsEncrypted,
            "Wrapped TAR GZip catalog exposes final encrypted file paths");
        string output = Path.Combine(root, "one-step-output"); stages.Clear();
        await backend.ExtractAsync(wrapper, new ExtractOptions { DestinationDirectory = output, Password = password,
            SelectedEntries = new HashSet<string> { "operation-source/first.txt" } }, job);
        string first = Path.Combine(output, "operation-source", "first.txt");
        check(File.ReadAllText(first) == "Synthetic first file" && Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length == 1,
            "One-step wrapped extraction honors selected inner paths and leaves no intermediate archive");
        check(stages.FindIndex(s => s.Stage == "Unwrapping TAR GZip") >= 0 && stages.FindIndex(s => s.Stage == "Extracting files") > stages.FindIndex(s => s.Stage == "Unwrapping TAR GZip"),
            "Wrapper extraction reports its stages in actual order");
        File.WriteAllText(first, "existing file");
        await backend.ExtractAsync(wrapper, new ExtractOptions { DestinationDirectory = output, Password = password, OverwriteMode = OverwriteMode.SkipExisting }, job);
        check(File.ReadAllText(first) == "existing file", "Automatic inner extraction preserves skip-existing behavior");
        await backend.ExtractAsync(wrapper, new ExtractOptions { DestinationDirectory = output, Password = password,
            SelectedEntries = new HashSet<string> { "operation-source/first.txt" }, OverwriteMode = OverwriteMode.AutoRename }, job);
        check(File.ReadAllText(first) == "existing file" && Directory.GetFiles(Path.GetDirectoryName(first)!, "*.txt").Length == 2,
            "Automatic inner extraction preserves auto-rename behavior");
        using (var emptyDisk = new LunaJobController { FreeSpaceProbe = _ => new("synthetic full volume", 0) })
        {
            string blocked = Path.Combine(root, "no-space-parent", "blocked.zip");
            check(await Reject(() => ArchiveCreationService.CreateAsync(new[] { source }, new CompressOptions { OutputArchivePath = blocked, Format = OutputArchiveFormat.Zip }, emptyDisk)) && !Directory.Exists(Path.GetDirectoryName(blocked)),
                "Creation checks space before creating output directories or archives");
            check(await Reject(() => backend.ExtractAsync(wrapper, new ExtractOptions { DestinationDirectory = output, Password = password }, emptyDisk)) && File.ReadAllText(first) == "existing file",
                "Wrapper staging refuses insufficient space and preserves existing output");
        }
        string plain = Path.Combine(root, "preflight.zip");
        await ArchiveCreationService.CreateAsync(new[] { source }, new CompressOptions { OutputArchivePath = plain, Format = OutputArchiveFormat.Zip }, job);
        using (var emptyDisk = new LunaJobController { FreeSpaceProbe = _ => new("synthetic full volume", 0) })
        {
            string blocked = Path.Combine(root, "no-space-extraction");
            check(await Reject(() => backend.ExtractAsync(plain, new ExtractOptions { DestinationDirectory = blocked }, emptyDisk)) && !Directory.Exists(blocked),
                "Extraction checks destination space before writing files or directories");
        }
        string larc = Path.Combine(root, "preflight.larc");
        await ArchiveCreationService.CreateAsync(new[] { source }, new CompressOptions { OutputArchivePath = larc, Password = password }, job);
        using (var emptyDisk = new LunaJobController { FreeSpaceProbe = _ => new("synthetic full volume", 0) })
            check(await Reject(() => backend.ExtractAsync(larc, new ExtractOptions { DestinationDirectory = output, Password = password }, emptyDisk)), "LARC authentication checks temporary space before decrypting");
        string split = Path.Combine(root, "preflight-split.zip"); await SplitArchive.CreateAsync(plain, split, 65536, job);
        using (var emptyDisk = new LunaJobController { FreeSpaceProbe = _ => new("synthetic full volume", 0) })
            check(await Reject(() => backend.ExtractAsync(split + ".001", new ExtractOptions { DestinationDirectory = output }, emptyDisk)), "Split assembly checks temporary space before joining");
        using (var cancelled = new LunaJobController())
        {
            cancelled.ProgressChanged += progress => { if (progress.Stage == "Unwrapping TAR GZip") cancelled.Cancel(); };
            string cancelledOutput = Path.Combine(root, "cancelled-unwrapping");
            check(await Reject(() => backend.ExtractAsync(wrapper, new ExtractOptions { DestinationDirectory = cancelledOutput, Password = password }, cancelled)) && !Directory.Exists(cancelledOutput),
                "Cancelling wrapper unwrapping leaves no destination files");
        }
        string malformed = Path.Combine(root, "malformed.tar.gz.7z");
        await ArchiveCreationService.CreateAsync(new[] { Path.Combine(source, "first.txt") }, new CompressOptions { OutputArchivePath = malformed, Format = OutputArchiveFormat.SevenZip }, job);
        check(await Reject(() => backend.ExtractAsync(malformed, new ExtractOptions { DestinationDirectory = output }, job)), "Automatic wrapping rejects a container without one root TAR GZip");
        string inner = Path.Combine(root, "ordinary.tar.gz"), ordinary = Path.Combine(root, "ordinary.7z");
        await backend.CompressAsync(new[] { source }, new CompressOptions { OutputArchivePath = inner, Format = OutputArchiveFormat.TarGz }, job);
        await ArchiveCreationService.CreateAsync(new[] { inner }, new CompressOptions { OutputArchivePath = ordinary, Format = OutputArchiveFormat.SevenZip }, job);
        string ordinaryOutput = Path.Combine(root, "ordinary-output");
        await backend.ExtractAsync(ordinary, new ExtractOptions { DestinationDirectory = ordinaryOutput }, job);
        check(File.Exists(Path.Combine(ordinaryOutput, "ordinary.tar.gz")), "Ordinary 7z extraction does not recursively unpack arbitrary embedded archives");
        string maliciousTar = Path.Combine(root, "malicious.tar.gz"), maliciousWrapper = Path.Combine(root, "malicious.tar.gz.7z");
        using (var outputFile = File.Create(maliciousTar))
        using (var gzip = new System.IO.Compression.GZipStream(outputFile, System.IO.Compression.CompressionMode.Compress))
        using (var tar = new System.Formats.Tar.TarWriter(gzip))
        using (var payload = new MemoryStream("synthetic unsafe payload"u8.ToArray()))
            tar.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "../operation-escape.txt") { DataStream = payload });
        await ArchiveCreationService.CreateAsync(new[] { maliciousTar }, new CompressOptions { OutputArchivePath = maliciousWrapper, Format = OutputArchiveFormat.SevenZip }, job);
        string escaped = Path.Combine(root, "operation-escape.txt"); File.WriteAllText(escaped, "existing outside file");
        string unsafeOutput = Path.Combine(root, "unsafe-wrapper-output");
        check(await Reject(() => backend.ExtractAsync(maliciousWrapper, new ExtractOptions { DestinationDirectory = unsafeOutput }, job)) &&
            File.ReadAllText(escaped) == "existing outside file" && !Directory.Exists(unsafeOutput), "Automatic second-layer extraction rejects TAR traversal before destination changes");
        using var progressJob = new LunaJobController(); var updates = new List<OperationProgress>(); progressJob.ProgressChanged += updates.Add;
        progressJob.Report("Measured transfer", 0, 4096); await Task.Delay(600);
        progressJob.Report("Measured transfer", 1024, 4096);
        check(updates[^1].BytesPerSecond > 0 && updates[^1].Remaining > TimeSpan.Zero, "Byte progress exposes measured rate and ETA after warm-up");
        progressJob.Pause(); progressJob.Report("Measured transfer", 1024, 1024); double pausedRate = updates[^1].BytesPerSecond;
        await Task.Delay(200); progressJob.Report("Measured transfer", 1024, 1024);
        check(updates[^1].BytesPerSecond == pausedRate, "Paused time is excluded from progress throughput");
        progressJob.Resume(); progressJob.Report("Unknown encoder step");
        check(updates[^1].IsIndeterminate && updates[^1].BytesPerSecond == 0 && updates[^1].Remaining == null, "Unknown-length stages do not invent percentage, speed or ETA");
        progressJob.Report("Item stage", 1, 4, isBytes: false);
        check(updates[^1].Percent == 25 && !updates[^1].Detail.Contains("MiB/s"), "Item counts are not mislabeled as byte throughput");

        static async Task<bool> Reject(Func<Task> action)
        {
            try { await action(); return false; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OperationCanceledException) { return true; }
        }
    }
}
