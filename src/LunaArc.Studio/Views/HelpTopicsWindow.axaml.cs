using Avalonia.Controls;
using Avalonia.Interactivity;
using LunaArc.Studio.Models;
using LunaArc.Studio.ViewModels;

namespace LunaArc.Studio.Views;

public partial class HelpTopicsWindow : Window
{
    private HelpTopicsViewModel? _observed;
    public HelpTopicsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_observed != null) _observed.PropertyChanged -= OnTopicChanged;
            _observed = Vm;
            if (_observed != null) _observed.PropertyChanged += OnTopicChanged;
        };
        Closed += (_, _) => { if (_observed != null) _observed.PropertyChanged -= OnTopicChanged; };
    }
    private void OnTopicChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HelpTopicsViewModel.SelectedTopic))
            this.FindControl<ScrollViewer>("HelpArticleScroll")!.Offset = default;
    }
    private HelpTopicsViewModel? Vm => DataContext as HelpTopicsViewModel;
    private void OnTopicSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm != null && e.AddedItems.OfType<HelpTopic>().FirstOrDefault() is { } topic) Navigate(topic);
    }
    private void OnRelatedTopicClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HelpTopic topic }) Navigate(topic);
    }
    private void Navigate(HelpTopic topic)
    {
        if (Vm == null) return;
        Vm.SelectedTopic = topic;
        this.FindControl<ScrollViewer>("HelpArticleScroll")!.Offset = default;
    }
}
