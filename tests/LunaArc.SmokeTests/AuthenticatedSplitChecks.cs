using System.Security.Cryptography;
using System.Text.Json.Nodes;
using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;

internal static class AuthenticatedSplitChecks
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        string directory = Path.Combine(root, "authenticated-split"); Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "private-日本語.bin"); byte[] data = RandomNumberGenerator.GetBytes(2 * 1024 * 1024 + 71);
        File.WriteAllBytes(source, data);
        var backend = new StandardArchiveHandler(); using var job = new LunaJobController();
        const string password = "synthetic-authenticated-password";
        string archive = Path.Combine(directory, "authenticated.larc");
        await ArchiveCreationService.CreateAsync(new[] { source }, new CompressOptions { OutputArchivePath = archive,
            Password = password, Level = LunaCompressionLevel.Store }, job, test: true);
        byte[] encoded = File.ReadAllBytes(archive);
        check(encoded.AsSpan(0, 8).SequenceEqual("LUNAARC\x02"u8), "Password-protected LARC creation writes the v2 magic");
        check(await ArchivePasswordDetection.RequiresPasswordAsync(archive), "LARC v2 password detection validates the envelope without decrypting it");
        var catalog = await backend.ReadCatalogAsync(archive, password);
        check(catalog.GetNode(0).IsEncrypted && catalog.GetFullPath(0) == Path.GetFileName(source), "LARC v2 catalog preserves encrypted entry metadata");
        string output = Path.Combine(directory, "out");
        await backend.ExtractAsync(archive, new ExtractOptions { DestinationDirectory = output, Password = password }, job);
        string destination = Path.Combine(output, Path.GetFileName(source));
        check(File.ReadAllBytes(destination).SequenceEqual(data), "Multi-chunk authenticated LARC round trip");
        File.WriteAllText(destination, "existing destination");
        foreach (string mutation in new[] { "header", "cipher", "final-tag", "truncate", "append", "reorder" })
        {
            byte[] bytes = encoded.ToArray();
            switch (mutation)
            {
                case "header": bytes[24] ^= 1; break;
                case "cipher": bytes[48 + 100] ^= 1; break;
                case "final-tag": bytes[^1] ^= 1; break;
                case "truncate": Array.Resize(ref bytes, bytes.Length - 16); break;
                case "append": Array.Resize(ref bytes, bytes.Length + 1); break;
                case "reorder":
                    const int record = 1024 * 1024 + 16;
                    encoded.AsSpan(48 + record, record).CopyTo(bytes.AsSpan(48));
                    encoded.AsSpan(48, record).CopyTo(bytes.AsSpan(48 + record)); break;
            }
            string bad = Path.Combine(directory, mutation + ".larc"); File.WriteAllBytes(bad, bytes);
            check(await Rejected(() => backend.ExtractAsync(bad, new ExtractOptions { DestinationDirectory = output, Password = password }, job)), "LARC v2 rejects " + mutation);
            check(File.ReadAllText(destination) == "existing destination", "Authentication failure preserves the complete existing destination");
        }
        check(await Rejected(() => backend.ReadCatalogAsync(archive, "incorrect")), "LARC v2 rejects wrong passwords before exposing metadata");
        check(await Rejected(() => backend.ReadCatalogAsync(archive)), "LARC v2 requires a password");
        using (var cancelled = new LunaJobController())
        {
            cancelled.Cancel();
            check(await Rejected(() => backend.ReadCatalogAsync(archive, password, cancelled.Token)), "LARC v2 honors cancellation");
        }
        foreach (var format in new[] { OutputArchiveFormat.LarcNative, OutputArchiveFormat.Zip, OutputArchiveFormat.SevenZip, OutputArchiveFormat.TarGz, OutputArchiveFormat.EncryptedTarGz })
        {
            string ext = format switch { OutputArchiveFormat.Zip => ".zip", OutputArchiveFormat.SevenZip => ".7z",
                OutputArchiveFormat.TarGz => ".tar.gz", OutputArchiveFormat.EncryptedTarGz => ".tar.gz.7z", _ => ".larc" };
            string splitBase = Path.Combine(directory, "split-" + format + ext), first = SplitArchive.FirstPart(splitBase);
            string? key = format == OutputArchiveFormat.TarGz ? null : password;
            await ArchiveCreationService.CreateAsync(new[] { source }, new CompressOptions { OutputArchivePath = splitBase, Format = format,
                Password = key, SplitVolumeSizeBytes = 1024 * 1024 }, job);
            check(File.Exists(first) && File.Exists(splitBase + ".lunasplit.json") && !File.Exists(splitBase), format + " publishes a numbered set and manifest");
            check(await ArchivePasswordDetection.RequiresPasswordAsync(first) == (key != null), format + " split password detection");
            check((await backend.TestArchiveAsync(first, key, job)).IsHealthy, format + " split set verifies after automatic assembly");
            string unpack = splitBase + "-out";
            await backend.ExtractAsync(first, new ExtractOptions { DestinationDirectory = unpack, Password = key }, job);
            check(File.ReadAllBytes(Path.Combine(unpack, Path.GetFileName(source))).SequenceEqual(data), format + " split round trip content matches");
            check(await Rejected(() => ArchiveCreationService.CreateAsync(new[] { source }, new CompressOptions { OutputArchivePath = splitBase,
                SplitVolumeSizeBytes = 1024 * 1024 }, job)), "Existing split set is never overwritten");
            string second = splitBase + ".002"; byte[] saved = File.ReadAllBytes(second);
            File.Delete(second);
            check(await Rejected(() => backend.ReadCatalogAsync(first, key)), "Missing split part is rejected before archive parsing");
            File.WriteAllBytes(second, saved); saved[1] ^= 1; File.WriteAllBytes(second, saved);
            check(await Rejected(() => backend.ReadCatalogAsync(first, key)), "Altered split part fails SHA-256 validation");
        }
        string manifestPath = Path.Combine(directory, "split-LarcNative.larc.lunasplit.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["Parts"]![0]!["Name"] = "../private-日本語.bin"; File.WriteAllText(manifestPath, manifest.ToJsonString());
        check(await Rejected(() => backend.ReadCatalogAsync(Path.Combine(directory, "split-LarcNative.larc.001"), password)), "Split manifest rejects path traversal");
        check(LunaArc.Shell.ExplorerRequest.GetFolderName("sample.tar.gz.001") == "sample", "Explorer split folder names omit both part and archive extensions");
        static async Task<bool> Rejected(Func<Task> action)
        {
            try { await action(); return false; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or ArgumentException or OperationCanceledException) { return true; }
        }
    }
}
