using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace UploadManager.Core.Services;

/// <summary>
/// Per-user preferences that survive an application restart.
/// </summary>
public sealed class UserSettings
{
    public string? LastFolder { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool ShowOnlySupportedFiles { get; set; } = true;
}

public interface IUserSettingsStore
{
    UserSettings Load();

    void Save(UserSettings settings);
}

/// <summary>
/// Stores <see cref="UserSettings"/> as JSON file (by default below <c>%LOCALAPPDATA%</c>).
/// </summary>
public sealed class JsonUserSettingsStore(string filePath, ILogger<JsonUserSettingsStore> logger) : IUserSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath { get; } = filePath;

    public UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var stream = File.OpenRead(FilePath);
                return JsonSerializer.Deserialize<UserSettings>(stream, SerializerOptions) ?? new UserSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Benutzereinstellungen konnten nicht gelesen werden: {Path}", FilePath);
        }

        return new UserSettings();
    }

    public void Save(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Benutzereinstellungen konnten nicht gespeichert werden: {Path}", FilePath);
        }
    }
}
