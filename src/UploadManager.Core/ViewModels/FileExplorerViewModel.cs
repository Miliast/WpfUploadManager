using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UploadManager.Core.Services;

namespace UploadManager.Core.ViewModels;

/// <summary>
/// State of the file explorer on the left side. The tree itself (loading, expanding, drag &amp; drop)
/// is handled by the reusable <c>FileTreeView</c> control, which binds to this view model.
/// </summary>
public sealed partial class FileExplorerViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly INotificationService _notificationService;
    private readonly Func<string, bool> _isSupportedFile;

    public FileExplorerViewModel(
        IFileDialogService fileDialogService,
        INotificationService notificationService,
        Func<string, bool> isSupportedFile,
        bool showOnlySupportedFiles = true)
    {
        _fileDialogService = fileDialogService;
        _notificationService = notificationService;
        _isSupportedFile = isSupportedFile;
        ShowOnlySupportedFiles = showOnlySupportedFiles;
    }

    /// <summary>Raised with the file path when a file is double-clicked or confirmed with Enter.</summary>
    public event EventHandler<string>? FileActivated;

    /// <summary>Raised when the tree should reload its content (expanded folders are kept).</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>Opened folder; its contents are the top level of the tree.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoot))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial string? RootPath { get; private set; }

    /// <summary>Display name of the opened folder.</summary>
    [ObservableProperty]
    public partial string? RootName { get; private set; }

    /// <summary>Full path of the entry selected in the tree (file or folder).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFilePath))]
    public partial string? SelectedPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileFilter))]
    public partial bool ShowOnlySupportedFiles { get; set; }

    public bool HasRoot => RootPath is not null;

    /// <summary>The selected entry if it is a file, otherwise <see langword="null"/>.</summary>
    public string? SelectedFilePath => SelectedPath is not null && File.Exists(SelectedPath) ? SelectedPath : null;

    /// <summary>File filter for the tree: only files that fit an upload field, or <see langword="null"/> for all files.</summary>
    public Func<string, bool>? FileFilter => ShowOnlySupportedFiles ? _isSupportedFile : null;

    /// <summary>Shows the contents of <paramref name="path"/> in the tree.</summary>
    public bool Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            _notificationService.ShowWarning("Ordner nicht gefunden", $"„{path}“ existiert nicht oder ist nicht erreichbar.");
            return false;
        }

        var directory = new DirectoryInfo(path);

        if (string.Equals(directory.FullName, RootPath, StringComparison.OrdinalIgnoreCase))
        {
            RefreshRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        SelectedPath = null;
        RootName = string.IsNullOrEmpty(directory.Name) ? directory.FullName : directory.Name;
        RootPath = directory.FullName;
        return true;
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var folder = _fileDialogService.PickFolder(RootPath);
        if (folder is not null)
        {
            Open(folder);
        }
    }

    [RelayCommand(CanExecute = nameof(HasRoot))]
    private void Refresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ActivateFile(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            FileActivated?.Invoke(this, path);
        }
    }
}
