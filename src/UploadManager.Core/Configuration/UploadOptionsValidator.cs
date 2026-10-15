using Microsoft.Extensions.Options;

namespace UploadManager.Core.Configuration;

/// <summary>
/// Validates <see cref="UploadOptions"/> at startup so configuration errors surface immediately.
/// </summary>
public sealed class UploadOptionsValidator : IValidateOptions<UploadOptions>
{
    public ValidateOptionsResult Validate(string? name, UploadOptions options)
    {
        var errors = new List<string>();

        if (options.Slots.Count == 0)
        {
            errors.Add("Es ist kein Upload-Feld konfiguriert (Upload:Slots).");
        }

        for (var i = 0; i < options.Slots.Count; i++)
        {
            var slot = options.Slots[i];
            var label = $"Upload:Slots:{i}";

            if (string.IsNullOrWhiteSpace(slot.Key))
            {
                errors.Add($"{label}: 'Key' darf nicht leer sein.");
            }

            if (string.IsNullOrWhiteSpace(slot.Title))
            {
                errors.Add($"{label}: 'Title' darf nicht leer sein.");
            }

            if (slot.MaxSizeMb <= 0)
            {
                errors.Add($"{label}: 'MaxSizeMb' muss größer als 0 sein.");
            }

            if (slot.AllowedExtensions.Any(e => UploadSlotDefinition.NormalizeExtension(e).Length == 0))
            {
                errors.Add($"{label}: 'AllowedExtensions' enthält einen leeren Eintrag.");
            }
        }

        var duplicateKeys = options.Slots
            .Where(s => !string.IsNullOrWhiteSpace(s.Key))
            .GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateKeys.Count > 0)
        {
            errors.Add($"Doppelte Slot-Keys: {string.Join(", ", duplicateKeys)}.");
        }

        if (!options.UseSimulation)
        {
            if (options.Endpoint is null || !options.Endpoint.IsAbsoluteUri
                || (options.Endpoint.Scheme != Uri.UriSchemeHttps && options.Endpoint.Scheme != Uri.UriSchemeHttp))
            {
                errors.Add("Upload:Endpoint muss eine absolute http(s)-URL sein, wenn UseSimulation = false ist.");
            }
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            errors.Add("Upload:Timeout muss größer als 0 sein.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
