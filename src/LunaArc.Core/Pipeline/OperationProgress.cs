namespace LunaArc.Core.Pipeline;

public sealed record OperationProgress(string Stage, long Completed, long Total, string File,
    bool IsBytes, double BytesPerSecond, TimeSpan? Remaining)
{
    public bool IsIndeterminate => Total <= 0;
    public double Percent => Total > 0 ? Math.Clamp(100.0 * Completed / Total, 0, 100) : 0;
    public string Detail => IsBytes && BytesPerSecond > 0
        ? $"{BytesPerSecond / 1048576:F1} MiB/s" + (Remaining is { } time ? $" | ETA {time:hh\\:mm\\:ss}" : "")
        : Total > 0 && !IsBytes ? $"{Completed:N0} / {Total:N0} items" : "Estimating...";
}
