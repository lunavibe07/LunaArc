using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.Pipeline;
using LunaArc.Shell;
using LunaArc.Studio.Views;

internal static class EncryptedCreationChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Window, string> capture)
    {
        string source = Path.Combine(root, "password-general-source.txt"); File.WriteAllText(source, "Synthetic password General fixture");
        foreach (int format in new[] { 1, 2, 3 })
        {
            using var session = new StandaloneCompressionSession();
            var operation = session.RunAsync(new(ExplorerAction.Add, source));
            var options = session.OptionsWindow!;
            options.Options.SelectedFormatIndex = format;
            Click(options.FindControl<Button>("SetArchivePasswordButton")!);
            var dialog = options.OwnedWindows.Single();
            var panel = (StackPanel)dialog.Content!;
            var fields = panel.Children.OfType<TextBox>().ToArray();
            fields[0].Text = fields[1].Text = "synthetic-ui-暗号";
            if (format == 1) capture(dialog, "compression-aes-password.png");
            Click(panel.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single(button => button.Name == "AcceptArchivePassword"));
            check(options.Options.PasswordInput == "synthetic-ui-暗号" && !options.OwnedWindows.Any(), "General accepts password for format " + format);
            if (format == 2)
            {
                check(options.OutputPath.EndsWith(".tar.gz.7z") && !options.FindControl<CheckBox>("DeleteSourceAfterArchivingBox")!.IsEnabled,
                    "TAR password changes extension to .tar.gz.7z and disables nested source deletion");
                options.Options.PasswordInput = string.Empty;
                check(options.OutputPath.EndsWith(".tar.gz") && options.FindControl<CheckBox>("DeleteSourceAfterArchivingBox")!.IsEnabled,
                    "Removing TAR password restores ordinary TAR GZip and source deletion availability");
                options.Options.PasswordInput = "synthetic-ui-暗号";
                capture(options, "compression-encrypted-targz.png");
            }
            string output = options.OutputPath;
            options.Options.TestCreatedArchive = true;
            Click(options.FindControl<Button>("AcceptOptionsButton")!);
            Pump(operation);
            string destination = Path.Combine(root, "password-general-roundtrip-" + format);
            using var job = new LunaJobController();
            var backend = new StandardArchiveHandler();
            Pump(backend.ExtractAsync(output, new ExtractOptions { DestinationDirectory = destination, Password = "synthetic-ui-暗号" }, job));
            if (format == 2)
            {
                string inner = Directory.GetFiles(destination, "*.tar.gz").Single();
                string unpacked = destination + "-inner";
                Pump(backend.ExtractAsync(inner, new ExtractOptions { DestinationDirectory = unpacked }, job));
                destination = unpacked;
            }
            check(File.ReadAllText(Path.Combine(destination, Path.GetFileName(source))) == File.ReadAllText(source)
                && !session.ProgressWindow.IsVisible && options.Options.PasswordInput.Length == 0, "Password General creates/decrypts format " + format + " and clears draft secrets");
        }
    }
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void Pump(Task task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Encrypted creation UI did not complete.");
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(2);
        }
        task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
}
