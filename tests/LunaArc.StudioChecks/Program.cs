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
using LunaArc.Shell;

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
            Check(window.Icon != null, "Application logo loads from embedded resources");
            window.FindControl<MenuItem>("AboutMenu")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var about = window.OwnedWindows.Single();
            var aboutContent = (Grid)about.Content!;
            var aboutLogo = ((StackPanel)aboutContent.Children[0]).Children.OfType<Image>().Single();
            Check(aboutLogo.Source is Avalonia.Media.Imaging.Bitmap { PixelSize.Width: 256, PixelSize.Height: 256 }, "About dialog displays the supplied logo");
            Capture(about, "about.png");
            Click(aboutContent.Children.OfType<Button>().Single());
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
            Check(options.Options.SelectedFormatIndex == vm.SelectedFormatIndex && options.FindControl<TabControl>("CompressionTabs")!.Items.Count == 1, "Compression options show active format and only General");
            Check(!options.FindControl<CheckBox>("DeleteSourceAfterArchivingBox")!.IsEnabled, "Source deletion cannot be saved in default Options");
            Capture(options, "options.png");
            int originalLevel = vm.SelectedLevelIndex;
            options.Options.SelectedLevelIndex = 5;
            options.Options.PasswordInput = "synthetic-cancelled-password";
            Click(options.FindControl<Button>("CancelOptionsButton")!);
            Check(vm.SelectedLevelIndex == originalLevel && vm.PasswordInput.Length == 0 && options.Options.PasswordInput.Length == 0, "Cancel preserves compression settings and clears draft secrets");

            string compressionSource = Path.Combine(temporary, "compression source.txt");
            File.WriteAllText(compressionSource, "Synthetic compression dialog fixture");
            Task createZip = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.AddZip, compressionSource));
            Dispatcher.UIThread.RunJobs();
            var zipOptions = (ArchiveOptionsWindow)window.OwnedWindows.Single();
            Check(zipOptions.FindControl<CheckBox>("DeleteSourceAfterArchivingBox")!.IsChecked == false, "Source deletion defaults off for every creation");
            Check(new[] { "RecoveryRecordBox", "LockArchiveBox" }.All(name =>
                zipOptions.FindControl<CheckBox>(name) is { IsEnabled: true, IsChecked: false }), "Recovery and lock are enabled and default off");
            Check(!zipOptions.FindControl<CheckBox>("CreateSolidBox")!.IsEnabled && !zipOptions.FindControl<CheckBox>("CreateSfxBox")!.IsEnabled,
                "ZIP disables solid; development builds require published SFX stub");
            zipOptions.Options.IsSevenZip = true;
            Check(zipOptions.FindControl<CheckBox>("CreateSolidBox")!.IsEnabled, "Selecting 7z enables solid compression");
            zipOptions.Options.SolidArchive = true;
            Capture(zipOptions, "compression-solid.png");
            zipOptions.Options.IsZip = true;
            Check(!zipOptions.Options.SolidArchive, "Leaving 7z clears solid compression");
            Check(zipOptions.Options.IsZip && zipOptions.FindControl<TextBox>("ArchiveNameBox")!.IsEnabled, "Explorer Add ZIP opens General with an editable output name");
            zipOptions.Options.ArchiveName = archivePath;
            Click(zipOptions.FindControl<Button>("AcceptOptionsButton")!);
            Check(window.OwnedWindows.Contains(zipOptions) && zipOptions.Options.Status.Contains("already exists"), "General rejects an existing output archive without closing");
            zipOptions.Options.ArchiveName = Path.Combine(temporary, "Created from General.zip");
            Check(zipOptions.Options.Status.Length == 0, "Correcting an archive path clears the stale validation error");
            zipOptions.FindControl<ComboBox>("LevelPicker")!.SelectedIndex = 5;
            zipOptions.FindControl<CheckBox>("TestCreatedArchiveBox")!.IsChecked = true;
            zipOptions.Options.ArchiveComment = "Synthetic General comment";
            zipOptions.Options.AddRecoveryRecord = true;
            zipOptions.Options.LockArchive = true;
            Capture(zipOptions, "compression-general.png");
            zipOptions.Width = 600; zipOptions.Height = 670;
            Capture(zipOptions, "compression-compact.png");
            string zipOutput = zipOptions.Options.ArchiveName;
            Click(zipOptions.FindControl<Button>("AcceptOptionsButton")!);
            Pump(createZip);
            Check(File.Exists(zipOutput) && vm.StatusMessage.Contains("verified"), "General creates and verifies the selected ZIP output");
            Check(File.Exists(zipOutput + ".lrec") && LunaArc.Core.Security.ArchiveLock.IsLocked(zipOutput), "General creates recovery and lock companions");
            window.FindControl<MenuItem>("UnlockArchiveMenu")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(!LunaArc.Core.Security.ArchiveLock.IsLocked(zipOutput), "Tools explicitly unlocks the current archive");
            using (var createdZip = ZipFile.OpenRead(zipOutput))
            using (var reader = new StreamReader(createdZip.Entries.Single().Open()))
                Check(reader.ReadToEnd() == "Synthetic compression dialog fixture" && createdZip.Comment == "Synthetic General comment", "Independent ZIP reader verifies content and comment from General");
            byte[] originalZipHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(zipOutput));
            Pump(vm.CompressFilesToArchiveAsync(new[] { compressionSource }, zipOutput));
            Check(originalZipHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(zipOutput))), "Compression preserves an existing archive at the final output path");
            Check(Directory.GetFiles(temporary, ".lunaarc-*").Length == 0 && File.Exists(compressionSource), "Compression keeps sources and cleans temporary output");

            Task createLarc = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.Add, compressionSource));
            Dispatcher.UIThread.RunJobs();
            var larcOptions = (ArchiveOptionsWindow)window.OwnedWindows.Single();
            larcOptions.Options.ArchiveName = Path.Combine(temporary, "Created encrypted.larc");
            Click(larcOptions.FindControl<Button>("SetArchivePasswordButton")!);
            var passwordDialog = larcOptions.OwnedWindows.Single();
            var passwordContent = (StackPanel)passwordDialog.Content!;
            var passwordFields = passwordContent.Children.OfType<TextBox>().ToArray();
            Check(passwordFields.All(field => field.PasswordChar != '\0'), "Set password masks both password fields");
            passwordFields[0].Text = "synthetic-created-password";
            passwordFields[1].Text = "different-confirmation";
            var passwordAccept = passwordContent.Children.OfType<StackPanel>().Single().Children.OfType<Button>().First();
            Click(passwordAccept);
            Check(larcOptions.OwnedWindows.Any(), "Set password rejects mismatched confirmation");
            passwordFields[1].Text = passwordFields[0].Text;
            Capture(passwordDialog, "compression-password.png");
            Click(passwordAccept);
            Check(larcOptions.Options.PasswordInput == "synthetic-created-password" && passwordFields.All(field => field.Text == string.Empty), "Accepted password belongs to the compression draft and child inputs are cleared");
            larcOptions.FindControl<RadioButton>("ZipFormatRadio")!.IsChecked = true;
            Check(larcOptions.Options.Validate() && window.OwnedWindows.Contains(larcOptions), "General accepts password-protected ZIP creation");
            larcOptions.FindControl<RadioButton>("LarcFormatRadio")!.IsChecked = true;
            larcOptions.FindControl<CheckBox>("TestCreatedArchiveBox")!.IsChecked = true;
            string larcOutput = larcOptions.Options.ArchiveName;
            Click(larcOptions.FindControl<Button>("AcceptOptionsButton")!);
            Pump(createLarc);
            Check(File.Exists(larcOutput) && vm.HasArchive && vm.CurrentArchivePath == larcOutput && vm.StatusMessage.Contains("verified"), "General creates, verifies and opens an encrypted LARC");
            Check(larcOptions.Options.PasswordInput.Length == 0, "Closing General clears its draft password");
            vm.PasswordInput = string.Empty;

            Task solidCreation = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.Add, compressionSource));
            Dispatcher.UIThread.RunJobs();
            var solidOptions = (ArchiveOptionsWindow)window.OwnedWindows.Single();
            solidOptions.Options.IsSevenZip = true;
            solidOptions.Options.ArchiveComment = string.Empty;
            solidOptions.Options.SolidArchive = true;
            string solidOutput = solidOptions.OutputPath;
            Click(solidOptions.FindControl<Button>("AcceptOptionsButton")!);
            Pump(solidCreation);
            Check(File.Exists(solidOutput) && vm.HasArchive && vm.CurrentArchivePath == solidOutput, "General creates and opens 7z output");
            Check(!vm.CreateSfx && !vm.AddRecoveryRecord && !vm.LockArchive, "Creation companions are one-operation settings");

            string damagedForUi = Path.Combine(temporary, "damaged-ui.zip");
            File.Copy(zipOutput, damagedForUi);
            File.Copy(zipOutput + ".lrec", damagedForUi + ".lrec");
            using (var damagedBytes = new FileStream(damagedForUi, FileMode.Open, FileAccess.Write)) damagedBytes.WriteByte(0xff);
            string repairedForUi = Path.Combine(temporary, "repaired-ui.zip");
            Pump(vm.RepairArchiveAsync(damagedForUi, repairedForUi));
            Check(File.ReadAllBytes(repairedForUi).SequenceEqual(File.ReadAllBytes(zipOutput)) && vm.StatusMessage.Contains("SHA-256 verified") && !vm.IsBusy,
                "Repair flow reconstructs exact contents and restores idle state");

            string deleteSource = Path.Combine(temporary, "delete source.txt");
            File.WriteAllText(deleteSource, "Synthetic opt-in deletion fixture");
            Task deleteCreation = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.AddZip, deleteSource));
            Dispatcher.UIThread.RunJobs();
            var deleteOptions = (ArchiveOptionsWindow)window.OwnedWindows.Single();
            deleteOptions.Options.ArchiveName = Path.Combine(temporary, "Created with deletion.zip");
            deleteOptions.FindControl<CheckBox>("DeleteSourceAfterArchivingBox")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Check(deleteOptions.FindControl<CheckBox>("TestCreatedArchiveBox") is { IsChecked: true, IsEnabled: false }, "Selecting source deletion forces archive testing");
            Capture(deleteOptions, "compression-delete.png");
            string deleteOutput = deleteOptions.OutputPath;
            Click(deleteOptions.FindControl<Button>("AcceptOptionsButton")!);
            Pump(deleteCreation);
            Check(!File.Exists(deleteSource) && File.Exists(deleteOutput) && vm.StatusMessage.Contains("Removed 1"), "General deletes only the verified archived source after publishing the archive");
            Check(!vm.DeleteSourceAfterArchiving, "Source deletion is consumed for one operation");
            string keptSource = Path.Combine(temporary, "cancel deletion.txt");
            File.WriteAllText(keptSource, "Cancelled source must remain");
            Task cancelDeleteCreation = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.AddZip, keptSource));
            Dispatcher.UIThread.RunJobs();
            var cancelDeleteOptions = (ArchiveOptionsWindow)window.OwnedWindows.Single();
            Check(!cancelDeleteOptions.Options.DeleteSourceAfterArchiving, "The next General dialog does not inherit source deletion");
            cancelDeleteOptions.FindControl<CheckBox>("DeleteSourceAfterArchivingBox")!.IsChecked = true;
            Click(cancelDeleteOptions.FindControl<Button>("CancelOptionsButton")!);
            Pump(cancelDeleteCreation);
            Check(File.Exists(keptSource) && !vm.DeleteSourceAfterArchiving, "Cancelling General keeps sources and does not enable deletion");
            using (var busyVm = new MainWindowViewModel { IsBusy = true, DeleteSourceAfterArchiving = true })
            {
                Pump(busyVm.CompressFilesToArchiveAsync(new[] { keptSource }, Path.Combine(temporary, "busy-output.zip")));
                Check(!busyVm.DeleteSourceAfterArchiving && File.Exists(keptSource), "A rejected busy creation also consumes its deletion opt-in");
            }
            Pump(vm.OpenArchiveFileAsync(archivePath));

            string compressionFolder = Path.Combine(temporary, "compression folder");
            Directory.CreateDirectory(compressionFolder);
            var invalidOutput = new CompressionOptionsViewModel(vm, new[] { compressionFolder }) { ArchiveName = Path.Combine(compressionFolder, "self.larc"), SelectedFormatIndex = 0 };
            Check(!invalidOutput.Validate() && invalidOutput.Status.Contains("outside"), "General rejects output inside a selected source folder");

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

            Task extraction = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.Extract, archivePath));
            Pump(WaitUntilAsync(() => window.OwnedWindows.Any() || extraction.IsCompleted));
            var extractionOptions = (ExtractOptionsWindow)window.OwnedWindows.Single();
            Check(extractionOptions.FindControl<RadioButton>("SkipExistingBox")!.IsChecked == true, "Explorer extraction defaults to preserving existing files");
            Check(extractionOptions.FindControl<RadioButton>("ExtractAndReplaceRadio")!.IsChecked == true, "Update mode defaults to extracting all eligible files");
            Check(extractionOptions.FindControl<TextBox>("ExtractionPasswordBox")!.PasswordChar != '\0', "Extraction password masked");
            var extractionVm = (ExtractionOptionsViewModel)extractionOptions.DataContext!;
            Check(extractionOptions.FindControl<CheckBox>("KeepBrokenFilesBox") is { IsChecked: false }, "Keeping broken files is an explicit opt-in");
            string longDestination = Path.Combine(temporary, "a long parent directory for the extraction dialog", "Downloads", "LunaArc-Windows-Setup (2)");
            extractionVm.Destination = longDestination;
            var destinationBox = extractionOptions.FindControl<TextBox>("DestinationBox")!;
            var destinationPreview = extractionOptions.FindControl<TextBlock>("DestinationPreview")!;
            extractionOptions.FindControl<Button>("BrowseDestinationButton")!.Focus();
            Dispatcher.UIThread.RunJobs();
            Check(destinationPreview.IsVisible && destinationPreview.Text!.Contains("…") && destinationPreview.Text.EndsWith("LunaArc-Windows-Setup (2)")
                && destinationBox.Text == longDestination, "Destination preview abbreviates parents while preserving the complete extraction path");
            Capture(extractionOptions, "extract-path-compact.png");
            destinationBox.Focus();
            Dispatcher.UIThread.RunJobs();
            Check(!destinationPreview.IsVisible && destinationBox.SelectedText == longDestination, "Focusing destination exposes and selects the full editable path");
            Capture(extractionOptions, "extract-path-edit.png");
            extractionOptions.FindControl<Button>("BrowseDestinationButton")!.Focus();
            Dispatcher.UIThread.RunJobs();
            Check(destinationPreview.IsVisible && extractionVm.Destination == longDestination && extractionVm.ValidateDestination(),
                "Leaving destination editing preserves its full validated path");
            string browserRoot = Path.Combine(temporary, "folder browser");
            Directory.CreateDirectory(Path.Combine(browserRoot, "Target"));
            var folderNode = new ExtractionFolderNode("Test folders", browserRoot, CancellationToken.None);
            extractionVm.Folders.Insert(0, folderNode);
            Pump(folderNode.LoadChildrenAsync());
            folderNode.IsExpanded = true;
            var targetNode = folderNode.Children.Single();
            extractionOptions.FindControl<TreeView>("DestinationTree")!.SelectedItem = targetNode;
            Dispatcher.UIThread.RunJobs();
            Check(extractionVm.Destination == targetNode.Path, "Folder tree selection updates the destination path");
            Click(extractionOptions.FindControl<Button>("NewFolderButton")!);
            var folderDialog = extractionOptions.OwnedWindows.Single();
            var folderContent = (StackPanel)folderDialog.Content!;
            folderContent.Children.OfType<TextBox>().Single().Text = "Created folder";
            Click(folderContent.Children.OfType<StackPanel>().Single().Children.OfType<Button>().First());
            Pump(WaitUntilAsync(() => !extractionOptions.OwnedWindows.Any()));
            Check(Directory.Exists(Path.Combine(targetNode.Path, "Created folder")) && extractionVm.Destination.EndsWith("Created folder"), "New folder dialog creates and selects the requested folder");
            var invalidFolder = extractionVm.CreateFolderAsync("../escape");
            Pump(invalidFolder);
            Check(!invalidFolder.Result, "New folder rejects multi-segment names");
            Check(!extractionOptions.FindControl<TabItem>("ExtractionPasswordTab")!.IsVisible, "Unencrypted archive hides the Password tab");
            Capture(extractionOptions, "extract-no-password.png");
            string chosenDestination = Path.Combine(temporary, "chosen extraction folder");
            extractionOptions.FindControl<TextBox>("DestinationBox")!.Text = chosenDestination;
            Click(extractionOptions.FindControl<Button>("StartExtractionButton")!);
            Pump(extraction);
            Check(File.Exists(Path.Combine(chosenDestination, "readme.md")), "Extract files command honors the chosen folder");

            Task protectedExtraction = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.Extract, larcOutput));
            Pump(WaitUntilAsync(() => window.OwnedWindows.Any() || protectedExtraction.IsCompleted));
            var protectedOptions = (ExtractOptionsWindow)window.OwnedWindows.Single();
            Check(protectedOptions.FindControl<TabItem>("ExtractionPasswordTab")!.IsVisible, "Header-encrypted LARC shows Password before its catalog can be opened");
            string protectedDestination = Path.Combine(temporary, "password-required-output");
            protectedOptions.FindControl<TextBox>("DestinationBox")!.Text = protectedDestination;
            protectedOptions.FindControl<TabControl>("ExtractionTabs")!.SelectedIndex = 1;
            protectedOptions.FindControl<TextBox>("ExtractionPasswordBox")!.Text = "synthetic-created-password";
            protectedOptions.FindControl<CheckBox>("ShowExtractionPasswordBox")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Check(protectedOptions.FindControl<TextBox>("ExtractionPasswordBox")!.PasswordChar == '\0', "Visible Password tab can reveal the supplied password");
            Capture(protectedOptions, "extract-password.png");
            Click(protectedOptions.FindControl<Button>("StartExtractionButton")!);
            Pump(protectedExtraction);
            Check(File.Exists(Path.Combine(protectedDestination, Path.GetFileName(compressionSource))) && !vm.IsBusy,
                "Entering a password in the conditional tab unlocks and extracts header-encrypted LARC");
            Check(protectedOptions.FindControl<TextBox>("ExtractionPasswordBox")!.Text == string.Empty, "Closing conditional Password clears its secret");
            Pump(vm.OpenArchiveFileAsync(archivePath));

            Task renameExtraction = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.Extract, archivePath));
            Pump(WaitUntilAsync(() => window.OwnedWindows.Any() || renameExtraction.IsCompleted));
            var renameOptions = (ExtractOptionsWindow)window.OwnedWindows.Single();
            renameOptions.FindControl<TextBox>("DestinationBox")!.Text = chosenDestination;
            renameOptions.FindControl<RadioButton>("RenameRadio")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Check(((ExtractionOptionsViewModel)renameOptions.DataContext!).RenameAutomatically && renameOptions.FindControl<RadioButton>("SkipExistingBox")!.IsChecked == false, "Overwrite radios choose one backend policy");
            Capture(renameOptions, "extract-options.png");
            renameOptions.Width = 600; renameOptions.Height = 630;
            Dispatcher.UIThread.RunJobs();
            Capture(renameOptions, "extract-compact.png");
            Click(renameOptions.FindControl<Button>("StartExtractionButton")!);
            Pump(renameExtraction);
            Check(File.Exists(Path.Combine(chosenDestination, "readme (1).md")), "Rename extraction preserves existing files and writes another copy");

            string freshenDestination = Path.Combine(temporary, "freshen extraction");
            Directory.CreateDirectory(freshenDestination);
            File.WriteAllText(Path.Combine(freshenDestination, "readme.md"), "Old copy");
            File.SetLastWriteTimeUtc(Path.Combine(freshenDestination, "readme.md"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Task freshenExtraction = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.Extract, archivePath));
            Pump(WaitUntilAsync(() => window.OwnedWindows.Any() || freshenExtraction.IsCompleted));
            var freshenOptions = (ExtractOptionsWindow)window.OwnedWindows.Single();
            freshenOptions.FindControl<TextBox>("DestinationBox")!.Text = freshenDestination;
            freshenOptions.FindControl<RadioButton>("FreshenExistingRadio")!.IsChecked = true;
            freshenOptions.FindControl<RadioButton>("OverwriteAllRadio")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            var freshenVm = (ExtractionOptionsViewModel)freshenOptions.DataContext!;
            Check(freshenVm.FreshenExisting && freshenVm.OverwriteAll && freshenOptions.FindControl<RadioButton>("ExtractAndReplaceRadio")!.IsChecked == false,
                "Update mode and overwrite mode are independent radio groups");
            Capture(freshenOptions, "extract-freshen.png");
            Click(freshenOptions.FindControl<Button>("StartExtractionButton")!);
            Pump(freshenExtraction);
            Check(File.ReadAllText(Path.Combine(freshenDestination, "readme.md")).StartsWith("Synthetic LunaArc")
                && Directory.GetFiles(freshenDestination, "*", SearchOption.AllDirectories).Length == 1
                && Directory.GetDirectories(freshenDestination).Length == 0, "Freshen from the dialog updates an existing older file without adding files or folders");

            string askDestination = Path.Combine(temporary, "ask extraction");
            Directory.CreateDirectory(askDestination);
            File.WriteAllText(Path.Combine(askDestination, "readme.md"), "Keep original");
            Task askExtraction = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.Extract, archivePath));
            Pump(WaitUntilAsync(() => window.OwnedWindows.Any() || askExtraction.IsCompleted));
            var askOptions = (ExtractOptionsWindow)window.OwnedWindows.Single();
            askOptions.FindControl<TextBox>("DestinationBox")!.Text = askDestination;
            askOptions.FindControl<RadioButton>("AskOverwriteRadio")!.IsChecked = true;
            Click(askOptions.FindControl<Button>("StartExtractionButton")!);
            Pump(WaitUntilAsync(() => window.OwnedWindows.Any()));
            var conflict = window.OwnedWindows.Single();
            Check(conflict.Title == "File already exists", "Ask mode opens a conflict dialog from the extraction worker");
            Capture(conflict, "extract-conflict.png");
            var conflictButtons = ((StackPanel)conflict.Content!).Children.OfType<StackPanel>().Single();
            Click(conflictButtons.Children.OfType<Button>().Single(button => button.Name == "ConflictSkip"));
            Pump(askExtraction);
            Check(File.ReadAllText(Path.Combine(askDestination, "readme.md")) == "Keep original" && !vm.IsBusy, "Conflict Skip preserves the original and restores idle state");
            Task cancelConflictExtraction = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.Extract, archivePath));
            Pump(WaitUntilAsync(() => window.OwnedWindows.Any() || cancelConflictExtraction.IsCompleted));
            var cancelConflictOptions = (ExtractOptionsWindow)window.OwnedWindows.Single();
            cancelConflictOptions.FindControl<TextBox>("DestinationBox")!.Text = askDestination;
            cancelConflictOptions.FindControl<RadioButton>("AskOverwriteRadio")!.IsChecked = true;
            Click(cancelConflictOptions.FindControl<Button>("StartExtractionButton")!);
            Pump(WaitUntilAsync(() => window.OwnedWindows.Any()));
            var cancelConflict = window.OwnedWindows.Single();
            var cancelConflictButtons = ((StackPanel)cancelConflict.Content!).Children.OfType<StackPanel>().Single();
            Click(cancelConflictButtons.Children.OfType<Button>().Single(button => button.IsCancel));
            Pump(cancelConflictExtraction);
            Check(!vm.IsBusy && vm.StatusMessage.Contains("cancelled") && File.ReadAllText(Path.Combine(askDestination, "readme.md")) == "Keep original", "Cancelling an overwrite prompt releases the job and preserves existing data");
            string corruptZip = Path.Combine(temporary, "broken-ui.zip");
            byte[] brokenMarker = System.Text.Encoding.UTF8.GetBytes("Synthetic stored ZIP CRC failure for the extraction dialog");
            using (var zip = ZipFile.Open(corruptZip, ZipArchiveMode.Create))
            using (var payload = zip.CreateEntry("payload.txt", CompressionLevel.NoCompression).Open()) payload.Write(brokenMarker);
            byte[] corruptBytes = File.ReadAllBytes(corruptZip);
            int markerIndex = corruptBytes.AsSpan().IndexOf(brokenMarker);
            Check(markerIndex >= 0, "Broken-file UI fixture contains a stored payload");
            corruptBytes[markerIndex] ^= 1;
            File.WriteAllBytes(corruptZip, corruptBytes);
            string brokenDestination = Path.Combine(temporary, "broken-ui-output");
            Directory.CreateDirectory(brokenDestination);
            File.WriteAllText(Path.Combine(brokenDestination, "payload.txt"), "Preserve existing destination");
            Task brokenExtraction = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.Extract, corruptZip));
            Pump(WaitUntilAsync(() => window.OwnedWindows.Any() || brokenExtraction.IsCompleted));
            var brokenOptions = (ExtractOptionsWindow)window.OwnedWindows.Single();
            brokenOptions.FindControl<TextBox>("DestinationBox")!.Text = brokenDestination;
            brokenOptions.FindControl<RadioButton>("OverwriteAllRadio")!.IsChecked = true;
            brokenOptions.FindControl<CheckBox>("KeepBrokenFilesBox")!.IsChecked = true;
            Capture(brokenOptions, "extract-keep-broken.png");
            Click(brokenOptions.FindControl<Button>("StartExtractionButton")!);
            Pump(brokenExtraction);
            Check(File.ReadAllText(Path.Combine(brokenDestination, "payload.txt")) == "Preserve existing destination"
                && Directory.GetFiles(brokenDestination, "*.broken-*").Length == 1 && !vm.IsBusy,
                "Keep broken files flows from General to extraction, retains failed data separately and preserves the destination");
            Pump(vm.OpenArchiveFileAsync(archivePath));
            Click(window.FindControl<Button>("ExtractButton")!);
            Pump(WaitUntilAsync(() => window.OwnedWindows.Any()));
            Check(window.OwnedWindows.Single() is ExtractOptionsWindow, "Toolbar Extract To uses the same extraction dialog");
            Click(((ExtractOptionsWindow)window.OwnedWindows.Single()).FindControl<Button>("CancelExtractionButton")!);
            File.WriteAllText(Path.Combine(temporary, "notes.txt"), "Existing notes must survive");
            Pump(window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.ExtractHere, archivePath)));
            Check(File.ReadAllText(Path.Combine(temporary, "notes.txt")) == "Existing notes must survive", "Extract Here preserves existing files by default");
            Check(File.Exists(Path.Combine(temporary, "project.json")), "Extract Here writes next to the archive");
            Pump(window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.ExtractFolder, archivePath)));
            Check(File.Exists(Path.Combine(temporary, "Sample archive", "readme.md")), "Extract to folder creates the archive-named destination");
            Task cancelledExtraction = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.Extract, archivePath));
            Pump(WaitUntilAsync(() => window.OwnedWindows.Any() || cancelledExtraction.IsCompleted));
            var cancelledOptions = (ExtractOptionsWindow)window.OwnedWindows.Single();
            cancelledOptions.FindControl<TextBox>("ExtractionPasswordBox")!.Text = "synthetic-cancelled-secret";
            Click(cancelledOptions.FindControl<Button>("CancelExtractionButton")!);
            Pump(cancelledExtraction);
            Check(cancelledOptions.FindControl<TextBox>("ExtractionPasswordBox")!.Text == string.Empty && vm.StatusMessage == "Extraction cancelled.", "Cancelling extraction clears the password and does not start a job");
            Task cancelledAdd = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.AddZip, Path.Combine(temporary, "notes.txt")));
            Dispatcher.UIThread.RunJobs();
            var shellAddOptions = (ArchiveOptionsWindow)window.OwnedWindows.Single();
            Check(vm.SelectedFormatIndex == 1, "Explorer ZIP creation selects ZIP before opening options");
            Click(shellAddOptions.FindControl<Button>("CancelOptionsButton")!);
            Pump(cancelledAdd);

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
            Task recoveredDialogExtraction = window.HandleExplorerRequestAsync(new ExplorerRequest(ExplorerAction.Extract, vm.CurrentArchivePath));
            Pump(WaitUntilAsync(() => window.OwnedWindows.Any() || recoveredDialogExtraction.IsCompleted));
            var recoveredOptions = (ExtractOptionsWindow)window.OwnedWindows.Single();
            Check(recoveredOptions.FindControl<TabItem>("ExtractionPasswordTab")!.IsVisible, "Encrypted ZIP retains an optional Password tab after unlocking the archive session");
            string recoveredDialogDestination = Path.Combine(temporary, "recovered-dialog-output");
            recoveredOptions.FindControl<TextBox>("DestinationBox")!.Text = recoveredDialogDestination;
            Click(recoveredOptions.FindControl<Button>("StartExtractionButton")!);
            Pump(recoveredDialogExtraction);
            Check(Directory.GetFiles(recoveredDialogDestination, "message.txt", SearchOption.AllDirectories).Length == 1, "Blank password in extraction dialog retains the current archive's unlocked password");

            window.Hide();
            StandaloneExtractionChecks.Run(temporary, archivePath, larcOutput, "synthetic-created-password", Check, Capture);
            StandaloneCompressionChecks.Run(temporary, Check, Capture);
            EncryptedCreationChecks.Run(temporary, Check, Capture);
            window.Show();
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
            // Allow layout and tab transitions to reach the state being asserted.
            var settle = System.Diagnostics.Stopwatch.StartNew();
            while (settle.ElapsedMilliseconds < 220)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(10);
            }
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Dialog did not become ready.");
            await Task.Delay(10);
        }
    }
}
