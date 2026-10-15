namespace UploadManager.Core.Configuration;

/// <summary>
/// Describes one upload field: which file is expected and which rules it has to satisfy.
/// </summary>
public sealed class UploadSlotDefinition
{
    /// <summary>
    /// Stable technical key, also used as form field name for the HTTP upload.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Allowed file extensions (e.g. <c>.pdf</c>). An empty list allows every file type.
    /// </summary>
    public List<string> AllowedExtensions { get; set; } = [];

    public double MaxSizeMb { get; set; } = 50;

    public bool IsRequired { get; set; } = true;

    public long MaxSizeBytes => (long)(MaxSizeMb * 1024 * 1024);

    public bool Accepts(string path)
    {
        if (AllowedExtensions.Count == 0)
        {
            return true;
        }

        var extension = NormalizeExtension(Path.GetExtension(path));
        return extension.Length > 0
            && AllowedExtensions.Any(allowed => string.Equals(NormalizeExtension(allowed), extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Converts <c>pdf</c>, <c>.PDF</c> or <c> .pdf </c> to <c>.pdf</c>.
    /// </summary>
    public static string NormalizeExtension(string? extension)
    {
        var trimmed = extension?.Trim().TrimStart('.') ?? string.Empty;
        return trimmed.Length == 0 ? string.Empty : "." + trimmed.ToLowerInvariant();
    }
}
