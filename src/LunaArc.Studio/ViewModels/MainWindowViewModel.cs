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
    private long _archivePackedBytes;
    private string? _recoveredSessionPassword;
    private string? ArchivePassword => _recoveredSessionPassword ??
        (string.IsNullOrEmpty(PasswordInput) ? null : PasswordInput);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(HasArchive))]
    [NotifyPropertyChangedFor(nameof(CanUseArchive))]
    [NotifyPropertyChangedFor(nameof(ArchiveDisplayName))]
    [NotifyPropertyChangedFor(nameof(ArchiveSubtitle))]
    [NotifyPropertyChangedFor(nameof(SelectionType))]
    private string _currentArchivePath = string.Empty;
    [ObservableProperty] private string _extractionDestination = string.Empty;
    public string ArchiveDisplayName => HasArchive ? Path.GetFileName(CurrentArchivePath) : "Your archives, in orbit.";
    public string ArchiveSubtitle => HasArchive && _currentArena != null
        ? $"{_currentArena.Count:N0} entries  |  {FormatSize(_archivePackedBytes)} packed" : "Open an archive or create something new.";
    public string SelectionName => SelectedItem?.Name ?? "Archive details";
    public string SelectionType => SelectedItem?.TypeDisplay ?? (HasArchive ? "Select a file or folder" : "No archive opened");
    public string SelectionSize => SelectedItem?.OriginalSizeDisplay is { Length: > 0 } size ? size : "—";
    public string SelectionPacked => SelectedItem?.CompressedSizeDisplay ?? "—";
    public string SelectionModified => SelectedItem?.ModifiedDisplay ?? "—";

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

    [ObservableProperty] private bool _testCreatedArchive;
    [ObservableProperty] private bool _deleteSourceAfterArchiving;
    [ObservableProperty] private bool _createSfx;
    [ObservableProperty] private bool _solidArchive;
    [ObservableProperty] private bool _addRecoveryRecord;
    [ObservableProperty] private bool _lockArchive;
    public long? SplitVolumeSizeBytes { get; set; }

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
    public bool HasEncryptedEntries => _currentArena != null &&
        Enumerable.Range(0, _currentArena.Count).Any(i => _currentArena.GetNode(i).IsEncrypted);

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
    [NotifyPropertyChangedFor(nameof(SelectionName))]
    [NotifyPropertyChangedFor(nameof(SelectionType))]
    [NotifyPropertyChangedFor(nameof(SelectionSize))]
    [NotifyPropertyChangedFor(nameof(SelectionPacked))]
    [NotifyPropertyChangedFor(nameof(SelectionModified))]
    [NotifyPropertyChangedFor(nameof(SelectionIsFolder))]
    private VfsRowItem? _selectedItem;
    public bool SelectionIsFolder => SelectedItem?.IsDirectory == true;

    public ObservableCollection<VfsRowItem> VisibleItems { get; } = new();

    public MainWindowViewModel()
    {
        _liveWatcher.OnTempFileModified += HandleLiveFileEdited;
    }

    public async Task<bool> OpenArchiveFileAsync(string archivePath, string? recoveredPassword = null)
    {
        try
        {
            IsBusy = true;
            StatusMessage = $"Reading catalog {Path.GetFileName(archivePath)}...";
            var sw = Stopwatch.StartNew();

            string? sessionPassword = recoveredPassword ?? (archivePath == CurrentArchivePath ? _recoveredSessionPassword : null);
            string? pwd = sessionPassword ?? (string.IsNullOrEmpty(PasswordInput) ? null : PasswordInput);
            _currentArena = await _backend.ReadCatalogAsync(archivePath, pwd);
            _archivePackedBytes = new FileInfo(archivePath).Length;
            _recoveredSessionPassword = sessionPassword;
            CurrentArchivePath = archivePath;
            ExtractionDestination = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(archivePath))!, ExplorerRequest.GetFolderName(archivePath));
            _folderHistory.Clear();
            _currentParentIndex = -1;
            SearchQuery = string.Empty;

            ArchiveComment = _currentArena.ArchiveComment;

            NavigateToFolder(-1);
            OnPropertyChanged(nameof(ArchiveSubtitle));
            StatusMessage = $"✅ Loaded {_currentArena.Count:N0} entries in {sw.ElapsedMilliseconds} ms | Original size: {FormatSize(_currentArena.TotalUncompressedBytes)}";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Could not open archive: {ex.Message}";
            return false;
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
                CompressedSizeDisplay = node.IsDirectory ? "-" : node.CompressedSize == 0 && node.UncompressedSize > 0 ? "—" : FormatSize(node.CompressedSize),
                RatioDisplay = node.IsDirectory || node.CompressedSize == 0 && node.UncompressedSize > 0 ? "-" : $"{node.CompressionRatio:0.0}%",
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
        _recoveredSessionPassword = null;
        CurrentArchivePath = string.Empty;
        ExtractionDestination = string.Empty;
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
                    Password = ArchivePassword,
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

    public Task<bool> ExtractCurrentArchiveToAsync(string destinationFolder, bool skipExisting = false) =>
        ExtractCurrentArchiveToAsync(destinationFolder, skipExisting ? OverwriteMode.SkipExisting : OverwriteMode.OverwriteAll);

    public async Task<bool> ExtractCurrentArchiveToAsync(string destinationFolder, OverwriteMode overwriteMode,
        Func<string, OverwriteMode>? onConflict = null, ExtractionUpdateMode updateMode = ExtractionUpdateMode.ExtractAndReplace, bool keepBrokenFiles = false)
    {
        if (IsBusy) return false;
        if (string.IsNullOrWhiteSpace(CurrentArchivePath) || !File.Exists(CurrentArchivePath))
        {
            StatusMessage = "⚠️ Open an archive before extracting.";
            return false;
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
                    Password = ArchivePassword,
                    SelectedEntries = selectedSet,
                    SmartFolderCreation = false,
                    OverwriteMode = overwriteMode,
                    UpdateMode = updateMode,
                    KeepBrokenFiles = keepBrokenFiles,
                    OnFileConflictCallback = onConflict
                },
                _activeJob,
                (done, total, file) =>
                {
                    UpdateProgress(done, total, $"Extracting: {file}");
                }));

            ProgressPercent = 100;
            StatusMessage = $"✅ Extracted to: {destinationFolder}";
            return true;
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
        return false;
    }

    public async Task CompressFilesToArchiveAsync(IReadOnlyList<string> sourcePaths, string outputArchivePath)
    {
        long? splitSize = SplitVolumeSizeBytes; SplitVolumeSizeBytes = null;
        bool deleteSources = DeleteSourceAfterArchiving, sfx = CreateSfx, recovery = AddRecoveryRecord, locked = LockArchive;
        DeleteSourceAfterArchiving = CreateSfx = AddRecoveryRecord = LockArchive = false;
        if (IsBusy) return;
        _activeJob = new LunaJobController();
        IsBusy = true;
        ProgressPercent = 0;
        try
        {
            outputArchivePath = CompressionOptionsViewModel.ValidateOutputPath(outputArchivePath, sourcePaths);
            StatusMessage = "Creating archive and requested companion files...";
            var format = SelectedFormatIndex switch { 1 => OutputArchiveFormat.Zip, 2 when PasswordInput.Length > 0 => OutputArchiveFormat.EncryptedTarGz, 2 => OutputArchiveFormat.TarGz,
                3 => OutputArchiveFormat.SevenZip, _ => OutputArchiveFormat.LarcNative };
            string result = await Task.Run(() => ArchiveCreationService.CreateAsync(sourcePaths, new CompressOptions
            {
                OutputArchivePath = outputArchivePath, Format = format,
                Level = (LunaCompressionLevel)Math.Clamp(SelectedLevelIndex, 0, 5),
                Password = PasswordInput.Length == 0 ? null : PasswordInput, ArchiveComment = ArchiveComment,
                SolidArchive = SolidArchive, DeleteSourceAfterArchiving = deleteSources, SplitVolumeSizeBytes = splitSize
            }, _activeJob, sfx, recovery, locked, TestCreatedArchive,
                progress: (done, total, file) => UpdateProgress(done, total, $"Compressing: {file}")));
            ProgressPercent = 100;
            await OpenArchiveFileAsync(splitSize.HasValue ? SplitArchive.FirstPart(outputArchivePath) : outputArchivePath);
            StatusMessage = result + " " + outputArchivePath;
        }
        catch (OperationCanceledException) { StatusMessage = "Compression cancelled. Remaining sources were kept."; }
        catch (Exception ex) { StatusMessage = "Compression error: " + ex.Message; }
        finally { IsBusy = false; _activeJob.Dispose(); _activeJob = null; }
    }

    public async Task RepairArchiveAsync(string damaged, string output)
    {
        if (IsBusy) return;
        _activeJob = new LunaJobController();
        IsBusy = true;
        try
        {
            StatusMessage = "Verifying recovery data and reconstructing damaged blocks...";
            await Task.Run(() => LunaArc.Core.Security.ArchiveRecoveryRecord.RepairAsync(damaged,
                LunaArc.Core.Security.ArchiveRecoveryRecord.RecordPath(damaged), output, _activeJob));
            StatusMessage = "Recovered archive (SHA-256 verified): " + output + ". The damaged original was preserved.";
        }
        catch (OperationCanceledException) { StatusMessage = "Repair cancelled. The damaged original was preserved."; }
        catch (Exception ex) { StatusMessage = "Repair failed: " + ex.Message; }
        finally { IsBusy = false; _activeJob.Dispose(); _activeJob = null; }
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
            string? pwd = ArchivePassword;
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
                ? "Classic menu installed. For the main Windows 11 menu, choose Tools > Enable Windows 11 right-click menu."
                : "LunaArc shell integration registered.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Shell registration error: {ex.Message}";
        }
    }

    public async Task EnableWindows11MenuAsync(bool trustCertificateConsent)
    {
        if (IsBusy || !trustCertificateConsent) return;
        IsBusy = true;
        try
        {
            StatusMessage = "Enabling the Windows 11 menu. Complete the Windows permission prompt if shown...";
            await Windows11MenuInstaller.EnableAsync(AppContext.BaseDirectory, trustCertificateConsent);
            StatusMessage = "Windows 11 menu enabled. Reopen Explorer windows; if Windows has cached the old menu, sign out and back in.";
        }
        catch (Exception ex) { StatusMessage = "Could not enable the Windows 11 menu: " + ex.Message; }
        finally { IsBusy = false; }
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
        _recoveredSessionPassword = null;
        PasswordInput = string.Empty;
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
