using System.Security.Cryptography;
using System.Text.Json;

namespace LunaArc.Core.Security;

/// <summary>Versioned authenticated encryption; the master password is never persisted.</summary>
public sealed class PasswordVault
{
    private static readonly byte[] Magic = "LUNAVAULT\x01"u8.ToArray();
    private const int MaximumBytes = 1024 * 1024;
    private readonly string _path;

    public PasswordVault(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LunaArc", "password_vault.enc");

    public List<string> Load(string masterPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(masterPassword);
        if (!File.Exists(_path)) return new();
        if (new FileInfo(_path).Length > MaximumBytes) throw new InvalidDataException("Vault is too large.");
        byte[] data = File.ReadAllBytes(_path);
        int prefix = Magic.Length;
        if (data.Length < prefix + 44 || !data.AsSpan(0, prefix).SequenceEqual(Magic))
            throw new InvalidDataException("Unsupported or damaged vault format.");
        byte[] key = DeriveKey(masterPassword, data.AsSpan(prefix, 16));
        byte[] plain = new byte[data.Length - prefix - 44];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(data.AsSpan(prefix + 16, 12), data.AsSpan(prefix + 44),
                data.AsSpan(prefix + 28, 16), plain, Magic);
            return JsonSerializer.Deserialize<List<string>>(plain) ?? throw new InvalidDataException("Invalid vault contents.");
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    public void Save(IEnumerable<string> passwords, string masterPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(masterPassword);
        // Refuse to overwrite an existing vault with a mistyped master password.
        if (File.Exists(_path)) _ = Load(masterPassword);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(passwords.Distinct(StringComparer.Ordinal).ToArray());
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] key = DeriveKey(masterPassword, salt);
        string fullPath = Path.GetFullPath(_path);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (plain.Length > MaximumBytes - Magic.Length - 44) throw new InvalidDataException("Vault is too large.");
            byte[] data = new byte[Magic.Length + 44 + plain.Length];
            Magic.CopyTo(data, 0);
            salt.CopyTo(data, Magic.Length);
            nonce.CopyTo(data, Magic.Length + 16);
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, data.AsSpan(Magic.Length + 44), data.AsSpan(Magic.Length + 28, 16), Magic);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options)) { stream.Write(data); stream.Flush(true); }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32);
}
