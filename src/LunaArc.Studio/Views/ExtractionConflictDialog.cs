using Avalonia.Controls;
using LunaArc.Core.Contracts;

namespace LunaArc.Studio.Views;

internal static class ExtractionConflictDialog
{
    public static async Task<OverwriteMode> ShowAsync(Window owner, string path)
    {
        var dialog = new Window { Title = "File already exists", Width = 460, SizeToContent = SizeToContent.Height,
            CanResize = false, FontSize = 12, Background = Avalonia.Media.Brush.Parse("#102035"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        foreach (var (label, mode) in new[] { ("Overwrite", OverwriteMode.OverwriteAll), ("Skip", OverwriteMode.SkipExisting), ("Rename", OverwriteMode.AutoRename) })
        {
            var button = new Button { Content = label, Name = "Conflict" + label };
            button.Click += (_, _) => dialog.Close((OverwriteMode?)mode);
            buttons.Children.Add(button);
        }
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => dialog.Close();
        buttons.Children.Add(cancel);
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 16, Children =
        { new TextBlock { Text = "A file with this name already exists:\n\n" + path + "\n\nChoose how to handle this file.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons } };
        return await dialog.ShowDialog<OverwriteMode?>(owner) ?? throw new OperationCanceledException();
    }

}
