namespace LunaArc.Core.Pipeline;

/// <summary>
/// Controls pause, resume and cancellation of archive operations.
/// </summary>
public sealed class LunaJobController : IDisposable
{
    private readonly ManualResetEventSlim _pauseGate = new(initialState: true);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _progressLock = new();
    private readonly System.Diagnostics.Stopwatch _stageClock = new();
    private string _stage = string.Empty;
    private long _lastCompleted;
    private double _lastReport;
    public event Action<OperationProgress>? ProgressChanged;
    public Func<string, DiskSpaceVolume?>? FreeSpaceProbe { get; init; }

    public void Report(string stage, long completed = 0, long total = 0, string file = "", bool isBytes = true)
    {
        OperationProgress update;
        lock (_progressLock)
        {
            bool changed = _stage != stage || completed < _lastCompleted;
            if (changed) { _stage = stage; _stageClock.Restart(); _lastReport = -1; if (IsPaused) _stageClock.Stop(); }
            _lastCompleted = completed;
            double seconds = _stageClock.Elapsed.TotalSeconds;
            if (!changed && completed < total && seconds - _lastReport < 0.1) return;
            _lastReport = seconds;
            double rate = isBytes && seconds >= 0.5 && completed > 0 ? completed / seconds : 0;
            update = new(stage, completed, total, file, isBytes, rate,
                rate > 0 && total >= completed ? TimeSpan.FromSeconds(Math.Min((total - completed) / rate, TimeSpan.MaxValue.TotalSeconds - 1)) : null);
        }
        ProgressChanged?.Invoke(update);
    }

    public bool IsPaused => !_pauseGate.IsSet;
    public bool IsCancelled => _cts.IsCancellationRequested;
    public CancellationToken Token => _cts.Token;

    public void Pause() { lock (_progressLock) { _pauseGate.Reset(); _stageClock.Stop(); } }
    public void Resume() { lock (_progressLock) { _pauseGate.Set(); _stageClock.Start(); } }

    public void Cancel()
    {
        // Publish cancellation before releasing paused workers.
        _cts.Cancel();
        _pauseGate.Set();
    }

    public void WaitIfPaused() => _pauseGate.Wait(_cts.Token);

    public void WaitIfPaused(CancellationToken cancellationToken) => _pauseGate.Wait(cancellationToken);

    public void Dispose()
    {
        _pauseGate.Dispose();
        _cts.Dispose();
    }
}
