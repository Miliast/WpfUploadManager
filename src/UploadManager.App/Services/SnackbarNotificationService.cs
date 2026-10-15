using UploadManager.Core.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace UploadManager.App.Services;

/// <summary>
/// Shows notifications as WPF-UI snackbar at the bottom of the main window.
/// </summary>
public sealed class SnackbarNotificationService(ISnackbarService snackbarService) : INotificationService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ErrorTimeout = TimeSpan.FromSeconds(8);

    public void ShowSuccess(string title, string message) =>
        Show(title, message, ControlAppearance.Success, SymbolRegular.CheckmarkCircle24, DefaultTimeout);

    public void ShowInfo(string title, string message) =>
        Show(title, message, ControlAppearance.Info, SymbolRegular.Info24, DefaultTimeout);

    public void ShowWarning(string title, string message) =>
        Show(title, message, ControlAppearance.Caution, SymbolRegular.Warning24, DefaultTimeout);

    public void ShowError(string title, string message) =>
        Show(title, message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24, ErrorTimeout);

    private void Show(string title, string message, ControlAppearance appearance, SymbolRegular symbol, TimeSpan timeout)
    {
        try
        {
            snackbarService.Show(title, message, appearance, new SymbolIcon(symbol), timeout);
        }
        catch (Exception ex) when (ex is ArgumentNullException or InvalidOperationException)
        {
            // The presenter is registered by the main window; before it exists there is nowhere to show the message.
        }
    }
}
