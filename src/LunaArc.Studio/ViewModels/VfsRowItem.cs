namespace LunaArc.Studio.ViewModels;

public sealed class VfsRowItem
{
    public required int NodeIndex { get; init; }
    public required string Icon { get; init; }
    public required string Name { get; init; }
    public required string FullRelativePath { get; init; }
    public required bool IsDirectory { get; init; }
    public bool IsFile => !IsDirectory;
    public string TypeDisplay => IsDirectory ? "File folder" :
        string.IsNullOrEmpty(Path.GetExtension(Name)) ? "File" : Path.GetExtension(Name)[1..].ToUpperInvariant() + " file";
    public long OriginalSizeBytes { get; init; }
    public long CompressedSizeBytes { get; init; }
    public required string OriginalSizeDisplay { get; init; }
    public required string CompressedSizeDisplay { get; init; }
    public required string RatioDisplay { get; init; }
    public required string CrcDisplay { get; init; }
    public required string ModifiedDisplay { get; init; }
}
