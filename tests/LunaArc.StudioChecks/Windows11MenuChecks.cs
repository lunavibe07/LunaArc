using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using LunaArc.Shell;
using LunaArc.Studio.Views;

internal static class Windows11MenuChecks
{
    public static void Run(Window owner, string root, Action<bool, string> check, Action<Window, string> capture)
    {
        // Build commands only; never import certificates, register menus or launch PowerShell in UI checks.
        string directory = Path.Combine(root, "menu package with spaces & symbols");
        Directory.CreateDirectory(directory);
        foreach (string file in new[] { "RegisterWindows11Menu.ps1", "LunaArc.exe", "LunaArc.ContextMenu.dll", "LunaArc.ShellMenu.msix", "LunaArc.Menu.cer" })
            File.WriteAllText(Path.Combine(directory, file), "synthetic command fixture only");
        bool denied = false;
        try { Windows11MenuInstaller.CreateStartInfo(directory, false); }
        catch (InvalidOperationException) { denied = true; }
        check(denied, "Windows 11 registration cannot be prepared without certificate consent");
        var start = Windows11MenuInstaller.CreateStartInfo(directory, true);
        check(!start.UseShellExecute && start.CreateNoWindow && start.RedirectStandardOutput && start.RedirectStandardError,
            "Windows menu registration keeps the original user and captures process errors");
        check(start.ArgumentList.Contains(Path.Combine(directory, "RegisterWindows11Menu.ps1")) && start.ArgumentList.Last() == "-TrustCertificate" && start.Arguments.Length == 0,
            "Windows menu script path stays one structured argument, including spaces and shell symbols");
        File.Delete(Path.Combine(directory, "LunaArc.ShellMenu.msix"));
        bool missing = false;
        try { Windows11MenuInstaller.CreateStartInfo(directory, true); }
        catch (FileNotFoundException) { missing = true; }
        check(missing, "Incomplete Windows menu packages are rejected before starting a process");
        var dialog = new Windows11MenuDialog();
        Task<bool> decision = dialog.ShowDialog<bool>(owner);
        capture(dialog, "windows11-menu-consent.png");
        check(Find<TextBlock>("MenuTrustExplanation").Text!.Contains("for all users") &&
            Find<Button>("CancelWindows11MenuButton").IsDefault, "Windows menu consent explains certificate trust and defaults to Cancel");
        Find<Button>("CancelWindows11MenuButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(decision.IsCompleted && !decision.GetAwaiter().GetResult(), "Cancelling menu consent does not enable registration");
        dialog = new Windows11MenuDialog(); decision = dialog.ShowDialog<bool>(owner);
        Find<Button>("EnableWindows11MenuButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(decision.IsCompleted && decision.GetAwaiter().GetResult(), "Only explicit Enable accepts certificate trust (no OS registration in this check)");
        T Find<T>(string name) where T : Control => dialog.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
    }
}
