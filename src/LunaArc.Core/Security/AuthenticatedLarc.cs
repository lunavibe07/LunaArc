using System.Buffers.Binary;
using System.Security.Cryptography;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Formats;

namespace LunaArc.Core.Security;

/// <summary>LARC v2: authenticated envelope around a plain v1 archive, including its metadata.</summary>
internal static class AuthenticatedLarc
{
    private const int Chunk = 1024 * 1024, Iterations = 600000, HeaderSize = 48;
    private const long MaxSize = 1L << 40;
    public static bool IsFile(string path)
    {
        using var input = File.OpenRead(path); Span<byte> magic = stackalloc byte[8];
        return input.Length >= 8 && input.Read(magic) == 8 && magic.SequenceEqual("LUNAARC\x02"u8);
    }
    private static long ValidateHeader(byte[] header, long fileLength)
    {
        long length = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(40));
        if (!header.AsSpan(0, 8).SequenceEqual("LUNAARC\x02"u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(32)) != Chunk ||
            BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(36)) != Iterations || length < 13 || length > MaxSize ||
            fileLength != HeaderSize + length + 16 * ((length + Chunk - 1) / Chunk + 1))
            throw new InvalidDataException("Invalid LARC v2 envelope or truncated authenticated data.");
        return length;
    }
    public static async Task CheckHeaderAsync(string path, CancellationToken ct)
    {
        using var input = File.OpenRead(path); byte[] header = new byte[HeaderSize];
        await input.ReadExactlyAsync(header, ct); ValidateHeader(header, input.Length);
    }
    public static async Task EncryptAsync(string plainPath, string output, string password, LunaJobController job)
    {
        using var input = File.OpenRead(plainPath);
        if (input.Length < 13 || input.Length > MaxSize) throw new InvalidDataException("LARC v2 supports archives up to 1 TiB.");
        byte[] header = new byte[HeaderSize]; "LUNAARC\x02"u8.CopyTo(header);
        RandomNumberGenerator.Fill(header.AsSpan(8, 24));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(32), Chunk);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(36), Iterations);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(40), input.Length);
        await using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            await destination.WriteAsync(header, job.Token);
            await TransformAsync(input, destination, password, header, input.Length, encrypt: true, job);
        }
        catch { await destination.DisposeAsync(); File.Delete(output); throw; }
    }
    public static async Task<TemporaryArchive?> OpenAsync(string path, string? password, LunaJobController job)
    {
        if (!IsFile(path)) return null;
        using var input = File.OpenRead(path); byte[] header = new byte[HeaderSize];
        await input.ReadExactlyAsync(header, job.Token); long length = ValidateHeader(header, input.Length);
        if (string.IsNullOrEmpty(password)) throw new UnauthorizedAccessException("This LARC v2 archive requires a password.");
        job.Report("Checking free space");
        DiskSpaceGuard.Ensure(new[] { new DiskSpaceRequirement(Path.GetTempPath(), length, "LARC authentication staging") }, job.FreeSpaceProbe);
        var result = new TemporaryArchive(".larc");
        try
        {
            await using (var output = new FileStream(result.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await TransformAsync(input, output, password, header, length, encrypt: false, job);
            if (!LarcNativeHandler.IsLarcFile(result.Path)) throw new InvalidDataException("Invalid authenticated LARC contents.");
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    private static async Task TransformAsync(Stream input, Stream output, string password, byte[] header, long length, bool encrypt, LunaJobController job)
    {
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, header.AsSpan(8, 16), Iterations, HashAlgorithmName.SHA256, 32);
        byte[] plain = new byte[Chunk], cipher = new byte[Chunk], tag = new byte[16], nonce = new byte[12], aad = new byte[HeaderSize + 8];
        header.CopyTo(aad, 0); header.AsSpan(24, 8).CopyTo(nonce);
        try
        {
            using var aes = new AesGcm(key, 16);
            string stage = encrypt ? "Encrypting archive" : "Authenticating archive";
            job.Report(stage, 0, length);
            uint index = 0;
            // A mandatory final empty record authenticates completion, even at an exact chunk boundary.
            for (long remaining = length; ; index++)
            {
                job.WaitIfPaused(); job.Token.ThrowIfCancellationRequested();
                int count = (int)Math.Min(Chunk, remaining);
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), index);
                BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(HeaderSize), index);
                BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(HeaderSize + 4), count);
                if (encrypt)
                {
                    await input.ReadExactlyAsync(plain.AsMemory(0, count), job.Token);
                    aes.Encrypt(nonce, plain.AsSpan(0, count), cipher.AsSpan(0, count), tag, aad);
                    await output.WriteAsync(cipher.AsMemory(0, count), job.Token); await output.WriteAsync(tag, job.Token);
                }
                else
                {
                    await input.ReadExactlyAsync(cipher.AsMemory(0, count), job.Token); await input.ReadExactlyAsync(tag, job.Token);
                    aes.Decrypt(nonce, cipher.AsSpan(0, count), tag, plain.AsSpan(0, count), aad);
                    await output.WriteAsync(plain.AsMemory(0, count), job.Token);
                }
                if (count == 0) break;
                remaining -= count;
                job.Report(stage, length - remaining, length);
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
}
