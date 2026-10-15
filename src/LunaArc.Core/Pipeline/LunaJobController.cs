namespace LunaArc.Core.Pipeline;

/// <summary>
/// Controls pause, resume and cancellation of archive operations.
/// </summary>
public sealed class LunaJobController : IDisposable
{
    private readonly ManualResetEventSlim _pauseGate = new(initialState: true);
    private readonly CancellationTokenSource _cts = new();

    public bool IsPaused => !_pauseGate.IsSet;
    public bool IsCancelled => _cts.IsCancellationRequested;
    public CancellationToken Token => _cts.Token;

    public void Pause() => _pauseGate.Reset();
    public void Resume() => _pauseGate.Set();

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
