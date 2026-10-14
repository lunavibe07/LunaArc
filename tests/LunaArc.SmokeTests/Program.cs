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

    foreach (var (format, extension, password, level) in new[]
    {
        (OutputArchiveFormat.LarcNative, "larc", (string?)null, LunaCompressionLevel.Normal),
        (OutputArchiveFormat.LarcNative, "larc", "synthetic-test-password", LunaCompressionLevel.Normal),
        (OutputArchiveFormat.LarcNative, "larc", (string?)null, LunaCompressionLevel.Store),
        (OutputArchiveFormat.Zip, "zip", (string?)null, LunaCompressionLevel.Normal),
        (OutputArchiveFormat.TarGz, "tar.gz", (string?)null, LunaCompressionLevel.Normal)
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
            await ExpectFailure(() => backend.ReadCatalogAsync(archive, "wrong-password"), "Wrong password is rejected");
            Check(!(await backend.TestArchiveAsync(archive, "wrong-password", job)).IsHealthy, "Wrong password fails archive test");
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
