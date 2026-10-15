using System.Security.Cryptography;

namespace LunaArc.Core.Security;

/// <summary>
/// AES-256-CBC encryption with PBKDF2-SHA256 key derivation.
/// </summary>
public static class LunaAes256Cipher
{
    // Same salt/IV/CBC layout as the byte-array API, with bounded payload buffers.
    public static async Task<CryptoStream> CreateEncryptingStreamAsync(Stream output, string password, CancellationToken ct)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize), iv = RandomNumberGenerator.GetBytes(IvSize);
        await output.WriteAsync(salt, ct);
        await output.WriteAsync(iv, ct);
        return CreateCryptoStream(output, password, salt, iv, encrypt: true);
    }

    public static async Task<CryptoStream> CreateDecryptingStreamAsync(Stream input, string password, CancellationToken ct)
    {
        byte[] salt = new byte[SaltSize], iv = new byte[IvSize];
        await input.ReadExactlyAsync(salt, ct); await input.ReadExactlyAsync(iv, ct);
        return CreateCryptoStream(input, password, salt, iv, encrypt: false);
    }

    private static CryptoStream CreateCryptoStream(Stream stream, string password, byte[] salt, byte[] iv, bool encrypt)
    {
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        try
        {
            using var aes = Aes.Create(); aes.Key = key; aes.IV = iv;
            return new CryptoStream(stream, encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor(),
                encrypt ? CryptoStreamMode.Write : CryptoStreamMode.Read, leaveOpen: true);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 60000;

    public static byte[] EncryptBytes(byte[] plainBytes, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] iv = RandomNumberGenerator.GetBytes(IvSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var ms = new MemoryStream();
        ms.Write(salt);
        ms.Write(iv);
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cs.Write(plainBytes, 0, plainBytes.Length);
        }
        return ms.ToArray();
    }

    public static byte[] DecryptBytes(ReadOnlySpan<byte> cipherWithSaltAndIv, string password)
    {
        if (cipherWithSaltAndIv.Length < SaltSize + IvSize)
            throw new CryptographicException("Invalid encrypted data.");

        byte[] salt = cipherWithSaltAndIv[..SaltSize].ToArray();
        byte[] iv = cipherWithSaltAndIv.Slice(SaltSize, IvSize).ToArray();
        byte[] cipherData = cipherWithSaltAndIv[(SaltSize + IvSize)..].ToArray();
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var inputMs = new MemoryStream(cipherData);
        using var cs = new CryptoStream(inputMs, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var outputMs = new MemoryStream();
        cs.CopyTo(outputMs);
        return outputMs.ToArray();
    }
}
