using LunaArc.Core.Contracts;

namespace LunaArc.Core.Pipeline;

internal static class ExtractionUpdatePolicy
{
    public static bool ShouldExtract(string target, DateTime? archiveModified, ExtractionUpdateMode mode)
    {
        if (mode == ExtractionUpdateMode.ExtractAndReplace) return true;
        if (mode is not (ExtractionUpdateMode.ExtractAndUpdate or ExtractionUpdateMode.FreshenExisting))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (!File.Exists(target)) return mode == ExtractionUpdateMode.ExtractAndUpdate;
        // Without a usable timestamp, preserve an existing file rather than guess.
        if (!archiveModified.HasValue || archiveModified.Value.Ticks == 0) return false;
        return archiveModified.Value.ToUniversalTime() > File.GetLastWriteTimeUtc(target);
    }
}
