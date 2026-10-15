using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LunaArc.Core.Contracts;
using LunaArc.Studio.ViewModels;

namespace LunaArc.Studio.Views;

public sealed record ExtractionChoice(string Destination, string Password, OverwriteMode OverwriteMode, bool DisplayAfterExtraction = false,
    ExtractionUpdateMode UpdateMode = ExtractionUpdateMode.ExtractAndReplace, bool KeepBrokenFiles = false);
public partial class ExtractOptionsWindow : Window
{
    public ExtractOptionsWindow()
    {
        InitializeComponent();
        DataContext = new ExtractionOptionsViewModel(string.Empty);
        Closed += (_, _) => { Vm.Dispose(); this.FindControl<TextBox>("ExtractionPasswordBox")!.Text = string.Empty; };
    }
    public ExtractOptionsWindow(string destination) : this() =>
        Vm.Destination = destination;
    private ExtractionOptionsViewModel Vm => (ExtractionOptionsViewModel)DataContext!;
    private void OnDestinationFocused(object? sender, Avalonia.Input.GotFocusEventArgs e)
    {
        if (sender is TextBox box) box.SelectAll();
    }
    private void OnDestinationUnfocused(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) box.SelectionStart = box.SelectionEnd = box.CaretIndex;
    }
    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Choose extraction folder", AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            Vm.Destination = path;
    }
    private void OnExtract(object? sender, RoutedEventArgs e)
    {
        if (!Vm.ValidateDestination()) { this.FindControl<TabControl>("ExtractionTabs")!.SelectedIndex = 0; this.FindControl<TextBox>("DestinationBox")!.Focus(); return; }
        Close(new ExtractionChoice(Vm.Destination, Vm.Password, Vm.OverwriteMode, Vm.DisplayAfterExtraction, Vm.UpdateMode, Vm.KeepBrokenFiles));
    }
    private async void OnNewFolder(object? sender, RoutedEventArgs e)
    {
        if (!Vm.ValidateDestination()) return;
        var dialog = CreateSmallDialog("New folder");
        var name = new TextBox { Name = "NewFolderName", Watermark = "Folder name" };
        var create = new Button { Name = "CreateFolderButton", Content = "Create", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => dialog.Close();
        create.Click += async (_, _) =>
        {
            create.IsEnabled = false;
            if (await Vm.CreateFolderAsync(name.Text ?? string.Empty)) dialog.Close();
            else { create.IsEnabled = true; name.Focus(); }
        };
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        error.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(Vm.Status)) { Source = Vm });
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 12, Children =
        { new TextBlock { Text = "Create a folder inside the destination path." }, name, error,
            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { create, cancel } } } };
        await dialog.ShowDialog(this);
    }
    private async void OnHelp(object? sender, RoutedEventArgs e)
    {
        var dialog = CreateSmallDialog("Extraction help");
        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 14, Children =
        { new TextBlock { Text = "Choose a destination or select a folder in the tree. New folder creates a folder inside that destination.\n\nUpdate mode selects files first: Extract and replace includes all files; Extract and update adds missing files and updates older copies; Freshen updates only existing older files. Equal/newer destination files and existing files without an archive timestamp are preserved by the last two modes.\n\nOverwrite mode then handles eligible conflicts. Choose Ask or Overwrite to replace older files; Skip still preserves them. Rename keeps both copies.\n\nMiscellaneous: Keep broken files retains available failed output under a unique .broken name for ZIP, LARC and SharpCompress extraction. TAR does not retain partial files; cancellation does not keep partial output. Existing destination files are preserved on failure. Display files opens the system file manager only after success.\n\nUse the Password tab for encrypted archives. Cancel leaves the source archive unchanged.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, close } };
        await dialog.ShowDialog(this);
    }
    private static Window CreateSmallDialog(string title) => new()
    {
        Title = title, Width = 440, SizeToContent = SizeToContent.Height, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark
    };
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
