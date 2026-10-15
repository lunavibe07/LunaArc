using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LunaArc.Studio.ViewModels;

namespace LunaArc.Studio.Views;

public partial class ArchiveOptionsWindow : Window
{
    private readonly MainWindowViewModel? _settings;
    public CompressionOptionsViewModel Options => (CompressionOptionsViewModel)DataContext!;
    public string OutputPath => Options.ArchiveName;
    public ArchiveOptionsWindow() => InitializeComponent();
    public ArchiveOptionsWindow(MainWindowViewModel settings, IReadOnlyList<string>? sources = null) : this()
    {
        _settings = settings;
        DataContext = new CompressionOptionsViewModel(settings, sources);
        Closed += (_, _) => Options.PasswordInput = string.Empty;
    }
    private void OnAcceptClicked(object? sender, RoutedEventArgs e)
    {
        if (!Options.Validate()) return;
        if (_settings != null) Options.ApplyTo(_settings);
        Close(true);
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
            if (!Options.IsLarc && !string.IsNullOrEmpty(password.Text)) { error.Text = "Select LARC in General to create an encrypted archive."; return; }
            dialog.Close(password.Text ?? string.Empty);
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 9, Children =
        {
            new TextBlock { Text = "LARC encrypts file names and contents. Leave both fields blank to remove the password.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
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
            new TextBlock { Text = "Enter a new archive path or use Browse. Save outside the selected source folders. Existing archives are preserved; choose another name if the destination already exists.\n\nChoose LARC, ZIP or TAR GZip and a compression method. Faster methods trade file size for speed. Set password supports encrypted LARC creation, including encrypted names.\n\nTest archived files verifies the completed archive before publishing it. Sources are kept. Comments are supported for LARC and ZIP.\n\nWhen opened from Options, this dialog edits defaults for the next archive; OK does not edit an archive already open.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, close
        } };
        await dialog.ShowDialog(this);
    }
    private static Window SmallDialog(string title) => new()
    {
        Title = title, Width = 450, SizeToContent = SizeToContent.Height, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
        Background = Avalonia.Media.Brush.Parse("#252525"), FontSize = 12
    };
}
