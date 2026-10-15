using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip;
using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;
using ZstdSharp;

internal static class LargeArchiveChecks
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var backend = new StandardArchiveHandler();
        const string password = "synthetic-large-archive-password";
        byte[] data = RandomNumberGenerator.GetBytes(8192);
        string source = Path.Combine(root, "stream-source.bin"); File.WriteAllBytes(source, data);
        foreach (bool encrypted in new[] { false, true })
        foreach (bool store in new[] { false, true })
        {
            string archive = Path.Combine(root, $"stream-{store}-{encrypted}.larc");
            using var job = new LunaJobController();
            await new LarcNativeHandler().CreateLarcArchiveAsync(new[] { source }, new CompressOptions { OutputArchivePath = archive,
                Password = encrypted ? password : null, Level = store ? LunaCompressionLevel.Store : LunaCompressionLevel.Normal }, job);
            // Original v1 byte-array decoding independently validates new stream framing and CBC layout.
            using var input = File.OpenRead(archive); input.Position = 9;
            byte[] length = new byte[4]; input.ReadExactly(length);
            byte[] header = new byte[BitConverter.ToInt32(length)]; input.ReadExactly(header);
            if (encrypted) header = LunaAes256Cipher.DecryptBytes(header, password);
            using var decoder = new Decompressor();
            using var json = JsonDocument.Parse(decoder.Unwrap(header).ToArray());
            var entry = json.RootElement.GetProperty("Entries")[0];
            byte[] payload = new byte[entry.GetProperty("CompSize").GetInt32()]; input.ReadExactly(payload);
            if (encrypted) payload = LunaAes256Cipher.DecryptBytes(payload, password);
            if (!store) payload = decoder.Unwrap(payload).ToArray();
            check(payload.SequenceEqual(data), $"Streaming LARC retains original v1 decoding: store={store}, encrypted={encrypted}");
            string output = archive + "-out";
            await backend.ExtractAsync(archive, new ExtractOptions { DestinationDirectory = output, Password = encrypted ? password : null }, job);
            check(File.ReadAllBytes(Path.Combine(output, Path.GetFileName(source))).SequenceEqual(data), "Streaming LARC round trip");
        }

        string zip = Path.Combine(root, "forced-zip64.zip");
        using (var writer = new ZipOutputStream(File.Create(zip)))
        {
            writer.UseZip64 = UseZip64.On; writer.Password = password;
            writer.PutNextEntry(new ZipEntry("payload.bin") { AESKeySize = 256, Size = data.Length });
            writer.Write(data); writer.CloseEntry(); writer.Finish();
        }
        // Force ZIP64 EOCD even for a small fixture, without allocating multi-GB test data.
        byte[] bytes = File.ReadAllBytes(zip);
        int end = bytes.Length - 22;
        long directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 16));
        long directorySize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 12));
        using (var output = new MemoryStream())
        using (var writer = new BinaryWriter(output))
        {
            writer.Write(bytes, 0, end);
            writer.Write(0x06064b50u); writer.Write(44L); writer.Write((ushort)45); writer.Write((ushort)45);
            writer.Write(0u); writer.Write(0u); writer.Write(1L); writer.Write(1L); writer.Write(directorySize); writer.Write(directoryOffset);
            writer.Write(0x07064b50u); writer.Write(0u); writer.Write((long)end); writer.Write(1u);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(end + 8), ushort.MaxValue);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(end + 10), ushort.MaxValue);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(end + 12), uint.MaxValue);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(end + 16), uint.MaxValue);
            writer.Write(bytes, end, 22); File.WriteAllBytes(zip, output.ToArray());
        }
        using (var job = new LunaJobController())
        {
            check((await backend.TestArchiveAsync(zip, password, job)).IsHealthy, "AES ZIP64 EOCD and entry sizes pass independent authentication");
            check(await Rejects(zip, "wrong-password"), "AES ZIP64 rejects wrong passwords");
            string output = zip + "-out";
            await backend.ExtractAsync(zip, new ExtractOptions { DestinationDirectory = output, Password = password }, job);
            check(File.ReadAllBytes(Path.Combine(output, "payload.bin")).SequenceEqual(data), "AES ZIP64 extracts verified contents");
            byte[] tampered = File.ReadAllBytes(zip);
            using (var reader = new ZipFile(zip))
            {
                int payload = 30 + BinaryPrimitives.ReadUInt16LittleEndian(tampered.AsSpan(26)) + BinaryPrimitives.ReadUInt16LittleEndian(tampered.AsSpan(28));
                tampered[payload + checked((int)reader[0].CompressedSize) - 1] ^= 1;
            }
            string modified = zip + ".tampered.zip"; File.WriteAllBytes(modified, tampered);
            check(await Rejects(modified, password), "ZIP64 AES authentication rejects a modified tag");
            File.WriteAllText(Path.Combine(output, "payload.bin"), "existing destination");
            bool refused = false;
            try { await backend.ExtractAsync(modified, new ExtractOptions { DestinationDirectory = output, Password = password }, job); }
            catch (CryptographicException) { refused = true; }
            check(refused && File.ReadAllText(Path.Combine(output, "payload.bin")) == "existing destination", "ZIP64 authentication failure preserves existing destination");
            bytes = File.ReadAllBytes(zip); bytes[bytes.Length - 22 - 20 + 8] ^= 0x80;
            string bad = zip + ".bad.zip"; File.WriteAllBytes(bad, bytes);
            check(await Rejects(bad, password), "ZIP64 rejects an invalid locator offset");
        }
        await CheckStreamingMemory(root, backend, password, check);
        string many = Path.Combine(root, "many-entries-zip64.zip");
        using (var writer = new ZipOutputStream(File.Create(many)))
        {
            writer.UseZip64 = UseZip64.Dynamic;
            for (int index = 0; index < ushort.MaxValue; index++)
            { writer.PutNextEntry(new ZipEntry($"folder-{index}/") { Size = 0 }); writer.CloseEntry(); }
            writer.Password = password; writer.PutNextEntry(new ZipEntry("payload.bin") { AESKeySize = 256, Size = data.Length });
            writer.Write(data); writer.CloseEntry(); writer.Finish();
        }
        using (var job = new LunaJobController())
            check((await backend.TestArchiveAsync(many, password, job)).IsHealthy, "Encrypted ZIP64 verifies an archive containing 65,536 entries");
        if (Environment.GetEnvironmentVariable("LUNAARC_LARGE_CHECKS") == "1")
        {
            string large = Path.Combine(root, "over-4GiB.bin"), largeZip = large + ".zip";
            using (var output = File.Create(large)) output.SetLength((long)uint.MaxValue + 18);
            using var job = new LunaJobController();
            await backend.CompressAsync(new[] { large }, new CompressOptions { OutputArchivePath = largeZip, Format = OutputArchiveFormat.Zip,
                Password = password, Level = LunaCompressionLevel.Fastest }, job);
            check((await backend.ReadCatalogAsync(largeZip, password)).TotalUncompressedBytes == (long)uint.MaxValue + 18,
                "Created AES ZIP64 retains the size of an actual file larger than 4 GiB");
            check((await backend.TestArchiveAsync(largeZip, password, job)).IsHealthy, "Actual over-4-GiB AES ZIP64 independently decodes and authenticates");
            Console.WriteLine("PASS: actual over-4-GiB encrypted ZIP64 creation and independent verification.");
        }
        async Task<bool> Rejects(string path, string key)
        {
            using var verifier = new LunaJobController();
            try { return !(await backend.TestArchiveAsync(path, key, verifier)).IsHealthy; }
            catch (Exception ex) when (ex is not OperationCanceledException) { return true; }
        }
    }

    private static async Task CheckStreamingMemory(string root, StandardArchiveHandler backend, string password, Action<bool, string> check)
    {
        string source = Path.Combine(root, "128MiB-stream.bin");
        using (var output = File.Create(source)) output.SetLength(128L * 1024 * 1024);
        string archive = source + ".larc";
        using var job = new LunaJobController();
        long allocated = GC.GetTotalAllocatedBytes(true);
        await backend.CompressAsync(new[] { source }, new CompressOptions { OutputArchivePath = archive, Password = password }, job);
        long creationAllocation = GC.GetTotalAllocatedBytes(true) - allocated;
        check(creationAllocation < 96L * 1024 * 1024, $"128 MiB encrypted LARC creation allocation stays below 96 MiB ({creationAllocation / 1048576} MiB observed)");
        allocated = GC.GetTotalAllocatedBytes(true);
        string destination = source + "-out";
        await backend.ExtractAsync(archive, new ExtractOptions { DestinationDirectory = destination, Password = password }, job);
        long extractionAllocation = GC.GetTotalAllocatedBytes(true) - allocated;
        check(extractionAllocation < 96L * 1024 * 1024, $"128 MiB encrypted LARC extraction allocation stays below 96 MiB ({extractionAllocation / 1048576} MiB observed)");
        using var original = File.OpenRead(source); using var restored = File.OpenRead(Path.Combine(destination, Path.GetFileName(source)));
        check(SHA256.HashData(original).SequenceEqual(SHA256.HashData(restored)), "Large streaming LARC content hashes match");
        // Cancellation during an entry must preserve an existing destination.
        using var cancelled = new LunaJobController();
        bool stopped = false;
        try
        {
            await backend.CompressAsync(new[] { source }, new CompressOptions { OutputArchivePath = source + "-cancel.larc" }, cancelled,
                (_, _, _) => cancelled.Cancel());
        }
        catch (OperationCanceledException) { stopped = true; }
        check(stopped, "Large LARC creation can cancel between chunks");
        Console.WriteLine($"LARC 128 MiB allocation: create={creationAllocation / 1048576} MiB, extract={extractionAllocation / 1048576} MiB.");
    }
}
