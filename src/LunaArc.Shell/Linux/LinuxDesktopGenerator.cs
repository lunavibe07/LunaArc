namespace LunaArc.Shell.Linux;

/// <summary>
/// Creates a desktop launcher and Nautilus scripts for the current Linux user.
/// </summary>
public sealed class LinuxDesktopGenerator : IOsShellRegistrar
{
    private static string UserDataRoot => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } root
        && Path.IsPathFullyQualified(root)
            ? root
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    public void RegisterContextMenuAndAssociations(string guiExecutablePath, string cliExecutablePath)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Linux desktop integration requires Linux.");
        guiExecutablePath = Path.GetFullPath(guiExecutablePath);
        cliExecutablePath = Path.GetFullPath(cliExecutablePath);
        if (!File.Exists(guiExecutablePath) || !File.Exists(cliExecutablePath))
            throw new FileNotFoundException("Publish the GUI and CLI to the same directory before registering shell integration.");

        string appsDir = Path.Combine(UserDataRoot, "applications");
        Directory.CreateDirectory(appsDir);
        File.WriteAllText(Path.Combine(appsDir, "lunaarc.desktop"), string.Join('\n', new[]
        {
            "[Desktop Entry]",
            "Type=Application",
            "Name=LunaArc Archiver",
            "Comment=Open and extract archives",
            $"Exec={QuoteDesktopArgument(guiExecutablePath)} %f",
            "Icon=package-x-generic",
            "Terminal=false",
            "Categories=Utility;Archiving;Compression;",
            "MimeType=application/zip;application/vnd.rar;application/x-7z-compressed;application/x-tar;application/gzip;",
            ""
        }));

        string scriptsDir = Path.Combine(UserDataRoot, "nautilus", "scripts");
        Directory.CreateDirectory(scriptsDir);
        WriteScript(Path.Combine(scriptsDir, "LunaArc - Extract Here"), cliExecutablePath, "extract-here");
        WriteScript(Path.Combine(scriptsDir, "LunaArc - Create .larc Archive"), cliExecutablePath, "pack-larc");
    }

    public void UnregisterAll()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Linux desktop integration requires Linux.");
        foreach (string relativePath in new[]
        {
            "applications/lunaarc.desktop",
            "nautilus/scripts/LunaArc - Extract Here",
            "nautilus/scripts/LunaArc - Create .larc Archive"
        })
        {
            string path = Path.Combine(UserDataRoot, relativePath);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string QuoteDesktopArgument(string path)
    {
        if (path.Any(char.IsControl)) throw new ArgumentException("Executable paths cannot contain control characters.");
        // Desktop Entry escaping is applied after Exec argument escaping.
        string escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%");
        return "\"" + escaped.Replace("\\", "\\\\") + "\"";
    }

    private static void WriteScript(string path, string executable, string command)
    {
        string quotedExecutable = "'" + executable.Replace("'", "'\"'\"'") + "'";
        File.WriteAllText(path, $"#!/usr/bin/env bash\nset -euo pipefail\nfor file in \"$@\"; do\n  {quotedExecutable} {command} \"$file\"\ndone\n");
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
