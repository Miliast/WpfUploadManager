namespace UploadManager.Core.Services;

/// <summary>
/// Abstraction over the native open dialogs, so view models stay testable.
/// </summary>
public interface IFileDialogService
{
    /// <returns>The selected folder or <see langword="null"/> if the dialog was cancelled.</returns>
    string? PickFolder(string? initialDirectory = null);

    /// <param name="title">Dialog title.</param>
    /// <param name="filter">Filter in Win32 format, e.g. <c>PDF (*.pdf)|*.pdf</c>.</param>
    /// <returns>The selected file or <see langword="null"/> if the dialog was cancelled.</returns>
    string? PickFile(string title, string filter);
}
