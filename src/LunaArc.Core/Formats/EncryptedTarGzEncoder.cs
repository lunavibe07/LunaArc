using LunaArc.Core.Contracts;
using LunaArc.Core.Pipeline;

namespace LunaArc.Core.Formats;

internal static class EncryptedTarGzEncoder
{
    public static async Task CreateAsync(IReadOnlyList<string> sources, CompressOptions options, LunaJobController job,
        Action<long, long, string>? progress)
    {
        if (string.IsNullOrEmpty(options.Password)) throw new ArgumentException("Encrypted TAR GZip requires a password.");
        if (options.DeleteSourceAfterArchiving || options.SplitVolumeSizeBytes.HasValue || options.SolidArchive || !string.IsNullOrEmpty(options.ArchiveComment))
            throw new NotSupportedException("The TAR GZip encryption wrapper does not support source deletion, split volumes, solid mode or comments.");
        string stage = Directory.CreateTempSubdirectory("LunaArc_TarWrapper_").FullName;
        try
        {
            string name = Path.GetFileName(options.OutputArchivePath);
            if (name.EndsWith(".tar.gz.7z", StringComparison.OrdinalIgnoreCase)) name = name[..^3];
            else name = "Archive.tar.gz";
            string inner = Path.Combine(stage, name);
            var backend = new StandardArchiveHandler();
            await backend.CompressAsync(sources, new CompressOptions
            { OutputArchivePath = inner, Format = OutputArchiveFormat.TarGz, Level = options.Level }, job, progress);
            await SevenZipEncoder.CreateAsync(new[] { inner }, new CompressOptions
            { OutputArchivePath = options.OutputArchivePath, Format = OutputArchiveFormat.SevenZip, Password = options.Password, Level = LunaCompressionLevel.Store }, job);
        }
        finally { Directory.Delete(stage, recursive: true); }
    }
}
