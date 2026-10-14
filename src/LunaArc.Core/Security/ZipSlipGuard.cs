namespace LunaArc.Core.Security;

/// <summary>
/// Validates extraction paths inside the destination root.
/// </summary>
public static class ZipSlipGuard
{
    public static string ValidateAndResolvePath(string destinationRoot, string entryRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryRelativePath);
        string normalized = entryRelativePath.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Contains('\0') ||
            normalized.Split('/').Any(part => part == ".."))
            throw new UnauthorizedAccessException($"Unsafe archive entry path: {entryRelativePath}");

        string fullRoot = Path.GetFullPath(destinationRoot);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
            fullRoot += Path.DirectorySeparatorChar;

        string sanitized = entryRelativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);

        string fullTarget = Path.GetFullPath(Path.Combine(fullRoot, sanitized));

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullTarget.StartsWith(fullRoot, comparison))
        {
            throw new UnauthorizedAccessException(
                $"[LunaArc Security] Blocked archive path outside the destination directory: {entryRelativePath}");
        }

        // Reject existing links on the path; lexical containment alone does not prevent link escapes.
        string rootWithoutSeparator = Path.TrimEndingDirectorySeparator(fullRoot);
        for (string? current = fullTarget; current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException($"Archive destination contains a symbolic link or reparse point: {current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            if (current.Equals(rootWithoutSeparator, comparison)) break;
        }

        return fullTarget;
    }

    public static string GetAutoRenamedPath(string existingFilePath)
    {
        string dir = Path.GetDirectoryName(existingFilePath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(existingFilePath);
        string ext = Path.GetExtension(existingFilePath);

        int counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name} ({counter}){ext}");
            counter++;
        } while (File.Exists(candidate));

        return candidate;
    }
}
