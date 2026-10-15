namespace UploadManager.Core.Services;

/// <summary>
/// A file that belongs to an upload slot.
/// </summary>
public sealed record UploadFile(string SlotKey, string FilePath, long SizeBytes);

/// <summary>
/// Progress of a single file within an upload.
/// </summary>
public readonly record struct UploadProgress(string SlotKey, long BytesSent, long TotalBytes)
{
    public double Fraction => TotalBytes <= 0 ? 1 : Math.Clamp((double)BytesSent / TotalBytes, 0, 1);
}

public interface IUploadService
{
    /// <summary>
    /// Uploads all files as one logical unit.
    /// </summary>
    /// <exception cref="OperationCanceledException">The upload was cancelled.</exception>
    Task UploadAsync(IReadOnlyList<UploadFile> files, IProgress<UploadProgress>? progress, CancellationToken cancellationToken);
}
