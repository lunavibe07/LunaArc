using Avalonia;
using Avalonia.Controls;
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
            ExplorerRequest? request = null;
            string? startupError = null;
            string? sfxDestination = null;
            try
            {
                if (Environment.ProcessPath is { } self && LunaArc.Core.Formats.SfxArchive.IsSfx(self))
                {
                    request = new ExplorerRequest(ExplorerAction.Extract, self);
                    sfxDestination = Path.Combine(Path.GetDirectoryName(self)!, Path.GetFileNameWithoutExtension(self) + "-extracted");
                }
                else if (desktop.Args is { Length: > 0 } args && args[0].StartsWith("--shell-", StringComparison.Ordinal))
                    request = ExplorerRequest.Parse(args);
            }
            catch (Exception ex) { startupError = "Could not start Explorer command: " + ex.Message; }

            if (request != null && StandaloneExtractionSession.IsExtractionAction(request.Action))
            {
                // Keep the process alive while the root options and progress windows switch.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var session = new StandaloneExtractionSession();
                desktop.MainWindow = session.ProgressWindow;
                bool started = false;
                session.ProgressWindow.Opened += async (_, _) =>
                {
                    if (started) return;
                    started = true;
                    try { await session.RunAsync(request, sfxDestination); }
                    finally { session.Dispose(); desktop.Shutdown(); }
                };
            }
            else if (request != null && StandaloneCompressionSession.IsCompressionAction(request.Action))
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var session = new StandaloneCompressionSession();
                desktop.MainWindow = session.ProgressWindow;
                bool started = false;
                session.ProgressWindow.Opened += async (_, _) =>
                {
                    if (started) return;
                    started = true;
                    try { await session.RunAsync(request); }
                    finally { session.Dispose(); desktop.Shutdown(); }
                };
            }
            else
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm };
                desktop.MainWindow = window;
                window.Opened += async (_, _) =>
                {
                    try
                    {
                        if (startupError != null) vm.StatusMessage = startupError;
                        else if (request != null) await window.HandleExplorerRequestAsync(request);
                        else if (desktop.Args is { Length: 1 } && File.Exists(desktop.Args[0]))
                            await vm.OpenArchiveFileAsync(desktop.Args[0]);
                    }
                    catch (Exception ex) { vm.StatusMessage = "Could not start Explorer command: " + ex.Message; }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
