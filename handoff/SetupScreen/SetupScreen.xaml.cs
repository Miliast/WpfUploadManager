// SetupScreen – logic of the "New run" setup screen (the look is in SetupScreen.xaml). Self-contained: these two files
// are the whole screen. Requirements: WPF, net8.0-windows or later (OpenFolderDialog), C# 12, no NuGet packages.
// Rename the namespace here and in SetupScreen.xaml (x:Class, xmlns:local) together if your project uses another one.
//
// Host it like any control:   <setup:SetupScreen RunRequested="OnRun" />
// and connect the two seams:
//   · RunRequested  – raised by "Run pipelines" or Ctrl+Enter once every field holds a file; carries the files.
//   · ImportData    – the call behind "Import" / "Refresh" (fetch the X data, return the number of records).
//                     Until it is set, the button stays disabled – the screen never pretends an import worked.
// Ctrl+Enter and Ctrl+O work while the keyboard focus is somewhere inside the screen.
//
// What is in this file, top to bottom:
//   SetupScreen ........ code-behind: dialogs, keys, typed paths, drag & drop (tree + Explorer)
//   SetupRunEventArgs .. what RunRequested hands over
//   SetupModel ......... state the XAML binds to: fields, project tree, readiness, auto-fill, X import
//   Slot ............... one field (row) of the fields card
//   small parts ........ FileItem, TreeRow, Observable, PathIcon, CompactPath, Look, VisibleWhen, Fmt

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Conduit.Setup;

public partial class SetupScreen : UserControl
{
    /// <summary>Drag format of our own tree rows (relative path) – real files additionally travel as FileDrop.</summary>
    private const string RelFormat = "Conduit.Setup.RelativePath";

    private Point? _pressedAt;

    public SetupScreen()
    {
        InitializeComponent();
        DataContext = Model;
    }

    public SetupModel Model { get; } = new();

    /// <summary>"Run pipelines" or Ctrl+Enter with every field filled.</summary>
    public event EventHandler<SetupRunEventArgs>? RunRequested;

    /// <summary>The import behind "Import" / "Refresh": fetch the data, return the record count. Null = button disabled.</summary>
    public Func<Task<int>>? ImportData
    {
        get => Model.ImportData;
        set => Model.ImportData = value;
    }

    // ── Top bar and keys ───────────────────────────────────────────────────

    private async void OnRunClick(object sender, RoutedEventArgs e) => await RunAsync();

    private async Task RunAsync()
    {
        await CommitTypedPathAsync(); // a path still being typed counts
        await Model.WhenPathsChecked();
        if (Model.IsReady) RunRequested?.Invoke(this, new SetupRunEventArgs(Model.IsPython, [.. Model.Slots.Select(s => s.Item!)]));
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunAsync();
        }
        else if (e.Key == Key.O)
        {
            e.Handled = true;
            PickProjectFolder();
        }
    }

    // ── Dialogs ────────────────────────────────────────────────────────────

    public void PickProjectFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Open project folder" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Model.OpenProject(dialog.FolderName);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) => PickProjectFolder();

    private void OnSample(object sender, RoutedEventArgs e) => Model.LoadSample();

    private void OnImport(object sender, RoutedEventArgs e) => _ = Model.ImportAsync();

    private void OnAutoFill(object sender, RoutedEventArgs e) => Model.AutoFill();

    private void OnBrowseField(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Slot slot) return;
        var owner = Window.GetWindow(this);
        string? path;
        if (slot.IsFolder)
        {
            var dialog = new OpenFolderDialog { Title = "Choose the test folder", InitialDirectory = Model.ProjectRoot ?? "" };
            path = dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
        }
        else
        {
            string filter = slot.Index switch
            {
                1 => "package.json|package.json",
                2 => "Source files|*.ts;*.tsx;*.js",
                3 => "Dockerfile|Dockerfile*",
                5 => "Requirements|requirements*.txt;*.txt",
                _ => "Pipeline config|*.yml;*.yaml",
            };
            var dialog = new OpenFileDialog { Title = $"Choose {slot.Title.ToLowerInvariant()}", Filter = filter, InitialDirectory = Model.ProjectRoot ?? "" };
            path = dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }
        if (path is not null) Model.Assign(slot.Index, Model.External(path));
    }

    // ── Path fields: typed or pasted paths ─────────────────────────────────

    /// <summary>Enter takes the path, Esc puts the old one back.</summary>
    private async void OnPathKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            await CommitAsync(box);
            box.CaretIndex = box.Text.Length;
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            Keyboard.ClearFocus();
        }
    }

    private async void OnPathLostFocus(object sender, KeyboardFocusChangedEventArgs e) => await CommitAsync((TextBox)sender);

    private Task CommitAsync(TextBox box) => box.DataContext is Slot slot ? Model.AssignPathAsync(slot, box.Text) : Task.CompletedTask;

    private Task CommitTypedPathAsync() =>
        Keyboard.FocusedElement is TextBox { Name: "PathBox" } box && IsAncestorOf(box) ? CommitAsync(box) : Task.CompletedTask;

    // ── Sidebar tree: click toggles a folder, dragging starts drag & drop ──

    private void OnTreeRowDown(object sender, MouseButtonEventArgs e) => _pressedAt = e.GetPosition((IInputElement)sender);

    private void OnTreeRowUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressedAt is null) return;
        _pressedAt = null;
        if (((FrameworkElement)sender).DataContext is TreeRow { IsDir: true } row) Model.ToggleFolder(row);
    }

    private void OnTreeRowMove(object sender, MouseEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (_pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed || element.DataContext is not TreeRow row) return;
        var delta = e.GetPosition(element) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _pressedAt = null;
        var data = new DataObject();
        data.SetData(RelFormat, row.Rel);
        if (Model.FindItem(row.Rel)?.FullPath is { } full) data.SetData(DataFormats.FileDrop, new[] { full }); // also droppable into Explorer
        DragDrop.DoDragDrop(element, data, DragDropEffects.Copy);
    }

    // ── Drop targets ───────────────────────────────────────────────────────

    /// <summary>A single field lights up ("Drop to replace ci.yml") when the dragged item belongs into it.</summary>
    private void OnFieldDragOver(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Slot slot) return;
        bool fits = Dropped(e.Data).Any(d => SetupModel.SlotFor(d.Name, d.IsDir) == slot.Index);
        slot.IsDragOver = fits;
        e.Effects = fits ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFieldDragLeave(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Slot slot) slot.IsDragOver = false;
    }

    private void OnFieldDrop(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Slot slot) return;
        slot.IsDragOver = false;
        var item = Dropped(e.Data).FirstOrDefault(d => SetupModel.SlotFor(d.Name, d.IsDir) == slot.Index);
        if (item is not null) Model.Assign(slot.Index, item);
        e.Handled = true;
    }

    /// <summary>Anywhere else on the sheet: every dropped file goes into its matching field.</summary>
    private void OnPageDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Dropped(e.Data).Any(d => SetupModel.SlotFor(d.Name, d.IsDir) >= 0) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPageDrop(object sender, DragEventArgs e)
    {
        foreach (var item in Dropped(e.Data)) Model.Assign(SetupModel.SlotFor(item.Name, item.IsDir), item);
    }

    /// <summary>A folder dropped on the sidebar opens as the project.</summary>
    private void OnSidebarDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Dropped(e.Data).Any(d => d.IsDir && d.FullPath is not null && d.Rel is null) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSidebarDrop(object sender, DragEventArgs e)
    {
        var folder = Dropped(e.Data).FirstOrDefault(d => d.IsDir && d.FullPath is not null && d.Rel is null);
        if (folder?.FullPath is { } path) Model.OpenProject(path);
    }

    /// <summary>Everything in a drag: our own tree items and/or real files from Explorer.</summary>
    private List<FileItem> Dropped(IDataObject data)
    {
        if (data.GetData(RelFormat) is string rel) return Model.FindItem(rel) is { } item ? [item] : [];
        if (data.GetData(DataFormats.FileDrop) is string[] paths) return [.. paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Model.External)];
        return [];
    }
}

/// <summary>What "Run pipelines" hands over: the mode and one file per field, in field order.</summary>
public sealed class SetupRunEventArgs(bool isPython, IReadOnlyList<FileItem> files) : EventArgs
{
    public bool IsPython { get; } = isPython;

    /// <summary>Test folder, package.json, source, Dockerfile, pipeline config (+ requirements.txt in Python mode).
    /// <see cref="FileItem.FullPath"/> is null only for the built-in sample project, which has no files on disk.</summary>
    public IReadOnlyList<FileItem> Files { get; } = files;
}

// ════════════════════════════════════════════════════════════════════════════
// State
// ════════════════════════════════════════════════════════════════════════════

/// <summary>Everything the screen shows. The view only binds to it and forwards user input.</summary>
public sealed class SetupModel : Observable
{
    private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", "bin", "obj", ".vs", ".idea", "dist" };

    // Where the X data comes from and what it is for: next to "Import" while empty, inside the subtitle once loaded.
    private const string XRoute = "Gen X · from BAX";

    private readonly Slot[] _allSlots;
    private readonly List<Entry> _tree = [];
    private readonly Dictionary<string, List<Entry>> _kids = [];
    private readonly HashSet<string> _expanded = [];
    private readonly DispatcherTimer _ageTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly List<Task> _checks = [];
    private string? _projectRoot;
    private string _treeFilter = "";
    private bool _python;
    private int _matchCount;

    private Func<Task<int>>? _importData;
    private int _xState, _records; // X: 0 none, 1 fetching, 2 loaded
    private string? _xError;
    private DateTime _xLoadedAt;

    public SetupModel()
    {
        _allSlots =
        [
            new(0, "Test folder", "Specs for all nodes", "", "#1F4BFF", isFolder: true),
            new(1, "Project manifest", "package.json", "JSON", "#E3A008", isFolder: false),
            new(2, "Source file", ".ts, .tsx or .js", "TS", "#3178C6", isFolder: false),
            new(3, "Container", "Dockerfile", "DOCKER", "#0B8FD1", isFolder: false),
            new(4, "Pipeline config", ".yml or .yaml", "YML", "#7C5CDB", isFolder: false),
            new(5, "Requirements", "requirements.txt", "TXT", "#6B7280", isFolder: false), // Python only
        ];
        Slots = _allSlots[..5];
        _ageTimer.Tick += (_, _) => Raise(nameof(XAgeText));
    }

    private readonly record struct Entry(string Rel, bool IsDir, long Size);

    // ── Mode (Node | Python) and fields ────────────────────────────────────

    /// <summary>The fields of the current mode: five for Node, six (plus "Requirements") for Python.</summary>
    public IReadOnlyList<Slot> Slots { get; private set; }

    public bool IsPython
    {
        get => _python;
        set
        {
            if (_python == value) return;
            _python = value;
            if (!value) _allSlots[5].Fill(null, "");
            Slots = value ? _allSlots : _allSlots[..5];
            SetupChanged();
        }
    }

    public bool IsNode
    {
        get => !_python;
        set => IsPython = !value;
    }

    public int FilledCount => Slots.Count(s => s.IsFilled);
    public bool IsReady => FilledCount == Slots.Count;
    public string MissingText => Fmt.Plural(Slots.Count - FilledCount, "file") + " missing";
    public string ReadyText => $" · {Fmt.Plural(Slots.Count, "file")}";

    public string SetupSubtitle => IsReady
        ? "Everything is in place. Swap any file by dropping a new one on it."
        : $"Add the {(Slots.Count == 6 ? "six" : "five")} files below, or drop them anywhere on this page.";

    // ── Project and tree (sidebar) ─────────────────────────────────────────

    public string? ProjectRoot => _projectRoot;
    public string? ProjectName { get; private set; }
    public bool HasProject => ProjectName is not null;
    public string ProjectFilesText => Fmt.Plural(_tree.Count(e => !e.IsDir), "file");
    public IReadOnlyList<TreeRow> TreeRows { get; private set; } = [];
    public bool NoTreeMatches => HasProject && TreeRows.Count == 0;

    public string TreeFilter
    {
        get => _treeFilter;
        set { if (Set(ref _treeFilter, value ?? "")) RebuildTree(); }
    }

    public bool ShowAutoFill => _matchCount > 0;
    public string AutoFillText => $"Auto-fill {Fmt.Plural(_matchCount, "field")}";

    /// <summary>Reads the folder into the sidebar tree and fills every field it can match.</summary>
    public void OpenProject(string root)
    {
        _projectRoot = root;
        ProjectName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _tree.Clear();
        Scan(root, "");
        IndexTree();
        foreach (var slot in _allSlots) slot.Fill(null, "");
        foreach (var (slot, item) in Matches()) Assign(slot, item, notify: false);
        _expanded.Clear();
        if (Slots[0].Item?.Rel is { } tests) _expanded.Add(tests);
        SetupChanged();
    }

    /// <summary>The demo project from the design (acme-storefront) – no files on disk, only the tree.</summary>
    public void LoadSample()
    {
        _projectRoot = null;
        ProjectName = "acme-storefront";
        _tree.Clear();
        void Add(string rel, long size = -1) => _tree.Add(new Entry(rel, size < 0, Math.Max(size, 0))); // no size = folder
        Add("tests");
        foreach (var (node, specs) in new[] { ("node-18", "payments refunds"), ("node-20", "format locales"), ("node-22", "queue retry") })
        {
            Add($"tests/{node}");
            foreach (var s in specs.Split(' ')) Add($"tests/{node}/{s}.spec.ts", 1800);
        }
        Add("src");
        Add("src/index.ts", 106);
        foreach (var f in "app cart checkout payments refunds money locale format queue worker jobs mailer webhook config logger".Split(' '))
            Add($"src/{f}.ts", 900);
        Add(".github");
        Add(".github/workflows");
        Add(".github/workflows/ci.yml", 812);
        Add(".github/workflows/release.yml", 640);
        Add("Dockerfile", 240);
        Add("package.json", 264);
        Add("requirements.txt", 96);
        Add("README.md", 2048);
        IndexTree();

        foreach (var slot in _allSlots) slot.Fill(null, "");
        foreach (var rel in new[] { "tests", "package.json", "src/index.ts", "Dockerfile" })
        {
            var item = FindItem(rel)!;
            Assign(SlotFor(item.Name, item.IsDir), item, notify: false);
        }
        _expanded.Clear();
        _expanded.UnionWith(["tests", "tests/node-18", "tests/node-20"]);
        SetupChanged();
    }

    public void ToggleFolder(TreeRow row)
    {
        if (!row.IsDir) return;
        if (!_expanded.Remove(row.Rel)) _expanded.Add(row.Rel);
        RebuildTree();
    }

    public void AutoFill()
    {
        foreach (var (slot, item) in Matches()) Assign(slot, item, notify: false);
        SetupChanged();
    }

    // ── Filling a field ────────────────────────────────────────────────────

    /// <summary>Puts a file/folder into a field; the grey detail before "Browse" is the spec count or the size.</summary>
    public void Assign(int slot, FileItem item, bool notify = true, int? specs = null)
    {
        if (slot < 0 || slot >= Slots.Count) return;
        string detail = slot == 0 ? Fmt.Plural(specs ?? SpecsIn(item), "spec") : Fmt.Size(item.Size);
        Slots[slot].Fill(item, detail);
        if (notify) SetupChanged();
    }

    /// <summary>
    /// A path typed or pasted into a field (on Enter or when the field loses focus). Relative paths start at the project
    /// folder; absolute and network paths (\\server\share\…) are taken as they are. The disk is asked off the UI thread,
    /// because an unreachable share can take seconds. Only a path that exists and fits the field fills it – anything else
    /// leaves the field empty with the reason in red, so "Ready" never counts a bad path.
    /// </summary>
    public Task AssignPathAsync(Slot field, string text)
    {
        var task = AssignPathCoreAsync(field, text);
        _checks.RemoveAll(t => t.IsCompleted);
        if (!task.IsCompleted) _checks.Add(task);
        return task;
    }

    /// <summary>Typed paths still being checked on disk. Run waits for them, so it never starts with an unchecked path.</summary>
    public Task WhenPathsChecked()
    {
        _checks.RemoveAll(t => t.IsCompleted);
        return Task.WhenAll([.. _checks]);
    }

    private async Task AssignPathCoreAsync(Slot field, string text)
    {
        text = text.Trim().Trim('"').Trim();
        if (text == field.PathText) return;
        int version = ++field.Version;

        if (text.Length == 0)
        {
            field.Fill(null, "");
            SetupChanged();
            return;
        }

        string full;
        if (!Path.IsPathRooted(text))
        {
            string rel = text.Replace('\\', '/').TrimEnd('/');
            if (rel.StartsWith("./", StringComparison.Ordinal)) rel = rel[2..];
            if (FindItem(rel) is { } known) // in the open (or sample) project's tree – no disk needed
            {
                if (known.IsDir != field.IsFolder) field.Reject(text, "Not found");
                else if (SlotFor(known.Name, known.IsDir) != field.Index) field.Reject(text, $"Expected {field.Hint}");
                else Assign(field.Index, known, notify: false);
                SetupChanged();
                return;
            }
            if (_projectRoot is null)
            {
                field.Reject(text, "Type the full path");
                SetupChanged();
                return;
            }
            full = Path.Combine(_projectRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        }
        else
        {
            full = text;
        }

        field.SetChecking(text);
        SetupChanged();
        string? root = _projectRoot;
        bool folder = field.IsFolder;
        var (item, specs) = await Task.Run(() => Probe(full, folder, root));
        if (version != field.Version) return; // typed again in the meantime

        if (item is null) field.Reject(text, "Not found");
        else if (SlotFor(item.Name, item.IsDir) != field.Index) field.Reject(text, $"Expected {field.Hint}");
        else Assign(field.Index, item, notify: false, specs);
        SetupChanged();
    }

    /// <summary>Disk check for a typed path (runs off the UI thread, touches no shared state).</summary>
    private static (FileItem? Item, int Specs) Probe(string path, bool folder, string? root)
    {
        try
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); // "tests\" → name "tests"
            if (folder ? !Directory.Exists(full) : !File.Exists(full)) return (null, 0);
            var item = MakeItem(full, root);
            return (item, folder ? CountSpecs(full) : 0);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return (null, 0); // characters a path can't contain, or a share that refuses us
        }
    }

    /// <summary>A real file or folder (Explorer drop, file dialog, typed path).</summary>
    public FileItem External(string path) => MakeItem(path, _projectRoot);

    private static FileItem MakeItem(string path, string? root)
    {
        bool dir = Directory.Exists(path);
        string? rel = root is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, path).Replace('\\', '/') : null;
        return new FileItem(Path.GetFileName(path), dir, dir ? 0 : new FileInfo(path).Length, path, rel);
    }

    /// <summary>An item of the open project by its relative path (drag &amp; drop from the sidebar, typed relative path).</summary>
    public FileItem? FindItem(string rel)
    {
        var e = _tree.FirstOrDefault(t => string.Equals(t.Rel, rel, StringComparison.OrdinalIgnoreCase));
        return e.Rel is null ? null : ToItem(e);
    }

    /// <summary>Which field a file belongs to: 0 test folder, 1 package.json, 2 source, 3 Dockerfile, 4 yml, 5 requirements.txt; -1 none.</summary>
    public static int SlotFor(string name, bool isDir)
    {
        if (isDir) return 0;
        name = FileName(name);
        if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)) return 5;
        if (name.StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase)) return 3;
        if (HasExtension(name, ".yml", ".yaml")) return 4;
        if (HasExtension(name, ".ts", ".tsx", ".js")) return 2;
        return -1;
    }

    /// <summary>Best file of the project tree for every field that is still empty.</summary>
    private List<(int Slot, FileItem Item)> Matches()
    {
        var found = new Entry?[Slots.Count];
        var tests = _tree.FirstOrDefault(t => t.IsDir && t.Rel.Count(c => c == '/') < 2
            && FileName(t.Rel).ToLowerInvariant() is "tests" or "test" or "__tests__" or "spec" or "e2e");
        if (tests.Rel is not null) found[0] = tests;
        foreach (var rel in new[] { "package.json", "src/index.ts", "src/index.tsx", "src/index.js", "index.ts", "index.js", "Dockerfile" })
        {
            var e = _tree.FirstOrDefault(t => !t.IsDir && t.Rel.Equals(rel, StringComparison.OrdinalIgnoreCase));
            if (e.Rel is not null) found[SlotFor(e.Rel, false)] ??= e;
        }
        var yml = _tree.FirstOrDefault(t => !t.IsDir && t.Rel.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase) && SlotFor(t.Rel, false) == 4);
        if (yml.Rel is not null) found[4] = yml;
        if (found.Length > 5)
        {
            var req = _tree.FirstOrDefault(t => !t.IsDir && FileName(t.Rel).Equals("requirements.txt", StringComparison.OrdinalIgnoreCase));
            if (req.Rel is not null) found[5] = req;
        }

        var result = new List<(int, FileItem)>();
        for (int i = 0; i < found.Length; i++)
            if (found[i] is { } e && !Slots[i].IsFilled) result.Add((i, ToItem(e)));
        return result;
    }

    /// <summary>After any change to project or fields: recount, rebuild the tree and refresh every binding.</summary>
    private void SetupChanged()
    {
        _matchCount = Matches().Count;
        RebuildTree();
        Raise(string.Empty);
    }

    private void RebuildTree()
    {
        var rows = new List<TreeRow>();
        string filter = _treeFilter.Trim();
        if (filter.Length > 0)
        {
            foreach (var e in _tree.Where(t => !t.IsDir && t.Rel.Contains(filter, StringComparison.OrdinalIgnoreCase)).Take(300))
                rows.Add(MakeRow(e, 0, e.Rel));
        }
        else
        {
            Level("", 0);
        }
        TreeRows = rows;
        Raise(nameof(TreeRows), nameof(NoTreeMatches));

        void Level(string parent, int depth)
        {
            if (!_kids.TryGetValue(parent, out var kids)) return;
            foreach (var k in kids)
            {
                // Compact folders: ".github" with only "workflows" inside shows as ".github/workflows".
                var e = k;
                while (e.IsDir && _kids.TryGetValue(e.Rel, out var inner) && inner is [{ IsDir: true } only]) e = only;
                string name = k.Rel == e.Rel ? FileName(e.Rel) : e.Rel[(parent.Length == 0 ? 0 : parent.Length + 1)..];
                rows.Add(MakeRow(e, depth, name));
                if (e.IsDir && _expanded.Contains(e.Rel)) Level(e.Rel, depth + 1);
            }
        }
    }

    private TreeRow MakeRow(Entry e, int depth, string name)
    {
        int slot = Slots.FirstOrDefault(s => s.Item?.Rel == e.Rel)?.Index ?? -1;
        var icon = e.IsDir ? (slot == 0 ? TreeIcon.TestFolder : TreeIcon.Folder)
            : IsSpec(e.Rel) ? TreeIcon.Spec
            : SlotFor(e.Rel, false) == 3 ? TreeIcon.Container
            : TreeIcon.File;
        bool isCount = slot < 0 && e.IsDir && _kids.ContainsKey(e.Rel);
        string note = slot >= 0 ? Slots[slot].Title : isCount ? _kids[e.Rel].Count.ToString(CultureInfo.InvariantCulture) : "";
        return new TreeRow(e.Rel, name, e.IsDir, depth, _expanded.Contains(e.Rel), icon, note, isCount);
    }

    private void Scan(string dir, string rel)
    {
        if (_tree.Count > 3000) return;
        try
        {
            foreach (var d in Directory.EnumerateDirectories(dir).Order(StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(d);
                if (IgnoredDirs.Contains(name)) continue;
                string r = rel.Length == 0 ? name : $"{rel}/{name}";
                _tree.Add(new Entry(r, true, 0));
                Scan(d, r);
            }
            foreach (var f in Directory.EnumerateFiles(dir).Order(StringComparer.OrdinalIgnoreCase))
                _tree.Add(new Entry(rel.Length == 0 ? Path.GetFileName(f) : $"{rel}/{Path.GetFileName(f)}", false, new FileInfo(f).Length));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Unreadable folders are simply left out of the tree.
        }
    }

    private void IndexTree()
    {
        _kids.Clear();
        foreach (var e in _tree.OrderBy(e => !e.IsDir).ThenBy(e => FileName(e.Rel), StringComparer.OrdinalIgnoreCase))
        {
            int cut = e.Rel.LastIndexOf('/');
            string parent = cut < 0 ? "" : e.Rel[..cut];
            if (!_kids.TryGetValue(parent, out var list)) _kids[parent] = list = [];
            list.Add(e);
        }
        _treeFilter = "";
    }

    private FileItem ToItem(Entry e) =>
        new(FileName(e.Rel), e.IsDir, e.Size, _projectRoot is null ? null : Path.Combine(_projectRoot, e.Rel.Replace('/', Path.DirectorySeparatorChar)), e.Rel);

    private int SpecsIn(FileItem folder) => folder.Rel is { } rel && _tree.Any(t => t.Rel == rel)
        ? _tree.Count(t => !t.IsDir && t.Rel.StartsWith(rel + "/", StringComparison.Ordinal) && IsSpec(t.Rel))
        : CountSpecs(folder.FullPath);

    private static int CountSpecs(string? dir)
    {
        if (dir is null || !Directory.Exists(dir)) return 0;
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Take(5000).Count(IsSpec); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return 0; }
    }

    private static bool IsSpec(string path) =>
        path.Contains(".spec.", StringComparison.OrdinalIgnoreCase) || path.Contains(".test.", StringComparison.OrdinalIgnoreCase);

    private static bool HasExtension(string name, params string[] extensions) =>
        extensions.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase));

    private static string FileName(string rel) => rel[(rel.LastIndexOfAny(['/', '\\']) + 1)..];

    // ── "Get data from X" card ─────────────────────────────────────────────

    public Func<Task<int>>? ImportData
    {
        get => _importData;
        set
        {
            _importData = value;
            Raise(nameof(CanImport), nameof(ImportToolTip));
        }
    }

    public bool XLoaded => _xState == 2;
    public bool CanImport => _importData is not null && _xState != 1;
    public string? ImportToolTip => _importData is null ? "Not connected yet (SetupScreen.ImportData)" : null;
    public string XTitle => XLoaded ? "X data loaded" : "Get data from X";
    public string XButtonText => _xState == 1 ? "Fetching…" : XLoaded ? "Refresh" : "Import";
    public string XSubtitle => _xError ?? "Latest test data from the X interface";
    public string XHint => $"For {XRoute}";

    // Loaded subtitle in three runs: "160 records " + "for Gen X · from BAX" (darker) + " · 2 min ago".
    public string XRecordsText => $"{Fmt.Plural(_records, "record")} ";
    public string XRouteText => $"for {XRoute}";

    public string XAgeText
    {
        get
        {
            int minutes = (int)(DateTime.Now - _xLoadedAt).TotalMinutes;
            return $" · {(minutes < 1 ? "just now" : $"{minutes} min ago")}";
        }
    }

    public async Task ImportAsync()
    {
        if (!CanImport) return;
        _xState = 1;
        _xError = null;
        Raise(string.Empty);
        try
        {
            _records = await _importData!();
            _xState = 2;
            _xLoadedAt = DateTime.Now;
            _ageTimer.Start();
        }
        catch (Exception ex)
        {
            _xState = 0;
            _xError = $"Import failed: {ex.Message}";
        }
        Raise(string.Empty);
    }
}

/// <summary>One field of the fields card (one 68 px row).</summary>
public sealed class Slot(int index, string title, string hint, string tag, string tagColor, bool isFolder) : Observable
{
    private bool _isDragOver, _checking;
    private string? _typed;

    public int Index { get; } = index;
    public string Title { get; } = title;
    /// <summary>What belongs here (".ts, .tsx or .js") – the grey placeholder of the empty path field.</summary>
    public string Hint { get; } = hint;
    public string Tag { get; } = tag;
    public string TagColor { get; } = tagColor;
    public bool IsFolder { get; } = isFolder;

    public FileItem? Item { get; private set; }
    public string Detail { get; private set; } = "";
    public bool IsFilled => Item is not null;

    /// <summary>Why a typed path was not taken ("Not found"); shown in red before "Browse".</summary>
    public string? Error { get; private set; }
    public bool HasError => Error is not null;

    /// <summary>Grey text before "Browse": spec count / size, "Checking…" while the disk is asked, or the error.</summary>
    public string Meta => Error ?? (_checking ? "Checking…" : Item is null ? "" : Detail);

    /// <summary>What the path field shows: relative inside the project, absolute outside it, or the typed text that didn't resolve.</summary>
    public string PathText => _typed ?? Item?.Rel ?? Item?.FullPath ?? "";

    /// <summary>Counts commits of typed paths, so a slow disk answer for an older text is dropped.</summary>
    internal int Version { get; set; }

    /// <summary>Something droppable hovers over the field: it lights up and says what will happen.</summary>
    public bool IsDragOver
    {
        get => _isDragOver;
        set { if (Set(ref _isDragOver, value)) Raise(nameof(DisplayTitle)); }
    }

    public string DisplayTitle => !IsDragOver ? Title
        : Item is null ? $"Drop to add {Title.ToLowerInvariant()}"
        : $"Drop to replace {Item.Name}";

    internal void Fill(FileItem? item, string detail)
    {
        Version++; // a disk check still running for an older typed path must not overwrite this
        Item = item;
        Detail = detail;
        _typed = null;
        _checking = false;
        Error = null;
        Raise(string.Empty);
    }

    /// <summary>A typed path that leads nowhere (or to the wrong kind of file): the field keeps the text, the slot is empty again.</summary>
    internal void Reject(string typed, string error)
    {
        Item = null;
        Detail = "";
        _typed = typed;
        _checking = false;
        Error = error;
        Raise(string.Empty);
    }

    /// <summary>The disk is being asked about a typed path. The previous file stays until the answer comes, so the Run
    /// button doesn't flicker off under the mouse; Run itself waits for the answer (SetupModel.WhenPathsChecked).</summary>
    internal void SetChecking(string typed)
    {
        _typed = typed;
        _checking = true;
        Error = null;
        Raise(string.Empty);
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Small parts
// ════════════════════════════════════════════════════════════════════════════

/// <summary>A file or folder that can go into a field – from the sidebar tree, Explorer, a dialog or a typed path.</summary>
public sealed record FileItem(string Name, bool IsDir, long Size, string? FullPath, string? Rel);

public enum TreeIcon { Folder, TestFolder, Spec, Container, File }

/// <summary>One visible row of the project tree in the sidebar.</summary>
public sealed record TreeRow(string Rel, string Name, bool IsDir, int Depth, bool IsExpanded, TreeIcon Icon, string Note, bool NoteIsCount)
{
    public double Indent => Depth * 16;
}

/// <summary>Base for everything the XAML binds to.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises for each name; <c>string.Empty</c> refreshes every binding to this object.</summary>
    protected void Raise(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>
/// Stroke icon from SVG path data (the Geo.* keys in the XAML). <see cref="Box"/> is the SVG viewBox size, the element can be
/// any size. Without its own Foreground it takes the parent's – inside a button it follows the button's colour.
/// </summary>
public sealed class PathIcon : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(PathIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BoxProperty = DependencyProperty.Register(
        nameof(Box), typeof(double), typeof(PathIcon), new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(PathIcon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(PathIcon), new FrameworkPropertyMetadata(1.3, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(bool), typeof(PathIcon), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Data { get => (Geometry?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public double Box { get => (double)GetValue(BoxProperty); set => SetValue(BoxProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Box, Box);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Data is null || Box <= 0) return;
        drawingContext.PushTransform(new ScaleTransform(ActualWidth / Box, ActualHeight / Box));
        if (Filled) drawingContext.DrawGeometry(Foreground, null, Data);
        else drawingContext.DrawGeometry(null, new Pen(Foreground, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Data);
        drawingContext.Pop();
    }
}

/// <summary>
/// One line of path text that never hides the file name: when the path is wider than the element, folders from the middle
/// give way to "…" – <c>\\corp-fs01\platform\…\node-ci\2026-q4\Dockerfile</c>. The root (server + share, drive + first
/// folder) and as many trailing parts as fit stay.
/// </summary>
public sealed class CompactPath : FrameworkElement
{
    private const string Gap = "…";

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CompactPath), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(CompactPath), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(CompactPath), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(CompactPath), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    /// <summary>Takes whatever width it is given; height of one line.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(0, Format("M").Height);

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.WidthChanged) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (string.IsNullOrEmpty(Text) || ActualWidth <= 0) return;
        var text = Format(Fit(Text, ActualWidth));
        drawingContext.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        drawingContext.DrawText(text, new Point(0, (ActualHeight - text.Height) / 2));
        drawingContext.Pop();
    }

    private string Fit(string path, double width)
    {
        if (Fits(path, width)) return path;

        // Parts with the separator in front: "\\corp-fs01" "\platform" "\shared" … "\Dockerfile".
        var parts = new List<string>();
        int start = 0;
        while (start < path.Length && path[start] is '\\' or '/') start++; // the leading "\\" of a share belongs to the first part
        for (int i = start; i <= path.Length; i++)
        {
            if (i < path.Length && path[i] is not ('\\' or '/')) continue;
            if (i > start) parts.Add(path[(parts.Count == 0 ? 0 : start)..i]);
            start = i;
        }
        if (parts.Count < 3) return path; // nothing in the middle to drop: the clip cuts it

        string sep = path.Contains('\\') ? "\\" : "/";
        string head = parts[0] + parts[1];
        string tail = parts[^1];
        for (int i = parts.Count - 2; i >= 2; i--)
        {
            if (!Fits(head + sep + Gap + parts[i] + tail, width)) break;
            tail = parts[i] + tail;
        }
        return Fits(head + sep + Gap + tail, width) ? head + sep + Gap + tail : Gap + tail;
    }

    private bool Fits(string text, double width) => Format(text).WidthIncludingTrailingWhitespace <= width;

    private FormattedText Format(string text) => new(
        text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}

/// <summary>Attached corner radius for the flat button template (Button itself has none).</summary>
public static class Look
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Look), new FrameworkPropertyMetadata(default(CornerRadius)));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);
}

/// <summary>true / non-empty → Visible (Invert: → Collapsed).</summary>
public sealed class VisibleWhen : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool on = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int i => i != 0,
            ICollection c => c.Count > 0,
            _ => true,
        };
        return on != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Culture-invariant text helpers (a German PC would otherwise write "2,1 KB").</summary>
internal static class Fmt
{
    public static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => FormattableString.Invariant($"{bytes / 1024.0:0.#} KB"),
        _ => FormattableString.Invariant($"{bytes / 1048576.0:0.#} MB"),
    };
}
