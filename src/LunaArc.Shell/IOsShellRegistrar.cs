namespace LunaArc.Shell;

public interface IOsShellRegistrar
{
    void RegisterContextMenuAndAssociations(string guiExecutablePath, string cliExecutablePath);
    void UnregisterAll();
}

public static class ShellRegistrarFactory
{
    public static IOsShellRegistrar CreateForCurrentOs()
    {
        if (OperatingSystem.IsWindows())
            return new Windows.WinRegistryMenu();
        if (OperatingSystem.IsLinux())
            return new Linux.LinuxDesktopGenerator();
        throw new PlatformNotSupportedException("Shell integration supports Windows and Linux only.");
    }
}
