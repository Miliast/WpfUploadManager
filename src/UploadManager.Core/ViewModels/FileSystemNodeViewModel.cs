using System.Collections.ObjectModel;
using System.Globalization;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using UploadManager.Core.Configuration;
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
        Depth = parent is null ? 0 : parent.Depth + 1;
        Extension = IsDirectory ? string.Empty : UploadSlotDefinition.NormalizeExtension(Path.GetExtension(entry.Name));
        ChildCount = entry.ChildCount;
        _fileFilter = fileFilter;

        // Placeholder so the tree shows an expander before the children are loaded.
        // Folders that are known to be empty get no expander at all.
        if (IsDirectory && ChildCount != 0)
        {
            Children.Add(new FileSystemNodeViewModel("Wird geladen …", this));
        }
    }

    private FileSystemNodeViewModel(string placeholderText, FileSystemNodeViewModel parent)
    {
        Name = placeholderText;
        FullPath = string.Empty;
        Extension = string.Empty;
        Parent = parent;
        Depth = parent.Depth + 1;
        IsPlaceholder = true;
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public bool IsPlaceholder { get; }

    public bool IsFile => !IsDirectory && !IsPlaceholder;

    /// <summary>Nesting level; top-level entries have depth 0.</summary>
    public int Depth { get; }

    /// <summary>Lower-case extension including the dot (e.g. <c>.pdf</c>); empty for folders.</summary>
    public string Extension { get; }

    public long SizeBytes { get; }

    public string SizeText => IsFile ? FileSizeFormatter.Format(SizeBytes) : string.Empty;

    /// <summary>Right-aligned detail in the tree: item count for folders, size for files.</summary>
    public string DetailText => IsDirectory
        ? ChildCount?.ToString(CultureInfo.CurrentCulture) ?? string.Empty
        : SizeText;

    public FileSystemNodeViewModel? Parent { get; }

    public ObservableCollection<FileSystemNodeViewModel> Children { get; } = [];

    /// <summary>Number of direct entries of a folder (<see langword="null"/> if unknown).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    public partial int? ChildCount { get; private set; }

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

            ChildCount = Children.Count;
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
