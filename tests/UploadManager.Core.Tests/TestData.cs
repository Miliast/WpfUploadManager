using UploadManager.Core.Configuration;

namespace UploadManager.Core.Tests;

internal static class TestData
{
    public static UploadSlotDefinition Pdf(bool required = true, double maxSizeMb = 1) => new()
    {
        Key = "document",
        Title = "Dokument",
        AllowedExtensions = [".pdf"],
        MaxSizeMb = maxSizeMb,
        IsRequired = required,
    };

    public static UploadOptions Options() => new()
    {
        UseSimulation = true,
        Slots =
        [
            new() { Key = "document", Title = "Dokument", AllowedExtensions = [".pdf", ".docx"], IsRequired = true },
            new() { Key = "data", Title = "Daten", AllowedExtensions = [".csv", ".xlsx"], IsRequired = true },
            new() { Key = "config", Title = "Konfiguration", AllowedExtensions = [".json"], IsRequired = true },
            new() { Key = "attachment", Title = "Anhang", AllowedExtensions = [".png", ".zip"], IsRequired = false },
        ],
    };
}
