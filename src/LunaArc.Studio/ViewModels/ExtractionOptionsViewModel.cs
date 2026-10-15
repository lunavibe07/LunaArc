using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LunaArc.Core.Contracts;

namespace LunaArc.Studio.ViewModels;

public partial class ExtractionOptionsViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    [ObservableProperty] private string _destination = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _showPassword;
    [ObservableProperty] private bool _displayAfterExtraction;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private ExtractionFolderNode? _selectedFolder;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AskBeforeOverwrite))]
    [NotifyPropertyChangedFor(nameof(OverwriteAll))]
    [NotifyPropertyChangedFor(nameof(SkipExisting))]
    [NotifyPropertyChangedFor(nameof(RenameAutomatically))]
    [NotifyPropertyChangedFor(nameof(UpdateNewer))]
    private OverwriteMode _overwriteMode = OverwriteMode.SkipExisting;

    public ObservableCollection<ExtractionFolderNode> Folders { get; } = new();
    public bool AskBeforeOverwrite { get => OverwriteMode == OverwriteMode.Ask; set { if (value) OverwriteMode = OverwriteMode.Ask; } }
    public bool OverwriteAll { get => OverwriteMode == OverwriteMode.OverwriteAll; set { if (value) OverwriteMode = OverwriteMode.OverwriteAll; } }
    public bool SkipExisting { get => OverwriteMode == OverwriteMode.SkipExisting; set { if (value) OverwriteMode = OverwriteMode.SkipExisting; } }
    public bool RenameAutomatically { get => OverwriteMode == OverwriteMode.AutoRename; set { if (value) OverwriteMode = OverwriteMode.AutoRename; } }
    public bool UpdateNewer { get => OverwriteMode == OverwriteMode.UpdateNewer; set { if (value) OverwriteMode = OverwriteMode.UpdateNewer; } }
    public char PasswordCharacter => ShowPassword ? '\0' : '●';

    public ExtractionOptionsViewModel(string destination)
    {
        Destination = destination;
        AddFolder("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddFolder("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddFolder("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddFolder("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        foreach (var drive in DriveInfo.GetDrives()) AddFolder(drive.Name, drive.Name);
    }
    private void AddFolder(string name, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            Folders.Add(new ExtractionFolderNode(name, path, _lifetime.Token));
    }
    partial void OnShowPasswordChanged(bool value) => OnPropertyChanged(nameof(PasswordCharacter));
    partial void OnSelectedFolderChanged(ExtractionFolderNode? value)
    {
        if (value?.Path is { Length: > 0 } path) { Destination = path; Status = string.Empty; }
    }
    public bool ValidateDestination()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Destination)) throw new ArgumentException("Choose a destination folder.");
            if (!Path.IsPathFullyQualified(Destination)) throw new ArgumentException("Enter a full destination path.");
            Destination = Path.GetFullPath(Destination);
            if (File.Exists(Destination)) throw new IOException("The destination is a file. Choose a folder.");
            Status = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        { Status = ex.Message; return false; }
    }
    public async Task<bool> CreateFolderAsync(string name)
    {
        if (!ValidateDestination()) return false;
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\')
            || (OperatingSystem.IsWindows() && (name.EndsWith(' ') || name.EndsWith('.'))))
        { Status = "Enter a single valid folder name."; return false; }
        string path = Path.Combine(Destination, name);
        CancellationToken token = _lifetime.Token;
        try
        {
            await Task.Run(() => Directory.CreateDirectory(path), token);
            token.ThrowIfCancellationRequested();
            Destination = path;
            Status = "Folder created.";
            Folders.Insert(0, new ExtractionFolderNode(name, path, token));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        { Status = "Could not create folder: " + ex.Message; return false; }
    }
    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); Password = string.Empty; }
}

public partial class ExtractionFolderNode : ObservableObject
{
    private readonly CancellationToken _token;
    private bool _loaded;
    public string Name { get; }
    public string Path { get; }
    public ObservableCollection<ExtractionFolderNode> Children { get; } = new();
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string _error = string.Empty;
    public ExtractionFolderNode(string name, string path, CancellationToken token)
    {
        Name = name; Path = path; _token = token;
        if (path.Length > 0) Children.Add(new ExtractionFolderNode("Loading...", string.Empty, token));
    }
    partial void OnIsExpandedChanged(bool value) { if (value) _ = LoadChildrenAsync(); }
    public async Task LoadChildrenAsync()
    {
        if (_loaded || Path.Length == 0) return;
        _loaded = true;
        try
        {
            var paths = await Task.Run(() => Directory.EnumerateDirectories(Path)
                .Select(path => { _token.ThrowIfCancellationRequested(); return path; })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(), _token);
            _token.ThrowIfCancellationRequested();
            Children.Clear();
            foreach (string path in paths) Children.Add(new ExtractionFolderNode(System.IO.Path.GetFileName(path), path, _token));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (_token.IsCancellationRequested) return;
            Children.Clear(); Error = "Folder unavailable: " + ex.Message;
        }
    }
}
