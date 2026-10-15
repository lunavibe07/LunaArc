using System.Diagnostics;
using LunaArc.Core.Contracts;
using LunaArc.Core.Formats;
using LunaArc.Core.Pipeline;
using LunaArc.Core.Security;
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

        if (Environment.ProcessPath is { } self && SfxArchive.IsSfx(self))
            return await RunSfxAsync(self, args);

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
            string[] archiveCommands = { "list", "l", "extract-here", "x", "extract-smart", "extract-folder", "pack-larc", "pack-zip", "pack-7z", "pack-targz", "make-sfx", "recovery-record", "repair", "lock", "unlock", "test", "t" };
            bool scopedUnregister = command == "unregister-shell" && args.Length == 3 && args[1] == "--if-path";
            if (command is "register-shell" or "unregister-shell" && args.Length != 1 && !scopedUnregister)
                throw new ArgumentException($"Command '{command}' takes no arguments.");
            if (archiveCommands.Contains(command) && (args.Length < 2 || args[1].StartsWith('-')))
                throw new ArgumentException($"Command '{command}' requires a file or directory path.");
            for (int i = 2; !scopedUnregister && i < args.Length; i += 2)
            {
                if (args[i] is not ("-p" or "-o" or "--solid" or "--sfx" or "--recovery" or "--lock") || i + 1 >= args.Length)
                    throw new ArgumentException("Options require values: -p <password>, -o <output>, --solid/--sfx/--recovery/--lock <on|off>.");
                if (args[i] == "-o" && command is not ("extract-here" or "x" or "pack-larc" or "pack-zip" or "pack-7z" or "pack-targz" or "make-sfx" or "repair"))
                    throw new ArgumentException("This command does not accept an output option.");
                if (args[i].StartsWith("--", StringComparison.Ordinal) &&
                    (!command.StartsWith("pack-", StringComparison.Ordinal) || args[i + 1] is not ("on" or "off")))
                    throw new ArgumentException("Archiving flags require a pack command and an on/off value.");
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

                case "extract-smart" or "extract-folder":
                {
                    string archivePath = Path.GetFullPath(args[1]);
                    string parentDir = Path.GetDirectoryName(archivePath) ?? Environment.CurrentDirectory;
                    string archiveNameNoExt = GetArchiveFolderName(archivePath);
                    string? pwd = GetOptionValue(args, "-p");
                    var catalog = await backend.ReadCatalogAsync(archivePath, pwd);

                    string destDir = command == "extract-smart" && catalog.HasSingleRootFolder(out _)
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

                case "pack-larc" or "pack-zip" or "pack-7z" or "pack-targz":
                {
                    string source = Path.GetFullPath(args[1]);
                    var format = command switch { "pack-zip" => OutputArchiveFormat.Zip, "pack-7z" => OutputArchiveFormat.SevenZip,
                        "pack-targz" when !string.IsNullOrEmpty(GetOptionValue(args, "-p")) => OutputArchiveFormat.EncryptedTarGz,
                        "pack-targz" => OutputArchiveFormat.TarGz, _ => OutputArchiveFormat.LarcNative };
                    bool sfx = GetOptionValue(args, "--sfx") == "on";
                    string extension = sfx ? (OperatingSystem.IsWindows() ? ".exe" : ".run") : format switch
                    { OutputArchiveFormat.Zip => ".zip", OutputArchiveFormat.SevenZip => ".7z", OutputArchiveFormat.TarGz => ".tar.gz", OutputArchiveFormat.EncryptedTarGz => ".tar.gz.7z", _ => ".larc" };
                    string output = GetOptionValue(args, "-o") ?? source + extension;
                    string result = await ArchiveCreationService.CreateAsync(new[] { source }, new CompressOptions
                    {
                        OutputArchivePath = output, Format = format, Password = GetOptionValue(args, "-p"),
                        SolidArchive = GetOptionValue(args, "--solid") == "on"
                    }, controller, sfx, GetOptionValue(args, "--recovery") == "on", GetOptionValue(args, "--lock") == "on", test: true);
                    Console.WriteLine(result + " " + Path.GetFullPath(output));
                    break;
                }
                case "make-sfx":
                {
                    if (!SfxArchive.IsBundledProcess) throw new NotSupportedException("Use the published self-contained CLI to create a standalone extractor.");
                    string archive = Path.GetFullPath(args[1]);
                    string output = GetOptionValue(args, "-o") ?? throw new ArgumentException("Specify -o <new executable>.");
                    var format = archive.EndsWith(".larc", StringComparison.OrdinalIgnoreCase) ? OutputArchiveFormat.LarcNative
                        : archive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? OutputArchiveFormat.TarGz
                        : archive.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ? OutputArchiveFormat.SevenZip : OutputArchiveFormat.Zip;
                    if (!(await backend.TestArchiveAsync(archive, GetOptionValue(args, "-p"), controller)).IsHealthy) throw new InvalidDataException("Archive verification failed.");
                    ArchiveLock.EnsureWritable(output);
                    await SfxArchive.CreateAsync(Environment.ProcessPath!, archive, output, format, controller.Token);
                    Console.WriteLine("Created standalone command-line extractor: " + output);
                    break;
                }
                case "recovery-record":
                    await ArchiveRecoveryRecord.CreateAsync(args[1], ArchiveRecoveryRecord.RecordPath(args[1]), controller);
                    Console.WriteLine("Created recovery record: " + ArchiveRecoveryRecord.RecordPath(args[1]));
                    break;
                case "repair":
                    await ArchiveRecoveryRecord.RepairAsync(args[1], ArchiveRecoveryRecord.RecordPath(args[1]),
                        GetOptionValue(args, "-o") ?? throw new ArgumentException("Specify -o <new repaired archive>."), controller);
                    Console.WriteLine("Archive recovered and SHA-256 verified. The original was preserved.");
                    break;
                case "lock":
                    await ArchiveLock.LockAsync(args[1], controller.Token);
                    Console.WriteLine("Archive locked (advisory LunaArc protection). Keep its .lunalock companion.");
                    break;
                case "unlock":
                    ArchiveLock.Unlock(args[1]);
                    Console.WriteLine("Archive unlocked.");
                    break;

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
                    Console.WriteLine(OperatingSystem.IsWindows()
                        ? "LunaArc context menus installed. On Windows 11, select Show more options after right-clicking an archive."
                        : "LunaArc shell integration registered.");
                    break;
                }

                case "unregister-shell":
                {
                    if (scopedUnregister)
                    {
                        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Scoped menu cleanup is supported on Windows only.");
                        bool removed = new LunaArc.Shell.Windows.WinRegistryMenu().UnregisterIfOwnedBy(args[2]);
                        Console.WriteLine(removed ? "LunaArc shell integration removed." : "No context menus owned by this installation; other registrations preserved.");
                        break;
                    }
                    ShellRegistrarFactory.CreateForCurrentOs().UnregisterAll();
                    Console.WriteLine("LunaArc shell integration removed.");
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

    private static async Task<int> RunSfxAsync(string self, string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help")
            {
                Console.WriteLine("LunaArc standalone extractor. Usage: <this executable> -o <destination> [-p password]. Existing files are skipped; extracted files are never launched.");
                return 0;
            }
            for (int i = 0; i < args.Length; i += 2)
                if (args[i] is not ("-o" or "-p") || i + 1 >= args.Length) throw new ArgumentException("Use -o <destination> [-p password].");
            string destination = GetOptionValue(args, "-o") ?? throw new ArgumentException("Choose -o <destination>.");
            using var job = new LunaJobController();
            var backend = new StandardArchiveHandler();
            string? password = GetOptionValue(args, "-p");
            if (!(await backend.TestArchiveAsync(self, password, job)).IsHealthy) throw new InvalidDataException("Payload verification failed.");
            await backend.ExtractAsync(self, new ExtractOptions { DestinationDirectory = Path.GetFullPath(destination), Password = password, OverwriteMode = OverwriteMode.SkipExisting }, job);
            Console.WriteLine("Extracted to " + Path.GetFullPath(destination));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("SFX extraction failed: " + ex.Message); return 1; }
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

    private static string GetArchiveFolderName(string archivePath)
    {
        string name = Path.GetFileName(archivePath);
        foreach (string extension in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst" })
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return name[..^extension.Length];
        return Path.GetFileNameWithoutExtension(name);
    }

    private static void PrintBanner()
    {
        Console.WriteLine("====================================================================");
        Console.WriteLine($"LUNAARC ARCHIVER CLI v{typeof(Program).Assembly.GetName().Version?.ToString(3)} (Windows & Linux)");
        Console.WriteLine("====================================================================");
        Console.WriteLine("Usage:");
        Console.WriteLine("  luna-cli list <archive> [-p password]         : List archive entries");
        Console.WriteLine("  luna-cli extract-here <archive> [-o directory]  : Extract archive");
        Console.WriteLine("  luna-cli extract-smart <archive> [-p password] : Extract into a suitable folder");
        Console.WriteLine("  luna-cli extract-folder <archive> [-p password]: Always extract into an archive-named folder");
        Console.WriteLine("  luna-cli pack-larc <source> [-p password]       : Create a .larc archive");
        Console.WriteLine("  luna-cli pack-zip <source> [-p password] : Create ZIP (WinZip AES-256 with password)");
        Console.WriteLine("  luna-cli pack-7z <source> [-p password] [--solid on] : Create 7z (AES-256 with encrypted names)");
        Console.WriteLine("  luna-cli pack-targz <source> [-p password] : TAR GZip; password wraps it in .tar.gz.7z (extract twice)");
        Console.WriteLine("  Pack commands also accept --sfx on, --recovery on, --lock on and -o <new output>.");
        Console.WriteLine("  luna-cli make-sfx <archive> -o <new executable> : Published CLI only; standalone extractor");
        Console.WriteLine("  luna-cli recovery-record <archive>             : Create a .lrec companion");
        Console.WriteLine("  luna-cli repair <archive> -o <new archive>      : Repair using its .lrec companion");
        Console.WriteLine("  luna-cli lock|unlock <archive>                 : Advisory LunaArc lock");
        Console.WriteLine("  luna-cli test <archive> [-p password]         : Test archive integrity");
        Console.WriteLine("  luna-cli register-shell                        : Register shell integration");
        Console.WriteLine("  luna-cli unregister-shell                      : Remove shell integration");
        Console.WriteLine("  luna-cli unregister-shell --if-path <directory> : Remove Windows menus only when owned by that installation");
    }
}
