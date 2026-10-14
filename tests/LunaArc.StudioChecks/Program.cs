using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using LunaArc.Studio;
using LunaArc.Studio.ViewModels;
using LunaArc.Studio.Views;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string outputDirectory = Path.GetFullPath(args.Length > 0 ? args[0] : ".tools/ui-preview");
        Directory.CreateDirectory(outputDirectory);
        string temporary = Path.Combine(Path.GetTempPath(), "LunaArc_UI_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        int checks = 0;
        var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Check(window.ActualThemeVariant == ThemeVariant.Light, "Light desktop theme");
            Check(!window.FindControl<Button>("ExtractButton")!.IsEnabled, "Extraction disabled without an archive");
            Capture(window, "empty.png");

            string archivePath = Path.Combine(temporary, "Sample archive.zip");
            using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                foreach (string path in new[] { "Documents/invoice.txt", "Documents/specification.txt", "Photos/holiday.txt", "notes.txt", "readme.md", "project.json", "report.csv", "release.log" })
                {
                    using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                    writer.Write("Synthetic LunaArc interface fixture: " + path);
                }
            }
            Pump(vm.OpenArchiveFileAsync(archivePath));
            var table = window.FindControl<DataGrid>("ArchiveGrid")!;
            Check(vm.HasArchive && vm.VisibleItems.Count == 7, "Archive catalog bound to the table");
            Check(table.Columns.Count == 6, "Name, Size, Packed, Type, Modified and CRC32 columns");
            Check(window.FindControl<Button>("ExtractButton")!.IsEnabled, "Extraction enabled after opening");
            var testButton = window.FindControl<Button>("TestButton")!;
            Check(ReferenceEquals(testButton.Command, vm.TestCurrentArchiveCommand), "Test toolbar binds to the archive command");
            Pump(vm.TestCurrentArchiveCommand.ExecuteAsync(null));
            Check(!vm.IsBusy && vm.StatusMessage.Contains("Archive test passed"), "Test command completes and restores idle state");
            Check(window.Title == "Sample archive.zip - LunaArc", "Archive name in the window title");
            vm.SelectedItem = vm.VisibleItems.First(row => row.Name == "readme.md");
            Check(window.FindControl<Button>("ViewButton")!.IsEnabled, "View enabled for a selected entry");
            Capture(window, "archive.png");

            Click(window.FindControl<Button>("FindButton")!);
            Check(vm.IsSearchVisible, "Find button opens the filter");
            vm.SearchQuery = "README";
            Dispatcher.UIThread.RunJobs();
            Check(vm.VisibleItems.Count == 1 && vm.VisibleItems[0].Name == "readme.md", "Case-insensitive name filtering");
            Capture(window, "find.png");
            vm.SearchQuery = string.Empty;
            vm.IsSearchVisible = false;
            Check(vm.VisibleItems.Count == 7, "Clearing filter restores the folder");
            vm.SelectedItem = vm.VisibleItems.First(row => row.Name == "Documents");
            Pump(vm.OpenOrEnterSelectedItemAsync());
            Check(vm.VisibleItems.Count == 2 && vm.CanGoUp, "Folder navigation uses the VFS");
            Check(vm.SelectedItem == null, "Navigation clears stale selection");
            vm.GoUpFolder();
            Check(vm.VisibleItems.Count == 7 && !vm.CanGoUp, "Up returns to archive root");

            Click(window.FindControl<Button>("InfoButton")!);
            var information = window.OwnedWindows.Single();
            Check(information.Title == "Archive information", "Info button opens a real dialog");
            Click(((Grid)information.Content!).Children.OfType<Button>().Single());
            Check(window.OwnedWindows.Count() == 0, "Info dialog closes");

            Click(window.FindControl<Button>("OptionsButton")!);
            var options = (ArchiveOptionsWindow)window.OwnedWindows.Single();
            Check(options.FindControl<ComboBox>("FormatPicker")!.SelectedIndex == vm.SelectedFormatIndex, "Options show active format");
            Capture(options, "options.png");
            int originalLevel = vm.SelectedLevelIndex;
            vm.SelectedLevelIndex = 5;
            Click(options.FindControl<Button>("CancelOptionsButton")!);
            Check(vm.SelectedLevelIndex == originalLevel, "Cancel restores compression settings");

            window.FindControl<MenuItem>("RecoverPasswordMenu")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var recovery = (PasswordRecoveryWindow)window.OwnedWindows.Single();
            var recoveryVm = (PasswordRecoveryViewModel)recovery.DataContext!;
            Check(recoveryVm.ArchivePath == archivePath, "Recovery menu opens a real dialog with the archive path");
            Check(recovery.FindControl<TextBox>("VaultMasterPassword")!.PasswordChar != '\0', "Vault master password is masked");
            Capture(recovery, "recovery.png");
            recoveryVm.CandidateText = "synthetic-password";
            Pump(recoveryVm.StartAsync());
            Check(recoveryVm.CanOpen && recoveryVm.RecoveredPassword.Length == 0, "Unencrypted archive is verified without guessing");
            recoveryVm.ArchivePath = Path.Combine(temporary, "missing.zip");
            Check(!recoveryVm.CanOpen, "Changing archive invalidates the previous recovery result");
            Pump(recoveryVm.StartAsync());
            Check(!recoveryVm.IsBusy && !recoveryVm.CanOpen, "Recovery errors restore idle state");
            Click(recovery.FindControl<Button>("CloseRecoveryButton")!);
            Check(recoveryVm.CandidateText.Length == 0 && recoveryVm.MasterPassword.Length == 0, "Closing recovery clears secret fields");
            var vaultVm = new PasswordRecoveryViewModel(new LunaArc.Core.Security.PasswordVault(Path.Combine(temporary, "ui-vault.enc")))
            { CandidateText = "synthetic-ui-vault-password", MasterPassword = "synthetic-ui-master" };
            Pump(vaultVm.SaveVaultAsync());
            Check(!vaultVm.IsBusy && vaultVm.MasterPassword.Length == 0 && File.Exists(Path.Combine(temporary, "ui-vault.enc")), "Vault save encrypts data and clears master input");
            vaultVm.CandidateText = string.Empty;
            vaultVm.MasterPassword = "incorrect-master";
            Pump(vaultVm.LoadVaultAsync());
            Check(vaultVm.CandidateText.Length == 0 && vaultVm.Status.Contains("could not be unlocked"), "Wrong master does not populate candidate fields");
            vaultVm.MasterPassword = "synthetic-ui-master";
            Pump(vaultVm.LoadVaultAsync());
            Check(vaultVm.CandidateText == "synthetic-ui-vault-password" && vaultVm.MasterPassword.Length == 0, "Unlocked vault supplies the candidate list");
            vaultVm.ClearSecrets();

            window.FindControl<MenuItem>("RecoverPasswordMenu")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var encryptedRecovery = (PasswordRecoveryWindow)window.OwnedWindows.Single();
            var encryptedRecoveryVm = (PasswordRecoveryViewModel)encryptedRecovery.DataContext!;
            encryptedRecoveryVm.ArchivePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "encrypted.zip");
            encryptedRecoveryVm.ModeIndex = 1;
            encryptedRecoveryVm.Mask = "fixture?";
            Pump(encryptedRecoveryVm.StartAsync());
            Check(encryptedRecoveryVm.CanOpen && encryptedRecoveryVm.RecoveredPassword == "fixture3", "Rendered recovery flow finds a real encrypted ZIP password");
            Check(encryptedRecovery.FindControl<TextBox>("RecoveredPasswordBox")!.PasswordChar != '\0', "Recovered password hidden by default");
            encryptedRecovery.FindControl<CheckBox>("ShowRecoveredPassword")!.IsChecked = true;
            Check(encryptedRecovery.FindControl<TextBox>("RecoveredPasswordBox")!.PasswordChar == '\0', "Show checkbox reveals the verified password");
            encryptedRecovery.FindControl<CheckBox>("ShowRecoveredPassword")!.IsChecked = false;
            Capture(encryptedRecovery, "recovery-found.png");
            Click(encryptedRecovery.FindControl<Button>("OpenRecoveredArchiveButton")!);
            while (vm.IsBusy) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            Check(vm.HasArchive && vm.CurrentArchivePath.EndsWith("encrypted.zip"), "Open archive returns the recovered password to the main view");
            Pump(vm.TestCurrentArchiveAsync());
            Check(vm.StatusMessage.Contains("Archive test passed") && vm.PasswordInput.Length == 0, "Recovered session password is used for testing without changing compression settings");
            Pump(vm.ExtractCurrentArchiveToAsync(Path.Combine(temporary, "recovered-output")));
            Check(Directory.GetFiles(Path.Combine(temporary, "recovered-output"), "message.txt", SearchOption.AllDirectories).Length == 1, "Recovered session password is used for extraction");

            var compactVm = new MainWindowViewModel();
            var compact = new MainWindow { DataContext = compactVm, Width = 720, Height = 440 };
            try
            {
                compact.Show();
                Pump(compactVm.OpenArchiveFileAsync(archivePath));
                Check(compact.ClientSize.Width == 720, "Minimum-width viewport applied");
                var optionsButton = compact.FindControl<Button>("OptionsButton")!;
                var rightEdge = optionsButton.TranslatePoint(new Point(optionsButton.Bounds.Width, 0), compact);
                Check(rightEdge is { X: <= 720 }, "Entire toolbar fits at minimum width");
                Capture(compact, "compact.png");
            }
            finally { compact.Close(); }
            vm.CloseArchive();
            Check(!vm.HasArchive && vm.VisibleItems.Count == 0 && window.Title == "LunaArc Archiver", "Close archive resets title and rows");
            Console.WriteLine($"PASS: {checks} rendered Studio UI checks. Screenshots: {outputDirectory}");
            return 0;
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            window.Close();
            Directory.Delete(temporary, recursive: true);
        }

        void Check(bool condition, string description)
        {
            Dispatcher.UIThread.RunJobs();
            if (!condition) throw new InvalidOperationException("FAIL: " + description);
            checks++;
        }
        void Capture(Window target, string name)
        {
            using var frame = target.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered UI frame.");
            frame.Save(Path.Combine(outputDirectory, name));
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
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("UI operation did not finish.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }
}
