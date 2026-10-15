using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LunaArc.Shell;
using LunaArc.Studio.ViewModels;
using LunaArc.Studio.Views;

internal static class StandaloneExtractionChecks
{
    public static void Run(string root, string zip, string encryptedLarc, string password,
        Action<bool, string> check, Action<Window, string> capture)
    {
        check(StandaloneExtractionSession.IsExtractionAction(ExplorerAction.Extract)
            && StandaloneExtractionSession.IsExtractionAction(ExplorerAction.ExtractHere)
            && StandaloneExtractionSession.IsExtractionAction(ExplorerAction.ExtractFolder)
            && !StandaloneExtractionSession.IsExtractionAction(ExplorerAction.Open)
            && !StandaloneExtractionSession.IsExtractionAction(ExplorerAction.Add), "Startup routes only extraction actions into a standalone session");
        using (var session = new StandaloneExtractionSession())
        {
            string output = Path.Combine(root, "standalone-cancelled");
            var operation = session.RunAsync(new(ExplorerAction.Extract, zip), output);
            Pump(WaitUntil(() => session.OptionsWindow?.IsVisible == true));
            var options = session.OptionsWindow!;
            check(options.Owner == null && !session.ProgressWindow.IsVisible, "Standalone options are the only visible session window and have no browser owner");
            capture(options, "extract-standalone.png");
            Click(options.FindControl<Button>("CancelExtractionButton")!);
            Pump(operation);
            check(!session.ProgressWindow.IsVisible && session.OptionsWindow == null && !Directory.Exists(output), "Cancelling standalone options closes the session without creating a destination");
        }
        using (var session = new StandaloneExtractionSession())
        {
            string output = Path.Combine(root, "standalone-conflict"); Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "readme.md"), "Standalone original");
            var operation = session.RunAsync(new(ExplorerAction.Extract, zip), output);
            Pump(WaitUntil(() => session.OptionsWindow?.IsVisible == true));
            var options = session.OptionsWindow!;
            options.FindControl<RadioButton>("AskOverwriteRadio")!.IsChecked = true;
            Click(options.FindControl<Button>("StartExtractionButton")!);
            Pump(WaitUntil(() => session.ProgressWindow.OwnedWindows.Any()));
            var conflict = session.ProgressWindow.OwnedWindows.Single();
            check(session.ProgressWindow.IsVisible && session.ProgressWindow.Owner == null && conflict.Owner == session.ProgressWindow,
                "Standalone overwrite prompt belongs to the visible progress window");
            capture(session.ProgressWindow, "extract-standalone-progress.png");
            var buttons = ((StackPanel)conflict.Content!).Children.OfType<StackPanel>().Single();
            Click(buttons.Children.OfType<Button>().Single(button => button.Name == "ConflictSkip"));
            Pump(operation);
            check(File.ReadAllText(Path.Combine(output, "readme.md")) == "Standalone original"
                && File.Exists(Path.Combine(output, "project.json")) && !session.ProgressWindow.IsVisible,
                "Standalone extraction preserves skipped conflicts and automatically closes after success");
        }
        using (var session = new StandaloneExtractionSession())
        {
            string output = Path.Combine(root, "standalone-password");
            var operation = session.RunAsync(new(ExplorerAction.Extract, encryptedLarc), output);
            Pump(WaitUntil(() => session.OptionsWindow?.IsVisible == true));
            var options = session.OptionsWindow!;
            check(options.FindControl<TabItem>("ExtractionPasswordTab")!.IsVisible && options.Owner == null, "Standalone encrypted extraction offers Password without a browser owner");
            options.FindControl<TextBox>("ExtractionPasswordBox")!.Text = password;
            Click(options.FindControl<Button>("StartExtractionButton")!);
            Pump(operation);
            check(Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length > 0 && !session.ProgressWindow.IsVisible,
                "Standalone password entry decrypts LARC and closes after extraction");
        }
        using (var session = new StandaloneExtractionSession())
        {
            string here = Path.Combine(root, "standalone-here"); Directory.CreateDirectory(here);
            string copy = Path.Combine(here, "sample.zip"); File.Copy(zip, copy);
            Pump(session.RunAsync(new(ExplorerAction.ExtractHere, copy)));
            check(File.Exists(Path.Combine(here, "readme.md")) && session.OptionsWindow == null && !session.ProgressWindow.IsVisible,
                "Standalone Extract Here runs directly with no options or archive browser");
        }
        using (var session = new StandaloneExtractionSession())
        {
            var operation = session.RunAsync(new(ExplorerAction.ExtractHere, Path.Combine(root, "missing-standalone.zip")));
            Pump(WaitUntil(() => !session.Progress.IsWorking));
            check(session.ProgressWindow.IsVisible && session.Progress.StatusMessage.Contains("could not finish") && session.Progress.CloseText == "Close",
                "Standalone failures remain visible for acknowledgement");
            capture(session.ProgressWindow, "extract-standalone-error.png");
            Click(session.ProgressWindow.FindControl<Button>("CancelStandaloneExtractionButton")!);
            Pump(operation);
            check(!session.ProgressWindow.IsVisible, "Closing a standalone error finishes the session");
        }
        using (var vm = new ExtractionProgressViewModel { CanPause = true, ArchiveName = "Synthetic pause/cancel check" })
        {
            var progress = new ExtractionProgressWindow { DataContext = vm }; progress.Show();
            Click(progress.FindControl<Button>("PauseExtractionButton")!);
            check(vm.Controller.IsPaused && vm.PauseText == "Resume", "Standalone progress can pause a running job");
            progress.Close();
            check(progress.IsVisible && vm.Controller.IsCancelled, "Closing active standalone progress requests cancellation and waits for the job");
            vm.Finish("Cancelled"); progress.Close();
            check(!progress.IsVisible, "Standalone progress closes after cancellation is acknowledged");
        }
    }

    private static void Click(Button button)
    {
        if (button.Command != null) button.Command.Execute(button.CommandParameter);
        else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
    private static async Task WaitUntil(Func<bool> condition) { while (!condition()) await Task.Delay(10); }
    private static void Pump(Task task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Standalone extraction UI did not complete.");
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(2);
        }
        task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
}
