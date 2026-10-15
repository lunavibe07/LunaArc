using System.Buffers.Binary;
using System.Security.Cryptography;
using SharpCompress.Archives;
using LunaArc.Core.Pipeline;

namespace LunaArc.Core.Security;

/// <summary>Verifies WinZip AES authentication; AE-2 uses a zero CRC by design.</summary>
internal static class ZipAesIntegrity
{
    public static HashSet<IArchiveEntry> Validate(string path, string? password,
        IReadOnlyList<IArchiveEntry> entries, LunaJobController job)
    {
        var noCrc = new HashSet<IArchiveEntry>();
        if (!entries.Any(e => e.IsEncrypted)) return noCrc;
        using var stream = File.OpenRead(path);
        byte[] tail = new byte[(int)Math.Min(stream.Length, 65557)];
        stream.Position = stream.Length - tail.Length;
        stream.ReadExactly(tail);
        int end = -1;
        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (U32(tail, i) == 0x06054b50 && i + 22 + U16(tail, i + 20) == tail.Length)
            { end = i; break; }
        }
        if (end < 0) throw new InvalidDataException("ZIP end record is missing.");
        if (U16(tail, end + 4) != 0 || U16(tail, end + 6) != 0)
            throw new NotSupportedException("Encrypted split ZIP verification is unsupported.");
        long directoryOffset = U32(tail, end + 16), directorySize = U32(tail, end + 12);
        long count = U16(tail, end + 10), endOffset = stream.Length - tail.Length + end;
        long directoryLimit = endOffset;
        if (directoryOffset == uint.MaxValue || directorySize == uint.MaxValue || count == ushort.MaxValue)
        {
            if (endOffset < 20) throw new InvalidDataException("Missing ZIP64 locator.");
            stream.Position = endOffset - 20;
            byte[] locator = new byte[20]; stream.ReadExactly(locator);
            if (U32(locator, 0) != 0x07064b50 || U32(locator, 4) != 0 || U32(locator, 16) != 1)
                throw new InvalidDataException("Invalid or split ZIP64 locator.");
            long recordOffset = I64(locator, 8);
            if (recordOffset > endOffset - 20 - 56) throw new InvalidDataException("Invalid ZIP64 end offset.");
            stream.Position = recordOffset;
            byte[] record = new byte[56]; stream.ReadExactly(record);
            long recordLength = I64(record, 4);
            if (U32(record, 0) != 0x06064b50 || recordLength < 44 || recordLength > endOffset - 20 - recordOffset - 12 ||
                U32(record, 16) != 0 || U32(record, 20) != 0 || I64(record, 24) != I64(record, 32))
                throw new InvalidDataException("Invalid or split ZIP64 end record.");
            count = I64(record, 32); directorySize = I64(record, 40); directoryOffset = I64(record, 48);
            directoryLimit = recordOffset;
        }
        if (count != entries.Count || directoryOffset > directoryLimit || directorySize > directoryLimit - directoryOffset ||
            U16(tail, end + 8) != U16(tail, end + 10))
            throw new InvalidDataException("Inconsistent ZIP directory.");
        long directoryEnd = directoryOffset + directorySize;
        stream.Position = directoryOffset;
        byte[] header = new byte[46];
        for (int index = 0; index < count; index++)
        {
            job.WaitIfPaused();
            job.Token.ThrowIfCancellationRequested();
            stream.ReadExactly(header);
            if (U32(header, 0) != 0x02014b50) throw new InvalidDataException("Invalid ZIP directory record.");
            byte[] name = new byte[U16(header, 28)];
            stream.ReadExactly(name);
            byte[] extra = new byte[U16(header, 30)];
            stream.ReadExactly(extra);
            stream.Position += U16(header, 32);
            if (stream.Position > directoryEnd) throw new InvalidDataException("ZIP directory exceeds its bounds.");
            if (U16(header, 34) != 0) throw new InvalidDataException("Split ZIP entries are unsupported.");
            if (U16(header, 10) != 99) continue;
            if ((U16(header, 8) & 1) == 0 || !entries[index].IsEncrypted)
                throw new InvalidDataException("Invalid ZIP AES encryption flags.");
            int aes = -1, zip64 = -1, zip64Length = 0;
            for (int offset = 0; offset < extra.Length;)
            {
                if (extra.Length - offset < 4) throw new InvalidDataException("Invalid ZIP extra field.");
                int size = U16(extra, offset + 2);
                if (size > extra.Length - offset - 4) throw new InvalidDataException("Truncated ZIP extra field.");
                if (U16(extra, offset) == 0x9901) { if (size != 7) throw new InvalidDataException("Invalid ZIP AES extra field."); aes = offset + 4; }
                if (U16(extra, offset) == 1) { if (zip64 >= 0) throw new InvalidDataException("Duplicate ZIP64 metadata."); zip64 = offset + 4; zip64Length = size; }
                offset += 4 + size;
            }
            if (aes < 0 || U16(extra, aes + 2) != 0x4541 || U16(extra, aes) is not (1 or 2))
                throw new InvalidDataException("Unsupported ZIP AES metadata.");
            int keyBytes = extra[aes + 4] switch { 1 => 16, 2 => 24, 3 => 32, _ => throw new InvalidDataException("Invalid ZIP AES strength.") };
            long compressed = U32(header, 20), localOffset = U32(header, 42);
            int cursor = zip64;
            long ReadExtended()
            {
                if (cursor < 0 || cursor > zip64 + zip64Length - 8) throw new InvalidDataException("Missing ZIP64 entry metadata.");
                long value = I64(extra, cursor); cursor += 8; return value;
            }
            long original = U32(header, 24);
            if (original == uint.MaxValue) original = ReadExtended();
            if (compressed == uint.MaxValue) compressed = ReadExtended();
            if (localOffset == uint.MaxValue) localOffset = ReadExtended();
            if (original != entries[index].Size || compressed != entries[index].CompressedSize)
                throw new InvalidDataException("ZIP entry sizes disagree with its directory.");
            long nextRecord = stream.Position;
            ValidatePayload(stream, localOffset, directoryOffset, compressed, name, password, keyBytes, job);
            stream.Position = nextRecord;
            if (U16(extra, aes) == 2) noCrc.Add(entries[index]);
        }
        return noCrc;
    }

    private static void ValidatePayload(Stream stream, long offset, long directoryOffset, long compressed,
        byte[] name, string? password, int keyBytes, LunaJobController job)
    {
        if (password == null) throw new CryptographicException("The ZIP archive requires a password.");
        if (offset < 0 || offset > directoryOffset - 30) throw new InvalidDataException("Invalid ZIP local offset.");
        stream.Position = offset;
        byte[] local = new byte[30];
        stream.ReadExactly(local);
        if (U32(local, 0) != 0x04034b50 || U16(local, 8) != 99 || (U16(local, 6) & 1) == 0)
            throw new InvalidDataException("Invalid ZIP AES local header.");
        byte[] localName = new byte[U16(local, 26)];
        stream.ReadExactly(localName);
        if (!localName.AsSpan().SequenceEqual(name)) throw new InvalidDataException("ZIP local and directory names differ.");
        stream.Position += U16(local, 28);
        if (stream.Position > directoryOffset) throw new InvalidDataException("ZIP local metadata exceeds its bounds.");
        int saltBytes = keyBytes / 2;
        if (compressed < saltBytes + 12 || compressed > directoryOffset - stream.Position)
            throw new InvalidDataException("Invalid ZIP AES payload bounds.");
        byte[] salt = new byte[saltBytes], verifier = new byte[2];
        stream.ReadExactly(salt);
        stream.ReadExactly(verifier);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, 1000, HashAlgorithmName.SHA1, keyBytes * 2 + 2);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(verifier, derived.AsSpan(keyBytes * 2, 2)))
                throw new CryptographicException("The ZIP password did not verify.");
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, derived.AsSpan(keyBytes, keyBytes));
            byte[] buffer = new byte[65536];
            long left = compressed - saltBytes - 12;
            while (left > 0)
            {
                job.WaitIfPaused();
                job.Token.ThrowIfCancellationRequested();
                int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (read == 0) throw new InvalidDataException("Truncated ZIP AES data.");
                hmac.AppendData(buffer, 0, read);
                left -= read;
            }
            byte[] stored = new byte[10];
            stream.ReadExactly(stored);
            byte[] computed = hmac.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(stored, computed.AsSpan(0, 10)))
                throw new CryptographicException("ZIP AES authentication failed: incorrect password or damaged data.");
        }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static long I64(byte[] bytes, int offset)
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, 8));
        if (value > long.MaxValue) throw new InvalidDataException("ZIP64 value exceeds supported stream bounds.");
        return (long)value;
    }
}
