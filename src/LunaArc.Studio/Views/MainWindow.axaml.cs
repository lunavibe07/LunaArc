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
        if (Vm == null) return;
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
}
