using System.Diagnostics;
using System.Security.Cryptography;
using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;

internal static class AdvancedArchivingChecks
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        string folder = Path.Combine(root, "advanced"); Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "sources"); Directory.CreateDirectory(source);
        string text = string.Concat(Enumerable.Repeat("Synthetic shared solid dictionary content.\n", 5000));
        await File.WriteAllTextAsync(Path.Combine(source, "one.txt"), text);
        await File.WriteAllTextAsync(Path.Combine(source, "two.txt"), text + "Two");
        await File.WriteAllTextAsync(Path.Combine(source, "日本語.txt"), "Unicode fixture");
        await File.WriteAllBytesAsync(Path.Combine(source, "empty"), Array.Empty<byte>());
        Directory.CreateDirectory(Path.Combine(source, "empty-dir"));
        var backend = new StandardArchiveHandler();
        using var job = new LunaJobController();
        string zip = Path.Combine(folder, "base.zip");
        await ArchiveCreationService.CreateAsync(new[] { source }, new CompressOptions { OutputArchivePath = zip, Format = OutputArchiveFormat.Zip }, job, recovery: true, locked: true, test: true);
        check(File.Exists(zip + ".lrec") && ArchiveLock.IsLocked(zip) && (File.GetAttributes(zip) & FileAttributes.ReadOnly) != 0, "Advanced: companions and read-only lock published");
        check((await backend.TestArchiveAsync(zip, null, job)).IsHealthy, "Advanced: locked archives remain readable");
        await Reject(() => backend.CompressAsync(new[] { source }, new CompressOptions { OutputArchivePath = zip, Format = OutputArchiveFormat.Zip }, job), "Advanced: locked archive cannot be overwritten");
        ArchiveLock.Unlock(zip);
        check(!ArchiveLock.IsLocked(zip) && (File.GetAttributes(zip) & FileAttributes.ReadOnly) == 0, "Advanced: explicit unlock removes lock and read-only hint");
        await Reject(() => backend.CompressAsync(new[] { source }, new CompressOptions { OutputArchivePath = zip, Format = OutputArchiveFormat.Zip }, job), "Advanced: unlocked output still requires a new name");
        string reserved = Path.Combine(folder, "reserved.zip"); File.WriteAllText(reserved + ".lunalock", "reserved lock");
        await Reject(() => backend.CompressAsync(new[] { source }, new CompressOptions { OutputArchivePath = reserved, Format = OutputArchiveFormat.Zip }, job), "Advanced: companion lock survives removal of the archive");
        check(File.ReadAllText(reserved + ".lunalock") == "reserved lock", "Advanced: foreign companion is preserved");
        ArchiveLock.Unlock(reserved);
        check(!ArchiveLock.IsLocked(reserved), "Advanced: explicit unlock can remove an orphan companion");

        string stub = Path.Combine(folder, "synthetic-stub"); File.WriteAllBytes(stub, "Synthetic non-executable test stub"u8.ToArray());
        string sfx = Path.Combine(folder, "synthetic.sfx");
        await SfxArchive.CreateAsync(stub, zip, sfx, OutputArchiveFormat.Zip);
        check(SfxArchive.IsSfx(sfx) && (await backend.TestArchiveAsync(sfx, null, job)).IsHealthy, "SFX: versioned footer and payload verification");
        check(!await ArchivePasswordDetection.RequiresPasswordAsync(sfx), "SFX: plain payload does not request a password");
        var catalog = await backend.ReadCatalogAsync(sfx);
        check(catalog.TotalUncompressedBytes == (await backend.ReadCatalogAsync(zip)).TotalUncompressedBytes, "SFX: catalog matches embedded ZIP");
        string extracted = Path.Combine(folder, "sfx-extracted");
        await backend.ExtractAsync(sfx, new ExtractOptions { DestinationDirectory = extracted }, job);
        check(File.ReadAllText(Path.Combine(extracted, "sources", "two.txt")) == text + "Two", "SFX: guarded extraction round trip");
        byte[] corrupted = await File.ReadAllBytesAsync(sfx); corrupted[new FileInfo(stub).Length + 10] ^= 1;
        string badSfx = Path.Combine(folder, "bad.sfx"); await File.WriteAllBytesAsync(badSfx, corrupted);
        await Reject(() => backend.ReadCatalogAsync(badSfx), "SFX: altered payload is rejected");
        corrupted = await File.ReadAllBytesAsync(sfx); Array.Fill(corrupted, (byte)0xff, corrupted.Length - 56, 8); await File.WriteAllBytesAsync(badSfx, corrupted);
        await Reject(() => backend.ReadCatalogAsync(badSfx), "SFX: invalid footer offsets rejected");
        await Reject(() => SfxArchive.CreateAsync(sfx, zip, Path.Combine(folder, "nested.sfx"), OutputArchiveFormat.Zip), "SFX: nested extractor stub rejected");
        string failed = Path.Combine(folder, "failed.exe");
        await Reject(() => ArchiveCreationService.CreateAsync(new[] { source }, new CompressOptions { OutputArchivePath = failed, Format = OutputArchiveFormat.Zip, DeleteSourceAfterArchiving = true }, job,
            sfx: true, recovery: true, locked: true, sfxStub: Path.Combine(folder, "missing-stub")), "Advanced: failed SFX creation preserves sources");
        check(!File.Exists(failed) && File.Exists(Path.Combine(source, "one.txt")) && !Directory.GetFiles(folder, ".lunaarc-*").Any(), "Advanced: failed creation leaves no published archive or staging files");

        // Distinct, incompressible blocks exercise parity across group boundaries.
        string original = Path.Combine(folder, "recovery-original.bin");
        byte[] data = RandomNumberGenerator.GetBytes(17 * 1024 * 1024 + 79);
        await File.WriteAllBytesAsync(original, data);
        string record = original + ".lrec"; await ArchiveRecoveryRecord.CreateAsync(original, record, job);
        string damaged = Path.Combine(folder, "damaged.bin"), repaired = Path.Combine(folder, "repaired.bin");
        byte[] broken = data.ToArray(); broken[10] ^= 0x44; broken[16 * 1024 * 1024 + 50] ^= 0x88;
        await File.WriteAllBytesAsync(damaged, broken);
        await ArchiveRecoveryRecord.RepairAsync(damaged, record, repaired, job);
        check(SHA256.HashData(await File.ReadAllBytesAsync(repaired)).SequenceEqual(SHA256.HashData(data)), "Recovery: independent group damage reconstructs the exact original hash");
        check(File.ReadAllBytes(damaged).SequenceEqual(broken), "Recovery: damaged original is preserved");
        File.Delete(repaired);
        await File.WriteAllBytesAsync(damaged, data[..^40]);
        await ArchiveRecoveryRecord.RepairAsync(damaged, record, repaired, job);
        check(SHA256.HashData(await File.ReadAllBytesAsync(repaired)).SequenceEqual(SHA256.HashData(data)), "Recovery: truncated last block is repaired");
        File.Delete(repaired);
        broken = data.ToArray(); broken[10] ^= 1; broken[1024 * 1024 + 10] ^= 1; await File.WriteAllBytesAsync(damaged, broken);
        await Reject(() => ArchiveRecoveryRecord.RepairAsync(damaged, record, repaired, job), "Recovery: two damaged blocks in one group fail safely");
        check(!File.Exists(repaired), "Recovery: excessive damage publishes no repaired file");
        byte[] badRecord = await File.ReadAllBytesAsync(record); badRecord[^100] ^= 1;
        string invalidRecord = record + ".bad"; await File.WriteAllBytesAsync(invalidRecord, badRecord);
        await Reject(() => ArchiveRecoveryRecord.RepairAsync(original, invalidRecord, repaired, job), "Recovery: corrupt parity record rejected before publication");
        await File.WriteAllBytesAsync(invalidRecord, badRecord[..100]);
        await Reject(() => ArchiveRecoveryRecord.RepairAsync(original, invalidRecord, repaired, job), "Recovery: truncated record rejected");
        badRecord = await File.ReadAllBytesAsync(record);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(badRecord.AsSpan(8), long.MaxValue);
        await File.WriteAllBytesAsync(invalidRecord, badRecord);
        await Reject(() => ArchiveRecoveryRecord.RepairAsync(original, invalidRecord, repaired, job), "Recovery: oversized metadata rejected before allocation");
        await Reject(() => ArchiveRecoveryRecord.RepairAsync(original, record, original, job), "Recovery: refuses overwriting originals");
        using (var cancelled = new LunaJobController())
        {
            cancelled.Cancel();
            await Reject(() => ArchiveRecoveryRecord.RepairAsync(original, record, repaired, cancelled), "Recovery: cancellation publishes no output");
        }
        check(!Directory.GetFiles(folder, ".lunaarc-repair-*").Any(), "Recovery: private partial repairs are cleaned up");
        string empty = Path.Combine(folder, "empty.bin"); File.WriteAllBytes(empty, Array.Empty<byte>());
        await ArchiveRecoveryRecord.CreateAsync(empty, empty + ".lrec", job);
        await ArchiveRecoveryRecord.RepairAsync(empty, empty + ".lrec", empty + ".repaired", job);
        check(new FileInfo(empty + ".repaired").Length == 0, "Recovery: empty input supported");

        if (!SevenZipEncoder.IsAvailable) throw new InvalidOperationException("Advanced checks need LUNAARC_7ZIP_PATH or the packaged 7-Zip encoder. See README.");
        foreach (bool solid in new[] { false, true })
        {
            string seven = Path.Combine(folder, solid ? "solid.7z" : "non-solid.7z");
            await ArchiveCreationService.CreateAsync(new[] { source }, new CompressOptions { OutputArchivePath = seven, Format = OutputArchiveFormat.SevenZip, SolidArchive = solid }, job, test: true);
            var info = new ProcessStartInfo(SevenZipEncoder.ExecutablePath) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            foreach (string arg in new[] { "l", "-slt", "--", seven }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!; string listing = await process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync();
            check(process.ExitCode == 0 && listing.Contains(solid ? "Solid = +" : "Solid = -"), $"7z: independent encoder confirms solid={solid}");
            check((await backend.TestArchiveAsync(seven, null, job)).IsHealthy, $"7z: managed decoder verifies solid={solid}");
            check(!await ArchivePasswordDetection.RequiresPasswordAsync(seven), $"7z: solid={solid} is not confused with encryption");
            string sevenOutput = seven + "-extracted"; await backend.ExtractAsync(seven, new ExtractOptions { DestinationDirectory = sevenOutput }, job);
            check(File.ReadAllText(Path.Combine(sevenOutput, "sources", "two.txt")) == text + "Two" && File.Exists(Path.Combine(sevenOutput, "sources", "日本語.txt")), $"7z: Unicode and solid={solid} extraction round trip");
        }
        await Reject(() => backend.CompressAsync(new[] { source }, new CompressOptions { OutputArchivePath = Path.Combine(folder, "invalid.zip"), Format = OutputArchiveFormat.Zip, SolidArchive = true }, job), "Solid: unsupported formats rejected explicitly");
        string disposable = Path.Combine(folder, "combined-source"); Directory.CreateDirectory(disposable);
        File.WriteAllText(Path.Combine(disposable, "one.txt"), text); File.WriteAllText(Path.Combine(disposable, "two.txt"), text);
        string combined = Path.Combine(folder, "combined.sfx");
        string combinedResult = await ArchiveCreationService.CreateAsync(new[] { disposable }, new CompressOptions
        {
            OutputArchivePath = combined, Format = OutputArchiveFormat.SevenZip, SolidArchive = true, DeleteSourceAfterArchiving = true
        }, job, sfx: true, recovery: true, locked: true, sfxStub: stub);
        check(!Directory.Exists(disposable) && combinedResult.Contains("Removed 2") && File.Exists(combined + ".lrec") && ArchiveLock.IsLocked(combined),
            "Combined: solid SFX, parity, locking and verified source deletion complete together");
        check((await backend.TestArchiveAsync(combined, null, job)).IsHealthy, "Combined: locked SFX remains healthy after source deletion");
        ArchiveLock.Unlock(combined);
        using (var inFlight = new LunaJobController())
        {
            string cancelledOutput = Path.Combine(folder, "in-flight.7z");
            var packing = Task.Run(() => backend.CompressAsync(new[] { original }, new CompressOptions
            {
                OutputArchivePath = cancelledOutput, Format = OutputArchiveFormat.SevenZip, Level = LunaCompressionLevel.Best
            }, inFlight));
            await Task.Delay(200); inFlight.Cancel();
            await Reject(() => packing, "7z: in-flight cancellation terminates the job");
            check(!File.Exists(cancelledOutput) && File.Exists(original), "7z: cancelled encoding publishes no output and keeps source");
        }
        using (var cancelled = new LunaJobController())
        {
            cancelled.Cancel();
            await Reject(() => backend.CompressAsync(new[] { source }, new CompressOptions { OutputArchivePath = Path.Combine(folder, "cancelled.7z"), Format = OutputArchiveFormat.SevenZip }, cancelled), "7z: cancelled jobs preserve sources");
        }

        async Task Reject(Func<Task> action, string message)
        {
            try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or OperationCanceledException or NotSupportedException) { check(true, message); return; }
            check(false, message);
        }
    }
}
