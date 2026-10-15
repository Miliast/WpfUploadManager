using System.IO;
using System.Windows;
using Microsoft.Win32;
using UploadManager.Core.Services;

namespace UploadManager.App.Services;

/// <summary>
/// Uses the native Windows open dialogs.
/// </summary>
public sealed class WpfFileDialogService : IFileDialogService
{
    public string? PickFolder(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Ordner auswählen",
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return ShowDialog(dialog) ? dialog.FolderName : null;
    }

    public string? PickFile(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
        };

        return ShowDialog(dialog) ? dialog.FileName : null;
    }

    private static bool ShowDialog(CommonDialog dialog)
    {
        var owner = Application.Current?.MainWindow;
        return (owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true;
    }
}
