using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace LunaArc.Studio.Views;

public sealed record ExtractionChoice(string Destination, string Password, bool SkipExisting);
public partial class ExtractOptionsWindow : Window
{
    public ExtractOptionsWindow()
    {
        InitializeComponent();
        Closed += (_, _) => this.FindControl<TextBox>("ExtractionPasswordBox")!.Text = string.Empty;
    }
    public ExtractOptionsWindow(string destination) : this() =>
        this.FindControl<TextBox>("DestinationBox")!.Text = destination;
    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        { Title = "Choose extraction folder", AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            this.FindControl<TextBox>("DestinationBox")!.Text = path;
    }
    private void OnExtract(object? sender, RoutedEventArgs e)
    {
        string destination = this.FindControl<TextBox>("DestinationBox")!.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(destination)) { this.FindControl<TextBox>("DestinationBox")!.Focus(); return; }
        Close(new ExtractionChoice(destination, this.FindControl<TextBox>("ExtractionPasswordBox")!.Text ?? string.Empty,
            this.FindControl<CheckBox>("SkipExistingBox")!.IsChecked == true));
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
