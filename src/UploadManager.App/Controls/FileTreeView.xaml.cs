// FileTreeView – modern file tree for WPF (part 2 of 2: logic).
// Copy FileTreeView.xaml and FileTreeView.xaml.cs into any WPF project (.NET 8 or newer). No dependencies.
// License: MIT-0 – use, change and redistribute freely, no attribution required.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ModernFileTree;

/// <summary>
/// File tree with lazy loading, item counts, colored folders, file type colors, indent guides,
/// smooth hover/selection and drag &amp; drop of files to other controls or applications.
/// </summary>
/// <remarks>
/// Main members:
/// <list type="bullet">
///   <item><see cref="RootPath"/>: folder whose contents are shown.</item>
///   <item><see cref="ExtensionFilter"/> (e.g. <c>.pdf;.docx</c>) and/or <see cref="FileFilter"/>: which files are shown.</item>
///   <item><see cref="SelectedPath"/> (two-way bindable) and <see cref="SelectedItem"/>.</item>
///   <item><see cref="FileActivated"/> / <see cref="FileActivatedCommand"/>: double-click or Enter on a file.</item>
///   <item><see cref="Refresh"/>: reloads and keeps expanded folders and the selection.</item>
/// </list>
/// </remarks>
public partial class FileTreeView : UserControl
{
    public static readonly DependencyProperty RootPathProperty = DependencyProperty.Register(
        nameof(RootPath), typeof(string), typeof(FileTreeView),
        new PropertyMetadata(null, (d, _) => ((FileTreeView)d).ScheduleReload(keepState: false)));

    public static readonly DependencyProperty ExtensionFilterProperty = DependencyProperty.Register(
        nameof(ExtensionFilter), typeof(string), typeof(FileTreeView),
        new PropertyMetadata(null, (d, _) => ((FileTreeView)d).ScheduleReload(keepState: true)));

    public static readonly DependencyProperty FileFilterProperty = DependencyProperty.Register(
        nameof(FileFilter), typeof(Func<string, bool>), typeof(FileTreeView),
        new PropertyMetadata(null, (d, _) => ((FileTreeView)d).ScheduleReload(keepState: true)));

    public static readonly DependencyProperty SelectedPathProperty = DependencyProperty.Register(
        nameof(SelectedPath), typeof(string), typeof(FileTreeView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((FileTreeView)d).OnSelectedPathChanged(e.NewValue as string)));

    public static readonly DependencyProperty FileActivatedCommandProperty = DependencyProperty.Register(
        nameof(FileActivatedCommand), typeof(ICommand), typeof(FileTreeView), new PropertyMetadata(null));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(FileTreeView),
        new PropertyMetadata("No items", (d, _) => ((FileTreeView)d).UpdateMessage()));

    public static readonly DependencyProperty LoadingTextProperty = DependencyProperty.Register(
        nameof(LoadingText), typeof(string), typeof(FileTreeView), new PropertyMetadata("Loading …"));

    private static readonly DependencyPropertyKey SelectedItemPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectedItem), typeof(FileTreeNode), typeof(FileTreeView), new PropertyMetadata(null));

    private static readonly DependencyPropertyKey IsLoadingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsLoading), typeof(bool), typeof(FileTreeView), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey MessagePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Message), typeof(string), typeof(FileTreeView), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedItemProperty = SelectedItemPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsLoadingProperty = IsLoadingPropertyKey.DependencyProperty;

    public static readonly DependencyProperty MessageProperty = MessagePropertyKey.DependencyProperty;

    private int _loadVersion;
    private bool _reloadScheduled;
    private bool _reloadKeepsState = true;
    private bool _syncingSelection;
    private bool _loaded;
    private string? _loadError;
    private Point _dragStart;
    private FileTreeNode? _dragCandidate;

    public FileTreeView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when a file is double-clicked or confirmed with Enter.</summary>
    public event EventHandler<FileTreeEventArgs>? FileActivated;

    /// <summary>Top-level entries (contents of <see cref="RootPath"/>).</summary>
    public ObservableCollection<FileTreeNode> Items { get; } = [];

    /// <summary>Folder whose contents are shown.</summary>
    public string? RootPath
    {
        get => (string?)GetValue(RootPathProperty);
        set => SetValue(RootPathProperty, value);
    }

    /// <summary>Allowed file extensions, separated by <c>;</c> or <c>,</c> (e.g. <c>.pdf;.docx</c>). Empty shows all files.</summary>
    public string? ExtensionFilter
    {
        get => (string?)GetValue(ExtensionFilterProperty);
        set => SetValue(ExtensionFilterProperty, value);
    }

    /// <summary>Optional predicate (full path → visible). Runs on a background thread.</summary>
    public Func<string, bool>? FileFilter
    {
        get => (Func<string, bool>?)GetValue(FileFilterProperty);
        set => SetValue(FileFilterProperty, value);
    }

    /// <summary>Full path of the selected file or folder. Setting it selects an already loaded entry.</summary>
    public string? SelectedPath
    {
        get => (string?)GetValue(SelectedPathProperty);
        set => SetValue(SelectedPathProperty, value);
    }

    /// <summary>Executed with the file path when a file is activated (double-click or Enter).</summary>
    public ICommand? FileActivatedCommand
    {
        get => (ICommand?)GetValue(FileActivatedCommandProperty);
        set => SetValue(FileActivatedCommandProperty, value);
    }

    /// <summary>Text shown when the folder has no (visible) entries.</summary>
    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    /// <summary>Text of the placeholder row while a folder is loading.</summary>
    public string LoadingText
    {
        get => (string)GetValue(LoadingTextProperty);
        set => SetValue(LoadingTextProperty, value);
    }

    public FileTreeNode? SelectedItem => (FileTreeNode?)GetValue(SelectedItemProperty);

    public bool IsLoading => (bool)GetValue(IsLoadingProperty);

    /// <summary>Message shown in the middle (empty folder or error), otherwise <see langword="null"/>.</summary>
    public string? Message => (string?)GetValue(MessageProperty);

    /// <summary>Reloads the tree; expanded folders and the selection are kept.</summary>
    public void Refresh() => ScheduleReload(keepState: true);

    /// <summary>Reloads the tree and waits until the top level is loaded.</summary>
    public Task RefreshAsync() => ReloadAsync(keepState: true);

    private void ScheduleReload(bool keepState)
    {
        // Several properties often change at once (e.g. via bindings): reload only once.
        _reloadKeepsState &= keepState;
        if (_reloadScheduled)
        {
            return;
        }

        _reloadScheduled = true;
        Dispatcher.InvokeAsync(RunScheduledReloadAsync, DispatcherPriority.Loaded);
    }

    private Task RunScheduledReloadAsync()
    {
        var keepState = _reloadKeepsState;
        _reloadScheduled = false;
        _reloadKeepsState = true;
        return ReloadAsync(keepState);
    }

    private async Task ReloadAsync(bool keepState)
    {
        var version = ++_loadVersion;
        var rootPath = RootPath;
        var expandedPaths = keepState ? CollectExpandedPaths(Items) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedPath = keepState ? SelectedItem?.FullPath : null;

        _loadError = null;
        if (!keepState)
        {
            _loaded = false;
        }

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            Items.Clear();
            UpdateMessage();
            return;
        }

        SetValue(IsLoadingPropertyKey, true);
        try
        {
            var filter = CreateFilter();
            IReadOnlyList<FileTreeNode.Entry> entries;
            try
            {
                entries = await Task.Run(() => FileTreeNode.ReadChildren(rootPath, filter));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
            {
                if (version == _loadVersion)
                {
                    Items.Clear();
                    _loadError = ex.Message;
                    UpdateMessage();
                }

                return;
            }

            if (version != _loadVersion)
            {
                return; // A newer load has started in the meantime.
            }

            Items.Clear();
            foreach (var entry in entries)
            {
                Items.Add(new FileTreeNode(entry, parent: null, filter, LoadingText));
            }

            await RestoreExpansionAsync(Items, expandedPaths);

            if (selectedPath is not null && FindLoadedNode(Items, selectedPath) is { } selected)
            {
                selected.IsSelected = true;
            }

            _loaded = true;
            UpdateMessage();
        }
        finally
        {
            if (version == _loadVersion)
            {
                SetValue(IsLoadingPropertyKey, false);
            }
        }
    }

    private Func<string, bool>? CreateFilter()
    {
        var custom = FileFilter;
        var extensions = (ExtensionFilter ?? string.Empty)
            .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => "." + e.TrimStart('*', '.'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (custom is null && extensions.Count == 0)
        {
            return null;
        }

        return path => (extensions.Count == 0 || extensions.Contains(Path.GetExtension(path)))
            && (custom?.Invoke(path) ?? true);
    }

    private void UpdateMessage()
    {
        var message = _loadError ?? (_loaded && Items.Count == 0 ? EmptyText : null);
        SetValue(MessagePropertyKey, message);
    }

    private void OnSelectedPathChanged(string? path)
    {
        if (_syncingSelection || string.IsNullOrEmpty(path))
        {
            return;
        }

        if (FindLoadedNode(Items, path) is { } node)
        {
            node.IsSelected = true;
        }
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        var node = e.NewValue is FileTreeNode { IsPlaceholder: false } selected ? selected : null;
        SetValue(SelectedItemPropertyKey, node);

        _syncingSelection = true;
        try
        {
            SetCurrentValue(SelectedPathProperty, node?.FullPath);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnTreeMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindNode(e.OriginalSource) is { IsFile: true } node)
        {
            Activate(node);
            e.Handled = true;
        }
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedItem is { IsFile: true } node)
        {
            Activate(node);
            e.Handled = true;
        }
    }

    private void Activate(FileTreeNode node)
    {
        FileActivated?.Invoke(this, new FileTreeEventArgs(node));

        if (FileActivatedCommand is { } command && command.CanExecute(node.FullPath))
        {
            command.Execute(node.FullPath);
        }
    }

    private void OnTreePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Tree);
        _dragCandidate = FindNode(e.OriginalSource) is { IsFile: true } node ? node : null;
    }

    private void OnTreePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(Tree);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var node = _dragCandidate;
        _dragCandidate = null;

        // Standard file drop format: works for drop targets in this app and e.g. Windows Explorer.
        DragDrop.DoDragDrop(Tree, new DataObject(DataFormats.FileDrop, new[] { node.FullPath }), DragDropEffects.Copy);
    }

    private static FileTreeNode? FindNode(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null and not TreeViewItem)
        {
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return (current as TreeViewItem)?.DataContext as FileTreeNode;
    }

    private static HashSet<string> CollectExpandedPaths(IEnumerable<FileTreeNode> nodes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<FileTreeNode>(nodes);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is { IsDirectory: true, IsExpanded: true })
            {
                result.Add(node.FullPath);
                foreach (var child in node.Children)
                {
                    stack.Push(child);
                }
            }
        }

        return result;
    }

    private static async Task RestoreExpansionAsync(IEnumerable<FileTreeNode> nodes, HashSet<string> expandedPaths)
    {
        if (expandedPaths.Count == 0)
        {
            return;
        }

        foreach (var node in nodes.Where(n => n.IsDirectory && expandedPaths.Contains(n.FullPath)).ToList())
        {
            await node.LoadChildrenAsync();
            node.IsExpanded = true;
            await RestoreExpansionAsync(node.Children, expandedPaths);
        }
    }

    private static FileTreeNode? FindLoadedNode(IEnumerable<FileTreeNode> nodes, string fullPath)
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

public sealed class FileTreeEventArgs(FileTreeNode node) : EventArgs
{
    public FileTreeNode Node { get; } = node;

    public string Path => Node.FullPath;
}

/// <summary>
/// A folder or file in <see cref="FileTreeView"/>. Folders load their children on first expand.
/// </summary>
public sealed class FileTreeNode : INotifyPropertyChanged
{
    // Layout: content starts at 6px and moves 16px per level; the guide line sits under the parent's chevron.
    private const double RowOffset = 6;
    private const double IndentPerLevel = 16;
    private const double GuideOffset = 16;

    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    private static readonly ConcurrentDictionary<string, Brush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] FolderPalette =
    [
        "#8B8B93", "#8A7CD8", "#B7ABF2", "#F2B33D", "#8794F7",
        "#3A73DB", "#E2573A", "#3BAF7E", "#E06AA3", "#3BB0C4",
    ];

    private readonly Func<string, bool>? _filter;
    private readonly string _loadingText;
    private bool _childrenLoaded;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isLoading;
    private int? _childCount;
    private string? _errorMessage;

    internal FileTreeNode(Entry entry, FileTreeNode? parent, Func<string, bool>? filter, string loadingText)
    {
        Name = entry.Name;
        FullPath = entry.FullPath;
        IsDirectory = entry.IsDirectory;
        SizeBytes = entry.SizeBytes;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
        Extension = IsDirectory ? string.Empty : System.IO.Path.GetExtension(entry.Name).ToLowerInvariant();
        _childCount = entry.ChildCount;
        _filter = filter;
        _loadingText = loadingText;

        // Placeholder so the expander is shown before the children are loaded (not for known-empty folders).
        if (IsDirectory && _childCount != 0)
        {
            Children.Add(new FileTreeNode(this, loadingText));
        }
    }

    private FileTreeNode(FileTreeNode parent, string loadingText)
    {
        Name = loadingText;
        FullPath = string.Empty;
        Extension = string.Empty;
        Parent = parent;
        Depth = parent.Depth + 1;
        IsPlaceholder = true;
        _loadingText = loadingText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public FileTreeNode? Parent { get; }

    public ObservableCollection<FileTreeNode> Children { get; } = [];

    /// <summary>Number of direct (visible) entries of a folder, <see langword="null"/> if unknown.</summary>
    public int? ChildCount
    {
        get => _childCount;
        private set
        {
            if (Set(ref _childCount, value))
            {
                OnPropertyChanged(nameof(DetailText));
            }
        }
    }

    /// <summary>Right-aligned detail: item count for folders, size for files.</summary>
    public string DetailText => IsDirectory
        ? ChildCount?.ToString(CultureInfo.CurrentCulture) ?? string.Empty
        : IsFile ? FormatSize(SizeBytes) : string.Empty;

    public string? ToolTip => IsPlaceholder ? null : FullPath;

    public Brush IconBrush => IsDirectory ? GetFolderBrush(Name) : GetFileBrush(Extension);

    public Thickness Indent => new(RowOffset + (Depth * IndentPerLevel), 0, 0, 0);

    public Thickness GuideIndent => new(GuideOffset + (Depth * IndentPerLevel), 0, 0, 0);

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value) && value)
            {
                _ = LoadChildrenAsync();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (Set(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => ErrorMessage is not null;

    /// <summary>Loads the direct children once (enumeration runs on a background thread).</summary>
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
            var entries = await Task.Run(() => ReadChildren(FullPath, _filter));

            Children.Clear();
            foreach (var entry in entries)
            {
                Children.Add(new FileTreeNode(entry, this, _filter, _loadingText));
            }

            ChildCount = Children.Count;
            _childrenLoaded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Children.Clear();
            ErrorMessage = ex is UnauthorizedAccessException ? "Access denied" : ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Folders first, then files; each sorted by name. Hidden and system entries are skipped.</summary>
    internal static IReadOnlyList<Entry> ReadChildren(string directory, Func<string, bool>? filter)
    {
        var info = new DirectoryInfo(directory);

        var directories = info.EnumerateDirectories("*", EnumerationOptions)
            .Select(d => new Entry(d.FullName, d.Name, IsDirectory: true, 0, CountChildren(d, filter)))
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase);

        var files = info.EnumerateFiles("*", EnumerationOptions)
            .Where(f => filter?.Invoke(f.FullName) ?? true)
            .Select(f => new Entry(f.FullName, f.Name, IsDirectory: false, f.Length, null))
            .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase);

        return [.. directories, .. files];
    }

    private static int? CountChildren(DirectoryInfo directory, Func<string, bool>? filter)
    {
        try
        {
            return directory.EnumerateDirectories("*", EnumerationOptions).Count()
                + directory.EnumerateFiles("*", EnumerationOptions).Count(f => filter?.Invoke(f.FullName) ?? true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }

    private static Brush GetFolderBrush(string name)
    {
        // FNV-1a hash: stable across sessions, so a folder always keeps its color.
        var hash = 2166136261u;
        foreach (var c in name.ToUpperInvariant())
        {
            hash = (hash ^ c) * 16777619u;
        }

        return GetBrush(FolderPalette[hash % (uint)FolderPalette.Length]);
    }

    private static Brush GetFileBrush(string extension) => extension switch
    {
        ".pdf" => GetBrush("#E5534B"),
        ".doc" or ".docx" or ".odt" or ".rtf" => GetBrush("#4A86E8"),
        ".xlsx" or ".xls" or ".csv" or ".tsv" or ".ods" => GetBrush("#33A36B"),
        ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".ini" or ".config"
            or ".cs" or ".xaml" or ".js" or ".ts" or ".py" or ".html" or ".css" or ".sql" => GetBrush("#E8A93A"),
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp" or ".tif" or ".tiff" => GetBrush("#A77BF3"),
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => GetBrush("#B08B5B"),
        ".ppt" or ".pptx" or ".odp" => GetBrush("#E07A3F"),
        ".mp4" or ".mov" or ".avi" or ".mkv" or ".mp3" or ".wav" or ".flac" or ".m4a" => GetBrush("#3BB0C4"),
        _ => GetBrush("#8B8B93"),
    };

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit == 0 ? "0" : "0.#", CultureInfo.CurrentCulture) + " " + units[unit];
    }

    private static Brush GetBrush(string hex) => BrushCache.GetOrAdd(hex, static key =>
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(key));
        brush.Freeze();
        return brush;
    });

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    internal sealed record Entry(string FullPath, string Name, bool IsDirectory, long SizeBytes, int? ChildCount);
}
