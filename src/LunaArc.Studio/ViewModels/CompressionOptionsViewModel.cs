using CommunityToolkit.Mvvm.ComponentModel;

namespace LunaArc.Studio.ViewModels;

public partial class CompressionOptionsViewModel : ObservableObject
{
    public bool IsCreation { get; }
    public IReadOnlyList<string> SourcePaths { get; }
    public string SourceSummary => IsCreation ? $"{SourcePaths.Count} selected file(s) or folder(s)" : "Default settings for the next archive";
    [ObservableProperty] private string _archiveName = string.Empty;
    [ObservableProperty] private int _selectedLevelIndex = 3;
    [ObservableProperty] private string _passwordInput = string.Empty;
    [ObservableProperty] private string _archiveComment = string.Empty;
    [ObservableProperty] private bool _testCreatedArchive;
    [ObservableProperty] private bool _createSfx;
    [ObservableProperty] private bool _solidArchive;
    [ObservableProperty] private bool _addRecoveryRecord;
    [ObservableProperty] private bool _lockArchive;
    public bool CanCreateSfx => IsCreation && LunaArc.Core.Formats.SfxArchive.CanCreate;
    public bool CanUseSolid => IsSevenZip;
    public bool IsSevenZip { get => SelectedFormatIndex == 3; set { if (value) SelectedFormatIndex = 3; } }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleTest))]
    private bool _deleteSourceAfterArchiving;
    public bool CanToggleTest => !DeleteSourceAfterArchiving;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLarc))]
    [NotifyPropertyChangedFor(nameof(IsZip))]
    [NotifyPropertyChangedFor(nameof(IsTarGzip))]
    [NotifyPropertyChangedFor(nameof(IsSevenZip))]
    [NotifyPropertyChangedFor(nameof(CanUseSolid))]
    [NotifyPropertyChangedFor(nameof(FormatNote))]
    [NotifyPropertyChangedFor(nameof(CanComment))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSources))]
    private int _selectedFormatIndex;

    public bool IsLarc { get => SelectedFormatIndex == 0; set { if (value) SelectedFormatIndex = 0; } }
    public bool IsZip { get => SelectedFormatIndex == 1; set { if (value) SelectedFormatIndex = 1; } }
    public bool IsTarGzip { get => SelectedFormatIndex == 2; set { if (value) SelectedFormatIndex = 2; } }
    public bool CanComment => SelectedFormatIndex is 0 or 1;
    public bool CanDeleteSources => IsCreation && !(IsTarGzip && PasswordInput.Length > 0);
    public string Extension => CreateSfx ? (OperatingSystem.IsWindows() ? ".exe" : ".run") : PayloadExtension;
    private string PayloadExtension => SelectedFormatIndex switch { 1 => ".zip", 2 when PasswordInput.Length > 0 => ".tar.gz.7z", 2 => ".tar.gz", 3 => ".7z", _ => ".larc" };
    public string FormatNote => IsLarc ? "LARC supports password encryption of names and contents."
        : IsZip ? "ZIP supports WinZip AES-256 passwords and comments. File names remain visible. ZIP64 supports large files and many entries; split encryption is unsupported."
        : IsSevenZip ? "7z supports AES-256 passwords with encrypted file names and solid LZMA2 compression. Encoding needs the packaged 7-Zip tool and staging space. Pause applies before/after encoding; Cancel stops it."
        : "With a password, creates .tar.gz.7z: an AES-256 encrypted 7z containing one TAR GZip. Extract twice. Source deletion is unavailable for this wrapper.";
    public string PasswordSummary => PasswordInput.Length == 0 ? "No password set" : "Password set";

    public CompressionOptionsViewModel(MainWindowViewModel settings, IReadOnlyList<string>? sources = null)
    {
        SourcePaths = sources?.ToArray() ?? Array.Empty<string>();
        IsCreation = SourcePaths.Count > 0;
        SelectedFormatIndex = settings.SelectedFormatIndex;
        SelectedLevelIndex = settings.SelectedLevelIndex;
        PasswordInput = settings.PasswordInput;
        ArchiveComment = settings.ArchiveComment;
        TestCreatedArchive = settings.TestCreatedArchive;
        SolidArchive = settings.SolidArchive && IsSevenZip;
        string parent = (IsCreation ? Path.GetDirectoryName(Path.GetFullPath(SourcePaths[0])) : null)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string name = IsCreation && SourcePaths.Count == 1 ? LunaArc.Shell.ExplorerRequest.GetFolderName(SourcePaths[0]) : "Archive";
        if (string.IsNullOrWhiteSpace(name)) name = "Archive";
        string path = Path.Combine(parent, name + Extension);
        for (int suffix = 1; File.Exists(path) || Directory.Exists(path); suffix++) path = Path.Combine(parent, $"{name} ({suffix}){Extension}");
        ArchiveName = path;
    }
    partial void OnArchiveNameChanged(string value) => Status = string.Empty;
    partial void OnCreateSfxChanged(bool value) => UpdateExtension();
    partial void OnDeleteSourceAfterArchivingChanged(bool value)
    {
        if (value) TestCreatedArchive = true;
    }
    partial void OnArchiveCommentChanged(string value) => Status = string.Empty;
    partial void OnPasswordInputChanged(string value)
    {
        OnPropertyChanged(nameof(PasswordSummary));
        OnPropertyChanged(nameof(CanDeleteSources));
        if (!CanDeleteSources) DeleteSourceAfterArchiving = false;
        if (IsTarGzip) UpdateExtension();
        Status = string.Empty;
    }
    partial void OnSelectedFormatIndexChanged(int value)
    {
        if (value != 3) SolidArchive = false;
        if (!CanDeleteSources) DeleteSourceAfterArchiving = false;
        UpdateExtension();
    }
    private void UpdateExtension()
    {
        if (ArchiveName.Length == 0) return;
        string name = ArchiveName;
        foreach (string extension in new[] { ".tar.gz.7z", ".tar.gz", ".larc", ".zip", ".7z", ".exe", ".run" })
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) { name = name[..^extension.Length]; break; }
        ArchiveName = name + Extension;
        Status = string.Empty;
    }

    public bool Validate()
    {
        try
        {
            if (SelectedFormatIndex is < 0 or > 3 || SelectedLevelIndex is < 0 or > 5)
                throw new ArgumentException("Choose a supported format and compression method.");
            if ((IsSevenZip || IsTarGzip) && PasswordInput.Length > 0 && (PasswordInput.Length > 1024 || PasswordInput.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0))
                throw new ArgumentException("7z passwords must be at most 1024 characters and cannot contain line breaks or NUL.");
            if (!CanDeleteSources && DeleteSourceAfterArchiving) throw new ArgumentException("Source deletion is unavailable for the nested TAR GZip wrapper.");
            if (!CanComment && ArchiveComment.Length > 0) throw new ArgumentException("This format does not support archive comments. Clear the comment or choose LARC / ZIP.");
            if (SolidArchive && !IsSevenZip) throw new ArgumentException("Select 7z for solid compression.");
            if ((IsSevenZip || IsTarGzip && PasswordInput.Length > 0) && !LunaArc.Core.Formats.SevenZipEncoder.IsAvailable) throw new ArgumentException("The 7-Zip encoder is missing. Use a published LunaArc package.");
            if (CreateSfx && !CanCreateSfx) throw new ArgumentException("SFX creation requires a published self-contained LunaArc package.");
            if (System.Text.Encoding.UTF8.GetByteCount(ArchiveComment) > 65535) throw new ArgumentException("The archive comment must fit within 65,535 UTF-8 bytes.");
            if (IsCreation)
            {
                if (!ArchiveName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Use the {Extension} extension for the selected format.");
                ArchiveName = ValidateOutputPath(ArchiveName, SourcePaths);
            }
            Status = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { Status = ex.Message; return false; }
    }

    public static string ValidateOutputPath(string output, IReadOnlyList<string> sources)
    {
        if (string.IsNullOrWhiteSpace(output) || !Path.IsPathFullyQualified(output)) throw new ArgumentException("Enter a full archive path.");
        string path = Path.GetFullPath(output);
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("That destination already exists. Choose a new archive name.");
        if (string.IsNullOrWhiteSpace(Path.GetFileName(path))) throw new ArgumentException("Enter an archive file name.");
        if (sources.Count == 0) throw new ArgumentException("Select files or folders to compress.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (string source in sources)
        {
            string full = Path.GetFullPath(source);
            if (!File.Exists(full) && !Directory.Exists(full)) throw new FileNotFoundException("A selected source no longer exists.");
            if (path.Equals(full, comparison) || (Directory.Exists(full) && path.StartsWith(Path.TrimEndingDirectorySeparator(full) + Path.DirectorySeparatorChar, comparison)))
                throw new ArgumentException("Save the archive outside the selected source folders.");
        }
        return path;
    }
    public void ApplyTo(MainWindowViewModel settings)
    {
        settings.SelectedFormatIndex = SelectedFormatIndex;
        settings.SelectedLevelIndex = SelectedLevelIndex;
        settings.PasswordInput = PasswordInput;
        settings.ArchiveComment = ArchiveComment;
        settings.TestCreatedArchive = TestCreatedArchive;
        settings.DeleteSourceAfterArchiving = IsCreation && DeleteSourceAfterArchiving;
        settings.CreateSfx = IsCreation && CreateSfx;
        settings.SolidArchive = SolidArchive;
        settings.AddRecoveryRecord = IsCreation && AddRecoveryRecord;
        settings.LockArchive = IsCreation && LockArchive;
    }
}
