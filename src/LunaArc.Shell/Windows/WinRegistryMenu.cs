using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LunaArc.Shell.Windows;

/// <summary>
/// Registers per-user classic Explorer verbs independently of the default archive application.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinRegistryMenu : IOsShellRegistrar
{
    private static readonly string[] SupportedExtensions = { ".larc", ".rar", ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz" };
    private readonly RegistryKey? _testClasses;

    public WinRegistryMenu() { }

    // Test registration under an isolated key, without changing Explorer associations.
    internal WinRegistryMenu(RegistryKey testClasses) => _testClasses = testClasses;

    public void RegisterContextMenuAndAssociations(string guiExecutablePath, string cliExecutablePath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows shell integration requires Windows.");
        guiExecutablePath = Path.GetFullPath(guiExecutablePath);
        cliExecutablePath = Path.GetFullPath(cliExecutablePath);
        if (!File.Exists(guiExecutablePath) || !File.Exists(cliExecutablePath))
            throw new FileNotFoundException("Keep LunaArc.exe and luna-cli.exe together before registering shell integration.");

        WithClasses(classes =>
        {
            foreach (string ext in SupportedExtensions)
            {
                string progId = $"LunaArc{ext}";
                using var extKey = classes.CreateSubKey(ext);
                using var openWith = extKey.CreateSubKey("OpenWithProgids");
                openWith.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);

                using var progKey = classes.CreateSubKey(progId);
                progKey.SetValue("", $"LunaArc Archive ({ext})");
                using var icon = progKey.CreateSubKey("DefaultIcon");
                icon.SetValue("", $"\"{guiExecutablePath}\",0");
                using var open = progKey.CreateSubKey(@"shell\open\command");
                open.SetValue("", $"\"{guiExecutablePath}\" \"%1\"");

                string shell = $@"SystemFileAssociations\{ext}\shell";
                WriteVerb(classes, shell, "LunaArc_Open", "LunaArc: Open Archive", guiExecutablePath, null);
                WriteVerb(classes, shell, "LunaArc_ExtractHere", "LunaArc: Extract Here", cliExecutablePath, "extract-here");
                WriteVerb(classes, shell, "LunaArc_ExtractSub", "LunaArc: Extract to Folder", cliExecutablePath, "extract-folder");
                // Migrate legacy menu entries so they do not appear twice when LunaArc is the default app.
                classes.DeleteSubKeyTree($@"{progId}\shell\LunaArc_ExtractHere", false);
                classes.DeleteSubKeyTree($@"{progId}\shell\LunaArc_ExtractSub", false);
            }

            foreach (string shell in new[] { @"*\shell", @"Directory\shell" })
            {
                WriteVerb(classes, shell, "LunaArc_PackLarc", "LunaArc: Create .larc Archive", cliExecutablePath, "pack-larc");
                WriteVerb(classes, shell, "LunaArc_PackZip", "LunaArc: Create ZIP Archive", cliExecutablePath, "pack-zip");
            }
        });
        NotifyExplorer();
    }

    public void UnregisterAll()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows shell integration requires Windows.");
        WithClasses(classes =>
        {
            foreach (string ext in SupportedExtensions)
            {
                string progId = $"LunaArc{ext}";
                using var extKey = classes.OpenSubKey(ext, writable: true);
                // Older releases set this default value. Do not leave a reference to a removed ProgID.
                if (extKey?.GetValue("") is string value && value == progId) extKey.DeleteValue("", false);
                using var openWith = extKey?.OpenSubKey("OpenWithProgids", writable: true);
                openWith?.DeleteValue(progId, false);
                foreach (string verb in new[] { "LunaArc_Open", "LunaArc_ExtractHere", "LunaArc_ExtractSub" })
                    classes.DeleteSubKeyTree($@"SystemFileAssociations\{ext}\shell\{verb}", false);
                classes.DeleteSubKeyTree(progId, false);
            }
            foreach (string shell in new[] { @"*\shell", @"Directory\shell" })
                foreach (string verb in new[] { "LunaArc_PackLarc", "LunaArc_PackZip" })
                    classes.DeleteSubKeyTree($@"{shell}\{verb}", false);
        });
        NotifyExplorer();
    }

    public bool UnregisterIfOwnedBy(string installationDirectory)
    {
        string directory = Path.GetFullPath(installationDirectory);
        string cli = Path.Combine(directory, "luna-cli.exe");
        string gui = Path.Combine(directory, "LunaArc.exe");
        bool removed = false;
        WithClasses(classes =>
        {
            foreach (string ext in SupportedExtensions)
            {
                string shell = $@"SystemFileAssociations\{ext}\shell";
                removed |= RemoveOwnedVerb(classes, shell, "LunaArc_Open", $"\"{gui}\" \"%1\"");
                removed |= RemoveOwnedVerb(classes, shell, "LunaArc_ExtractHere", $"\"{cli}\" extract-here \"%1\"");
                removed |= RemoveOwnedVerb(classes, shell, "LunaArc_ExtractSub", $"\"{cli}\" extract-folder \"%1\"");
                string progId = $"LunaArc{ext}";
                if (CommandMatches(classes, $@"{progId}\shell\open", $"\"{gui}\" \"%1\""))
                {
                    using var extKey = classes.OpenSubKey(ext, writable: true);
                    if (extKey?.GetValue("") is string value && value.Equals(progId, StringComparison.OrdinalIgnoreCase))
                        extKey.DeleteValue("", false);
                    using var openWith = extKey?.OpenSubKey("OpenWithProgids", writable: true);
                    openWith?.DeleteValue(progId, false);
                    classes.DeleteSubKeyTree(progId, false);
                    removed = true;
                }
            }
            foreach (string shell in new[] { @"*\shell", @"Directory\shell" })
            {
                removed |= RemoveOwnedVerb(classes, shell, "LunaArc_PackLarc", $"\"{cli}\" pack-larc \"%1\"");
                removed |= RemoveOwnedVerb(classes, shell, "LunaArc_PackZip", $"\"{cli}\" pack-zip \"%1\"");
            }
        });
        if (removed) NotifyExplorer();
        return removed;
    }

    private static bool CommandMatches(RegistryKey classes, string verbPath, string expected)
    {
        using var command = classes.OpenSubKey(verbPath + @"\command");
        return command?.GetValue("") is string value && value.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RemoveOwnedVerb(RegistryKey classes, string shell, string name, string expected)
    {
        string verbPath = $@"{shell}\{name}";
        if (!CommandMatches(classes, verbPath, expected)) return false;
        classes.DeleteSubKeyTree(verbPath, false);
        return true;
    }

    private static void WriteVerb(RegistryKey classes, string shell, string name, string label, string executable, string? command)
    {
        using var verb = classes.CreateSubKey($@"{shell}\{name}");
        verb.SetValue("", label);
        verb.SetValue("Icon", $"\"{executable}\",0");
        verb.SetValue("MultiSelectModel", "Single");
        using var invocation = verb.CreateSubKey("command");
        invocation.SetValue("", command == null
            ? $"\"{executable}\" \"%1\""
            : $"\"{executable}\" {command} \"%1\"");
    }

    private void WithClasses(Action<RegistryKey> action)
    {
        if (_testClasses != null) { action(_testClasses); return; }
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        action(classes);
    }

    private void NotifyExplorer()
    {
        if (_testClasses == null && OperatingSystem.IsWindows())
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
