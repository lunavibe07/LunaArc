using System.Runtime.Versioning;
using LunaArc.Shell.Windows;
using Microsoft.Win32;

[SupportedOSPlatform("windows")]
internal static class WindowsShellChecks
{
    public static int Verify(string temporaryDirectory)
    {
        int checks = 0;
        string testKey = @"Software\LunaArc\ShellTests\" + Guid.NewGuid().ToString("N");
        string executableDirectory = Path.Combine(temporaryDirectory, "published folder with spaces");
        Directory.CreateDirectory(executableDirectory);
        string gui = Path.Combine(executableDirectory, "LunaArc.exe");
        string cli = Path.Combine(executableDirectory, "luna-cli.exe");
        File.WriteAllText(gui, "Synthetic test file; not executable.");
        File.WriteAllText(cli, "Synthetic test file; not executable.");
        try
        {
            using var classes = Registry.CurrentUser.CreateSubKey(testKey + @"\Classes");
            using (var zip = classes.CreateSubKey(".zip")) zip.SetValue("", "ExistingApp.Zip");
            using (var openWith = classes.CreateSubKey(@".zip\OpenWithProgids"))
                openWith.SetValue("ExistingApp.Zip", Array.Empty<byte>(), RegistryValueKind.None);
            using (var otherVerb = classes.CreateSubKey(@"SystemFileAssociations\.zip\shell\OtherApp"))
                otherVerb.SetValue("", "Existing application command");
            using (var legacy = classes.CreateSubKey(@"LunaArc.zip\shell\LunaArc_ExtractHere")) legacy.SetValue("", "Legacy menu");

            var registrar = new WinRegistryMenu(classes);
            registrar.RegisterContextMenuAndAssociations(gui, cli);
            Check(Read(classes, ".zip") == "ExistingApp.Zip", "Default ZIP application is preserved");
            foreach (string extension in new[] { ".larc", ".rar", ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz" })
            {
                string shell = $@"SystemFileAssociations\{extension}\shell";
                Check(Read(classes, shell + @"\LunaArc_ExtractHere\command") == $"\"{cli}\" extract-here \"%1\"", "Extract Here: " + extension);
                Check(Read(classes, shell + @"\LunaArc_ExtractSub\command") == $"\"{cli}\" extract-folder \"%1\"", "Extract to Folder: " + extension);
                Check(Read(classes, shell + @"\LunaArc_Open\command") == $"\"{gui}\" \"%1\"", "Open Archive: " + extension);
            }
            Check(Read(classes, @"*\shell\LunaArc_PackZip\command") == $"\"{cli}\" pack-zip \"%1\"", "File packing command is quoted");
            Check(Read(classes, @"Directory\shell\LunaArc_PackLarc\command") == $"\"{cli}\" pack-larc \"%1\"", "Directory packing command is quoted");
            using (var legacy = classes.OpenSubKey(@"LunaArc.zip\shell\LunaArc_ExtractHere"))
                Check(legacy == null, "Legacy duplicate extraction verb removed");

            Check(!registrar.UnregisterIfOwnedBy(Path.Combine(temporaryDirectory, "other installation")), "Uninstall from another installation preserves active menus");
            Check(Read(classes, @"SystemFileAssociations\.zip\shell\LunaArc_ExtractHere\command") != null, "Scoped cleanup leaves another installation's extraction verb intact");

            registrar.RegisterContextMenuAndAssociations(gui, cli);
            using (var verbs = classes.OpenSubKey(@"SystemFileAssociations\.zip\shell"))
                Check(verbs!.GetSubKeyNames().Length == 4, "Repeated installation is idempotent and preserves other verbs");

            registrar.UnregisterAll();
            Check(Read(classes, ".zip") == "ExistingApp.Zip", "Uninstall preserves default ZIP application");
            Check(Read(classes, @"SystemFileAssociations\.zip\shell\OtherApp") == "Existing application command", "Uninstall preserves another application's menu");
            using (var openWith = classes.OpenSubKey(@".zip\OpenWithProgids"))
            {
                Check(openWith!.GetValue("LunaArc.zip") == null, "Uninstall removes LunaArc Open With value");
                Check(openWith.GetValue("ExistingApp.Zip") != null, "Uninstall preserves another Open With value");
            }
            foreach (string key in new[] { @"SystemFileAssociations\.zip\shell\LunaArc_ExtractHere", @"Directory\shell\LunaArc_PackLarc", @"*\shell\LunaArc_PackZip", "LunaArc.zip" })
            {
                using var removed = classes.OpenSubKey(key);
                Check(removed == null, "Uninstall removes own key: " + key);
            }
            using (var legacyDefault = classes.CreateSubKey(".larc")) legacyDefault.SetValue("", "LunaArc.larc");
            registrar.UnregisterAll();
            Check(Read(classes, ".larc") == null, "Uninstall cleans dangling legacy default without deleting the extension");
            registrar.RegisterContextMenuAndAssociations(gui, cli);
            Check(registrar.UnregisterIfOwnedBy(executableDirectory), "Scoped cleanup removes menus owned by its installation");
            Check(Read(classes, @"SystemFileAssociations\.zip\shell\LunaArc_ExtractHere\command") == null, "Owned scoped cleanup removes extraction menu");
            Console.WriteLine($"PASS: {checks} isolated Windows shell-registration checks (no Explorer configuration changed).");
            return checks;
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testKey, false);
        }

        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + description);
            checks++;
        }
    }

    private static string? Read(RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue("") as string;
    }
}
