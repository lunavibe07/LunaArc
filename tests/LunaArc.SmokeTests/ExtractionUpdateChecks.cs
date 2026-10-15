using System.IO.Compression;
using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.Pipeline;

internal static class ExtractionUpdateChecks
{
    public static async Task RunAsync(string root, StandardArchiveHandler backend, Action<bool, string> check)
    {
        DateTime archiveTime = new(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        string source = Path.Combine(root, "update-source");
        Directory.CreateDirectory(Path.Combine(source, "missing"));
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        foreach (string name in new[] { "older.txt", "equal.txt", "newer.txt", "missing/added.txt" })
        {
            string path = Path.Combine(source, name);
            File.WriteAllText(path, "Archive copy of " + name);
            File.SetLastWriteTimeUtc(path, archiveTime);
        }
        using var job = new LunaJobController();
        foreach (var (format, extension) in new[]
        {
            (OutputArchiveFormat.LarcNative, "larc"), (OutputArchiveFormat.Zip, "zip"), (OutputArchiveFormat.TarGz, "tar.gz")
        })
        {
            string archive = Path.Combine(root, "updates." + extension);
            await backend.CompressAsync(new[] { source }, new CompressOptions { OutputArchivePath = archive, Format = format }, job);
            await VerifyAsync(archive, extension);
            if (format == OutputArchiveFormat.TarGz)
            {
                string tar = Path.Combine(root, "updates.tar");
                using (var input = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress))
                using (var output = File.Create(tar)) await input.CopyToAsync(output);
                await VerifyAsync(tar, "tar");
            }
        }

        string unknownArchive = Path.Combine(root, "unknown-update-time.larc");
        byte[] unknownPayload = "Unknown-time archive copy"u8.ToArray();
        byte[] unknownHeader = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            Entries = new[] { new { Path = "unknown.txt", IsDir = false, OrigSize = unknownPayload.Length,
                CompSize = unknownPayload.Length, DataOffset = 0, Crc32 = LunaArc.Core.Security.Crc32Verifier.Compute(unknownPayload), LastModifiedUtcTicks = 0L } }
        });
        using (var compressor = new ZstdSharp.Compressor(1)) unknownHeader = compressor.Wrap(unknownHeader).ToArray();
        using (var output = new BinaryWriter(File.Create(unknownArchive)))
        {
            output.Write(LarcNativeHandler.MagicBytes.ToArray()); output.Write((byte)2);
            output.Write(unknownHeader.Length); output.Write(unknownHeader); output.Write(unknownPayload);
        }
        foreach (var mode in new[] { ExtractionUpdateMode.ExtractAndUpdate, ExtractionUpdateMode.FreshenExisting })
        {
            string destination = Path.Combine(root, "unknown-time-" + mode);
            Directory.CreateDirectory(destination);
            string target = Path.Combine(destination, "unknown.txt");
            File.WriteAllText(target, "Existing unknown-time copy");
            File.SetLastWriteTimeUtc(target, archiveTime.AddYears(-10));
            await backend.ExtractAsync(unknownArchive, new ExtractOptions { DestinationDirectory = destination, UpdateMode = mode }, job);
            check(File.ReadAllText(target) == "Existing unknown-time copy", $"{mode}: unknown archive timestamp preserves the destination");
        }

        async Task VerifyAsync(string archive, string format)
        {
            foreach (var mode in Enum.GetValues<ExtractionUpdateMode>())
            {
                string destination = Path.Combine(root, $"update-{format}-{mode}");
                string files = Seed(destination);
                int prompts = 0;
                await backend.ExtractAsync(archive, new ExtractOptions
                {
                    DestinationDirectory = destination, UpdateMode = mode, OverwriteMode = OverwriteMode.Ask,
                    OnFileConflictCallback = _ => { prompts++; return OverwriteMode.OverwriteAll; }
                }, job);
                check(File.ReadAllText(Path.Combine(files, "older.txt")) == "Archive copy of older.txt", $"{format}/{mode}: replaces an older file");
                bool all = mode == ExtractionUpdateMode.ExtractAndReplace;
                check(File.ReadAllText(Path.Combine(files, "equal.txt")) == (all ? "Archive copy of equal.txt" : "Existing equal.txt"), $"{format}/{mode}: equal timestamp policy");
                check(File.ReadAllText(Path.Combine(files, "newer.txt")) == (all ? "Archive copy of newer.txt" : "Existing newer.txt"), $"{format}/{mode}: newer destination policy");
                check(prompts == (all ? 3 : 1), $"{format}/{mode}: only eligible existing files trigger conflict prompts");
                check(File.Exists(Path.Combine(files, "missing/added.txt")) == (mode != ExtractionUpdateMode.FreshenExisting), $"{format}/{mode}: missing file policy");
                check(File.GetLastWriteTimeUtc(Path.Combine(files, "older.txt")) == archiveTime, $"{format}/{mode}: preserves source modification time");
                if (mode == ExtractionUpdateMode.FreshenExisting)
                    check(!Directory.Exists(Path.Combine(files, "missing")) && !Directory.Exists(Path.Combine(files, "empty")), $"{format}: freshen creates no missing directories");
            }
            string skipDestination = Path.Combine(root, "update-skip-" + format);
            string skipFiles = Seed(skipDestination);
            await backend.ExtractAsync(archive, new ExtractOptions
            { DestinationDirectory = skipDestination, UpdateMode = ExtractionUpdateMode.ExtractAndUpdate, OverwriteMode = OverwriteMode.SkipExisting }, job);
            check(File.ReadAllText(Path.Combine(skipFiles, "older.txt")) == "Existing older.txt" && File.Exists(Path.Combine(skipFiles, "missing/added.txt")), $"{format}: skip remains independent of update filtering");

            string renameDestination = Path.Combine(root, "update-rename-" + format);
            string renameFiles = Seed(renameDestination);
            await backend.ExtractAsync(archive, new ExtractOptions
            { DestinationDirectory = renameDestination, UpdateMode = ExtractionUpdateMode.FreshenExisting, OverwriteMode = OverwriteMode.AutoRename }, job);
            check(File.ReadAllText(Path.Combine(renameFiles, "older.txt")) == "Existing older.txt" && File.Exists(Path.Combine(renameFiles, "older (1).txt"))
                && !File.Exists(Path.Combine(renameFiles, "newer (1).txt")), $"{format}: freshen filters before renaming eligible conflicts");

            string absent = Path.Combine(root, "update-absent-" + format);
            await backend.ExtractAsync(archive, new ExtractOptions { DestinationDirectory = absent, UpdateMode = ExtractionUpdateMode.FreshenExisting }, job);
            check(!Directory.Exists(absent), $"{format}: freshen leaves a nonexistent destination untouched");
        }

        string Seed(string destination)
        {
            string files = Path.Combine(destination, "update-source");
            Directory.CreateDirectory(files);
            foreach (var (name, time) in new[] { ("older.txt", archiveTime.AddDays(-1)), ("equal.txt", archiveTime), ("newer.txt", archiveTime.AddDays(1)) })
            {
                string path = Path.Combine(files, name);
                File.WriteAllText(path, "Existing " + name);
                File.SetLastWriteTimeUtc(path, time);
            }
            return files;
        }
    }
}
