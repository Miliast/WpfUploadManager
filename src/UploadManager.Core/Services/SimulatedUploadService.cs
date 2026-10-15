using Microsoft.Extensions.Logging;

namespace UploadManager.Core.Services;

/// <summary>
/// Pretends to upload files; useful for UI development and demos without a backend.
/// </summary>
public sealed class SimulatedUploadService(ILogger<SimulatedUploadService> logger) : IUploadService
{
    private const int StepsPerFile = 25;

    public TimeSpan StepDelay { get; init; } = TimeSpan.FromMilliseconds(40);

    public async Task UploadAsync(IReadOnlyList<UploadFile> files, IProgress<UploadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        foreach (var file in files)
        {
            for (var step = 1; step <= StepsPerFile; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (StepDelay > TimeSpan.Zero)
                {
                    await Task.Delay(StepDelay, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(new UploadProgress(file.SlotKey, file.SizeBytes * step / StepsPerFile, file.SizeBytes));
            }

            logger.LogInformation("Simulierter Upload abgeschlossen: {Slot} -> {File}", file.SlotKey, file.FilePath);
        }
    }
}
