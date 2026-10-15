using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LunaArc.Studio.ViewModels;
using LunaArc.Shell;

namespace LunaArc.Studio.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => Vm?.Dispose();
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    public async Task HandleExplorerRequestAsync(ExplorerRequest request)
    {
        if (Vm == null || Vm.IsBusy) return;
        try
        {
            if (request.Action is ExplorerAction.Add or ExplorerAction.AddZip)
            {
                if (!File.Exists(request.Path) && !Directory.Exists(request.Path)) throw new FileNotFoundException("The selected source no longer exists.");
                Vm.SelectedFormatIndex = request.Action == ExplorerAction.AddZip ? 1 : 0;
                if (!await new ArchiveOptionsWindow(Vm).ShowDialog<bool>(this)) return;
                string extension = Vm.SelectedFormatIndex switch { 1 => "zip", 2 => "tar.gz", _ => "larc" };
                var output = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                { Title = "Save new archive", DefaultExtension = extension, SuggestedFileName = ExplorerRequest.GetFolderName(request.Path) + "." + extension });
                if (output?.TryGetLocalPath() is { } path) await Vm.CompressFilesToArchiveAsync(new[] { request.Path }, path);
                return;
            }
            if (request.Action == ExplorerAction.Open) { await Vm.OpenArchiveFileAsync(request.Path); return; }
            ExtractionChoice? choice = null;
            if (request.Action == ExplorerAction.Extract)
                choice = await new ExtractOptionsWindow(request.ExtractionDirectory).ShowDialog<ExtractionChoice?>(this);
            else
            {
                await Vm.OpenArchiveFileAsync(request.Path);
                if (Vm.CurrentArchivePath != request.Path || Vm.HasEncryptedEntries)
                    choice = await new ExtractOptionsWindow(request.ExtractionDirectory).ShowDialog<ExtractionChoice?>(this);
                else choice = new(request.ExtractionDirectory, string.Empty, true);
            }
            if (choice == null) { Vm.StatusMessage = "Extraction cancelled."; return; }
            if (await Vm.OpenArchiveFileAsync(request.Path, choice.Password))
                await Vm.ExtractCurrentArchiveToAsync(choice.Destination, choice.SkipExisting);
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
                    Patterns = new[] { "*.larc", "*.rar", "*.7z", "*.zip", "*.tar", "*.gz", "*.bz2", "*.xz" }
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
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select extraction folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            string? destPath = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(destPath))
                await Vm.ExtractCurrentArchiveToAsync(destPath);
        }
    }

    private async void OnCompressFilesClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var sourceFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select files to compress",
            AllowMultiple = true
        });

        if (sourceFiles.Count == 0) return;

        var options = new ArchiveOptionsWindow(Vm);
        if (!await options.ShowDialog<bool>(this)) return;

        string defaultExt = Vm.SelectedFormatIndex switch
        {
            1 => "zip",
            2 => "tar.gz",
            _ => "larc"
        };

        var saveFile = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save new archive",
            DefaultExtension = defaultExt,
            SuggestedFileName = $"Archive.{defaultExt}"
        });

        if (saveFile != null)
        {
            string? outPath = saveFile.TryGetLocalPath();
            var inPaths = sourceFiles.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToList();
            if (!string.IsNullOrEmpty(outPath) && inPaths.Count > 0)
            {
                await Vm.CompressFilesToArchiveAsync(inPaths, outPath);
            }
        }
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

    private async void OnAboutClicked(object? sender, RoutedEventArgs e) =>
        await ShowInformationAsync("About LunaArc", "LunaArc Archiver",
            $"Version {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}\nArchive manager for Windows and Linux.\n\nCreate .larc, ZIP and TAR GZip archives.\nBrowse, extract and test existing archives.\n\nToolbar icons: Microsoft Fluent UI System Icons (MIT).\nCopyright 2026 LunaArc Software.", showLogo: true);

    private Task ShowInformationAsync(string title, string heading, string body, bool showLogo = false)
    {
        var dialog = new Window
        {
            Title = title, Width = 470, Height = showLogo ? 420 : 310, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.White,
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
