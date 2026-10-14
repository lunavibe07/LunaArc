namespace LunaArc.Studio.ViewModels;

public sealed class VfsRowItem
{
    public required int NodeIndex { get; init; }
    public required string Icon { get; init; }
    public required string Name { get; init; }
    public required string FullRelativePath { get; init; }
    public required bool IsDirectory { get; init; }
    public required string OriginalSizeDisplay { get; init; }
    public required string CompressedSizeDisplay { get; init; }
    public required string RatioDisplay { get; init; }
    public required string CrcDisplay { get; init; }
    public required string ModifiedDisplay { get; init; }
}
