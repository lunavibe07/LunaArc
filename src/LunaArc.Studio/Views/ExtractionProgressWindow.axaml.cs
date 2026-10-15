using Avalonia.Controls;
using LunaArc.Studio.ViewModels;

namespace LunaArc.Studio.Views;

public partial class ExtractionProgressWindow : Window
{
    public ExtractionProgressWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (DataContext is ExtractionProgressViewModel { IsWorking: true } vm)
            {
                e.Cancel = true;
                vm.CloseOrCancel();
            }
        };
    }
}
