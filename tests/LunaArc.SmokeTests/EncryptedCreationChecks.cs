using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;

internal static class EncryptedCreationChecks
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        string source = Path.Combine(root, "encrypted-source"); Directory.CreateDirectory(source);
        Directory.CreateDirectory(Path.Combine(source, "empty-directory"));
        File.WriteAllText(Path.Combine(source, "日本語.txt"), "Synthetic encrypted creation fixture");
        File.WriteAllBytes(Path.Combine(source, "empty.bin"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(source, "data.bin"), System.Security.Cryptography.RandomNumberGenerator.GetBytes(8192));
        var backend = new StandardArchiveHandler();
        const string password = "synthetic-日本語-! pass";
        foreach (var format in new[] { OutputArchiveFormat.Zip, OutputArchiveFormat.SevenZip, OutputArchiveFormat.EncryptedTarGz })
        {
            if (format != OutputArchiveFormat.Zip && !SevenZipEncoder.IsAvailable) throw new Exception("Pinned encoder required for encryption checks.");
            string extension = format == OutputArchiveFormat.Zip ? ".zip" : format == OutputArchiveFormat.EncryptedTarGz ? ".tar.gz.7z" : ".7z";
            string archive = Path.Combine(root, "created-encrypted" + extension);
            using var job = new LunaJobController();
            await ArchiveCreationService.CreateAsync(new[] { source }, new CompressOptions
            { OutputArchivePath = archive, Format = format, Password = password, SolidArchive = format == OutputArchiveFormat.SevenZip,
                ArchiveComment = format == OutputArchiveFormat.Zip ? "Synthetic comment" : null }, job, test: true);
            check(await ArchivePasswordDetection.RequiresPasswordAsync(archive), format + " detects encryption before extraction");
            check((await backend.TestArchiveAsync(archive, password, job)).IsHealthy, format + " independently verifies created encrypted payload");
            bool rejected;
            try
            {
                rejected = !(await backend.TestArchiveAsync(archive, "wrong-synthetic-password", job)).IsHealthy;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { rejected = true; }
            check(rejected, format + " rejects wrong password");
            string destination = Path.Combine(root, "encrypted-roundtrip-" + format);
            await backend.ExtractAsync(archive, new ExtractOptions { DestinationDirectory = destination, Password = password }, job);
            check(File.ReadAllText(Path.Combine(destination, "encrypted-source", "日本語.txt")) == File.ReadAllText(Path.Combine(source, "日本語.txt"))
                && File.ReadAllBytes(Path.Combine(destination, "encrypted-source", "data.bin")).SequenceEqual(File.ReadAllBytes(Path.Combine(source, "data.bin")))
                && new FileInfo(Path.Combine(destination, "encrypted-source", "empty.bin")).Length == 0
                && Directory.Exists(Path.Combine(destination, "encrypted-source", "empty-directory")), format + " round-trips Unicode, binary data, empty file and directory");
            if (format == OutputArchiveFormat.Zip)
            {
                byte[] damaged = File.ReadAllBytes(archive);
                for (int i = 0; i + 46 < damaged.Length; i++)
                {
                    if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(damaged.AsSpan(i)) != 0x02014b50
                        || System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(damaged.AsSpan(i + 10)) != 99) continue;
                    int local = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(damaged.AsSpan(i + 42));
                    int compressed = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(damaged.AsSpan(i + 20));
                    int start = local + 30 + System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(damaged.AsSpan(local + 26))
                        + System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(damaged.AsSpan(local + 28));
                    damaged[start + compressed - 1] ^= 1; break;
                }
                string tampered = Path.Combine(root, "created-encrypted-tampered.zip"); File.WriteAllBytes(tampered, damaged);
                bool refused;
                try { refused = !(await backend.TestArchiveAsync(tampered, password, job)).IsHealthy; }
                catch (System.Security.Cryptography.CryptographicException) { refused = true; }
                check(refused, "Created AES ZIP rejects a modified authentication tag");
                string protectedDirectory = Path.Combine(root, "encrypted-failed-target", "encrypted-source"); Directory.CreateDirectory(protectedDirectory);
                string protectedFile = Path.Combine(protectedDirectory, "日本語.txt"); File.WriteAllText(protectedFile, "Existing destination fixture");
                bool extractionRefused = false;
                try { await backend.ExtractAsync(tampered, new ExtractOptions { DestinationDirectory = Path.GetDirectoryName(protectedDirectory)!, Password = password }, job); }
                catch (System.Security.Cryptography.CryptographicException) { extractionRefused = true; }
                check(extractionRefused && File.ReadAllText(protectedFile) == "Existing destination fixture", "Created AES ZIP authentication failure preserves existing destination data");
            }
        }
        foreach (string invalid in new[] { "synthetic\nline", "synthetic\rline", "synthetic\0nul" })
        {
            using var job = new LunaJobController();
            string output = Path.Combine(root, "invalid-password.7z");
            bool rejected = false;
            try { await backend.CompressAsync(new[] { source }, new CompressOptions { OutputArchivePath = output, Format = OutputArchiveFormat.SevenZip, Password = invalid }, job); }
            catch (ArgumentException) { rejected = true; }
            check(rejected && !File.Exists(output), "7z rejects unsupported password control characters before publishing");
        }
    }
}
