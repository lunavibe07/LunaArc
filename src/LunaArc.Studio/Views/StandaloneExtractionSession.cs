using Avalonia.Controls;
using Avalonia.Threading;
using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.Security;
using LunaArc.Shell;
using LunaArc.Studio.ViewModels;

namespace LunaArc.Studio.Views;

/// <summary>An extraction-only application session; never constructs the archive browser window.</summary>
public sealed class StandaloneExtractionSession : IDisposable
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ExtractionProgressViewModel Progress { get; } = new();
    public ExtractionProgressWindow ProgressWindow { get; }
    public ExtractOptionsWindow? OptionsWindow { get; private set; }
    public static bool IsExtractionAction(ExplorerAction action) => action is ExplorerAction.Extract or ExplorerAction.ExtractHere or ExplorerAction.ExtractFolder;

    public StandaloneExtractionSession()
    {
        ProgressWindow = new ExtractionProgressWindow { DataContext = Progress };
        ProgressWindow.Closed += (_, _) => _closed.TrySetResult();
        Progress.CloseRequested += () => ProgressWindow.Close();
    }

    public async Task RunAsync(ExplorerRequest request, string? destinationOverride = null)
    {
        var job = Progress.Controller;
        if (!ProgressWindow.IsVisible) ProgressWindow.Show();
        try
        {
            if (!IsExtractionAction(request.Action)) throw new ArgumentException("This session supports extraction commands only.");
            Progress.ArchiveName = Path.GetFileName(request.Path);
            bool encrypted = await Task.Run(() => ArchivePasswordDetection.RequiresPasswordAsync(request.Path, job.Token));
            job.Token.ThrowIfCancellationRequested();
            string destination = destinationOverride ?? request.ExtractionDirectory;
            ExtractionChoice? choice = new(destination, string.Empty, OverwriteMode.SkipExisting);
            if (request.Action == ExplorerAction.Extract || encrypted)
            {
                ProgressWindow.Hide();
                OptionsWindow = new ExtractOptionsWindow(destination, encrypted);
                choice = await OptionsWindow.ShowStandaloneAsync();
                OptionsWindow = null;
                if (choice == null) return;
                ProgressWindow.Show();
            }
            job.Token.ThrowIfCancellationRequested();
            Progress.StatusMessage = "Reading archive...";
            var backend = new StandardArchiveHandler();
            string? password = choice.Password.Length == 0 ? null : choice.Password;
            await Task.Run(() => backend.ReadCatalogAsync(request.Path, password, job.Token));
            Progress.CanPause = true;
            Progress.StatusMessage = "Extracting files...";
            await Task.Run(() => backend.ExtractAsync(request.Path, new ExtractOptions
            {
                DestinationDirectory = choice.Destination, Password = password, SmartFolderCreation = false,
                UpdateMode = choice.UpdateMode, OverwriteMode = choice.OverwriteMode, KeepBrokenFiles = choice.KeepBrokenFiles,
                OnFileConflictCallback = path => Dispatcher.UIThread.InvokeAsync(() => ExtractionConflictDialog.ShowAsync(ProgressWindow, path)).GetAwaiter().GetResult()
            }, job, (done, total, file) => Dispatcher.UIThread.Post(() =>
            {
                if (!Progress.IsWorking || job.IsCancelled) return;
                Progress.ProgressPercent = Math.Clamp(100.0 * done / Math.Max(1, total), 0, 100);
                Progress.StatusMessage = "Extracting: " + file;
            })));
            job.Token.ThrowIfCancellationRequested();
            Progress.ProgressPercent = 100;
            if (choice.DisplayAfterExtraction)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(choice.Destination) { UseShellExecute = true }); }
                catch (Exception ex)
                {
                    Progress.Finish("Extraction completed; could not display the folder: " + ex.Message);
                    await _closed.Task;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Progress.Finish("Extraction could not finish: " + ex.Message);
            if (!ProgressWindow.IsVisible) ProgressWindow.Show();
            await _closed.Task;
        }
        finally
        {
            Progress.Finish(Progress.StatusMessage);
            OptionsWindow?.Close();
            OptionsWindow = null;
            ProgressWindow.Close();
        }
    }
    public void Dispose() => Progress.Dispose();
}
