using System.Security.Cryptography;
using LunaArc.Core.Pipeline;

namespace LunaArc.Core.Security;

/// <summary>LunaArc recovery v1: SHA-256 block map and one XOR parity block per 16 data blocks.</summary>
public static class ArchiveRecoveryRecord
{
    private const int BlockSize = 1024 * 1024, GroupSize = 16, HeaderSize = 60, MaxBlocks = 1_000_000;
    public static string RecordPath(string archive) => archive + ".lrec";

    public static async Task CreateAsync(string archive, string record, LunaJobController job)
    {
        bool created = false;
        try
        {
            await using var input = File.OpenRead(archive);
            long length = input.Length;
            int count = GetCount(length);
            byte[] hashes = new byte[count * 32];
            await using var output = new FileStream(record, FileMode.CreateNew, FileAccess.ReadWrite);
            created = true;
            output.Position = HeaderSize + hashes.Length;
            using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] block = new byte[BlockSize], parity = new byte[BlockSize];
            for (int i = 0; i < count; i++)
            {
                job.WaitIfPaused(); job.Token.ThrowIfCancellationRequested();
                if (i % GroupSize == 0) Array.Clear(parity);
                Array.Clear(block);
                int size = (int)Math.Min(BlockSize, length - (long)i * BlockSize);
                await input.ReadExactlyAsync(block.AsMemory(0, size), job.Token);
                whole.AppendData(block, 0, size);
                SHA256.HashData(block.AsSpan(0, size)).CopyTo(hashes, i * 32);
                Xor(parity, block);
                if (i % GroupSize == GroupSize - 1 || i == count - 1) await output.WriteAsync(parity, job.Token);
            }
            output.Position = 0;
            using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write("LUNAREC1"u8); writer.Write(length); writer.Write(BlockSize); writer.Write(GroupSize); writer.Write(count);
                writer.Write(whole.GetHashAndReset()); writer.Write(hashes);
            }
            await output.FlushAsync(job.Token);
            output.Position = 0;
            byte[] digest = await SHA256.HashDataAsync(output, job.Token);
            output.Position = output.Length;
            await output.WriteAsync(digest, job.Token);
        }
        catch { if (created) File.Delete(record); throw; }
    }

    public static async Task RepairAsync(string damagedArchive, string record, string repairedArchive, LunaJobController job)
    {
        ArchiveLock.EnsureWritable(repairedArchive);
        if (File.Exists(repairedArchive) || Directory.Exists(repairedArchive)) throw new IOException("Choose a new repaired archive path.");
        string outputPath = Path.GetFullPath(repairedArchive);
        string temp = Path.Combine(Path.GetDirectoryName(outputPath)!, ".lunaarc-repair-" + Guid.NewGuid().ToString("N"));
        bool created = false;
        try
        {
            await using var recovery = File.OpenRead(record);
            using var reader = new BinaryReader(recovery, System.Text.Encoding.UTF8, leaveOpen: true);
            if (recovery.Length < HeaderSize + 32 || !reader.ReadBytes(8).AsSpan().SequenceEqual("LUNAREC1"u8)) throw new InvalidDataException("Invalid recovery record.");
            long length = reader.ReadInt64();
            if (reader.ReadInt32() != BlockSize || reader.ReadInt32() != GroupSize) throw new InvalidDataException("Unsupported recovery geometry.");
            int count = reader.ReadInt32();
            if (count != GetCount(length)) throw new InvalidDataException("Invalid recovery block count.");
            long expected = HeaderSize + (long)count * 32 + ((long)count + GroupSize - 1) / GroupSize * BlockSize + 32;
            if (recovery.Length != expected) throw new InvalidDataException("Truncated or oversized recovery record.");
            byte[] originalHash = reader.ReadBytes(32);
            byte[] hashes = reader.ReadBytes(count * 32);
            long parityOffset = recovery.Position;
            recovery.Position = 0;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[65536];
                for (long remaining = expected - 32; remaining > 0;)
                {
                    job.WaitIfPaused();
                    int read = await recovery.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), job.Token);
                    if (read == 0) throw new EndOfStreamException();
                    hash.AppendData(buffer, 0, read); remaining -= read;
                }
                byte[] stored = new byte[32]; await recovery.ReadExactlyAsync(stored, job.Token);
                if (!CryptographicOperations.FixedTimeEquals(stored, hash.GetHashAndReset())) throw new InvalidDataException("The recovery record is damaged.");
            }
            recovery.Position = parityOffset;
            await using var damaged = File.OpenRead(damagedArchive);
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite))
            {
                created = true;
                byte[][] blocks = Enumerable.Range(0, GroupSize).Select(_ => new byte[BlockSize]).ToArray();
                byte[] parity = new byte[BlockSize];
                for (int group = 0; group < count; group += GroupSize)
                {
                    job.WaitIfPaused(); job.Token.ThrowIfCancellationRequested();
                    await recovery.ReadExactlyAsync(parity, job.Token);
                    int bad = -1, members = Math.Min(GroupSize, count - group);
                    for (int j = 0; j < members; j++)
                    {
                        Array.Clear(blocks[j]);
                        int size = (int)Math.Min(BlockSize, length - (long)(group + j) * BlockSize);
                        long available = Math.Max(0, Math.Min(size, damaged.Length - damaged.Position));
                        await damaged.ReadExactlyAsync(blocks[j].AsMemory(0, (int)available), job.Token);
                        bool healthy = available == size && CryptographicOperations.FixedTimeEquals(SHA256.HashData(blocks[j].AsSpan(0, size)), hashes.AsSpan((group + j) * 32, 32));
                        if (!healthy)
                        {
                            if (bad != -1) throw new InvalidDataException("Damage exceeds recovery capacity: more than one block in a 16-block group. The original was preserved.");
                            bad = j;
                        }
                        else Xor(parity, blocks[j]);
                    }
                    if (bad >= 0) parity.CopyTo(blocks[bad], 0);
                    for (int j = 0; j < members; j++)
                    {
                        int size = (int)Math.Min(BlockSize, length - (long)(group + j) * BlockSize);
                        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(blocks[j].AsSpan(0, size)), hashes.AsSpan((group + j) * 32, 32))) throw new InvalidDataException("Reconstructed block verification failed.");
                        await output.WriteAsync(blocks[j].AsMemory(0, size), job.Token);
                    }
                }
                await output.FlushAsync(job.Token); output.Position = 0;
                if (!CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(output, job.Token), originalHash)) throw new InvalidDataException("Repaired archive verification failed.");
            }
            job.Token.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(damagedArchive) & UnixFileMode.UserExecute) != 0)
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.Move(temp, outputPath, overwrite: false); created = false;
        }
        finally { if (created) File.Delete(temp); }
    }

    private static int GetCount(long length)
    {
        if (length < 0 || length > (long)MaxBlocks * BlockSize) throw new InvalidDataException("Recovery supports archives up to 1,000,000 MiB.");
        return (int)((length + BlockSize - 1) / BlockSize);
    }
    private static void Xor(byte[] target, byte[] source)
    {
        var a = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(target);
        var b = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(source);
        for (int i = 0; i < a.Length; i++) a[i] ^= b[i];
    }
}
