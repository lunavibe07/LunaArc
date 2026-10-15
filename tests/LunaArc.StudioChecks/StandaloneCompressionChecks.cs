using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LunaArc.Core.Formats;
using LunaArc.Shell;
using LunaArc.Studio.Views;

internal static class StandaloneCompressionChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Window, string> capture)
    {
        check(StandaloneCompressionSession.IsCompressionAction(ExplorerAction.Add)
            && StandaloneCompressionSession.IsCompressionAction(ExplorerAction.AddZip)
            && !StandaloneCompressionSession.IsCompressionAction(ExplorerAction.Open)
            && !StandaloneCompressionSession.IsCompressionAction(ExplorerAction.Extract), "Startup isolates both compression commands and preserves Open");
        string source = Path.Combine(root, "standalone-compress-source.txt");
        File.WriteAllText(source, "Synthetic standalone compression fixture");
        using (var session = new StandaloneCompressionSession())
        {
            var operation = session.RunAsync(new(ExplorerAction.Add, source));
            var options = session.OptionsWindow!;
            string output = options.OutputPath;
            check(options.IsVisible && options.Owner == null && !session.ProgressWindow.IsVisible,
                "Explorer compression options have no browser owner or visible background progress window");
            capture(options, "compress-standalone.png");
            Click(options.FindControl<Button>("CancelOptionsButton")!);
            Pump(operation);
            check(!File.Exists(output) && File.Exists(source) && !session.ProgressWindow.IsVisible, "Cancelling standalone compression creates no output and preserves sources");
        }
        foreach (var action in new[] { ExplorerAction.Add, ExplorerAction.AddZip })
        {
            using var session = new StandaloneCompressionSession();
            var operation = session.RunAsync(new(action, source));
            var options = session.OptionsWindow!;
            string output = Path.Combine(root, action == ExplorerAction.Add ? "standalone-encrypted.larc" : "standalone-created.zip");
            options.Options.ArchiveName = output;
            options.Options.TestCreatedArchive = true;
            options.Options.AddRecoveryRecord = action == ExplorerAction.AddZip;
            options.Options.LockArchive = action == ExplorerAction.AddZip;
            if (action == ExplorerAction.Add) options.Options.PasswordInput = "synthetic-standalone-password";
            else check(options.Options.IsZip, "Add to ZIP selects ZIP in standalone options");
            Click(options.FindControl<Button>("AcceptOptionsButton")!);
            Pump(operation);
            var backend = new StandardArchiveHandler();
            using var job = new LunaArc.Core.Pipeline.LunaJobController();
            string extracted = Path.Combine(root, "standalone-roundtrip-" + action);
            Pump(backend.ExtractAsync(output, new LunaArc.Core.Contracts.ExtractOptions
            { DestinationDirectory = extracted, Password = action == ExplorerAction.Add ? "synthetic-standalone-password" : null }, job));
            check(File.Exists(output) && File.Exists(source) && !session.ProgressWindow.IsVisible
                && session.OptionsWindow == null && File.ReadAllText(Path.Combine(extracted, Path.GetFileName(source))) == File.ReadAllText(source),
                "Standalone " + action + " creates a valid archive and closes without a browser");
            if (action == ExplorerAction.AddZip)
            {
                check(File.Exists(output + ".lrec") && File.Exists(output + ".lunalock"), "Standalone compression preserves recovery and archive lock options");
                LunaArc.Core.Security.ArchiveLock.Unlock(output);
            }
        }
        using (var session = new StandaloneCompressionSession())
        {
            string removable = Path.Combine(root, "standalone-delete-source.txt");
            File.WriteAllText(removable, "Synthetic explicit cleanup fixture");
            string output = Path.Combine(root, "standalone-delete.zip");
            var operation = session.RunAsync(new(ExplorerAction.AddZip, removable));
            session.OptionsWindow!.Options.ArchiveName = output;
            session.OptionsWindow.Options.DeleteSourceAfterArchiving = true;
            Click(session.OptionsWindow.FindControl<Button>("AcceptOptionsButton")!);
            Pump(WaitUntil(() => !session.Progress.IsWorking));
            check(File.Exists(output) && !File.Exists(removable) && session.ProgressWindow.IsVisible
                && session.Progress.StatusMessage.Contains("Created and verified"),
                "Explicit source deletion verifies the archive and displays its cleanup result");
            session.Progress.CloseOrCancel();
            Pump(operation);
        }
        using (var session = new StandaloneCompressionSession())
        {
            var operation = session.RunAsync(new(ExplorerAction.Add, Path.Combine(root, "missing-compression-source")));
            check(session.ProgressWindow.IsVisible && session.ProgressWindow.Owner == null && !session.Progress.IsWorking
                && session.Progress.StatusMessage.Contains("could not finish"), "Standalone compression failures remain visible without a browser");
            capture(session.ProgressWindow, "compress-standalone-error.png");
            session.Progress.CloseOrCancel();
            Pump(operation);
            check(!session.ProgressWindow.IsVisible, "Acknowledging a compression error closes the standalone session");
        }
        using (var session = new StandaloneCompressionSession())
        {
            var operation = session.RunAsync(new(ExplorerAction.AddZip, source));
            string output = Path.Combine(root, "standalone-cancel-running.zip");
            session.OptionsWindow!.Options.ArchiveName = output;
            session.Progress.Controller.Pause();
            Click(session.OptionsWindow.FindControl<Button>("AcceptOptionsButton")!);
            Pump(WaitUntil(() => session.ProgressWindow.IsVisible));
            check(session.ProgressWindow.Owner == null && session.Progress.IsWorking && session.Progress.CanPause,
                "Running compression has only an ownerless compact progress window");
            capture(session.ProgressWindow, "compress-standalone-progress.png");
            session.ProgressWindow.Close();
            Pump(operation);
            check(!File.Exists(output) && File.Exists(source) && !session.ProgressWindow.IsVisible,
                "Closing active compression cancels a paused worker and preserves sources");
        }
        using (var session = new StandaloneCompressionSession())
        {
            var operation = session.RunAsync(new(ExplorerAction.AddZip, source));
            string output = session.OptionsWindow!.OutputPath;
            session.Progress.Controller.Cancel();
            Click(session.OptionsWindow.FindControl<Button>("AcceptOptionsButton")!);
            Pump(operation);
            check(!File.Exists(output) && File.Exists(source) && !session.ProgressWindow.IsVisible,
                "Standalone compression honors cancellation before archive creation");
        }
    }
    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
    private static void Pump(Task task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Standalone compression UI did not complete.");
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(2);
        }
        task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
    private static async Task WaitUntil(Func<bool> condition) { while (!condition()) await Task.Delay(10); }
}
