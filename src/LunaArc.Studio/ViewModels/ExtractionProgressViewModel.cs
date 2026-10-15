using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunaArc.Core.Pipeline;

namespace LunaArc.Studio.ViewModels;

public partial class ExtractionProgressViewModel : ObservableObject, IDisposable
{
    public LunaJobController Controller { get; } = new();
    [ObservableProperty] private string _archiveName = string.Empty;
    [ObservableProperty] private string _statusMessage = "Checking archive encryption...";
    [ObservableProperty] private double _progressPercent;
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
        if (IsWorking) { Controller.Cancel(); StatusMessage = "Cancelling extraction..."; }
        else CloseRequested?.Invoke();
    }
    public void Finish(string message)
    {
        IsWorking = CanPause = false;
        StatusMessage = message;
    }
    public void Dispose() { Controller.Cancel(); Controller.Dispose(); }
}
