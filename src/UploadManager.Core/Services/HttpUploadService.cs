using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UploadManager.Core.Configuration;
using UploadManager.Core.IO;

namespace UploadManager.Core.Services;

/// <summary>
/// Sends all files in a single <c>multipart/form-data</c> POST request to <see cref="UploadOptions.Endpoint"/>.
/// Each file is sent as form field named after its slot key.
/// </summary>
public sealed class HttpUploadService(
    IHttpClientFactory httpClientFactory,
    IOptions<UploadOptions> options,
    ILogger<HttpUploadService> logger) : IUploadService
{
    public const string HttpClientName = "upload";

    private const int BufferSize = 81_920;

    public async Task UploadAsync(IReadOnlyList<UploadFile> files, IProgress<UploadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);

        var endpoint = options.Value.Endpoint
            ?? throw new InvalidOperationException("Es ist kein Upload-Endpunkt konfiguriert (Upload:Endpoint).");

        // Disposing the multipart content also disposes every part and its file stream.
        using var content = new MultipartFormDataContent();

        foreach (var file in files)
        {
            var fileStream = new FileStream(file.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            var totalBytes = fileStream.Length;
            var slotKey = file.SlotKey;
            var progressStream = new ProgressReportingStream(
                fileStream,
                bytesRead => progress?.Report(new UploadProgress(slotKey, bytesRead, totalBytes)));

            var part = new StreamContent(progressStream, BufferSize);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, slotKey, Path.GetFileName(file.FilePath));
        }

        var client = httpClientFactory.CreateClient(HttpClientName);

        logger.LogInformation("Starte Upload von {Count} Datei(en) nach {Endpoint}", files.Count, endpoint);

        using var response = await client.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            logger.LogError("Upload fehlgeschlagen: {Status} {Body}", (int)response.StatusCode, body);
            throw new HttpRequestException(
                $"Der Server hat den Upload abgelehnt ({(int)response.StatusCode} {response.ReasonPhrase}).",
                inner: null,
                response.StatusCode);
        }

        logger.LogInformation("Upload erfolgreich ({Status})", (int)response.StatusCode);
    }
}
