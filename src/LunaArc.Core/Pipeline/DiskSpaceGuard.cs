namespace LunaArc.Core.Pipeline;

public sealed record DiskSpaceRequirement(string Path, long Bytes, string Purpose);
public sealed record DiskSpaceVolume(string Name, long AvailableBytes);

/// <summary>Conservative estimates, not filesystem reservations. Unknown volumes still rely on I/O errors.</summary>
public static class DiskSpaceGuard
{
    public static bool Ensure(IEnumerable<DiskSpaceRequirement> requirements, Func<string, DiskSpaceVolume?>? probe = null)
    {
        probe ??= Probe;
        var volumes = new Dictionary<string, (long Needed, long Free, string Purpose)>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        bool complete = true;
        foreach (var requirement in requirements)
        {
            if (requirement.Bytes < 0) throw new ArgumentOutOfRangeException(nameof(requirements));
            if (requirement.Bytes == 0) continue;
            var volume = probe(requirement.Path);
            if (volume == null) { complete = false; continue; }
            volumes.TryGetValue(volume.Name, out var current);
            volumes[volume.Name] = (checked(current.Needed + requirement.Bytes), current.Needed == 0 ? volume.AvailableBytes : Math.Min(current.Free, volume.AvailableBytes), requirement.Purpose);
        }
        foreach (var (name, estimate) in volumes)
        {
            long reserve = Math.Max(16L * 1024 * 1024, estimate.Needed / 20);
            long needed = checked(estimate.Needed + reserve);
            if (needed > estimate.Free)
                throw new IOException($"Not enough free space on {name} for {estimate.Purpose}. Estimated need: {needed / 1048576.0:F1} MiB; available: {estimate.Free / 1048576.0:F1} MiB. Free space or choose another destination.");
        }
        return complete;
    }

    private static DiskSpaceVolume? Probe(string path)
    {
        try
        {
            string full = System.IO.Path.GetFullPath(path);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var drive = DriveInfo.GetDrives().OrderByDescending(d => d.Name.Length).FirstOrDefault(d =>
                full.Equals(System.IO.Path.TrimEndingDirectorySeparator(d.Name), comparison) ||
                full.StartsWith(System.IO.Path.EndsInDirectorySeparator(d.Name) ? d.Name : d.Name + System.IO.Path.DirectorySeparatorChar, comparison));
            drive ??= new DriveInfo(System.IO.Path.GetPathRoot(full)!);
            return new(drive.Name, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}
