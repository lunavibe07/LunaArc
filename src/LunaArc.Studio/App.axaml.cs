using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LunaArc.Studio.ViewModels;
using LunaArc.Studio.Views;

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
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };

            if (desktop.Args is { Length: > 0 } && File.Exists(desktop.Args[0]))
            {
                _ = vm.OpenArchiveFileAsync(desktop.Args[0]);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
