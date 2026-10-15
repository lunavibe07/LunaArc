namespace LunaArc.Core.Pipeline;

internal sealed class TemporaryArchive : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("LunaArc_Read_").FullName;
    public string Path { get; }
    public bool IsEncrypted { get; }
    public TemporaryArchive(string extension, bool isEncrypted = false) { Path = System.IO.Path.Combine(_directory, "archive" + extension); IsEncrypted = isEncrypted; }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
