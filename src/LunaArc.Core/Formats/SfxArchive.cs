using System.Buffers.Binary;
using System.Security.Cryptography;
using LunaArc.Core.Contracts;

namespace LunaArc.Core.Formats;

/// <summary>Versioned executable overlay. Payload hash detects corruption, not publisher authenticity.</summary>
public static class SfxArchive
{
    private const int FooterSize = 64;
    public static string GuiStubPath => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "LunaArc.exe" : "LunaArc");
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty entry assembly Location intentionally detects the published single-file extractor, not a filesystem path.")]
    public static bool IsBundledProcess => System.Reflection.Assembly.GetEntryAssembly()?.Location.Length == 0;
    public static bool CanCreate => IsBundledProcess && File.Exists(GuiStubPath);

    public static bool IsSfx(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length < FooterSize) return false;
        input.Seek(-FooterSize, SeekOrigin.End);
        Span<byte> magic = stackalloc byte[8];
        input.ReadExactly(magic);
        return magic.SequenceEqual("LUNASFX1"u8);
    }

    public static async Task CreateAsync(string stub, string archive, string output, OutputArchiveFormat format, CancellationToken ct = default)
    {
        LunaArc.Core.Security.ArchiveLock.EnsureWritable(output);
        if (format is not (OutputArchiveFormat.LarcNative or OutputArchiveFormat.Zip or OutputArchiveFormat.TarGz or OutputArchiveFormat.SevenZip or OutputArchiveFormat.EncryptedTarGz))
            throw new NotSupportedException("Unsupported SFX payload format.");
        if (IsSfx(stub)) throw new InvalidDataException("An SFX cannot be used as an extractor stub.");
        bool created = false;
        try
        {
            await using var target = new FileStream(output, FileMode.CreateNew, FileAccess.ReadWrite);
            created = true;
            await using (var executable = File.OpenRead(stub)) await executable.CopyToAsync(target, ct);
            long offset = target.Position;
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var payload = File.OpenRead(archive))
            {
                byte[] buffer = new byte[65536];
                int read;
                while ((read = await payload.ReadAsync(buffer, ct)) > 0)
                {
                    digest.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            long length = target.Position - offset;
            await target.FlushAsync(ct);
            byte[] hash = digest.GetHashAndReset();
            byte[] footer = new byte[FooterSize];
            "LUNASFX1"u8.CopyTo(footer);
            BinaryPrimitives.WriteInt64LittleEndian(footer.AsSpan(8), offset);
            BinaryPrimitives.WriteInt64LittleEndian(footer.AsSpan(16), length);
            BinaryPrimitives.WriteInt32LittleEndian(footer.AsSpan(24), (int)format);
            hash.CopyTo(footer, 32);
            await target.WriteAsync(footer, ct);
        }
        catch { if (created) File.Delete(output); throw; }
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static async Task<Payload?> OpenPayloadAsync(string path, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        if (input.Length < FooterSize) return null;
        input.Seek(-FooterSize, SeekOrigin.End);
        byte[] footer = new byte[FooterSize];
        await input.ReadExactlyAsync(footer, ct);
        if (!footer.AsSpan(0, 8).SequenceEqual("LUNASFX1"u8)) return null;
        long offset = BinaryPrimitives.ReadInt64LittleEndian(footer.AsSpan(8));
        long length = BinaryPrimitives.ReadInt64LittleEndian(footer.AsSpan(16));
        string extension = (OutputArchiveFormat)BinaryPrimitives.ReadInt32LittleEndian(footer.AsSpan(24)) switch
        {
            OutputArchiveFormat.LarcNative => ".larc", OutputArchiveFormat.Zip => ".zip",
            OutputArchiveFormat.TarGz => ".tar.gz", OutputArchiveFormat.SevenZip or OutputArchiveFormat.EncryptedTarGz => ".7z",
            _ => throw new InvalidDataException("Unsupported SFX payload format.")
        };
        if (offset <= 0 || length <= 0 || offset > input.Length - FooterSize || length != input.Length - FooterSize - offset ||
            BinaryPrimitives.ReadInt32LittleEndian(footer.AsSpan(28)) != 0)
            throw new InvalidDataException("Invalid SFX payload bounds.");
        string directory = Directory.CreateTempSubdirectory("LunaArc_SFX_").FullName;
        var result = new Payload(directory, Path.Combine(directory, "payload" + extension));
        try
        {
            input.Position = offset;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var output = new FileStream(result.Path, FileMode.CreateNew, FileAccess.Write);
            byte[] buffer = new byte[65536];
            for (long remaining = length; remaining > 0;)
            {
                int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                if (read == 0) throw new EndOfStreamException();
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                remaining -= read;
            }
            if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), footer.AsSpan(32, 32)))
                throw new InvalidDataException("The SFX payload is damaged.");
            await output.DisposeAsync();
            if (IsSfx(result.Path)) throw new InvalidDataException("Nested SFX payloads are not supported.");
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public sealed class Payload(string directory, string path) : IDisposable
    {
        public string Path { get; } = path;
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
