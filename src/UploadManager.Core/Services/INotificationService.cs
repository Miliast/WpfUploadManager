namespace UploadManager.Core.Services;

/// <summary>
/// Shows short, non-blocking messages to the user (snackbar/toast).
/// </summary>
public interface INotificationService
{
    void ShowSuccess(string title, string message);

    void ShowInfo(string title, string message);

    void ShowWarning(string title, string message);

    void ShowError(string title, string message);
}
