using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LunaArc.Studio.ViewModels;

namespace LunaArc.Studio.Views;

public sealed record RecoveredArchive(string Path, string Password);

public partial class PasswordRecoveryWindow : Window
{
    private PasswordRecoveryViewModel Vm => (PasswordRecoveryViewModel)DataContext!;
    public PasswordRecoveryWindow()
    {
        InitializeComponent();
        DataContext = new PasswordRecoveryViewModel();
        Closing += (_, e) =>
        {
            if (Vm.IsBusy) { Vm.Cancel(); e.Cancel = true; }
        };
        Closed += (_, _) => Vm.ClearSecrets();
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an archive for password recovery", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Encrypted archive formats") { Patterns = new[] { "*.zip", "*.rar", "*.7z", "*.larc" } } }
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            Vm.ArchivePath = path;
            Vm.CanOpen = false; Vm.RecoveredPassword = string.Empty;
        }
    }

    private async void OnLoadVault(object? sender, RoutedEventArgs e) => await Vm.LoadVaultAsync();
    private async void OnSaveVault(object? sender, RoutedEventArgs e) => await Vm.SaveVaultAsync();
    private void OnShowPassword(object? sender, RoutedEventArgs e) =>
        this.FindControl<TextBox>("RecoveredPasswordBox")!.PasswordChar = (sender as CheckBox)?.IsChecked == true ? '\0' : '●';
    private void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (Vm.CanOpen && !Vm.IsBusy) Close(new RecoveredArchive(Vm.ArchivePath, Vm.RecoveredPassword));
    }
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
