using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;

internal static class VerifiedSourceCleanupChecks
{
    public static async Task RunAsync(string root, StandardArchiveHandler backend, Action<bool, string> check)
    {
        foreach (var (format, extension, password) in new[]
        {
            (OutputArchiveFormat.LarcNative, "larc", (string?)"synthetic-cleanup-password"),
            (OutputArchiveFormat.Zip, "zip", (string?)null), (OutputArchiveFormat.TarGz, "tar.gz", (string?)null)
        })
        {
            string folder = Path.Combine(root, "cleanup-" + extension);
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            File.WriteAllText(Path.Combine(folder, "first.txt"), "Synthetic source A");
            File.WriteAllText(Path.Combine(folder, "nested/second.txt"), "Synthetic source B");
            string single = Path.Combine(root, "single-" + extension + ".txt");
            File.WriteAllText(single, "Synthetic single source");
            using var job = new LunaJobController();
            var cleanup = await VerifiedSourceCleanup.CaptureAsync(new[] { folder, single }, job.Token);
            string archive = Path.Combine(root, "cleanup-result." + extension);
            await backend.CompressAsync(new[] { folder, single }, new CompressOptions { OutputArchivePath = archive, Format = format, Password = password }, job);
            check(Directory.Exists(folder) && File.Exists(single), $"{format}: sources remain before verified cleanup");
            var result = await cleanup.DeleteArchivedSourcesAsync(backend, archive, password, job);
            check(result.DeletedFiles == 3 && result.KeptFiles == 0, $"{format}: deletes matching archived sources");
            check(!Directory.Exists(folder) && !File.Exists(single) && File.Exists(archive), $"{format}: removes represented empty folders and retains the archive");
            string restored = Path.Combine(root, "cleanup-restored-" + extension);
            await backend.ExtractAsync(archive, new ExtractOptions { DestinationDirectory = restored, Password = password }, job);
            check(Directory.GetFiles(restored, "*", SearchOption.AllDirectories).Length == 3, $"{format}: deleted sources can be restored from the archive");
        }

        using var safeJob = new LunaJobController();
        string changed = Path.Combine(root, "cleanup-changed.txt");
        File.WriteAllText(changed, "Original source");
        var changedPlan = await VerifiedSourceCleanup.CaptureAsync(new[] { changed }, safeJob.Token);
        string changedArchive = Path.Combine(root, "cleanup-changed.zip");
        await backend.CompressAsync(new[] { changed }, new CompressOptions { OutputArchivePath = changedArchive, Format = OutputArchiveFormat.Zip }, safeJob);
        File.WriteAllText(changed, "Changed source must survive cleanup");
        var changedResult = await changedPlan.DeleteArchivedSourcesAsync(backend, changedArchive, null, safeJob);
        check(changedResult.DeletedFiles == 0 && File.ReadAllText(changed) == "Changed source must survive cleanup", "Cleanup preserves a source changed after capture");

        string growing = Path.Combine(root, "cleanup-growing");
        Directory.CreateDirectory(Path.Combine(growing, "empty-not-in-zip"));
        string captured = Path.Combine(growing, "captured.txt");
        File.WriteAllText(captured, "Captured source");
        var growingPlan = await VerifiedSourceCleanup.CaptureAsync(new[] { growing }, safeJob.Token);
        string growingArchive = Path.Combine(root, "cleanup-growing.zip");
        await backend.CompressAsync(new[] { growing }, new CompressOptions { OutputArchivePath = growingArchive, Format = OutputArchiveFormat.Zip }, safeJob);
        string added = Path.Combine(growing, "added-after-archive.txt");
        File.WriteAllText(added, "New source must survive cleanup");
        var growingResult = await growingPlan.DeleteArchivedSourcesAsync(backend, growingArchive, null, safeJob);
        check(growingResult.DeletedFiles == 1 && !File.Exists(captured) && File.ReadAllText(added) == "New source must survive cleanup", "Cleanup preserves files added after capture");
        check(Directory.Exists(Path.Combine(growing, "empty-not-in-zip")), "Cleanup preserves empty directories not represented in ZIP");

        string mismatch = Path.Combine(root, "cleanup-mismatch.txt");
        File.WriteAllText(mismatch, "Before capture");
        var mismatchPlan = await VerifiedSourceCleanup.CaptureAsync(new[] { mismatch }, safeJob.Token);
        File.WriteAllText(mismatch, "Different archive contents");
        string mismatchArchive = Path.Combine(root, "cleanup-mismatch.zip");
        await backend.CompressAsync(new[] { mismatch }, new CompressOptions { OutputArchivePath = mismatchArchive, Format = OutputArchiveFormat.Zip }, safeJob);
        File.WriteAllText(mismatch, "Before capture");
        var mismatchResult = await mismatchPlan.DeleteArchivedSourcesAsync(backend, mismatchArchive, null, safeJob);
        check(mismatchResult.DeletedFiles == 0 && File.Exists(mismatch), "A valid archive with different captured contents cannot delete sources");

        string corruptArchive = Path.Combine(root, "cleanup-corrupt.zip");
        byte[] corrupt = File.ReadAllBytes(mismatchArchive);
        corrupt[14] ^= 1;
        for (int i = 0; i <= corrupt.Length - 20; i++)
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(corrupt.AsSpan(i, 4)) == 0x02014b50)
            { corrupt[i + 16] ^= 1; break; }
        File.WriteAllBytes(corruptArchive, corrupt);
        bool corruptRejected = false;
        try { await mismatchPlan.DeleteArchivedSourcesAsync(backend, corruptArchive, null, safeJob); }
        catch (InvalidDataException) { corruptRejected = true; }
        check(corruptRejected && File.ReadAllText(mismatch) == "Before capture", "A failed archive integrity check cannot delete sources");

        using var cancelled = new LunaJobController();
        var cancelledPlan = await VerifiedSourceCleanup.CaptureAsync(new[] { mismatch }, cancelled.Token);
        cancelled.Cancel();
        bool cancellationObserved = false;
        try { await cancelledPlan.DeleteArchivedSourcesAsync(backend, mismatchArchive, null, cancelled); }
        catch (OperationCanceledException) { cancellationObserved = true; }
        check(cancellationObserved && File.Exists(mismatch), "Cancelled cleanup preserves its sources");

        bool overlapRejected = false;
        try { await VerifiedSourceCleanup.CaptureAsync(new[] { growing, added }, safeJob.Token); }
        catch (IOException) { overlapRejected = true; }
        check(overlapRejected && File.Exists(added), "Overlapping deletion inputs are rejected without deleting files");
        if (OperatingSystem.IsLinux())
        {
            string link = Path.Combine(root, "cleanup-link.txt");
            File.CreateSymbolicLink(link, mismatch);
            bool linkRejected = false;
            try { await VerifiedSourceCleanup.CaptureAsync(new[] { link }, safeJob.Token); }
            catch (IOException) { linkRejected = true; }
            check(linkRejected && File.Exists(mismatch), "Automatic cleanup rejects symbolic links");
        }
    }
}
