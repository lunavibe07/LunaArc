namespace LunaArc.Core.Pipeline;

internal sealed class TemporaryArchive : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("LunaArc_Read_").FullName;
    public string Path { get; }
    public TemporaryArchive(string extension) => Path = System.IO.Path.Combine(_directory, "archive" + extension);
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
