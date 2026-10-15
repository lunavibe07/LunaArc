using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;

namespace LunaArc.Core.Formats;

/// <summary>LunaArc split sets: raw numbered parts and a bounded, checksummed manifest.</summary>
public static class SplitArchive
{
    private sealed record Part(string Name, long Size, string Sha256);
    private sealed record Manifest(int Version, string ArchiveName, long Size, List<Part> Parts);
    public static bool IsPart(string path) => Regex.IsMatch(path, @"\.\d{3,5}$", RegexOptions.CultureInvariant);
    public static string FirstPart(string output) => output + ".001";

    public static async Task CreateAsync(string source, string outputBase, long volumeBytes, LunaJobController job)
    {
        if (volumeBytes < 65536) throw new ArgumentOutOfRangeException(nameof(volumeBytes), "Volumes must be at least 64 KiB.");
        outputBase = Path.GetFullPath(outputBase);
        string directory = Path.GetDirectoryName(outputBase)!, name = Path.GetFileName(outputBase);
        if (!new[] { ".larc", ".zip", ".7z", ".gz" }.Contains(Path.GetExtension(name).ToLowerInvariant()))
            throw new ArgumentException("Split sets require a .larc, .zip, .7z or .tar.gz output name.");
        string manifestPath = outputBase + ".lunasplit.json";
        using var input = File.OpenRead(source);
        long sourceLength = input.Length;
        long count = sourceLength / volumeBytes + (sourceLength % volumeBytes == 0 ? 0 : 1);
        if (count is < 1 or > 10000) throw new ArgumentException("A split set must have 1 to 10,000 parts.");
        Directory.CreateDirectory(directory);
        foreach (string path in Enumerable.Range(1, (int)count).Select(i => outputBase + $".{i:D3}").Append(manifestPath))
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException("A split output already exists. Choose a new name.");
        string stage = Directory.CreateDirectory(Path.Combine(directory, ".lunasplit-" + Guid.NewGuid().ToString("N"))).FullName;
        var moved = new List<string>(); bool committed = false;
        try
        {
            var parts = new List<Part>(); byte[] buffer = new byte[65536];
            job.Report("Splitting volumes", 0, sourceLength);
            for (int index = 1; index <= count; index++)
            {
                string partName = name + $".{index:D3}";
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long length = Math.Min(volumeBytes, sourceLength - input.Position), remaining = length;
                await using (var output = new FileStream(Path.Combine(stage, partName), FileMode.CreateNew, FileAccess.Write))
                {
                    while (remaining > 0)
                    {
                        job.WaitIfPaused(); job.Token.ThrowIfCancellationRequested();
                        int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), job.Token);
                        if (read == 0) throw new EndOfStreamException("Source changed while splitting.");
                        hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), job.Token); remaining -= read;
                        job.Report("Splitting volumes", input.Position, sourceLength, partName);
                    }
                }
                parts.Add(new Part(partName, length, Convert.ToHexString(hash.GetHashAndReset())));
            }
            if (input.Length != sourceLength) throw new IOException("Source changed while splitting.");
            var manifest = new Manifest(1, name, sourceLength, parts);
            string stagedManifest = Path.Combine(stage, "manifest.json");
            await File.WriteAllBytesAsync(stagedManifest, JsonSerializer.SerializeToUtf8Bytes(manifest), job.Token);
            foreach (var part in parts)
            {
                job.Token.ThrowIfCancellationRequested(); string destination = Path.Combine(directory, part.Name);
                File.Move(Path.Combine(stage, part.Name), destination, overwrite: false); moved.Add(destination);
            }
            job.Token.ThrowIfCancellationRequested();
            File.Move(stagedManifest, manifestPath, overwrite: false); committed = true;
        }
        finally
        {
            if (!committed) foreach (string path in moved) File.Delete(path);
            Directory.Delete(stage, recursive: true);
        }
    }

    internal static async Task<TemporaryArchive?> OpenAsync(string path, LunaJobController job)
    {
        if (!IsPart(path)) return null;
        string basePath = path[..path.LastIndexOf('.')], manifestPath = basePath + ".lunasplit.json";
        if (!path.Equals(FirstPart(basePath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Open the .001 part of the split set.");
        if (!File.Exists(manifestPath)) throw new InvalidDataException("Missing .lunasplit.json manifest. Keep it beside all numbered parts.");
        using var manifestInput = File.OpenRead(manifestPath);
        if (manifestInput.Length > 4 * 1024 * 1024) throw new InvalidDataException("Split manifest is too large.");
        byte[] metadata = new byte[(int)manifestInput.Length]; await manifestInput.ReadExactlyAsync(metadata, job.Token);
        Manifest manifest = JsonSerializer.Deserialize<Manifest>(metadata)
            ?? throw new InvalidDataException("Invalid split manifest.");
        if (manifest.Version != 1 || manifest.ArchiveName != Path.GetFileName(basePath) || manifest.Size <= 0 || manifest.Parts is not { Count: > 0 and <= 10000 })
            throw new InvalidDataException("Invalid split manifest metadata.");
        string extension = manifest.ArchiveName.EndsWith(".tar.gz.7z", StringComparison.OrdinalIgnoreCase) ? ".tar.gz.7z" : manifest.ArchiveName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? ".tar.gz" : Path.GetExtension(basePath);
        if (!new[] { ".larc", ".zip", ".7z", ".gz" , ".tar.gz", ".tar.gz.7z" }.Contains(extension.ToLowerInvariant()))
            throw new InvalidDataException("Unsupported split archive extension.");
        long total = 0;
        for (int i = 0; i < manifest.Parts.Count; i++)
        {
            var part = manifest.Parts[i];
            if (part == null || part.Name != manifest.ArchiveName + $".{i + 1:D3}" || part.Size <= 0 || part.Size > manifest.Size - total ||
                part.Sha256 == null || !Regex.IsMatch(part.Sha256, "^[0-9a-fA-F]{64}$")) throw new InvalidDataException("Invalid split part metadata.");
            total += part.Size;
        }
        if (total != manifest.Size) throw new InvalidDataException("Split set length mismatch.");
        job.Report("Checking free space");
        DiskSpaceGuard.Ensure(new[] { new DiskSpaceRequirement(Path.GetTempPath(), total, "split-set assembly") }, job.FreeSpaceProbe);
        job.Report("Assembling parts", 0, total);
        var result = new TemporaryArchive(extension);
        try
        {
            await using var output = new FileStream(result.Path, FileMode.CreateNew, FileAccess.Write);
            byte[] buffer = new byte[65536]; string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            foreach (var part in manifest.Parts)
            {
                string partPath = ZipSlipGuard.ValidateAndResolvePath(directory, part.Name);
                using var input = File.OpenRead(partPath);
                if (input.Length != part.Size) throw new InvalidDataException("Missing or truncated split part: " + part.Name);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long remaining = part.Size;
                while (remaining > 0)
                {
                    job.WaitIfPaused(); job.Token.ThrowIfCancellationRequested();
                    int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), job.Token);
                    if (read == 0) throw new EndOfStreamException("Truncated split part: " + part.Name);
                    hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), job.Token);
                    remaining -= read;
                    job.Report("Assembling parts", output.Position, total, part.Name);
                }
                if (input.Length != part.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(part.Sha256)))
                    throw new InvalidDataException("Split part checksum mismatch: " + part.Name);
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
