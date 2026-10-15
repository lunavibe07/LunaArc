using System.IO.Compression;
using System.Security.Cryptography;
using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.LiveEdit;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;
using LunaArc.Core.VfsIndex;

string root = Path.Combine(Path.GetTempPath(), "LunaArc_Smoke_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
try
{
    string source = Path.Combine(root, "fixture with spaces");
    Directory.CreateDirectory(Path.Combine(source, "nested"));
    File.WriteAllText(Path.Combine(source, "hello.txt"), "LunaArc archive round-trip fixture\n");
    File.WriteAllText(Path.Combine(source, "nested", "unicode-\u65e5\u672c.txt"), "Unicode path fixture\n");
    File.WriteAllBytes(Path.Combine(source, "empty.bin"), Array.Empty<byte>());
    File.WriteAllBytes(Path.Combine(source, "data.bin"), RandomNumberGenerator.GetBytes(150_000));
    var backend = new StandardArchiveHandler();
    await ExtractionUpdateChecks.RunAsync(root, backend, Check);
    await VerifiedSourceCleanupChecks.RunAsync(root, backend, Check);
    await AdvancedArchivingChecks.RunAsync(root, Check);

    foreach (var (format, extension, password, level) in new[]
    {
        (OutputArchiveFormat.LarcNative, "larc", (string?)null, LunaCompressionLevel.Normal),
        (OutputArchiveFormat.LarcNative, "larc", "synthetic-test-password", LunaCompressionLevel.Normal),
        (OutputArchiveFormat.LarcNative, "larc", (string?)null, LunaCompressionLevel.Store),
        (OutputArchiveFormat.Zip, "zip", (string?)null, LunaCompressionLevel.Normal),
        (OutputArchiveFormat.TarGz, "tar.gz", (string?)null, LunaCompressionLevel.Normal),
        (OutputArchiveFormat.TarGz, "tar.gz", (string?)null, LunaCompressionLevel.Store),
        (OutputArchiveFormat.TarGz, "tar.gz", (string?)null, LunaCompressionLevel.Best)
    })
    {
        string archive = Path.Combine(root, $"archive-{checks}.{extension}");
        using var job = new LunaJobController();
        await backend.CompressAsync(new[] { source }, new CompressOptions
        {
            OutputArchivePath = archive, Format = format, Password = password, Level = level,
            ArchiveComment = format == OutputArchiveFormat.LarcNative ? "Smoke fixture" : null
        }, job);
        var catalog = await backend.ReadCatalogAsync(archive, password);
        Check(await ArchivePasswordDetection.RequiresPasswordAsync(archive) == (password != null), $"{format}: encryption detection follows archive metadata");
        Check(catalog.TotalUncompressedBytes == Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Sum(p => new FileInfo(p).Length), $"{format}: catalog size");

        string destination = Path.Combine(root, $"extracted-{checks}");
        await backend.ExtractAsync(archive, new ExtractOptions { DestinationDirectory = destination, Password = password }, job);
        var extractedFiles = Directory.GetFiles(destination, "*", SearchOption.AllDirectories);
        Check(extractedFiles.Length == 4, $"{format}: extracted file count");
        foreach (string original in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, original);
            string extracted = extractedFiles.Single(p => p.EndsWith(relative, StringComparison.Ordinal));
            Check(SHA256.HashData(File.ReadAllBytes(original)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(extracted))),
                $"{format}: content hash for {relative}");
        }
        var result = await backend.TestArchiveAsync(archive, password, job);
        Check(result.IsHealthy && result.TotalFilesTested == 4 && result.PassedFiles == 4, $"{format}: integrity test");
        if (password != null)
        {
            string detectorStub = Path.Combine(root, "password-detector-stub");
            File.WriteAllText(detectorStub, "Synthetic non-executable stub");
            string encryptedSfx = Path.Combine(root, $"encrypted-{checks}.sfx");
            await SfxArchive.CreateAsync(detectorStub, archive, encryptedSfx, format);
            Check(await ArchivePasswordDetection.RequiresPasswordAsync(encryptedSfx), "SFX: encrypted LARC payload requires password input");
            await ExpectFailure(() => backend.ReadCatalogAsync(archive, "wrong-password"), "Wrong password is rejected");
            Check(!(await backend.TestArchiveAsync(archive, "wrong-password", job)).IsHealthy, "Wrong password fails archive test");
            Check(!await PasswordRecovery.VerifyArchiveAsync(backend, archive, null, default), "Locked catalog needs a password");
            var recovered = await PasswordRecovery.SearchAsync(new[] { "incorrect", password }, 2,
                (candidate, ct) => PasswordRecovery.VerifyArchiveAsync(backend, archive, candidate, ct), job);
            Check(recovered.Verified && recovered.Password == password, "LARC list recovery verifies decrypted contents");
            Check(!await PasswordRecovery.VerifyArchiveAsync(backend, archive, "incorrect", default), "Recovery rejects an incorrect password");
        }
        if (format == OutputArchiveFormat.Zip)
        {
            using var zip = ZipFile.OpenRead(archive);
            Check(zip.Entries.Count == 4, "ZIP opens with the independent .NET ZIP reader");
        }
        if (format == OutputArchiveFormat.TarGz)
        {
            var signature = File.ReadAllBytes(archive);
            Check(signature[0] == 0x1f && signature[1] == 0x8b, "TAR GZip signature");
        }
    }

    string target = Path.Combine(root, "safe");
    Check(LunaArc.Shell.ExplorerRequest.GetFolderName("release.TAR.GZ") == "release", "Explorer folder name handles compound extensions");
    var shellRequest = LunaArc.Shell.ExplorerRequest.Parse(new[] { "--shell-extract-here", Path.Combine(root, "archive with spaces.zip") });
    Check(shellRequest.Action == LunaArc.Shell.ExplorerAction.ExtractHere && shellRequest.ExtractionDirectory == root, "Explorer command parsing preserves paths with spaces");
    await ExpectFailure(() => Task.FromResult(LunaArc.Shell.ExplorerRequest.Parse(new[] { "--shell-unknown", root })), "Unknown Explorer command rejected");
    await ExpectFailure(() => Task.FromResult(LunaArc.Shell.ExplorerRequest.Parse(new[] { "--shell-open", root, "extra" })), "Explorer command rejects extra arguments");
    foreach (string name in new[] { "encrypted.zip", "encrypted-aes.zip", "encrypted.7z" })
    {
        Check(await ArchivePasswordDetection.RequiresPasswordAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), $"{name}: encrypted metadata enables password input");
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        Check(!await PasswordRecovery.VerifyArchiveAsync(backend, fixture, null, default), name + ": cannot unlock without a password");
        Check(!await PasswordRecovery.VerifyArchiveAsync(backend, fixture, "wrong-password", default), name + ": wrong password rejected");
        Check(await PasswordRecovery.VerifyArchiveAsync(backend, fixture, "fixture3", default), name + ": correct password verifies content");
        using var recoveryJob = new LunaJobController();
        var result = await PasswordRecovery.SearchAsync(PasswordRecovery.EnumerateMask("fixture?"), 10,
            (candidate, ct) => PasswordRecovery.VerifyArchiveAsync(backend, fixture, candidate, ct), recoveryJob);
        Check(result.Verified && result.Password == "fixture3", name + ": bounded mask recovery");
        string encryptedDestination = Path.Combine(root, "encrypted-output-" + name.Replace('.', '-'));
        await backend.ExtractAsync(fixture, new ExtractOptions { DestinationDirectory = encryptedDestination, Password = "fixture3" }, recoveryJob);
        Check(File.ReadAllText(Path.Combine(encryptedDestination, "message.txt")).TrimEnd() == "Synthetic LunaArc recovery fixture.", name + ": recovered password extracts original data");
    }
    string emptySource = Path.Combine(root, "empty-source");
    foreach (string name in new[] { "Rar.encrypted_filesOnly.rar", "Rar.encrypted_filesAndHeader.rar", "Rar5.encrypted_filesOnly.rar", "Rar5.encrypted_filesAndHeader.rar" })
    {
        Check(await ArchivePasswordDetection.RequiresPasswordAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), $"{name}: encryption detected even with locked headers");
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        Check(!await PasswordRecovery.VerifyArchiveAsync(backend, fixture, null, default), name + ": cannot unlock without a password");
        Check(!await PasswordRecovery.VerifyArchiveAsync(backend, fixture, "wrong-password", default), name + ": wrong password rejected");
        Check(await PasswordRecovery.VerifyArchiveAsync(backend, fixture, "test", default), name + ": correct password verifies content");
        using var recoveryJob = new LunaJobController();
        var result = await PasswordRecovery.SearchAsync(new[] { "incorrect", "test" }, 2,
            (candidate, ct) => PasswordRecovery.VerifyArchiveAsync(backend, fixture, candidate, ct), recoveryJob);
        Check(result.Verified && result.Password == "test", name + ": vault-list recovery");
    }
    Directory.CreateDirectory(emptySource);
    using (var emptyJob = new LunaJobController())
    {
        string emptyArchive = Path.Combine(root, "empty-encrypted.larc");
        await backend.CompressAsync(new[] { emptySource }, new CompressOptions
        { OutputArchivePath = emptyArchive, Format = OutputArchiveFormat.LarcNative, Password = "empty-fixture" }, emptyJob);
        Check(await PasswordRecovery.VerifyArchiveAsync(backend, emptyArchive, "empty-fixture", default), "Encrypted empty LARC is verified from the header flag");
        Check(await ArchivePasswordDetection.RequiresPasswordAsync(emptyArchive), "Encrypted empty LARC still requires its header password");
        string storedArchive = Path.Combine(root, "stored-unencrypted.larc");
        await backend.CompressAsync(new[] { source }, new CompressOptions
        { OutputArchivePath = storedArchive, Format = OutputArchiveFormat.LarcNative, Level = LunaCompressionLevel.Store }, emptyJob);
        Check(await PasswordRecovery.VerifyArchiveAsync(backend, storedArchive, null, default), "Store-mode flag is not interpreted as encryption");
    }
    string damagedAes = Path.Combine(root, "damaged-aes.zip");
    byte[] aesBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "encrypted-aes.zip"));
    int centralOffset = aesBytes.AsSpan().IndexOf(new byte[] { 0x50, 0x4b, 0x01, 0x02 });
    Check(centralOffset > 10, "AES ZIP authentication fixture located");
    aesBytes[centralOffset - 1] ^= 1;
    File.WriteAllBytes(damagedAes, aesBytes);
    Check(!await PasswordRecovery.VerifyArchiveAsync(backend, damagedAes, "fixture3", default), "Recovery rejects a damaged AES authentication tag");
    string damagedOutput = Path.Combine(root, "damaged-aes-output");
    Directory.CreateDirectory(damagedOutput);
    File.WriteAllText(Path.Combine(damagedOutput, "message.txt"), "Existing file");
    using (var damagedJob = new LunaJobController())
        await ExpectFailure(() => backend.ExtractAsync(damagedAes, new ExtractOptions
        { DestinationDirectory = damagedOutput, Password = "fixture3", OverwriteMode = OverwriteMode.OverwriteAll }, damagedJob), "AES authentication failure prevents extraction");
    Check(File.ReadAllText(Path.Combine(damagedOutput, "message.txt")) == "Existing file", "AES authentication failure preserves original destination");
    string ae1Archive = Path.Combine(root, "aes-ae1.zip");
    byte[] ae1Bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "encrypted-aes.zip"));
    byte[] aesExtraMarker = new byte[] { 0x01, 0x99, 0x07, 0x00, 0x02, 0x00, 0x41, 0x45 };
    for (int offset = 0; offset <= ae1Bytes.Length - aesExtraMarker.Length; offset++)
        if (ae1Bytes.AsSpan(offset, aesExtraMarker.Length).SequenceEqual(aesExtraMarker)) ae1Bytes[offset + 4] = 1;
    uint ae1Crc = Crc32Verifier.Compute("Synthetic LunaArc recovery fixture.\r\n"u8);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(ae1Bytes.AsSpan(14, 4), ae1Crc);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(ae1Bytes.AsSpan(centralOffset + 16, 4), ae1Crc);
    File.WriteAllBytes(ae1Archive, ae1Bytes);
    Check(await PasswordRecovery.VerifyArchiveAsync(backend, ae1Archive, "fixture3", default), "AES AE-1 validates both authentication and CRC");
    ae1Bytes[centralOffset + 16] ^= 1;
    File.WriteAllBytes(ae1Archive, ae1Bytes);
    Check(!await PasswordRecovery.VerifyArchiveAsync(backend, ae1Archive, "fixture3", default), "AES AE-1 does not skip CRC verification");
    Check(PasswordRecovery.CountMask("x?#") == 260, "Mask candidate count");
    Check(PasswordRecovery.EnumerateMask("x?").SequenceEqual(Enumerable.Range(0, 10).Select(i => "x" + i)), "Lazy mask enumeration");
    Check(PasswordRecovery.EnumerateMask(@"x\?\#").Single() == "x?#", "Literal mask escape");
    await ExpectFailure(() => Task.FromResult(PasswordRecovery.CountMask("########")), "Mask limit rejects exponential allocation");
    using (var recoveryJob = new LunaJobController())
    {
        var recovered = await PasswordRecovery.SearchAsync(PasswordRecovery.EnumerateMask("x?"), 10,
            (candidate, _) => Task.FromResult(candidate == "x3"), recoveryJob);
        Check(recovered.Verified && recovered.Password == "x3", "Mask recovery stops on a verified match");
        var exhausted = await PasswordRecovery.SearchAsync(new[] { "a", "b" }, 2,
            (_, _) => Task.FromResult(false), recoveryJob);
        Check(!exhausted.Verified && exhausted.Password == null, "Search exhaustion is distinct from success");
    }
    using (var pausedJob = new LunaJobController())
    {
        pausedJob.Pause();
        int calls = 0;
        Task<RecoveryResult> paused = Task.Run(() => PasswordRecovery.SearchAsync(new[] { "a" }, 1,
            (_, _) => { Interlocked.Increment(ref calls); return Task.FromResult(false); }, pausedJob));
        await Task.Delay(50);
        Check(calls == 0 && !paused.IsCompleted, "Paused recovery does not verify candidates");
        pausedJob.Cancel();
        await ExpectFailure(async () => await paused, "Cancelling paused recovery releases workers");
    }
    using (var cancelledJob = new LunaJobController())
    {
        cancelledJob.Cancel();
        await ExpectFailure(async () => await PasswordRecovery.SearchAsync(new[] { "a" }, 1,
            (_, _) => Task.FromResult(true), cancelledJob), "Cancellation is not reported as recovery failure");
    }
    var vault = new PasswordVault(Path.Combine(root, "vault.enc"));
    vault.Save(new[] { "synthetic-vault-secret", " leading space ", "synthetic-vault-secret" }, "synthetic-master");
    Check(vault.Load("synthetic-master").SequenceEqual(new[] { "synthetic-vault-secret", " leading space " }), "Authenticated vault round trip and deduplication");
    byte[] originalVault = File.ReadAllBytes(Path.Combine(root, "vault.enc"));
    Check(!System.Text.Encoding.UTF8.GetString(originalVault).Contains("synthetic-vault-secret"), "Vault contains no plaintext password");
    await ExpectFailure(() => Task.FromResult(vault.Load("wrong-master")), "Wrong vault master is rejected");
    await ExpectFailure(() => { vault.Save(new[] { "replacement" }, "wrong-master"); return Task.CompletedTask; }, "Wrong master cannot replace existing vault");
    Check(File.ReadAllBytes(Path.Combine(root, "vault.enc")).SequenceEqual(originalVault), "Failed vault save preserves original data");
    byte[] tamperedVault = originalVault.ToArray();
    tamperedVault[^1] ^= 1;
    File.WriteAllBytes(Path.Combine(root, "vault.enc"), tamperedVault);
    await ExpectFailure(() => Task.FromResult(vault.Load("synthetic-master")), "Tampered vault is rejected");
    File.WriteAllBytes(Path.Combine(root, "vault.enc"), originalVault[..20]);
    await ExpectFailure(() => Task.FromResult(vault.Load("synthetic-master")), "Truncated vault is rejected");
    Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Vault does not leave temporary files");
    foreach (string unsafePath in new[] { "../escape", "a/../../escape", "/absolute", "C:\\escape", "\\\\server\\share\\escape" })
        await ExpectFailure(() => Task.FromResult(ZipSlipGuard.ValidateAndResolvePath(target, unsafePath)), $"Reject unsafe path: {unsafePath}");
    Check(ZipSlipGuard.ValidateAndResolvePath(target, "nested/file.txt") == Path.Combine(target, "nested", "file.txt"), "Safe extraction path");
    using (var preview = new TempFileWatcher())
    {
        string previewPath = preview.PrepareTempPathForEntry("nested/preview.txt");
        Check(previewPath.StartsWith(preview.SessionDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Preview uses isolated session");
        await ExpectFailure(() => Task.FromResult(preview.PrepareTempPathForEntry("../escape.txt")), "Preview rejects traversal");
    }

    // An archive directory name is case-sensitive even when the host filesystem is not.
    var arena = new FlatVfsArena();
    arena.AddEntry("A/one", 1, 1, 0, null, false, false);
    arena.AddEntry("a/two", 1, 1, 0, null, false, false);
    Check(arena.GetChildrenIndices(-1).Count == 2, "VFS keeps differently cased folders distinct");

    using (var cancelled = new LunaJobController())
    {
        cancelled.Pause();
        cancelled.Cancel();
        Check(!cancelled.IsPaused && cancelled.IsCancelled, "Cancel releases pause");
        await ExpectFailure(() => backend.CompressAsync(new[] { source }, new CompressOptions
        { OutputArchivePath = Path.Combine(root, "cancelled.larc") }, cancelled), "Cancelled compression throws");
        Check(!File.Exists(Path.Combine(root, "cancelled.larc")), "Cancelled compression does not create output");
    }
    using (var job = new LunaJobController())
    {
        await ExpectFailure(() => backend.CompressAsync(new[] { source }, new CompressOptions
        { OutputArchivePath = Path.Combine(root, "unsupported.tar.zst"), Format = OutputArchiveFormat.TarZst }, job), "Unsupported TAR Zstandard output is rejected");
        await ExpectFailure(() => backend.CompressAsync(new[] { source }, new CompressOptions
        { OutputArchivePath = Path.Combine(root, "password.zip"), Format = OutputArchiveFormat.Zip, Password = "fixture" }, job), "Unsupported ZIP encryption is rejected");
        Check(!File.Exists(Path.Combine(root, "password.zip")), "Rejected encryption creates no archive");
        await ExpectFailure(() => backend.CompressAsync(new[] { source }, new CompressOptions
        { OutputArchivePath = Path.Combine(source, "self.zip"), Format = OutputArchiveFormat.Zip }, job), "Archive output inside source is rejected");
    }

    string corruptZip = Path.Combine(root, "corrupt.zip");
    byte[] marker = System.Text.Encoding.UTF8.GetBytes("Unique stored ZIP corruption fixture 0123456789");
    using (var zip = ZipFile.Open(corruptZip, ZipArchiveMode.Create))
    using (var data = zip.CreateEntry("preserve.txt", CompressionLevel.NoCompression).Open())
        data.Write(marker);
    byte[] corruptBytes = File.ReadAllBytes(corruptZip);
    int markerOffset = corruptBytes.AsSpan().IndexOf(marker);
    Check(markerOffset >= 0, "Stored ZIP corruption fixture found");
    corruptBytes[markerOffset] ^= 1;
    File.WriteAllBytes(corruptZip, corruptBytes);
    string preserveRoot = Path.Combine(root, "preserve");
    Directory.CreateDirectory(preserveRoot);
    string preserveFile = Path.Combine(preserveRoot, "preserve.txt");
    File.WriteAllText(preserveFile, "Existing destination must survive failed extraction.");
    using (var job = new LunaJobController())
    {
        Check(!(await backend.TestArchiveAsync(corruptZip, null, job)).IsHealthy, "Corrupted ZIP fails CRC check");
        await ExpectFailure(() => backend.ExtractAsync(corruptZip, new ExtractOptions
        { DestinationDirectory = preserveRoot }, job), "Corrupted ZIP extraction fails");
    }
    Check(File.ReadAllText(preserveFile) == "Existing destination must survive failed extraction.", "Failed extraction preserves existing file");
    Check(Directory.GetFiles(preserveRoot).Length == 1, "Failed extraction cleans its temporary output");

    string traversalZip = Path.Combine(root, "traversal.zip");
    using (var zip = ZipFile.Open(traversalZip, ZipArchiveMode.Create))
    using (var data = zip.CreateEntry("../outside.txt").Open())
        data.WriteByte(42);
    using (var job = new LunaJobController())
        await ExpectFailure(() => backend.ExtractAsync(traversalZip, new ExtractOptions
        { DestinationDirectory = Path.Combine(root, "traversal-target") }, job), "Malicious ZIP path is rejected during extraction");
    Check(!File.Exists(Path.Combine(root, "outside.txt")), "Malicious ZIP writes no file outside its root");

    string badLarc = Path.Combine(root, "bad-header.larc");
    File.WriteAllBytes(badLarc, "LUNAARC\u0001"u8.ToArray().Concat(new byte[] { 0, 255, 255, 255, 127 }).ToArray());
    await ExpectFailure(() => backend.ReadCatalogAsync(badLarc), "Oversized .larc header is rejected before allocation");
    await ExpectFailure(() => ArchivePasswordDetection.RequiresPasswordAsync(badLarc), "Damaged LARC header is an error, not an encryption prompt");

    if (OperatingSystem.IsLinux())
    {
        string linkRoot = Path.Combine(root, "links");
        Directory.CreateDirectory(linkRoot);
        Directory.CreateSymbolicLink(Path.Combine(linkRoot, "escape"), source);
        await ExpectFailure(() => Task.FromResult(ZipSlipGuard.ValidateAndResolvePath(linkRoot, "escape/hello.txt")), "Existing symlink escape is rejected");
    }
    Console.WriteLine($"PASS: {checks} archive and filesystem checks on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}.");
    if (OperatingSystem.IsWindows()) WindowsShellChecks.Verify(root);
}
finally
{
    Directory.Delete(root, recursive: true);
}

void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + description);
    checks++;
}

async Task ExpectFailure(Func<Task> action, string description)
{
    try { await action(); }
    catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or NotSupportedException or OperationCanceledException)
    { checks++; return; }
    throw new InvalidOperationException("FAIL: " + description);
}
