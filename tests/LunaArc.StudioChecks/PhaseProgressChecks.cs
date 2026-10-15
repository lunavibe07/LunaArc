using Avalonia.Controls;
using LunaArc.Core.Pipeline;
using LunaArc.Studio.ViewModels;
using LunaArc.Studio.Views;

internal static class PhaseProgressChecks
{
    public static void Run(Action<bool, string> check, Action<Window, string> capture)
    {
        using var vm = new ExtractionProgressViewModel { ArchiveName = "Synthetic phase progress fixture", CanPause = true };
        var window = new ExtractionProgressWindow { DataContext = vm }; window.Show();
        try
        {
            vm.ApplyProgress(new OperationProgress("Authenticating archive", 1048576, 4194304, "Archive.larc", true, 2097152, TimeSpan.FromSeconds(2)));
            capture(window, "phase-progress.png");
            check(window.FindControl<TextBlock>("ExtractionProgressStatus")!.Text!.Contains("Authenticating archive") &&
                window.FindControl<ProgressBar>("PhaseProgressBar")!.Value == 25, "Phase progress binds the actual stage and percentage");
            check(window.FindControl<TextBlock>("TransferDetailsText")!.Text!.Contains("MiB/s") && vm.TransferDetails.Contains("ETA"), "Phase progress displays measured speed and ETA");
            vm.ApplyProgress(new OperationProgress("Compressing 7z archive", 0, 0, "", true, 0, null));
            capture(window, "phase-indeterminate.png");
            check(window.FindControl<ProgressBar>("PhaseProgressBar")!.IsIndeterminate && !vm.TransferDetails.Contains("MiB/s"), "Unknown encoder work displays indeterminate progress without fabricated speed");
            vm.Finish("Completed");
            check(!vm.IsIndeterminate && vm.TransferDetails.Length == 0, "Completed progress clears stale throughput and ETA");
        }
        finally { vm.Finish("Closed"); window.Close(); }
    }
}
