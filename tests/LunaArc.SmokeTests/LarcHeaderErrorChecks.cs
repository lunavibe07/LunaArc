using LunaArc.Core.Formats;
using LunaArc.Core.Security;
using ZstdSharp;

internal static class LarcHeaderErrorChecks
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        using var compressor = new Compressor(3);
        byte[][] malformed = { "not-a-zstd-frame"u8.ToArray(), compressor.Wrap("{invalid-json"u8).ToArray() };
        var backend = new StandardArchiveHandler();
        for (int i = 0; i < malformed.Length; i++)
        foreach (bool encrypted in new[] { false, true })
        {
            const string password = "synthetic-header-error-password";
            string path = Path.Combine(root, $"invalid-header-{i}-{encrypted}.larc");
            byte[] payload = encrypted ? LunaAes256Cipher.EncryptBytes(malformed[i], password) : malformed[i];
            await using (var stream = File.Create(path))
            {
                await stream.WriteAsync(LarcNativeHandler.MagicBytes.ToArray());
                stream.WriteByte(encrypted ? (byte)1 : (byte)0);
                await stream.WriteAsync(BitConverter.GetBytes(payload.Length));
                await stream.WriteAsync(payload);
            }
            bool rejected = false;
            try { await backend.ReadCatalogAsync(path, encrypted ? password : null); }
            catch (InvalidDataException ex) { rejected = ex.InnerException is ZstdException or System.Text.Json.JsonException; }
            check(rejected, $"LARC malformed header {i}, encrypted={encrypted}: normalized data error after successful padding");
        }
    }
}
