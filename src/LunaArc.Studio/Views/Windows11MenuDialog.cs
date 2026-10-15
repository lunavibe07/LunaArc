using Avalonia.Controls;

namespace LunaArc.Studio.Views;

public sealed class Windows11MenuDialog : Window
{
    public Windows11MenuDialog()
    {
        Title = "Enable Windows 11 right-click menu";
        Width = 550; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Avalonia.Media.Brush.Parse("#102035");
        FontFamily = new Avalonia.Media.FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter");
        var enable = new Button { Name = "EnableWindows11MenuButton", Content = "Enable menu and trust certificate" };
        enable.Click += (_, _) => Close(true);
        var cancel = new Button { Name = "CancelWindows11MenuButton", Content = "Cancel", IsCancel = true, IsDefault = true };
        cancel.Click += (_, _) => Close(false);
        Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 20, Children =
        {
            new TextBlock { Text = "Extract directly from the right-click menu", FontSize = 20, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new TextBlock { Name = "MenuTrustExplanation", TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Text = "Adds the LunaArc submenu to the main Windows 11 menu, with Extract files, Extract to folder and Extract Here.\n\nThis preview package uses a test signing certificate. Enabling it authorizes adding the supplied certificate to Windows Trusted People for all users. Windows may ask for administrator permission for that step.\n\nOnly certificate import is elevated. Menu registration stays with your current account. Keep this LunaArc folder in place while its menu is registered.\n\nIf you cancel, the classic menu remains available under Show more options." },
            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, Children = { cancel, enable } }
        } };
    }
}
