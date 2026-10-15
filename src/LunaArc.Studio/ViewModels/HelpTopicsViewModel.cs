using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LunaArc.Studio.Models;

namespace LunaArc.Studio.ViewModels;

public partial class HelpTopicsViewModel : ObservableObject
{
    private readonly string _favoritesPath;
    private readonly List<HelpTopic> _history = new();
    private int _historyIndex = -1;
    private bool _movingHistory;
    public IReadOnlyList<HelpTopic> Contents => HelpCatalog.Contents;
    public IReadOnlyList<HelpTopic> Index { get; } = HelpCatalog.All.Where(t => t.Children.Length == 0).OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    public ObservableCollection<HelpTopic> Favorites { get; } = new();
    [ObservableProperty] private HelpTopic? _selectedTopic;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _showNavigation = true;
    [ObservableProperty] private string _status = "Offline help";
    public string NavigationButtonText => ShowNavigation ? "Hide navigation" : "Show navigation";
    public string FavoriteButtonText => SelectedTopic != null && Favorites.Contains(SelectedTopic) ? "Remove favorite" : "Add favorite";
    public IEnumerable<HelpTopic> RelatedTopics => SelectedTopic?.RelatedIds.Select(id => HelpCatalog.All.First(t => t.Id == id)) ?? Enumerable.Empty<HelpTopic>();
    public IReadOnlyList<HelpTopic> SearchResults { get; private set; } = Array.Empty<HelpTopic>();
    public string SearchSummary => SearchText.Trim().Length == 0 ? "Enter a word to search this guide." : $"{SearchResults.Count} topic(s) found";

    public HelpTopicsViewModel(string? favoritesPath = null)
    {
        _favoritesPath = favoritesPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaArc", "help-favorites.json");
        try
        {
            if (File.Exists(_favoritesPath))
            {
                if (new FileInfo(_favoritesPath).Length > 65536) throw new IOException("Favorites file exceeds its size limit.");
                var ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_favoritesPath)) ?? [];
                foreach (var topic in HelpCatalog.All.Where(t => ids.Contains(t.Id))) Favorites.Add(topic);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Status = "Could not load saved favorites. You can still browse help."; }
        SelectedTopic = Contents[0];
    }
    partial void OnSelectedTopicChanged(HelpTopic? value)
    {
        if (value == null) return;
        if (!_movingHistory)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            _history.Add(value); _historyIndex = _history.Count - 1;
        }
        BackCommand.NotifyCanExecuteChanged(); ForwardCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RelatedTopics)); OnPropertyChanged(nameof(FavoriteButtonText));
    }
    partial void OnSearchTextChanged(string value)
    {
        string query = value.Trim();
        SearchResults = query.Length == 0 ? Array.Empty<HelpTopic>() : Index.Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || t.Body.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        OnPropertyChanged(nameof(SearchResults)); OnPropertyChanged(nameof(SearchSummary));
    }
    partial void OnShowNavigationChanged(bool value) => OnPropertyChanged(nameof(NavigationButtonText));
    private bool CanGoBack() => _historyIndex > 0;
    private bool CanGoForward() => _historyIndex + 1 < _history.Count;
    [RelayCommand(CanExecute = nameof(CanGoBack))] private void Back() => MoveHistory(-1);
    [RelayCommand(CanExecute = nameof(CanGoForward))] private void Forward() => MoveHistory(1);
    private void MoveHistory(int offset)
    {
        _movingHistory = true;
        try { _historyIndex += offset; SelectedTopic = _history[_historyIndex]; }
        finally { _movingHistory = false; }
    }
    [RelayCommand] private void Home() => SelectedTopic = Contents[0];
    [RelayCommand] private void ToggleNavigation() => ShowNavigation = !ShowNavigation;
    [RelayCommand]
    private void ToggleFavorite()
    {
        if (SelectedTopic == null) return;
        if (!Favorites.Remove(SelectedTopic)) Favorites.Add(SelectedTopic);
        OnPropertyChanged(nameof(FavoriteButtonText));
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_favoritesPath)!);
            temporary = _favoritesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Favorites.Select(t => t.Id).ToArray()));
            File.Move(temporary, _favoritesPath, overwrite: true);
            Status = "Favorites saved";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status = "Could not save favorites. Current selections remain available in this window."; }
        finally { if (temporary != null && File.Exists(temporary)) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
    }
}
