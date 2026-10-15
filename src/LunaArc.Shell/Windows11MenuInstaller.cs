using System.Diagnostics;

namespace LunaArc.Shell;

public static class Windows11MenuInstaller
{
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    // Build structured arguments; paths must never be interpolated into shell commands.
    public static ProcessStartInfo CreateStartInfo(string directory, bool trustCertificateConsent)
    {
        if (!trustCertificateConsent) throw new InvalidOperationException("Explicit certificate consent is required.");
        directory = Path.GetFullPath(directory);
        foreach (string file in new[] { "RegisterWindows11Menu.ps1", "LunaArc.exe", "LunaArc.ContextMenu.dll", "LunaArc.ShellMenu.msix", "LunaArc.Menu.cer" })
            if (!File.Exists(Path.Combine(directory, file)))
                throw new FileNotFoundException("The Windows 11 menu requires the complete published Windows package.", file);
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(directory, "RegisterWindows11Menu.ps1"), "-TrustCertificate" }) start.ArgumentList.Add(argument);
        return start;
    }

    public static async Task EnableAsync(string directory, bool trustCertificateConsent)
    {
        if (!IsSupported) throw new PlatformNotSupportedException("This menu requires Windows 11.");
        using var process = Process.Start(CreateStartInfo(directory, trustCertificateConsent))
            ?? throw new IOException("Could not start Windows menu registration.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        string details = await error;
        if (process.ExitCode != 0)
            throw new IOException("Menu registration was declined or failed. " + details.Trim());
    }
}
