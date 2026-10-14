using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.LiveEdit;
using LunaArc.Core.Pipeline;
using LunaArc.Core.VfsIndex;
using LunaArc.Shell;

namespace LunaArc.Studio.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly StandardArchiveHandler _backend = new();
    private readonly TempFileWatcher _liveWatcher = new();
    private FlatVfsArena? _currentArena;
    private LunaJobController? _activeJob;
    private readonly Stack<int> _folderHistory = new();
    private readonly List<VfsRowItem> _folderRows = new();
    private int _currentParentIndex = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(HasArchive))]
    [NotifyPropertyChangedFor(nameof(CanUseArchive))]
    private string _currentArchivePath = string.Empty;

    [ObservableProperty]
    private string _breadcrumbPath = "Open an archive";

    [ObservableProperty]
    private string _statusMessage = "Ready — Open an archive or select files to compress.";

    [ObservableProperty]
    private string _archiveComment = string.Empty;

    [ObservableProperty]
    private string _passwordInput = string.Empty;

    [ObservableProperty]
    private int _selectedLevelIndex = 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEncrypt))]
    private int _selectedFormatIndex = 0;

    public bool CanEncrypt => SelectedFormatIndex == 0;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private string _visibleSummary = "No archive opened";

    public bool HasArchive => _currentArena != null && !string.IsNullOrWhiteSpace(CurrentArchivePath);
    public string WindowTitle => HasArchive ? Path.GetFileName(CurrentArchivePath) + " - LunaArc" : "LunaArc Archiver";
    public bool CanUseArchive => CanStartOperation && HasArchive;
    public bool CanViewSelection => CanUseArchive && SelectedItem != null;
    public bool CanGoUp => CanUseArchive && _currentParentIndex >= 0;

    [ObservableProperty]
    private double _progressPercent = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartOperation))]
    [NotifyPropertyChangedFor(nameof(CanUseArchive))]
    [NotifyPropertyChangedFor(nameof(CanViewSelection))]
    [NotifyPropertyChangedFor(nameof(CanGoUp))]
    private bool _isBusy = false;

    public bool CanStartOperation => !IsBusy;

    [ObservableProperty]
    private string _pauseButtonText = "⏸ Pause";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanViewSelection))]
    private VfsRowItem? _selectedItem;

    public ObservableCollection<VfsRowItem> VisibleItems { get; } = new();

    public MainWindowViewModel()
    {
        _liveWatcher.OnTempFileModified += HandleLiveFileEdited;
    }

    public async Task OpenArchiveFileAsync(string archivePath)
    {
        try
        {
            IsBusy = true;
            StatusMessage = $"Reading catalog {Path.GetFileName(archivePath)}...";
            var sw = Stopwatch.StartNew();

            string? pwd = string.IsNullOrWhiteSpace(PasswordInput) ? null : PasswordInput;
            _currentArena = await _backend.ReadCatalogAsync(archivePath, pwd);
            CurrentArchivePath = archivePath;
            _folderHistory.Clear();
            _currentParentIndex = -1;
            SearchQuery = string.Empty;

            ArchiveComment = _currentArena.ArchiveComment;

            NavigateToFolder(-1);
            StatusMessage = $"✅ Loaded {_currentArena.Count:N0} entries in {sw.ElapsedMilliseconds} ms | Original size: {FormatSize(_currentArena.TotalUncompressedBytes)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Could not open archive: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void NavigateToFolder(int folderNodeIndex)
    {
        if (_currentArena == null) return;
        _currentParentIndex = folderNodeIndex;
        SelectedItem = null;
        _folderRows.Clear();

        BreadcrumbPath = folderNodeIndex < 0
            ? CurrentArchivePath
            : CurrentArchivePath + " / " + _currentArena.GetFullPath(folderNodeIndex);

        var children = _currentArena.GetChildrenIndices(folderNodeIndex);
        foreach (int idx in children)
        {
            ref readonly var node = ref _currentArena.GetNode(idx);
            DateTime dt = node.LastWriteUnixTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(node.LastWriteUnixTime).LocalDateTime
                : DateTime.Now;

            _folderRows.Add(new VfsRowItem
            {
                NodeIndex = idx,
                Icon = node.IsDirectory ? "📁" : (node.IsEncrypted ? "🔒" : "📄"),
                Name = _currentArena.GetName(idx),
                FullRelativePath = _currentArena.GetFullPath(idx),
                IsDirectory = node.IsDirectory,
                IsEncrypted = node.IsEncrypted,
                OriginalSizeBytes = node.UncompressedSize,
                CompressedSizeBytes = node.CompressedSize,
                OriginalSizeDisplay = node.IsDirectory ? string.Empty : FormatSize(node.UncompressedSize),
                CompressedSizeDisplay = node.IsDirectory ? "-" : FormatSize(node.CompressedSize),
                RatioDisplay = node.IsDirectory ? "-" : $"{node.CompressionRatio:0.0}%",
                CrcDisplay = node.IsDirectory ? "-" : $"{node.Crc32:X8}",
                ModifiedDisplay = dt.ToString("yyyy-MM-dd HH:mm")
            });
        }
        RefreshVisibleRows();
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(CanViewSelection));
    }

    partial void OnSearchQueryChanged(string value) => RefreshVisibleRows();

    private void RefreshVisibleRows()
    {
        SelectedItem = null;
        VisibleItems.Clear();
        foreach (var row in _folderRows)
            if (string.IsNullOrWhiteSpace(SearchQuery) || row.Name.Contains(SearchQuery.Trim(), StringComparison.OrdinalIgnoreCase))
                VisibleItems.Add(row);
        int folders = VisibleItems.Count(row => row.IsDirectory);
        int files = VisibleItems.Count - folders;
        long visibleBytes = VisibleItems.Where(row => row.IsFile).Sum(row => row.OriginalSizeBytes);
        VisibleSummary = HasArchive ? $"{folders:N0} folders, {files:N0} files | {FormatSize(visibleBytes)}" : "No archive opened";
    }

    [RelayCommand]
    public void CloseArchive()
    {
        if (IsBusy) return;
        _currentArena = null;
        CurrentArchivePath = string.Empty;
        _currentParentIndex = -1;
        _folderRows.Clear();
        _folderHistory.Clear();
        SearchQuery = string.Empty;
        RefreshVisibleRows();
        BreadcrumbPath = "Open an archive";
        ArchiveComment = string.Empty;
        StatusMessage = "Ready — Open an archive or select files to compress.";
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(CanViewSelection));
    }

    [RelayCommand]
    public void GoUpFolder()
    {
        if (_currentArena == null || _currentParentIndex < 0) return;
        ref readonly var currentFolder = ref _currentArena.GetNode(_currentParentIndex);
        NavigateToFolder(currentFolder.ParentIndex);
    }

    [RelayCommand]
    public async Task OpenOrEnterSelectedItemAsync()
    {
        if (IsBusy) return;
        if (SelectedItem == null || _currentArena == null) return;

        if (SelectedItem.IsDirectory)
        {
            _folderHistory.Push(_currentParentIndex);
            NavigateToFolder(SelectedItem.NodeIndex);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Extracting '{SelectedItem.Name}' for preview...";
            using var job = new LunaJobController();
            _activeJob = job;
            string extractedFile = _liveWatcher.PrepareTempPathForEntry(SelectedItem.FullRelativePath);

            await Task.Run(() => _backend.ExtractAsync(
                CurrentArchivePath,
                new ExtractOptions
                {
                    DestinationDirectory = _liveWatcher.SessionDirectory,
                    Password = string.IsNullOrWhiteSpace(PasswordInput) ? null : PasswordInput,
                    SelectedEntries = new HashSet<string> { SelectedItem.FullRelativePath },
                    SmartFolderCreation = false
                },
                job));

            if (File.Exists(extractedFile))
            {
                _liveWatcher.OpenWithDefaultOsApp(extractedFile);
                StatusMessage = $"👀 Opening preview: {SelectedItem.Name}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Preview error: {ex.Message}";
        }
        finally
        {
            _activeJob = null;
            IsBusy = false;
            PauseButtonText = "⏸ Pause";
        }
    }

    public async Task ExtractCurrentArchiveToAsync(string destinationFolder)
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(CurrentArchivePath) || !File.Exists(CurrentArchivePath))
        {
            StatusMessage = "⚠️ Open an archive before extracting.";
            return;
        }

        _activeJob = new LunaJobController();
        IsBusy = true;
        ProgressPercent = 0;

        try
        {
            var selectedSet = SelectedItem != null && !SelectedItem.IsDirectory
                ? new HashSet<string> { SelectedItem.FullRelativePath }
                : null;

            await Task.Run(() => _backend.ExtractAsync(
                CurrentArchivePath,
                new ExtractOptions
                {
                    DestinationDirectory = destinationFolder,
                    Password = string.IsNullOrWhiteSpace(PasswordInput) ? null : PasswordInput,
                    SelectedEntries = selectedSet,
                    SmartFolderCreation = true
                },
                _activeJob,
                (done, total, file) =>
                {
                    UpdateProgress(done, total, $"Extracting: {file}");
                }));

            ProgressPercent = 100;
            StatusMessage = $"✅ Extracted to: {destinationFolder}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "🛑 Extraction cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Extraction error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _activeJob?.Dispose();
            _activeJob = null;
        }
    }

    public async Task CompressFilesToArchiveAsync(IReadOnlyList<string> sourcePaths, string outputArchivePath)
    {
        if (IsBusy) return;
        _activeJob = new LunaJobController();
        IsBusy = true;
        ProgressPercent = 0;

        try
        {
            OutputArchiveFormat fmt = SelectedFormatIndex switch
            {
                1 => OutputArchiveFormat.Zip,
                2 => OutputArchiveFormat.TarGz,
                _ => OutputArchiveFormat.LarcNative
            };

            LunaCompressionLevel level = (LunaCompressionLevel)Math.Clamp(SelectedLevelIndex, 0, 5);

            await Task.Run(() => _backend.CompressAsync(
                sourcePaths,
                new CompressOptions
                {
                    OutputArchivePath = outputArchivePath,
                    Format = fmt,
                    Level = level,
                    Password = string.IsNullOrWhiteSpace(PasswordInput) ? null : PasswordInput,
                    ArchiveComment = ArchiveComment
                },
                _activeJob,
                (done, total, file) =>
                {
                    UpdateProgress(done, total, $"Compressing: {file}");
                }));

            ProgressPercent = 100;
            StatusMessage = $"✅ Created archive: {outputArchivePath}";
            await OpenArchiveFileAsync(outputArchivePath);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "🛑 Compression cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Compression error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _activeJob?.Dispose();
            _activeJob = null;
        }
    }

    [RelayCommand]
    public async Task TestCurrentArchiveAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(CurrentArchivePath) || !File.Exists(CurrentArchivePath))
        {
            StatusMessage = "⚠️ Open an archive before testing.";
            return;
        }

        _activeJob = new LunaJobController();
        IsBusy = true;
        try
        {
            string? pwd = string.IsNullOrWhiteSpace(PasswordInput) ? null : PasswordInput;
            var result = await Task.Run(() => _backend.TestArchiveAsync(
                CurrentArchivePath,
                pwd,
                _activeJob,
                (curr, total, file) =>
                {
                    UpdateProgress(curr, total, $"Testing archive [{curr}/{total}]: {file}");
                }));

            StatusMessage = result.IsHealthy
                ? $"✅ Archive test passed ({result.PassedFiles:N0} files)."
                : $"❌ Found {result.FailedFiles} failed files: {string.Join(", ", result.BrokenFileNames)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Archive test error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _activeJob?.Dispose();
            _activeJob = null;
        }
    }

    [RelayCommand]
    public void TogglePauseJob()
    {
        if (_activeJob == null) return;
        if (_activeJob.IsPaused)
        {
            _activeJob.Resume();
            PauseButtonText = "⏸ Pause";
            StatusMessage = "▶️ Operation resumed.";
        }
        else
        {
            _activeJob.Pause();
            PauseButtonText = "▶️ Resume";
            StatusMessage = "⏸ Operation paused.";
        }
    }

    [RelayCommand]
    public void CancelActiveJob()
    {
        _activeJob?.Cancel();
        PauseButtonText = "⏸ Pause";
    }

    [RelayCommand]
    public void RegisterOsShellMenu()
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string guiExe = Path.Combine(baseDir, OperatingSystem.IsWindows() ? "LunaArc.exe" : "LunaArc");
            string cliExe = Path.Combine(baseDir, OperatingSystem.IsWindows() ? "luna-cli.exe" : "luna-cli");
            ShellRegistrarFactory.CreateForCurrentOs().RegisterContextMenuAndAssociations(guiExe, cliExe);
            StatusMessage = OperatingSystem.IsWindows()
                ? "Context menus installed. Right-click an archive and choose Show more options on Windows 11."
                : "LunaArc shell integration registered.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Shell registration error: {ex.Message}";
        }
    }

    private void HandleLiveFileEdited(string tempPath, string entryRelativePath)
    {
        Dispatcher.UIThread.Post(() => StatusMessage = $"💡 Preview file '{entryRelativePath}' was modified. Changes are not saved to the archive.");
    }

    private void UpdateProgress(long done, long total, string message)
    {
        var job = _activeJob;
        Dispatcher.UIThread.Post(() =>
        {
            if (job == null || job != _activeJob || !IsBusy) return;
            ProgressPercent = Math.Round((double)done * 100 / Math.Max(1, total), 1);
            StatusMessage = message;
        });
    }

    public void Dispose()
    {
        _activeJob?.Cancel();
        _liveWatcher.OnTempFileModified -= HandleLiveFileEdited;
        _liveWatcher.Dispose();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:0.1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.2} MB";
        double gb = mb / 1024.0;
        return $"{gb:0.2} GB";
    }
}
