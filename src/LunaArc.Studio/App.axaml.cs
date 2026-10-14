using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LunaArc.Studio.ViewModels;
using LunaArc.Studio.Views;
using LunaArc.Shell;

namespace LunaArc.Studio;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            var window = new MainWindow
            {
                DataContext = vm
            };
            desktop.MainWindow = window;

            window.Opened += async (_, _) =>
            {
                try
                {
                    if (desktop.Args is { Length: > 0 } args && args[0].StartsWith("--shell-", StringComparison.Ordinal))
                        await window.HandleExplorerRequestAsync(ExplorerRequest.Parse(args));
                    else if (desktop.Args is { Length: 1 } && File.Exists(desktop.Args[0]))
                        await vm.OpenArchiveFileAsync(desktop.Args[0]);
                }
                catch (Exception ex) { vm.StatusMessage = "Could not start Explorer command: " + ex.Message; }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
