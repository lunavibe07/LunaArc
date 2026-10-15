using System.Diagnostics;
using LunaArc.Core.Contracts;
using LunaArc.Core.Pipeline;

namespace LunaArc.Core.Formats;

/// <summary>Creates standard 7z archives. Reading and guarded extraction remain managed.</summary>
public static class SevenZipEncoder
{
    public static string ExecutablePath => Environment.GetEnvironmentVariable("LUNAARC_7ZIP_PATH")
        ?? Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "LunaArc.7zr.exe" : "LunaArc.7zz");
    public static bool IsAvailable => File.Exists(ExecutablePath);

    public static async Task CreateAsync(IReadOnlyList<string> sources, CompressOptions options, LunaJobController job)
    {
        if (!IsAvailable) throw new FileNotFoundException("7z creation requires the packaged 7-Zip encoder. Run the publish script or set LUNAARC_7ZIP_PATH.");
        if (options.SplitVolumeSizeBytes.HasValue || !string.IsNullOrEmpty(options.ArchiveComment))
            throw new NotSupportedException("7z output does not support split volumes or comments in LunaArc.");
        string stage = Directory.CreateTempSubdirectory("LunaArc_7z_").FullName;
        try
        {
            string inputDirectory = Path.Combine(stage, "input");
            Directory.CreateDirectory(inputDirectory);
            foreach (string source in sources)
            {
                string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
                for (string? ancestor = full; ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
                    if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("7z sources cannot traverse symbolic links or reparse points.");
                string name = Path.GetFileName(full);
                if (string.IsNullOrEmpty(name)) throw new ArgumentException("Filesystem roots cannot be archived as 7z sources.");
                await CopyAsync(full, Path.Combine(inputDirectory, name), job);
            }
            job.WaitIfPaused();
            job.Token.ThrowIfCancellationRequested();
            var info = new ProcessStartInfo(ExecutablePath) { UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = inputDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
            int level = new[] { 0, 1, 3, 5, 7, 9 }[(int)options.Level];
            foreach (string arg in new[] { "a", "-t7z", "-y", "-bd", "-bso0", "-bse0", "-m0=LZMA2", "-md=32m", "-mmt=2",
                $"-mx={level}", options.SolidArchive ? "-ms=on" : "-ms=off", "--", Path.Combine(stage, "result.7z"), "." })
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info) ?? throw new IOException("Could not start the 7-Zip encoder.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cancel = job.Token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            job.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException($"7-Zip encoding failed (exit code {process.ExitCode}). Sources were preserved.");
            job.WaitIfPaused();
            job.Token.ThrowIfCancellationRequested();
            File.Move(Path.Combine(stage, "result.7z"), options.OutputArchivePath, overwrite: false);
        }
        finally { Directory.Delete(stage, recursive: true); }
    }

    private static async Task CopyAsync(string source, string target, LunaJobController job)
    {
        job.WaitIfPaused();
        job.Token.ThrowIfCancellationRequested();
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("7z creation does not follow symbolic links or reparse points.");
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("7z source names overlap. Select distinct top-level names.");
        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(target);
            foreach (string child in Directory.EnumerateFileSystemEntries(source)) await CopyAsync(child, Path.Combine(target, Path.GetFileName(child)), job);
            Directory.SetLastWriteTimeUtc(target, Directory.GetLastWriteTimeUtc(source));
        }
        else
        {
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                await input.CopyToAsync(output, job.Token);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        }
    }
}
