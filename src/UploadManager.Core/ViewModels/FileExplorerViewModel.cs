using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UploadManager.Core.Services;

namespace UploadManager.Core.ViewModels;

/// <summary>
/// The file tree on the left side of the main window.
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

    /// <summary>Raised when a file is double-clicked or confirmed with Enter.</summary>
    public event EventHandler<FileSystemNodeViewModel>? FileActivated;

    public ObservableCollection<FileSystemNodeViewModel> RootNodes { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoot))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial string? RootPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedFile))]
    public partial FileSystemNodeViewModel? SelectedNode { get; set; }

    [ObservableProperty]
    public partial bool ShowOnlySupportedFiles { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    public bool HasRoot => RootPath is not null;

    public FileSystemNodeViewModel? SelectedFile => SelectedNode is { IsFile: true } ? SelectedNode : null;

    /// <summary>
    /// Shows <paramref name="path"/> as root of the tree. Expanded folders and the selection are kept
    /// when the same root is loaded again (refresh).
    /// </summary>
    public async Task<bool> LoadAsync(string path)
    {
        if (!Directory.Exists(path))
        {
            _notificationService.ShowWarning("Ordner nicht gefunden", $"„{path}“ existiert nicht oder ist nicht erreichbar.");
            return false;
        }

        var root = FileSystemNodeViewModel.CreateRoot(path, ShowOnlySupportedFiles ? _isSupportedFile : null);
        var isRefresh = string.Equals(root.FullPath, RootPath, StringComparison.OrdinalIgnoreCase);
        var expandedPaths = isRefresh ? GetExpandedPaths(RootNodes).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        var selectedPath = isRefresh ? SelectedNode?.FullPath : null;

        IsLoading = true;
        try
        {
            SelectedNode = null;
            RootNodes.Clear();
            RootPath = root.FullPath;
            RootNodes.Add(root);

            await root.LoadChildrenAsync();
            root.IsExpanded = true;
            await RestoreExpansionAsync(root, expandedPaths);

            if (selectedPath is not null && FindLoadedNode(RootNodes, selectedPath) is { } selected)
            {
                selected.IsSelected = true;
                SelectedNode = selected;
            }
        }
        finally
        {
            IsLoading = false;
        }

        return true;
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folder = _fileDialogService.PickFolder(RootPath);
        if (folder is not null)
        {
            await LoadAsync(folder);
        }
    }

    [RelayCommand(CanExecute = nameof(HasRoot))]
    private async Task RefreshAsync()
    {
        if (RootPath is not null)
        {
            await LoadAsync(RootPath);
        }
    }

    [RelayCommand]
    private void ActivateNode(FileSystemNodeViewModel? node)
    {
        node ??= SelectedNode;
        if (node is { IsFile: true })
        {
            FileActivated?.Invoke(this, node);
        }
    }

    partial void OnShowOnlySupportedFilesChanged(bool value)
    {
        if (RootPath is not null)
        {
            _ = LoadAsync(RootPath);
        }
    }

    private static IEnumerable<string> GetExpandedPaths(IEnumerable<FileSystemNodeViewModel> nodes) =>
        nodes.Where(n => n is { IsDirectory: true, IsExpanded: true })
            .SelectMany(n => GetExpandedPaths(n.Children).Prepend(n.FullPath));

    private static async Task RestoreExpansionAsync(FileSystemNodeViewModel node, HashSet<string> expandedPaths)
    {
        if (expandedPaths.Count == 0)
        {
            return;
        }

        foreach (var child in node.Children.Where(c => c.IsDirectory && expandedPaths.Contains(c.FullPath)).ToList())
        {
            await child.LoadChildrenAsync();
            child.IsExpanded = true;
            await RestoreExpansionAsync(child, expandedPaths);
        }
    }

    private static FileSystemNodeViewModel? FindLoadedNode(IEnumerable<FileSystemNodeViewModel> nodes, string fullPath)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            if (node.IsExpanded && FindLoadedNode(node.Children, fullPath) is { } match)
            {
                return match;
            }
        }

        return null;
    }
}
