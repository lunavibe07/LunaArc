using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LunaArc.Studio.Models;
using LunaArc.Studio.ViewModels;
using LunaArc.Studio.Views;

internal static class HelpTopicsChecks
{
    public static void Run(MainWindow browser, string root, Action<bool, string> check, Action<Window, string> capture)
    {
        var menu = browser.FindControl<MenuItem>("HelpTopicsMenu")!;
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var launched = browser.OwnedWindows.OfType<HelpTopicsWindow>().Single();
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        check(browser.OwnedWindows.OfType<HelpTopicsWindow>().Count() == 1 && launched.IsVisible,
            "Help Topics opens one owned, nonmodal guide from Help");
        launched.Close();
        browser.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F1 }); Dispatcher.UIThread.RunJobs();
        var keyboardHelp = browser.OwnedWindows.OfType<HelpTopicsWindow>().Single();
        check(keyboardHelp.IsVisible, "F1 opens Help Topics from the archive browser");
        keyboardHelp.Close();
        string favorites = Path.Combine(root, "help-favorites.json");
        var vm = new HelpTopicsViewModel(favorites);
        var window = new HelpTopicsWindow { DataContext = vm }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            check(HelpCatalog.All.Select(t => t.Id).Distinct().Count() == HelpCatalog.All.Count
                && HelpCatalog.All.All(t => !string.IsNullOrWhiteSpace(t.Body) && t.RelatedIds.All(id => HelpCatalog.All.Any(r => r.Id == id))),
                "Offline guide has unique topics, actual content and valid related links");
            capture(window, "help-topics.png");
            var index = window.FindControl<ListBox>("HelpIndexList")!;
            window.FindControl<TabControl>("HelpNavigationTabs")!.SelectedIndex = 1;
            index.SelectedItem = HelpCatalog.All.Single(t => t.Id == "passwords"); Dispatcher.UIThread.RunJobs();
            check(vm.SelectedTopic?.Id == "passwords" && window.FindControl<TextBlock>("HelpArticleBody")!.Text!.Contains("WinZip AES-256"),
                "Index selection displays the matching offline article");
            check(vm.BackCommand.CanExecute(null), "Selecting a help topic enables Back");
            vm.BackCommand.Execute(null); check(vm.SelectedTopic?.Id == "welcome" && vm.ForwardCommand.CanExecute(null), "Help Back restores the previous article");
            vm.ForwardCommand.Execute(null); check(vm.SelectedTopic?.Id == "passwords", "Help Forward restores the next article");
            vm.BackCommand.Execute(null);
            vm.SelectedTopic = HelpCatalog.All.Single(t => t.Id == "wrapped-tar");
            check(!vm.ForwardCommand.CanExecute(null), "Navigating after Back discards the old forward branch");
            vm.ToggleFavoriteCommand.Execute(null);
            check(new HelpTopicsViewModel(favorites).Favorites.Any(t => t.Id == "wrapped-tar"), "Favorites persist topic IDs and reload in a new guide");
            window.FindControl<TabControl>("HelpNavigationTabs")!.SelectedIndex = 3; capture(window, "help-favorites.png");
            vm.ToggleFavoriteCommand.Execute(null);
            check(new HelpTopicsViewModel(favorites).Favorites.Count == 0, "Removing a favorite updates saved favorites");
            var related = window.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is HelpTopic { Id: "extract" });
            related.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            check(vm.SelectedTopic?.Id == "extract", "Related topic links navigate to another offline article");
            window.FindControl<TabControl>("HelpNavigationTabs")!.SelectedIndex = 2;
            window.FindControl<TextBox>("HelpSearchBox")!.Text = "AES-256"; Dispatcher.UIThread.RunJobs();
            check(vm.SearchResults.Any(t => t.Id == "passwords") && vm.SearchResults.Any(t => t.Id == "recovery-password"), "Help search includes title and article text");
            window.FindControl<ListBox>("HelpSearchResults")!.SelectedItem = vm.SearchResults.First(t => t.Id == "passwords"); Dispatcher.UIThread.RunJobs();
            capture(window, "help-search.png");
            vm.SearchText = "nonexistent-synthetic-topic"; check(vm.SearchResults.Count == 0 && vm.SearchSummary.StartsWith("0"), "Help search explains no matching topics");
            vm.SearchText = " "; check(vm.SearchResults.Count == 0 && vm.SearchSummary.Contains("Enter"), "Empty help search shows a prompt");
            vm.ToggleNavigationCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            check(!window.FindControl<Border>("HelpNavigationPane")!.IsVisible && vm.NavigationButtonText == "Show navigation", "Hide navigation expands the reading pane");
            window.Width = 700; window.Height = 460; capture(window, "help-reading-compact.png");
            vm.ToggleNavigationCommand.Execute(null); vm.HomeCommand.Execute(null);
            check(vm.SelectedTopic?.Id == "welcome" && vm.ShowNavigation, "Help Home and Show navigation restore the guide");
            File.WriteAllText(favorites, "{broken-json");
            check(new HelpTopicsViewModel(favorites).Status.Contains("Could not load"), "Invalid favorites do not block offline help");
        }
        finally { window.Close(); }
    }
}
