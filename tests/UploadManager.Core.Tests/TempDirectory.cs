namespace UploadManager.Core.Tests;

/// <summary>
/// Creates an isolated temporary directory that is deleted after the test.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UploadManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateFile(string relativePath, int sizeBytes = 16)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, new byte[sizeBytes]);
        return fullPath;
    }

    public string CreateDirectory(string relativePath) =>
        Directory.CreateDirectory(System.IO.Path.Combine(Path, relativePath)).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}
