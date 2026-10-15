using UploadManager.Core.Services;

namespace UploadManager.Core.Tests.Fakes;

public sealed class FakeFileDialogService : IFileDialogService
{
    public string? NextFolder { get; set; }

    public string? NextFile { get; set; }

    public string? LastFilter { get; private set; }

    public string? PickFolder(string? initialDirectory = null) => NextFolder;

    public string? PickFile(string title, string filter)
    {
        LastFilter = filter;
        return NextFile;
    }
}

public sealed class FakeNotificationService : INotificationService
{
    public List<(string Level, string Title, string Message)> Messages { get; } = [];

    public void ShowSuccess(string title, string message) => Messages.Add(("success", title, message));

    public void ShowInfo(string title, string message) => Messages.Add(("info", title, message));

    public void ShowWarning(string title, string message) => Messages.Add(("warning", title, message));

    public void ShowError(string title, string message) => Messages.Add(("error", title, message));
}

public sealed class FakeThemeService : IThemeService
{
    public AppTheme ActualTheme { get; private set; } = AppTheme.Light;

    public void Apply(AppTheme theme) => ActualTheme = theme == AppTheme.System ? AppTheme.Light : theme;
}

public sealed class InMemoryUserSettingsStore : IUserSettingsStore
{
    public UserSettings Settings { get; set; } = new();

    public int SaveCount { get; private set; }

    public UserSettings Load() => Settings;

    public void Save(UserSettings settings)
    {
        Settings = settings;
        SaveCount++;
    }
}

/// <summary>
/// Upload service whose outcome is controlled by the test.
/// </summary>
public sealed class FakeUploadService : IUploadService
{
    public IReadOnlyList<UploadFile>? UploadedFiles { get; private set; }

    public Exception? ExceptionToThrow { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public async Task UploadAsync(IReadOnlyList<UploadFile> files, IProgress<UploadProgress>? progress, CancellationToken cancellationToken)
    {
        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(cancellationToken);
        }

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        UploadedFiles = files;
    }
}
