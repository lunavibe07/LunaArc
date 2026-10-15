using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LunaArc.Studio.ViewModels;

namespace LunaArc.Studio.Views;

public partial class ArchiveOptionsWindow : Window
{
    private TaskCompletionSource<bool>? _standaloneResult;
    private string? _acceptedOutputPath;
    private readonly MainWindowViewModel? _settings;
    public CompressionOptionsViewModel Options => (CompressionOptionsViewModel)DataContext!;
    public string OutputPath => _acceptedOutputPath ?? Options.ArchiveName;
    public ArchiveOptionsWindow() => InitializeComponent();
    public ArchiveOptionsWindow(MainWindowViewModel settings, IReadOnlyList<string>? sources = null) : this()
    {
        _settings = settings;
        DataContext = new CompressionOptionsViewModel(settings, sources);
        Closed += (_, _) => { Options.PasswordInput = string.Empty; _standaloneResult?.TrySetResult(false); };
    }
    public Task<bool> ShowStandaloneAsync()
    {
        _standaloneResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Show();
        return _standaloneResult.Task;
    }
    private void OnAcceptClicked(object? sender, RoutedEventArgs e)
    {
        if (!Options.Validate()) return;
        _acceptedOutputPath = Options.ArchiveName;
        if (_settings != null) Options.ApplyTo(_settings);
        if (_standaloneResult != null) { _standaloneResult.TrySetResult(true); Close(); }
        else Close(true);
    }
    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? parent = Path.GetDirectoryName(Options.ArchiveName);
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save new archive", SuggestedFileName = Path.GetFileName(Options.ArchiveName),
                DefaultExtension = Options.Extension.TrimStart('.'),
                SuggestedStartLocation = Directory.Exists(parent) ? await StorageProvider.TryGetFolderFromPathAsync(parent!) : null,
                FileTypeChoices = new[] { new FilePickerFileType("Selected archive format") { Patterns = new[] { "*" + Options.Extension } } }
            });
            if (file?.TryGetLocalPath() is { } path) Options.ArchiveName = path;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { Options.Status = "Could not browse for an archive path: " + ex.Message; }
    }
    private async void OnSetPassword(object? sender, RoutedEventArgs e)
    {
        var dialog = SmallDialog("Set archive password");
        var password = new TextBox { Name = "NewArchivePassword", PasswordChar = '●', Text = Options.PasswordInput, MaxLength = 1024 };
        var confirm = new TextBox { Name = "ConfirmArchivePassword", PasswordChar = '●', Text = Options.PasswordInput, MaxLength = 1024 };
        var error = new TextBlock { Foreground = Avalonia.Media.Brushes.Orange, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var show = new CheckBox { Name = "ShowNewArchivePassword", Content = "Show password" };
        show.IsCheckedChanged += (_, _) => password.PasswordChar = confirm.PasswordChar = show.IsChecked == true ? '\0' : '●';
        var accept = new Button { Name = "AcceptArchivePassword", Content = "OK", IsDefault = true, MinWidth = 75 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 75 };
        accept.Click += (_, _) =>
        {
            if (password.Text != confirm.Text) { error.Text = "The passwords do not match."; return; }
            dialog.Close(password.Text ?? string.Empty);
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 9, Children =
        {
            new TextBlock { Text = "LARC and 7z encrypt file names and contents. ZIP uses WinZip AES-256; file names remain visible. TAR GZip is wrapped in encrypted 7z (.tar.gz.7z); extract twice. Leave both fields blank to remove the password.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new TextBlock { Text = "Enter password" }, password, new TextBlock { Text = "Re-enter password" }, confirm, show, error,
            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 8, Children = { accept, cancel } }
        } };
        string? result = await dialog.ShowDialog<string?>(this);
        if (result != null) Options.PasswordInput = result;
        password.Text = confirm.Text = string.Empty;
    }
    private async void OnHelp(object? sender, RoutedEventArgs e)
    {
        var dialog = SmallDialog("Compression help");
        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 14, Children =
        {
            new TextBlock { Text = "Enter a new archive path or use Browse. Save outside the selected source folders. Existing archives are preserved; choose another name if the destination already exists.\n\nChoose LARC, ZIP, TAR GZip or 7z and a compression method. Set password supports LARC, WinZip AES-256 ZIP (names visible), and AES-256 7z (names encrypted). TAR GZip with a password creates .tar.gz.7z; extract the outer 7z first, then its inner TAR GZip. Source deletion is unavailable for that nested wrapper. Encrypted ZIP is limited to 3 GiB of source data and fewer than 65,535 entries; encrypted ZIP64 and split output are unsupported.\n\nTest archived files verifies the completed archive before publishing it. Delete files after archiving is off by default and applies to one creation operation. It forces testing, compares archived contents with captured sources, then removes matching source files and represented empty directories. Changed/new files are kept. Verification needs temporary extraction space. Links, overlapping sources and filesystem roots cannot be deleted automatically.\n\nSolid compression uses 7z output and the packaged 7-Zip encoder. It stages sources on disk; pause applies before/after encoding and cancellation stops the encoder. SFX embeds a self-contained LunaArc extractor for the current OS and architecture; it never runs extracted files. Published packages are required for SFX creation. Recovery writes a .lrec companion (one damaged 1 MiB block per 16-block group). Use Tools to repair to a new file. Keep the record beside the archive. Lock writes a .lunalock companion and marks the archive read-only; Tools provides explicit unlock. This is advisory protection in LunaArc, not encryption or tamper-proof access control. These options create no RAR records. Comments work with LARC and ZIP.\n\nWhen opened from Options, this dialog edits defaults; source deletion cannot be saved as a default.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, close
        } };
        await dialog.ShowDialog(this);
    }
    private static Window SmallDialog(string title) => new()
    {
        Title = title, Width = 450, SizeToContent = SizeToContent.Height, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
        Background = Avalonia.Media.Brush.Parse("#102035"), FontSize = 12
    };
}
