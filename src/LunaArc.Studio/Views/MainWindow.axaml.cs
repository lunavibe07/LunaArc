using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LunaArc.Studio.ViewModels;

namespace LunaArc.Studio.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => Vm?.Dispose();
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

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
            $"Version {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}\nArchive manager for Windows and Linux.\n\nCreate .larc, ZIP and TAR GZip archives.\nBrowse, extract and test existing archives.\n\nToolbar icons: Microsoft Fluent UI System Icons (MIT).\nCopyright 2026 LunaArc Software.");

    private Task ShowInformationAsync(string title, string heading, string body)
    {
        var dialog = new Window
        {
            Title = title, Width = 470, Height = 310, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.White,
            FontFamily = FontFamily, FontSize = 12
        };
        var close = new Button { Name = "CloseInformationButton", Content = "Close", Width = 85,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
        close.Click += (_, _) => dialog.Close();
        var content = new Grid { Margin = new Avalonia.Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        content.Children.Add(new TextBlock { Text = heading, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 0, 0, 12) });
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
