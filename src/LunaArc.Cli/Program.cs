using System.Diagnostics;
using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.Pipeline;
using LunaArc.Shell;

namespace LunaArc.Cli;

/// <summary>
/// Command-line archive operations and shell registration.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintBanner();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        var backend = new StandardArchiveHandler();
        using var controller = new LunaJobController();
        var sw = Stopwatch.StartNew();

        try
        {
            string[] archiveCommands = { "list", "l", "extract-here", "x", "extract-smart", "pack-larc", "pack-zip", "test", "t" };
            if (archiveCommands.Contains(command) && (args.Length < 2 || args[1].StartsWith('-')))
                throw new ArgumentException($"Command '{command}' requires a file or directory path.");
            for (int i = 2; i < args.Length; i += 2)
            {
                if (args[i] is not ("-p" or "-o") || i + 1 >= args.Length)
                    throw new ArgumentException("Options must be -p <password> or -o <directory>.");
                if (args[i] == "-o" && command is not ("extract-here" or "x"))
                    throw new ArgumentException("The -o option is supported by extract-here only.");
                if (args[i] == "-p" && command == "pack-zip")
                    throw new NotSupportedException("ZIP output does not support passwords. Use pack-larc instead.");
            }
            switch (command)
            {
                case "list" or "l":
                {
                    string archivePath = args[1];
                    string? pwd = GetOptionValue(args, "-p");
                    var arena = await backend.ReadCatalogAsync(archivePath, pwd);
                    Console.WriteLine($"🌙 Archive: {Path.GetFileName(archivePath)} | Entries: {arena.Count:N0} | Catalog read time: {sw.ElapsedMilliseconds} ms");
                    Console.WriteLine(new string('-', 80));
                    for (int i = 0; i < Math.Min(arena.Count, 200); i++)
                    {
                        var node = arena.GetNode(i);
                        string typeTag = node.IsDirectory ? "[DIR] " : "[FILE]";
                        Console.WriteLine($"{typeTag} {arena.GetFullPath(i),-45} | Original: {node.UncompressedSize,10:N0} B | Saved: {node.CompressionRatio,5}%");
                    }
                    break;
                }

                case "extract-here" or "x":
                {
                    string archivePath = Path.GetFullPath(args[1]);
                    string destDir = GetOptionValue(args, "-o") ?? (Path.GetDirectoryName(archivePath) ?? Environment.CurrentDirectory);
                    string? pwd = GetOptionValue(args, "-p");

                    await backend.ExtractAsync(
                        archivePath,
                        new ExtractOptions { DestinationDirectory = destDir, Password = pwd },
                        controller,
                        (done, total, file) => Console.Write($"\r🌙 Extracting: [{done * 100 / Math.Max(1, total),3}%] {file,-40}"));

                    Console.WriteLine($"\n✅ Extracted to '{destDir}' in {sw.ElapsedMilliseconds} ms.");
                    break;
                }

                case "extract-smart":
                {
                    string archivePath = Path.GetFullPath(args[1]);
                    string parentDir = Path.GetDirectoryName(archivePath) ?? Environment.CurrentDirectory;
                    string archiveNameNoExt = Path.GetFileNameWithoutExtension(archivePath);
                    string? pwd = GetOptionValue(args, "-p");
                    var catalog = await backend.ReadCatalogAsync(archivePath, pwd);

                    string destDir = catalog.HasSingleRootFolder(out _)
                        ? parentDir
                        : Path.Combine(parentDir, archiveNameNoExt);

                    await backend.ExtractAsync(
                        archivePath,
                        new ExtractOptions { DestinationDirectory = destDir, Password = pwd },
                        controller,
                        (done, total, file) => Console.Write($"\r🌙 Extracting: [{done * 100 / Math.Max(1, total),3}%] {file,-40}"));

                    Console.WriteLine($"\n✅ Extracted to '{destDir}'.");
                    break;
                }

                case "pack-larc":
                {
                    string sourcePath = Path.GetFullPath(args[1]);
                    string outputArchive = sourcePath + ".larc";
                    string? pwd = GetOptionValue(args, "-p");

                    await backend.CompressAsync(
                        new[] { sourcePath },
                        new CompressOptions
                        {
                            OutputArchivePath = outputArchive,
                            Format = OutputArchiveFormat.LarcNative,
                            Level = LunaCompressionLevel.Normal,
                            Password = pwd
                        },
                        controller,
                        (done, total, file) => Console.Write($"\r🌙 Creating .larc: [{done * 100 / Math.Max(1, total),3}%] {file,-40}"));

                    Console.WriteLine($"\n✅ Created archive: {outputArchive}");
                    break;
                }

                case "pack-zip":
                {
                    string sourcePath = Path.GetFullPath(args[1]);
                    string outputArchive = sourcePath + ".zip";

                    await backend.CompressAsync(
                        new[] { sourcePath },
                        new CompressOptions
                        {
                            OutputArchivePath = outputArchive,
                            Format = OutputArchiveFormat.Zip,
                            Level = LunaCompressionLevel.Normal
                        },
                        controller,
                        (done, total, file) => Console.Write($"\r🌙 Creating .zip: [{done * 100 / Math.Max(1, total),3}%] {file,-40}"));

                    Console.WriteLine($"\n✅ Created archive: {outputArchive}");
                    break;
                }

                case "test" or "t":
                {
                    string archivePath = Path.GetFullPath(args[1]);
                    string? pwd = GetOptionValue(args, "-p");
                    var res = await backend.TestArchiveAsync(
                        archivePath,
                        pwd,
                        controller,
                        (curr, total, file) => Console.Write($"\r🔍 Testing archive [{curr}/{total}]: {file,-40}"));

                    Console.WriteLine();
                    if (res.IsHealthy)
                        Console.WriteLine($"✅ Archive test passed ({res.PassedFiles}/{res.TotalFilesTested} files).");
                    else
                    {
                        Console.WriteLine($"❌ Found {res.FailedFiles} failed files: {string.Join(", ", res.BrokenFileNames)}");
                        return 1;
                    }
                    break;
                }

                case "register-shell":
                {
                    string baseDir = AppContext.BaseDirectory;
                    string cliExe = Path.Combine(baseDir, OperatingSystem.IsWindows() ? "luna-cli.exe" : "luna-cli");
                    string guiExe = Path.Combine(baseDir, OperatingSystem.IsWindows() ? "LunaArc.exe" : "LunaArc");
                    ShellRegistrarFactory.CreateForCurrentOs().RegisterContextMenuAndAssociations(guiExe, cliExe);
                    Console.WriteLine("✅ LunaArc shell integration registered.");
                    break;
                }

                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    PrintBanner();
                    return 2;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[LunaArc error]: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static string? GetOptionValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static void PrintBanner()
    {
        Console.WriteLine("====================================================================");
        Console.WriteLine("🌙 LUNAARC ARCHIVER CLI v1.0.0 (Windows & Linux High-Speed Engine)");
        Console.WriteLine("====================================================================");
        Console.WriteLine("Usage:");
        Console.WriteLine("  luna-cli list <archive> [-p password]         : List archive entries");
        Console.WriteLine("  luna-cli extract-here <archive> [-o directory]  : Extract archive");
        Console.WriteLine("  luna-cli extract-smart <archive> [-p password] : Extract into a suitable folder");
        Console.WriteLine("  luna-cli pack-larc <source> [-p password]       : Create a .larc archive");
        Console.WriteLine("  luna-cli pack-zip <source>          : Create a ZIP archive");
        Console.WriteLine("  luna-cli test <archive> [-p password]         : Test archive integrity");
        Console.WriteLine("  luna-cli register-shell                        : Register shell integration");
    }
}
