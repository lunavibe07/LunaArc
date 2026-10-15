namespace LunaArc.Shell;

public enum ExplorerAction { Open, Extract, ExtractFolder, ExtractHere, Add, AddZip }

public sealed record ExplorerRequest(ExplorerAction Action, string Path)
{
    public static ExplorerRequest Parse(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Explorer commands require exactly one path.");
        var action = args[0] switch
        {
            "--shell-open" => ExplorerAction.Open,
            "--shell-extract" => ExplorerAction.Extract,
            "--shell-extract-folder" => ExplorerAction.ExtractFolder,
            "--shell-extract-here" => ExplorerAction.ExtractHere,
            "--shell-add" => ExplorerAction.Add,
            "--shell-add-zip" => ExplorerAction.AddZip,
            _ => throw new ArgumentException("Unknown Explorer command.")
        };
        return new(action, System.IO.Path.GetFullPath(args[1]));
    }

    public string ExtractionDirectory => Action == ExplorerAction.ExtractHere
        ? System.IO.Path.GetDirectoryName(Path)!
        : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, GetFolderName(Path));

    public static string GetFolderName(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        if (name.EndsWith(".001", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        foreach (string extension in new[] { ".tar.gz", ".tar.bz2", ".tar.xz" })
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return name[..^extension.Length];
        string result = System.IO.Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrWhiteSpace(result) ? name : result;
    }
}
