namespace UploadManager.Core.Configuration;

/// <summary>
/// Settings for the upload section in <c>appsettings.json</c>.
/// </summary>
public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    /// <summary>
    /// When <see langword="true"/>, uploads are only simulated locally (no network traffic).
    /// </summary>
    public bool UseSimulation { get; set; } = true;

    /// <summary>
    /// Target endpoint that receives all files as one <c>multipart/form-data</c> request.
    /// Required when <see cref="UseSimulation"/> is <see langword="false"/>.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// HTTP timeout for a complete upload.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The upload fields shown in the UI, in display order.
    /// </summary>
    public List<UploadSlotDefinition> Slots { get; set; } = [];
}
