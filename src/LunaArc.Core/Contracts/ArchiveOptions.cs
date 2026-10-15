namespace LunaArc.Core.Contracts;

/// <summary>
/// User-facing compression presets, mapped by each output format.
/// </summary>
public enum LunaCompressionLevel
{
    Store = 0,
    Fastest = 1,
    Fast = 2,
    Normal = 3,
    Good = 4,
    Best = 5
}

/// <summary>
/// Conflict handling when an extracted destination already exists.
/// </summary>
public enum OverwriteMode
{
    Ask = 0,
    OverwriteAll = 1,
    SkipExisting = 2,
    UpdateNewer = 3,
    AutoRename = 4
}

/// <summary>
/// Selects which archive files are eligible before overwrite handling.
/// </summary>
public enum ExtractionUpdateMode
{
    ExtractAndReplace = 0,
    ExtractAndUpdate = 1,
    FreshenExisting = 2
}

/// <summary>
/// Output formats. TarZst is reserved and currently rejected by the backend.
/// </summary>
public enum OutputArchiveFormat
{
    LarcNative,
    Zip,            // .zip
    TarGz,          // .tar.gz
    TarZst          // .tar.zst
}

public sealed class ExtractOptions
{
    public required string DestinationDirectory { get; init; }
    public string? Password { get; init; }
    public OverwriteMode OverwriteMode { get; init; } = OverwriteMode.OverwriteAll;
    public ExtractionUpdateMode UpdateMode { get; init; } = ExtractionUpdateMode.ExtractAndReplace;
    public bool SmartFolderCreation { get; init; } = true;
    public bool KeepBrokenFiles { get; init; } = false;
    public HashSet<string>? SelectedEntries { get; init; }
    public Func<string, OverwriteMode>? OnFileConflictCallback { get; init; }
}

public sealed class CompressOptions
{
    public required string OutputArchivePath { get; init; }
    public OutputArchiveFormat Format { get; init; } = OutputArchiveFormat.LarcNative;
    public LunaCompressionLevel Level { get; init; } = LunaCompressionLevel.Normal;
    public string? Password { get; init; }
    public bool EncryptHeader { get; init; } = false;
    public bool SolidArchive { get; init; } = true;
    public bool DeleteSourceAfterArchiving { get; init; } = false;
    public long? SplitVolumeSizeBytes { get; init; } = null;
    public string? ArchiveComment { get; init; }
}

public record ArchiveTestResult(
    int TotalFilesTested,
    int PassedFiles,
    int FailedFiles,
    List<string> BrokenFileNames
)
{
    public bool IsHealthy => FailedFiles == 0;
}
