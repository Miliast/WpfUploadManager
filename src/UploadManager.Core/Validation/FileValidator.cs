using UploadManager.Core.Configuration;
using UploadManager.Core.Formatting;

namespace UploadManager.Core.Validation;

public readonly record struct FileValidationResult(bool IsValid, string? ErrorMessage, long SizeBytes)
{
    public static FileValidationResult Success(long sizeBytes) => new(true, null, sizeBytes);

    public static FileValidationResult Failure(string message, long sizeBytes = 0) => new(false, message, sizeBytes);
}

/// <summary>
/// Checks a file against the rules of an <see cref="UploadSlotDefinition"/>.
/// </summary>
public static class FileValidator
{
    public static FileValidationResult Validate(string? path, UploadSlotDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(path))
        {
            return FileValidationResult.Failure("Es wurde keine Datei angegeben.");
        }

        FileInfo file;
        try
        {
            file = new FileInfo(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return FileValidationResult.Failure("Der Dateipfad ist ungültig.");
        }

        if (!file.Exists)
        {
            return Directory.Exists(file.FullName)
                ? FileValidationResult.Failure("Ordner können nicht hochgeladen werden – bitte eine Datei wählen.")
                : FileValidationResult.Failure("Die Datei wurde nicht gefunden.");
        }

        if (!definition.Accepts(file.FullName))
        {
            var extension = UploadSlotDefinition.NormalizeExtension(file.Extension);
            var shown = extension.Length == 0 ? "ohne Endung" : $"„{extension}“";
            return FileValidationResult.Failure(
                $"Dateityp {shown} ist hier nicht erlaubt. Erlaubt: {DescribeExtensions(definition)}.",
                file.Length);
        }

        if (file.Length == 0)
        {
            return FileValidationResult.Failure("Die Datei ist leer.");
        }

        if (file.Length > definition.MaxSizeBytes)
        {
            return FileValidationResult.Failure(
                $"Die Datei ist zu groß ({FileSizeFormatter.Format(file.Length)}). Maximal erlaubt: {FileSizeFormatter.Format(definition.MaxSizeBytes)}.",
                file.Length);
        }

        return FileValidationResult.Success(file.Length);
    }

    /// <summary>
    /// Returns e.g. <c>PDF, DOCX</c> or <c>alle Dateitypen</c>.
    /// </summary>
    public static string DescribeExtensions(UploadSlotDefinition definition) =>
        definition.AllowedExtensions.Count == 0
            ? "alle Dateitypen"
            : string.Join(", ", definition.AllowedExtensions
                .Select(e => UploadSlotDefinition.NormalizeExtension(e).TrimStart('.').ToUpperInvariant())
                .Distinct());
}
