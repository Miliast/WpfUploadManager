using System.Collections.ObjectModel;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using UploadManager.Core.Formatting;
using UploadManager.Core.IO;

namespace UploadManager.Core.ViewModels;

/// <summary>
/// A folder or file in the explorer tree. Folders load their children lazily on first expand.
/// </summary>
public sealed partial class FileSystemNodeViewModel : ObservableObject
{
    private readonly Func<string, bool>? _fileFilter;
    private bool _childrenLoaded;

    public FileSystemNodeViewModel(FileSystemEntry entry, FileSystemNodeViewModel? parent, Func<string, bool>? fileFilter)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Name = entry.Name;
        FullPath = entry.FullPath;
        IsDirectory = entry.IsDirectory;
        SizeBytes = entry.SizeBytes;
        Parent = parent;
        _fileFilter = fileFilter;

        if (IsDirectory)
        {
            // Placeholder so the tree shows an expander before the children are loaded.
            Children.Add(new FileSystemNodeViewModel("Wird geladen …"));
        }
    }

    private FileSystemNodeViewModel(string placeholderText)
    {
        Name = placeholderText;
        FullPath = string.Empty;
        IsPlaceholder = true;
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public bool IsPlaceholder { get; }

    public bool IsFile => !IsDirectory && !IsPlaceholder;

    public long SizeBytes { get; }

    public string SizeText => IsFile ? FileSizeFormatter.Format(SizeBytes) : string.Empty;

    public FileSystemNodeViewModel? Parent { get; }

    public ObservableCollection<FileSystemNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    public bool HasError => ErrorMessage is not null;

    public static FileSystemNodeViewModel CreateRoot(string path, Func<string, bool>? fileFilter)
    {
        var info = new DirectoryInfo(path);
        var name = string.IsNullOrEmpty(info.Name) ? info.FullName : info.Name;
        return new FileSystemNodeViewModel(new FileSystemEntry(info.FullName, name, IsDirectory: true, 0, info.LastWriteTime), parent: null, fileFilter);
    }

    /// <summary>
    /// Loads the direct children once. Enumeration runs on a background thread;
    /// the collection is updated on the calling (UI) context.
    /// </summary>
    public async Task LoadChildrenAsync()
    {
        if (!IsDirectory || _childrenLoaded || IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var entries = await Task.Run(() => FileSystemReader.GetChildren(FullPath, _fileFilter));

            Children.Clear();
            foreach (var entry in entries)
            {
                Children.Add(new FileSystemNodeViewModel(entry, this, _fileFilter));
            }

            _childrenLoaded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Children.Clear();
            ErrorMessage = ex is UnauthorizedAccessException ? "Zugriff verweigert" : ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            _ = LoadChildrenAsync();
        }
    }
}
