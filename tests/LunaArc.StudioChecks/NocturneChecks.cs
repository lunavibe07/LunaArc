using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.Pipeline;
using LunaArc.Studio.ViewModels;
using LunaArc.Studio.Views;

internal static class NocturneChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Window, string> capture)
    {
        string directory = Path.Combine(root, "nocturne-fixture"); Directory.CreateDirectory(directory);
        foreach (string name in new[] { "textures", "audio", "documents" }) Directory.CreateDirectory(Path.Combine(directory, name));
        File.WriteAllText(Path.Combine(directory, "textures", "material.txt"), "Synthetic texture fixture");
        foreach (string name in new[] { "scene.blend", "palette.png", "readme.md" }) File.WriteAllText(Path.Combine(directory, name), "Synthetic Nocturne visual fixture (not a real media file)");
        string archive = Path.Combine(root, "LunaStudio-assets.7z");
        using (var job = new LunaJobController()) Pump(ArchiveCreationService.CreateAsync(Directory.GetFileSystemEntries(directory), new CompressOptions { OutputArchivePath = archive, Format = OutputArchiveFormat.SevenZip }, job, test: true));
        using var vm = new MainWindowViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1487, Height = 1058 }; window.Show();
        try
        {
            Pump(vm.OpenArchiveFileAsync(archive));
            vm.SelectedItem = vm.VisibleItems.Single(t => t.Name == "textures");
            vm.ExtractionDestination = OperatingSystem.IsWindows() ? @"D:\Projects\LunaStudio-assets" : "/home/demo/Projects/LunaStudio-assets";
            Dispatcher.UIThread.RunJobs();
            check(window.FindControl<Border>("SelectionInspector")!.IsVisible && vm.SelectionIsFolder && vm.SelectionName == "textures", "Nocturne inspector follows selected folders at desktop width");
            check(new FileInfo(archive).Length > 0 && !vm.ArchiveSubtitle.Contains("| 0 B packed"), "Nocturne archive heading uses the actual container size");
            capture(window, "nocturne-desktop.png");
            var table = window.FindControl<DataGrid>("ArchiveGrid")!;
            check(table.Columns.Count(c => c.IsVisible) == 4, "Nocturne defaults to Name, Size, Packed and Modified columns");
            var metadata = window.FindControl<MenuItem>("MetadataColumnsMenu")!;
            metadata.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
            check(table.Columns.All(c => c.IsVisible), "Additional Type and CRC32 columns remain available");
            metadata.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            vm.SelectedItem = vm.VisibleItems.Single(t => t.Name == "scene.blend"); Dispatcher.UIThread.RunJobs();
            check(window.FindControl<TextBlock>("InspectorSize")!.Text == vm.SelectedItem.OriginalSizeDisplay && !vm.SelectionIsFolder, "Nocturne inspector updates file size and type from actual metadata");
            vm.ExtractionDestination = Path.Combine(root, "nocturne-custom-output");
            window.FindControl<Button>("ExtractButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(WaitUntil(() => window.OwnedWindows.OfType<ExtractOptionsWindow>().Any()));
            var options = window.OwnedWindows.OfType<ExtractOptionsWindow>().Single();
            check(options.FindControl<TextBox>("DestinationBox")!.Text == vm.ExtractionDestination, "Extraction dock passes its chosen destination into the real options dialog");
            options.Close(); Pump(WaitUntil(() => !window.OwnedWindows.Any()));
            vm.IsBusy = true; Dispatcher.UIThread.RunJobs();
            check(!window.FindControl<Button>("ExtractButton")!.IsEnabled && !window.FindControl<TextBox>("QuickDestinationBox")!.IsEnabled && !table.IsEnabled,
                "Nocturne busy state prevents overlapping archive actions");
            vm.IsBusy = false;
            window.Width = 720; window.Height = 560; Dispatcher.UIThread.RunJobs();
            check(!window.FindControl<Border>("SelectionInspector")!.IsVisible, "Nocturne compact layout hides the inspector to preserve the file list");
            capture(window, "nocturne-compact.png");
            var extract = window.FindControl<Button>("ExtractButton")!;
            var right = extract.TranslatePoint(new Point(extract.Bounds.Width, 0), window);
            check(right is { X: <= 720 } && extract.Bounds.Width > 0, "Primary extraction control remains visible at minimum width");
            vm.CloseArchive();
            check(vm.ExtractionDestination.Length == 0 && vm.ArchiveDisplayName == "Your archives, in orbit.", "Closing an archive resets Nocturne destination and heading");
        }
        finally { foreach (var child in window.OwnedWindows.ToArray()) child.Close(); window.Close(); }
    }
    private static async Task WaitUntil(Func<bool> condition) { while (!condition()) await Task.Delay(10); }
    private static void Pump(Task task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Nocturne UI operation did not complete.");
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(2);
        }
        task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
}
