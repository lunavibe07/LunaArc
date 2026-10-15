using Avalonia.Threading;
using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Shell;
using LunaArc.Studio.ViewModels;

namespace LunaArc.Studio.Views;

/// <summary>An Explorer compression session without an archive browser window.</summary>
public sealed class StandaloneCompressionSession : IDisposable
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ExtractionProgressViewModel Progress { get; } = new() { CancellationMessage = "Cancelling compression...", StatusMessage = "Preparing compression options..." };
    public ExtractionProgressWindow ProgressWindow { get; }
    public ArchiveOptionsWindow? OptionsWindow { get; private set; }
    public static bool IsCompressionAction(ExplorerAction action) => action is ExplorerAction.Add or ExplorerAction.AddZip;

    public StandaloneCompressionSession()
    {
        ProgressWindow = new ExtractionProgressWindow { Title = "Compressing - LunaArc", DataContext = Progress };
        ProgressWindow.Closed += (_, _) => _closed.TrySetResult();
        Progress.CloseRequested += () => ProgressWindow.Close();
    }

    public async Task RunAsync(ExplorerRequest request)
    {
        using var settings = new MainWindowViewModel();
        var job = Progress.Controller;
        try
        {
            if (!IsCompressionAction(request.Action)) throw new ArgumentException("This session supports compression commands only.");
            if (!File.Exists(request.Path) && !Directory.Exists(request.Path)) throw new FileNotFoundException("The selected source no longer exists.");
            settings.SelectedFormatIndex = request.Action == ExplorerAction.AddZip ? 1 : 0;
            ProgressWindow.Hide();
            OptionsWindow = new ArchiveOptionsWindow(settings, new[] { request.Path });
            if (!await OptionsWindow.ShowStandaloneAsync()) return;
            string output = OptionsWindow.OutputPath;
            OptionsWindow = null;
            job.Token.ThrowIfCancellationRequested();
            Progress.ArchiveName = Path.GetFileName(output);
            Progress.StatusMessage = "Creating archive and requested companion files...";
            Progress.CanPause = true;
            ProgressWindow.Show();
            var options = new CompressOptions
            {
                OutputArchivePath = CompressionOptionsViewModel.ValidateOutputPath(output, new[] { request.Path }),
                Format = settings.SelectedFormatIndex switch { 1 => OutputArchiveFormat.Zip, 2 => OutputArchiveFormat.TarGz, 3 => OutputArchiveFormat.SevenZip, _ => OutputArchiveFormat.LarcNative },
                Level = (LunaCompressionLevel)Math.Clamp(settings.SelectedLevelIndex, 0, 5),
                Password = settings.PasswordInput.Length == 0 ? null : settings.PasswordInput,
                ArchiveComment = settings.ArchiveComment, SolidArchive = settings.SolidArchive,
                DeleteSourceAfterArchiving = settings.DeleteSourceAfterArchiving
            };
            string result = await Task.Run(() => ArchiveCreationService.CreateAsync(new[] { request.Path }, options, job,
                settings.CreateSfx, settings.AddRecoveryRecord, settings.LockArchive, settings.TestCreatedArchive,
                progress: (done, total, file) => Dispatcher.UIThread.Post(() =>
                {
                    if (!Progress.IsWorking || job.IsCancelled) return;
                    Progress.ProgressPercent = Math.Clamp(100.0 * done / Math.Max(1, total), 0, 100);
                    Progress.StatusMessage = "Compressing: " + file;
                })));
            Progress.ProgressPercent = 100;
            if (options.DeleteSourceAfterArchiving)
            {
                // Source cleanup can complete partially; keep its result visible to the user.
                Progress.Finish(result);
                await _closed.Task;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Progress.Finish("Compression could not finish: " + ex.Message);
            if (!ProgressWindow.IsVisible) ProgressWindow.Show();
            await _closed.Task;
        }
        finally
        {
            settings.PasswordInput = string.Empty;
            Progress.Finish(Progress.StatusMessage);
            OptionsWindow?.Close();
            OptionsWindow = null;
            ProgressWindow.Close();
        }
    }
    public void Dispose() => Progress.Dispose();
}
