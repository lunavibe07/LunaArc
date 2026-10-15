using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunaArc.Core.Pipeline;

namespace LunaArc.Studio.ViewModels;

public partial class ExtractionProgressViewModel : ObservableObject, IDisposable
{
    public LunaJobController Controller { get; } = new();
    public string CancellationMessage { get; init; } = "Cancelling extraction...";
    [ObservableProperty] private string _archiveName = string.Empty;
    [ObservableProperty] private string _statusMessage = "Checking archive encryption...";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _transferDetails = string.Empty;
    [ObservableProperty] private bool _isIndeterminate = true;
    public ExtractionProgressViewModel()
    {
        Controller.ProgressChanged += progress => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!IsWorking || Controller.IsCancelled) return;
            ApplyProgress(progress);
        });
    }
    public void ApplyProgress(OperationProgress progress)
    {
        ProgressPercent = progress.Percent; IsIndeterminate = progress.IsIndeterminate;
        StatusMessage = progress.Stage + (progress.File.Length > 0 ? ": " + progress.File : "...");
        TransferDetails = progress.Detail;
    }
    [ObservableProperty] private bool _canPause;
    [ObservableProperty] private string _pauseText = "Pause";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloseText))]
    private bool _isWorking = true;
    public string CloseText => IsWorking ? "Cancel" : "Close";
    public event Action? CloseRequested;

    [RelayCommand]
    private void PauseResume()
    {
        if (!CanPause) return;
        if (Controller.IsPaused) { Controller.Resume(); PauseText = "Pause"; }
        else { Controller.Pause(); PauseText = "Resume"; }
    }
    [RelayCommand]
    public void CloseOrCancel()
    {
        if (IsWorking) { Controller.Cancel(); StatusMessage = CancellationMessage; }
        else CloseRequested?.Invoke();
    }
    public void Finish(string message)
    {
        IsWorking = CanPause = false;
        IsIndeterminate = false;
        TransferDetails = string.Empty;
        StatusMessage = message;
    }
    public void Dispose() { Controller.Cancel(); Controller.Dispose(); }
}
