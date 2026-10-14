using System.Diagnostics;
using System.Collections.Concurrent;
using LunaArc.Core.Security;

namespace LunaArc.Core.LiveEdit;

/// <summary>
/// Tracks files extracted to an isolated preview session.
/// </summary>
public sealed class TempFileWatcher : IDisposable
{
    private readonly string _tempSessionDir;
    private readonly FileSystemWatcher _watcher;
    public event Action<string, string>? OnTempFileModified;
    private readonly ConcurrentDictionary<string, string> _trackedFiles = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public string SessionDirectory => _tempSessionDir;

    public TempFileWatcher()
    {
        _tempSessionDir = Path.Combine(Path.GetTempPath(), "LunaArc_LivePreview", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempSessionDir);

        _watcher = new FileSystemWatcher(_tempSessionDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += HandleFileChanged;
    }

    public string PrepareTempPathForEntry(string entryRelativePath)
    {
        string safeTempPath = ZipSlipGuard.ValidateAndResolvePath(_tempSessionDir, entryRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(safeTempPath)!);
        _trackedFiles[safeTempPath] = entryRelativePath;
        return safeTempPath;
    }

    public void OpenWithDefaultOsApp(string tempFilePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = tempFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not open the default application: {ex.Message}");
        }
    }

    private void HandleFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_trackedFiles.TryGetValue(e.FullPath, out string? entryRelPath))
        {
            OnTempFileModified?.Invoke(e.FullPath, entryRelPath);
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        try
        {
            if (Directory.Exists(_tempSessionDir))
                Directory.Delete(_tempSessionDir, recursive: true);
        }
        catch { }
    }
}
