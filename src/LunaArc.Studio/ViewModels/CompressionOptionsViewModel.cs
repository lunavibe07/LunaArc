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
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLarc))]
    [NotifyPropertyChangedFor(nameof(IsZip))]
    [NotifyPropertyChangedFor(nameof(IsTarGzip))]
    [NotifyPropertyChangedFor(nameof(FormatNote))]
    [NotifyPropertyChangedFor(nameof(CanComment))]
    private int _selectedFormatIndex;

    public bool IsLarc { get => SelectedFormatIndex == 0; set { if (value) SelectedFormatIndex = 0; } }
    public bool IsZip { get => SelectedFormatIndex == 1; set { if (value) SelectedFormatIndex = 1; } }
    public bool IsTarGzip { get => SelectedFormatIndex == 2; set { if (value) SelectedFormatIndex = 2; } }
    public bool CanComment => SelectedFormatIndex != 2;
    public string Extension => SelectedFormatIndex switch { 1 => ".zip", 2 => ".tar.gz", _ => ".larc" };
    public string FormatNote => IsLarc ? "LARC supports password encryption of names and contents."
        : IsZip ? "ZIP output supports comments. Password encryption is available with LARC."
        : "TAR GZip output supports compression; passwords and comments require another format.";
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
        string parent = (IsCreation ? Path.GetDirectoryName(Path.GetFullPath(SourcePaths[0])) : null)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string name = IsCreation && SourcePaths.Count == 1 ? LunaArc.Shell.ExplorerRequest.GetFolderName(SourcePaths[0]) : "Archive";
        if (string.IsNullOrWhiteSpace(name)) name = "Archive";
        string path = Path.Combine(parent, name + Extension);
        for (int suffix = 1; File.Exists(path) || Directory.Exists(path); suffix++) path = Path.Combine(parent, $"{name} ({suffix}){Extension}");
        ArchiveName = path;
    }
    partial void OnArchiveNameChanged(string value) => Status = string.Empty;
    partial void OnArchiveCommentChanged(string value) => Status = string.Empty;
    partial void OnPasswordInputChanged(string value)
    {
        OnPropertyChanged(nameof(PasswordSummary));
        Status = string.Empty;
    }
    partial void OnSelectedFormatIndexChanged(int value)
    {
        if (ArchiveName.Length == 0) return;
        string name = ArchiveName;
        foreach (string extension in new[] { ".tar.gz", ".larc", ".zip" })
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) { name = name[..^extension.Length]; break; }
        ArchiveName = name + Extension;
        Status = string.Empty;
    }

    public bool Validate()
    {
        try
        {
            if (SelectedFormatIndex is < 0 or > 2 || SelectedLevelIndex is < 0 or > 5)
                throw new ArgumentException("Choose a supported format and compression method.");
            if (!IsLarc && PasswordInput.Length > 0) throw new ArgumentException("Choose LARC for encryption, or clear the password using Set password.");
            if (!CanComment && ArchiveComment.Length > 0) throw new ArgumentException("TAR GZip does not support archive comments. Clear the comment or choose LARC / ZIP.");
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
    }
}
