namespace UploadManager.Core.IO;

public sealed record FileSystemEntry(string FullPath, string Name, bool IsDirectory, long SizeBytes, DateTime LastWriteTime);

public static class FileSystemReader
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    /// <summary>
    /// Lists the direct children of <paramref name="directory"/>: folders first, then files, each sorted by name.
    /// </summary>
    /// <param name="directory">Directory to enumerate.</param>
    /// <param name="fileFilter">Optional predicate; files for which it returns <see langword="false"/> are skipped.</param>
    public static IReadOnlyList<FileSystemEntry> GetChildren(string directory, Func<string, bool>? fileFilter = null)
    {
        var info = new DirectoryInfo(directory);

        var directories = info.EnumerateDirectories("*", Options)
            .Select(d => new FileSystemEntry(d.FullName, d.Name, IsDirectory: true, 0, d.LastWriteTime));

        var files = info.EnumerateFiles("*", Options)
            .Where(f => fileFilter?.Invoke(f.FullName) ?? true)
            .Select(f => new FileSystemEntry(f.FullName, f.Name, IsDirectory: false, f.Length, f.LastWriteTime));

        return
        [
            .. directories.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase),
            .. files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
    }
}
