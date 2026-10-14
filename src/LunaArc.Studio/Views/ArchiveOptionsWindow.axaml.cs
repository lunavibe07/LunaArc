using Avalonia.Controls;
using Avalonia.Interactivity;
using LunaArc.Studio.ViewModels;

namespace LunaArc.Studio.Views;

public partial class ArchiveOptionsWindow : Window
{
    private readonly int _originalFormat;
    private readonly int _originalLevel;
    private readonly string _originalPassword = string.Empty;
    private readonly string _originalComment = string.Empty;
    private bool _accepted;

    public ArchiveOptionsWindow()
    {
        InitializeComponent();
    }

    public ArchiveOptionsWindow(MainWindowViewModel viewModel) : this()
    {
        _originalFormat = viewModel.SelectedFormatIndex;
        _originalLevel = viewModel.SelectedLevelIndex;
        _originalPassword = viewModel.PasswordInput;
        _originalComment = viewModel.ArchiveComment;
        DataContext = viewModel;
        Closed += (_, _) =>
        {
            if (_accepted) return;
            viewModel.SelectedFormatIndex = _originalFormat;
            viewModel.SelectedLevelIndex = _originalLevel;
            viewModel.PasswordInput = _originalPassword;
            viewModel.ArchiveComment = _originalComment;
        };
    }

    private void OnAcceptClicked(object? sender, RoutedEventArgs e)
    {
        _accepted = true;
        Close(true);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
}
