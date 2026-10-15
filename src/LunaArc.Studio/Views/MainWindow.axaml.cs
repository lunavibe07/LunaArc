using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LunaArc.Studio.ViewModels;
using LunaArc.Shell;
using LunaArc.Core.Contracts;
using Avalonia.Threading;

namespace LunaArc.Studio.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        this.FindControl<MenuItem>("Windows11MenuItem")!.IsVisible = Windows11MenuInstaller.IsSupported;
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        Closed += (_, _) => Vm?.Dispose();
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;
    private bool _showMetadataColumns;
    private void UpdateResponsiveLayout()
    {
        bool compact = ClientSize.Width < 1000;
        this.FindControl<Grid>("WorkspaceGrid")!.ColumnDefinitions[0].Width = new GridLength(compact ? 100 : 140);
        foreach (string name in new[] { "OpenButton", "AddButton", "ToolsButton", "HelpButton", "OptionsButton" })
            this.FindControl<Button>(name)!.Width = compact ? 82 : 122;
        this.FindControl<Grid>("ArchiveWorkspace")!.Margin = new Avalonia.Thickness(compact ? 22 : 32, 14, compact ? 22 : 32, 0);
        this.FindControl<Grid>("FrameGrid")!.RowDefinitions[0].Height = new GridLength(compact ? 82 : 104);
        this.FindControl<Grid>("ArchiveHeader")!.Margin = new Avalonia.Thickness(6, compact ? 0 : 6, 0, compact ? 8 : 16);
        this.FindControl<Border>("ExtractionDock")!.Padding = compact ? new Avalonia.Thickness(12) : new Avalonia.Thickness(24, 36);
        this.FindControl<Border>("SelectionInspector")!.IsVisible = ClientSize.Width >= 1150;
        this.FindControl<TextBox>("SearchBox")!.Width = compact ? 140 : 240;
        this.FindControl<TextBox>("SearchBox")!.Watermark = compact ? "Find files..." : "Search this folder...";
        this.FindControl<TextBlock>("DestinationLabel")!.IsVisible = !compact;
        this.FindControl<TextBlock>("ExtractActionLabel")!.Text = compact ? "Extract" : "Extract files";
        var extract = this.FindControl<Button>("ExtractButton")!;
        extract.MinWidth = compact ? 110 : 210;
        extract.MinHeight = compact ? 42 : 56;
        extract.FontSize = compact ? 14 : 20;
        this.FindControl<TextBlock>("ArchiveNameText")!.FontSize = compact ? 23 : 32;
        var table = this.FindControl<DataGrid>("ArchiveGrid")!;
        table.RowHeight = compact ? 38 : 56;
        table.Classes.Set("compact", compact);
        table.Classes.Set("large", ClientSize.Width >= 1300);
        table.FontSize = compact ? 14 : ClientSize.Width >= 1300 ? 18 : 16;
        var columns = table.Columns;
        columns[3].IsVisible = columns[5].IsVisible = _showMetadataColumns;
    }
    private void OnToggleMetadataColumns(object? sender, RoutedEventArgs e)
    {
        _showMetadataColumns = !_showMetadataColumns;
        this.FindControl<MenuItem>("MetadataColumnsMenu")!.Header = _showMetadataColumns ? "Hide Type and CRC32 columns" : "Show Type and CRC32 columns";
        UpdateResponsiveLayout();
    }
    private void OnToolsClicked(object? sender, RoutedEventArgs e) => this.FindControl<MenuItem>("ToolsMenu")!.IsSubMenuOpen = true;
    private async void OnEnableWindows11MenuClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.IsBusy || !Windows11MenuInstaller.IsSupported) return;
        if (await new Windows11MenuDialog().ShowDialog<bool>(this))
            await Vm.EnableWindows11MenuAsync(trustCertificateConsent: true);
    }
    private async void OnBrowseDestinationClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || !Vm.CanUseArchive) return;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose extraction folder", AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) Vm.ExtractionDestination = path;
        }
        catch (Exception ex) { Vm.StatusMessage = "Could not choose extraction folder: " + ex.Message; }
    }

    private async void OnRepairArchiveClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.IsBusy) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select damaged archive (keep its .lrec companion beside it)", AllowMultiple = false });
            string? source = files.FirstOrDefault()?.TryGetLocalPath();
            if (source == null) return;
            var output = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save repaired archive to a new file", SuggestedFileName = "repaired-" + Path.GetFileName(source) });
            if (output?.TryGetLocalPath() is { } target) await Vm.RepairArchiveAsync(source, target);
        }
        catch (Exception ex) { Vm.StatusMessage = "Could not repair archive: " + ex.Message; }
    }

    private async void OnLockArchiveClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || !Vm.CanUseArchive) return;
        Vm.IsBusy = true;
        try { await Task.Run(() => LunaArc.Core.Security.ArchiveLock.LockAsync(Vm.CurrentArchivePath)); Vm.StatusMessage = "Archive locked. Keep its .lunalock companion. This is advisory protection, not encryption."; }
        catch (Exception ex) { Vm.StatusMessage = "Could not lock archive: " + ex.Message; }
        finally { Vm.IsBusy = false; }
    }

    private void OnUnlockArchiveClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || !Vm.CanUseArchive) return;
        try { LunaArc.Core.Security.ArchiveLock.Unlock(Vm.CurrentArchivePath); Vm.StatusMessage = "Archive unlocked."; }
        catch (Exception ex) { Vm.StatusMessage = "Could not unlock archive: " + ex.Message; }
    }

    public async Task HandleExplorerRequestAsync(ExplorerRequest request)
    {
        if (Vm == null || Vm.IsBusy) return;
        try
        {
            if (request.Action is ExplorerAction.Add or ExplorerAction.AddZip)
            {
                if (!File.Exists(request.Path) && !Directory.Exists(request.Path)) throw new FileNotFoundException("The selected source no longer exists.");
                Vm.SelectedFormatIndex = request.Action == ExplorerAction.AddZip ? 1 : 0;
                var options = new ArchiveOptionsWindow(Vm, new[] { request.Path });
                if (await options.ShowDialog<bool>(this)) await Vm.CompressFilesToArchiveAsync(new[] { request.Path }, options.OutputPath);
                return;
            }
            if (request.Action == ExplorerAction.Open) { await Vm.OpenArchiveFileAsync(request.Path); return; }
            ExtractionChoice? choice = null;
            if (request.Action == ExplorerAction.Extract)
                choice = await ShowExtractionOptionsAsync(request.ExtractionDirectory, request.Path);
            else
            {
                await Vm.OpenArchiveFileAsync(request.Path);
                if (Vm.CurrentArchivePath != request.Path || Vm.HasEncryptedEntries)
                    choice = await ShowExtractionOptionsAsync(request.ExtractionDirectory, request.Path);
                else choice = new(request.ExtractionDirectory, string.Empty, OverwriteMode.SkipExisting);
            }
            if (choice == null) { Vm.StatusMessage = "Extraction cancelled."; return; }
            if (await Vm.OpenArchiveFileAsync(request.Path, choice.Password.Length > 0 ? choice.Password : null))
                await RunExtractionAsync(choice);
        }
        catch (Exception ex) { Vm.StatusMessage = "Explorer command failed: " + ex.Message; }
    }

    private async void OnOpenArchiveClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open an archive",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Supported archives")
                {
                    Patterns = new[] { "*.larc", "*.001", "*.rar", "*.7z", "*.zip", "*.tar", "*.gz", "*.bz2", "*.xz" }
                }
            }
        });

        if (files.Count > 0)
        {
            string? localPath = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath))
                await Vm.OpenArchiveFileAsync(localPath);
        }
    }

    private async void OnExtractToClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || !Vm.CanUseArchive) return;
        try
        {
            string destination = string.IsNullOrWhiteSpace(Vm.ExtractionDestination)
                ? Path.Combine(Path.GetDirectoryName(Vm.CurrentArchivePath)!, ExplorerRequest.GetFolderName(Vm.CurrentArchivePath)) : Vm.ExtractionDestination;
            var choice = await ShowExtractionOptionsAsync(destination, Vm.CurrentArchivePath);
            if (choice == null) return;
            if (choice.Password.Length > 0 && !await Vm.OpenArchiveFileAsync(Vm.CurrentArchivePath, choice.Password)) return;
            Vm.ExtractionDestination = choice.Destination;
            await RunExtractionAsync(choice);
        }
        catch (Exception ex) { Vm.StatusMessage = "Could not prepare extraction: " + ex.Message; }
    }

    private async Task<ExtractionChoice?> ShowExtractionOptionsAsync(string destination, string archivePath)
    {
        if (Vm == null) return null;
        Vm.IsBusy = true;
        bool requiresPassword;
        try
        {
            Vm.StatusMessage = "Checking archive encryption...";
            requiresPassword = await Task.Run(() => LunaArc.Core.Security.ArchivePasswordDetection.RequiresPasswordAsync(archivePath));
        }
        finally { Vm.IsBusy = false; }
        Vm.StatusMessage = "Choose extraction options.";
        return await new ExtractOptionsWindow(destination, requiresPassword).ShowDialog<ExtractionChoice?>(this);
    }

    private async Task RunExtractionAsync(ExtractionChoice choice)
    {
        if (Vm == null) return;
        bool success = await Vm.ExtractCurrentArchiveToAsync(choice.Destination, choice.OverwriteMode,
            path => Dispatcher.UIThread.InvokeAsync(() => ConfirmOverwriteAsync(path)).GetAwaiter().GetResult(), choice.UpdateMode, choice.KeepBrokenFiles);
        if (success && choice.DisplayAfterExtraction)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(choice.Destination) { UseShellExecute = true }); }
            catch (Exception ex) { Vm.StatusMessage = "Extraction completed; could not display the folder: " + ex.Message; }
        }
    }

    private Task<OverwriteMode> ConfirmOverwriteAsync(string path) => ExtractionConflictDialog.ShowAsync(this, path);

    private async void OnCompressFilesClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var sourceFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select files to compress",
            AllowMultiple = true
        });

        if (sourceFiles.Count == 0) return;

        var inPaths = sourceFiles.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToArray();
        if (inPaths.Length == 0) return;
        var options = new ArchiveOptionsWindow(Vm, inPaths);
        if (await options.ShowDialog<bool>(this)) await Vm.CompressFilesToArchiveAsync(inPaths, options.OutputPath);
    }

    private async void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm != null)
            await Vm.OpenOrEnterSelectedItemAsync();
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnFindClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || !Vm.CanUseArchive) return;
        Vm.IsSearchVisible = true;
        this.FindControl<TextBox>("SearchBox")?.Focus();
    }

    private void OnCloseFindClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        Vm.SearchQuery = string.Empty;
        Vm.IsSearchVisible = false;
    }

    private async void OnOptionsClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.IsBusy) return;
        await new ArchiveOptionsWindow(Vm).ShowDialog<bool>(this);
    }

    private async void OnRecoverPasswordClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.IsBusy) return;
        var dialog = new PasswordRecoveryWindow();
        ((PasswordRecoveryViewModel)dialog.DataContext!).ArchivePath = Vm.CurrentArchivePath;
        var result = await dialog.ShowDialog<RecoveredArchive?>(this);
        if (result == null) return;
        await Vm.OpenArchiveFileAsync(result.Path, result.Password);
    }

    private async void OnInfoClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || !Vm.HasArchive) return;
        await ShowInformationAsync("Archive information", Path.GetFileName(Vm.CurrentArchivePath),
            "Path: " + Vm.CurrentArchivePath + "\n\nCurrent view: " + Vm.VisibleSummary +
            "\n\nComment:\n" + (string.IsNullOrWhiteSpace(Vm.ArchiveComment) ? "No comment." : Vm.ArchiveComment));
    }

    private HelpTopicsWindow? _helpTopics;
    private void OnHelpTopicsClicked(object? sender, RoutedEventArgs e)
    {
        if (_helpTopics != null) { _helpTopics.Activate(); return; }
        _helpTopics = new HelpTopicsWindow { DataContext = new HelpTopicsViewModel() };
        _helpTopics.Closed += (_, _) => _helpTopics = null;
        _helpTopics.Show(this);
    }

    private async void OnHomePageClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!await Launcher.LaunchUriAsync(new Uri("https://github.com/lunavibe07/LunaArc")) && Vm != null)
                Vm.StatusMessage = "Could not open the project home page. Visit https://github.com/lunavibe07/LunaArc";
        }
        catch (Exception ex)
        {
            if (Vm != null) Vm.StatusMessage = "Could not open the project home page: " + ex.Message;
        }
    }

    private async void OnAboutClicked(object? sender, RoutedEventArgs e) =>
        await ShowInformationAsync("About LunaArc", "LunaArc Archiver",
            $"Version {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}\nArchive manager for Windows and Linux.\n\nCreate LARC, ZIP, TAR GZip and 7z archives.\nBrowse, extract and test existing archives.\n\nToolbar icons: Microsoft Fluent UI System Icons (MIT).\nCopyright 2026 LunaSi Software.", showLogo: true);

    private Task ShowInformationAsync(string title, string heading, string body, bool showLogo = false)
    {
        var dialog = new Window
        {
            Title = title, Width = 470, Height = showLogo ? 420 : 310, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#102035"),
            FontFamily = FontFamily, FontSize = 12
        };
        var close = new Button { Name = "CloseInformationButton", Content = "Close", Width = 85,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
        close.Click += (_, _) => dialog.Close();
        var content = new Grid { Margin = new Avalonia.Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var header = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(0, 0, 0, 12) };
        if (showLogo)
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://LunaArc/Assets/app_logo.png"));
            var logo = new Avalonia.Media.Imaging.Bitmap(stream);
            header.Children.Add(new Image { Name = "AboutLogo", Source = logo, Width = 96, Height = 96,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left });
            dialog.Closed += (_, _) => logo.Dispose();
        }
        header.Children.Add(new TextBlock { Text = heading, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        content.Children.Add(header);
        var text = new TextBlock { Text = body, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var scroll = new ScrollViewer { Content = text };
        Grid.SetRow(scroll, 1);
        content.Children.Add(scroll);
        Grid.SetRow(close, 2);
        content.Children.Add(close);
        dialog.Content = content;
        return dialog.ShowDialog(this);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F1) { OnHelpTopicsClicked(this, new RoutedEventArgs()); e.Handled = true; return; }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.O && Vm?.CanStartOperation == true)
        {
            OnOpenArchiveClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        if (e.Key == Key.F3 && Vm?.CanUseArchive == true)
        {
            OnFindClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
