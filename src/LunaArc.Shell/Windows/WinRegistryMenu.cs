using Microsoft.Win32;

namespace LunaArc.Shell.Windows;

/// <summary>
/// Registers per-user Windows shell commands.
/// </summary>
public sealed class WinRegistryMenu : IOsShellRegistrar
{
    private static readonly string[] SupportedExtensions = { ".larc", ".rar", ".zip", ".7z", ".tar", ".gz" };

    public void RegisterContextMenuAndAssociations(string guiExecutablePath, string cliExecutablePath)
    {
        if (!OperatingSystem.IsWindows()) return;
        guiExecutablePath = Path.GetFullPath(guiExecutablePath);
        cliExecutablePath = Path.GetFullPath(cliExecutablePath);
        if (!File.Exists(guiExecutablePath) || !File.Exists(cliExecutablePath))
            throw new FileNotFoundException("Publish the GUI and CLI to the same directory before registering shell integration.");

        foreach (string ext in SupportedExtensions)
        {
            using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
            extKey?.SetValue("", $"LunaArc{ext}");

            using var progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\LunaArc{ext}");
            progKey?.SetValue("", $"LunaArc Archive ({ext})");

            using var cmdKey = progKey?.CreateSubKey(@"shell\open\command");
            cmdKey?.SetValue("", $"\"{guiExecutablePath}\" \"%1\"");

            using var extractHereKey = progKey?.CreateSubKey(@"shell\LunaArc_ExtractHere");
            extractHereKey?.SetValue("", "🌙 LunaArc: Extract Here");
            extractHereKey?.CreateSubKey("command")?.SetValue("", $"\"{cliExecutablePath}\" extract-here \"%1\"");

            using var extractFolderKey = progKey?.CreateSubKey(@"shell\LunaArc_ExtractSub");
            extractFolderKey?.SetValue("", "🌙 LunaArc: Extract to Folder");
            extractFolderKey?.CreateSubKey("command")?.SetValue("", $"\"{cliExecutablePath}\" extract-smart \"%1\"");
        }

        string[] targets = { @"Software\Classes\*\shell", @"Software\Classes\Directory\shell" };
        foreach (string target in targets)
        {
            using var packLarc = Registry.CurrentUser.CreateSubKey($@"{target}\LunaArc_PackLarc");
            packLarc?.SetValue("", "🌙 LunaArc: Create .larc Archive");
            packLarc?.CreateSubKey("command")?.SetValue("", $"\"{cliExecutablePath}\" pack-larc \"%1\"");

            using var packZip = Registry.CurrentUser.CreateSubKey($@"{target}\LunaArc_PackZip");
            packZip?.SetValue("", "🌙 LunaArc: Create ZIP Archive");
            packZip?.CreateSubKey("command")?.SetValue("", $"\"{cliExecutablePath}\" pack-zip \"%1\"");
        }
    }

    public void UnregisterAll()
    {
        if (!OperatingSystem.IsWindows()) return;

        foreach (string ext in SupportedExtensions)
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\LunaArc{ext}", throwOnMissingSubKey: false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\LunaArc_PackLarc", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\LunaArc_PackZip", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\LunaArc_PackLarc", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\LunaArc_PackZip", throwOnMissingSubKey: false);
    }
}
