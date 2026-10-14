namespace LunaArc.Core.Security;

/// <summary>
/// Computes CRC32 checksums for archive integrity checks.
/// </summary>
public static class Crc32Verifier
{
    private static readonly uint[] Table = GenerateTable();

    private static uint[] GenerateTable()
    {
        const uint poly = 0xEDB88320u;
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ poly : crc >> 1;
            table[i] = crc;
        }
        return table;
    }

    public static uint UpdateCrc32(uint runningCrc, ReadOnlySpan<byte> data)
    {
        uint crc = runningCrc;
        foreach (byte b in data)
        {
            byte idx = (byte)((crc ^ b) & 0xFF);
            crc = (crc >> 8) ^ Table[idx];
        }
        return crc;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => ~UpdateCrc32(0xFFFFFFFFu, data);
}
