using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Threading;

namespace Conduit;

public enum Screen { Setup, Running, Finished }

public enum TestFilter { All, Passed, Failed, Aborted }

public enum TreeIcon { Folder, TestFolder, Spec, Container, File }

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

/// <summary>A file or folder that can go into a field – from the sidebar tree, Explorer or a file dialog.</summary>
public sealed record FileItem(string Name, bool IsDir, long Size, string? FullPath, string? Rel);

/// <summary>One visible row of the project tree in the sidebar (screen 2).</summary>
public sealed record TreeRow(string Rel, string Name, bool IsDir, int Depth, bool IsExpanded, TreeIcon Icon, string Note, bool NoteIsCount)
{
    public double Indent => Depth * 16;
}

/// <summary>One of the five inputs of a run (screens 1/2), also listed in the run sidebar (3–6).</summary>
public sealed class Slot(int index, string title, string hint, string tag, string tagColor, bool isFolder) : Observable
{
    private bool _isDragOver;

    public int Index { get; } = index;
    public string Title { get; } = title;
    public string Hint { get; } = hint;
    public string Tag { get; } = tag;
    public string TagColor { get; } = tagColor;
    public bool IsFolder { get; } = isFolder;
    public string ActionText => IsFolder ? "Add folder" : "Add file";

    public FileItem? Item { get; private set; }
    public string Detail { get; private set; } = "";
    public bool IsFilled => Item is not null;
    public string Name => Item?.Name ?? Title;
    public string Subtitle => Item is null ? Hint : Detail;

    /// <summary>Something droppable hovers over the field: it lights up and says what will happen.</summary>
    public bool IsDragOver
    {
        get => _isDragOver;
        set { if (Set(ref _isDragOver, value)) Raise(nameof(DisplayTitle)); }
    }

    public string DisplayTitle => !IsDragOver ? Name
        : Item is null ? $"Drop to add {Title.ToLowerInvariant()}"
        : $"Drop to replace {Item.Name}";

    internal void Fill(FileItem? item, string detail)
    {
        Item = item;
        Detail = detail;
        Raise(string.Empty);
    }
}

/// <summary>
/// State and behaviour of the whole app (all seven Paper screens): project + fields, the run, logs and export.
/// The views only bind to it and forward user input; dialogs, drag &amp; drop and animations stay in their code-behind.
/// </summary>
public sealed class Workspace : Observable
{
    private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", "bin", "obj", ".vs", ".idea", "dist" };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Random _rnd = new(7);
    private Screen _screen;

    public Workspace()
    {
        Slots =
        [
            new(0, "Test folder", "Specs for all nodes", "", "#1F4BFF", isFolder: true),
            new(1, "Project manifest", "package.json", "JSON", "#E3A008", isFolder: false),
            new(2, "Source file", ".ts, .tsx or .js", "TS", "#3178C6", isFolder: false),
            new(3, "Container", "Dockerfile", "DOCKER", "#0B8FD1", isFolder: false),
            new(4, "Pipeline config", ".yml or .yaml", "YML", "#7C5CDB", isFolder: false),
        ];
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Run progress or screen changed (the window mirrors it on the taskbar).</summary>
    public event EventHandler? ProgressChanged;

    /// <summary>A run just ended (finished or stopped).</summary>
    public event EventHandler? RunFinished;

    public Screen Screen
    {
        get => _screen;
        private set
        {
            if (Set(ref _screen, value)) Raise(nameof(IsSetup), nameof(IsRunning), nameof(IsFinished), nameof(IsRun), nameof(Crumb));
        }
    }

    public bool IsSetup => Screen == Screen.Setup;
    public bool IsRunning => Screen == Screen.Running;
    public bool IsFinished => Screen == Screen.Finished;

    /// <summary>Running or finished – the screens with the run sidebar.</summary>
    public bool IsRun => !IsSetup;

    public string Crumb => IsSetup ? "New run" : $"Run #{_runNo}";

    // ════════════════════════════════════════════════════════════════════════
    // Setup (screens 1, 2)
    // ════════════════════════════════════════════════════════════════════════

    private readonly record struct Entry(string Rel, bool IsDir, long Size);

    private readonly List<Entry> _tree = [];
    private readonly Dictionary<string, List<Entry>> _kids = [];
    private readonly HashSet<string> _expanded = [];
    private string? _projectRoot;               // null while the sample project is open (virtual files)
    private bool _sampleSingle, _isFilling;
    private string _treeFilter = "";
    private int _xState, _matchCount, _plannedTests; // X: 0 none, 1 fetching, 2 loaded
    private DateTime _xLoadedAt;

    public IReadOnlyList<Slot> Slots { get; }
    public IReadOnlyList<TreeRow> TreeRows { get; private set; } = [];
    public string? ProjectName { get; private set; }
    public bool HasProject => ProjectName is not null;
    public bool ShowTip => !HasProject;
    public string ProjectFilesText => Fmt.Plural(_tree.Count(e => !e.IsDir), "file");
    public bool NoTreeMatches => HasProject && TreeRows.Count == 0;

    public string TreeFilter
    {
        get => _treeFilter;
        set { if (Set(ref _treeFilter, value ?? "")) RebuildTree(); }
    }

    public int FilledCount => Slots.Count(s => s.IsFilled);
    public bool IsReady => FilledCount == Slots.Count;
    public string MissingText => Fmt.Plural(Slots.Count - FilledCount, "file") + " missing";
    public string PlannedTestsText => $" · {_plannedTests} tests";
    public int SpecCount { get; private set; }
    public string SpecCountText => Fmt.Plural(SpecCount, "spec");

    public string SetupSubtitle => IsReady
        ? "Everything is in place. Swap any file by dropping a new one on it."
        : "Add the five files below, or drop them anywhere on this page.";

    // Auto-fill (sidebar button): puts every matching file of the project into its empty field.
    public bool IsFilling
    {
        get => _isFilling;
        set { if (Set(ref _isFilling, value)) Raise(nameof(CanAutoFill), nameof(ShowAutoFill)); }
    }

    public bool ShowAutoFill => _matchCount > 0 || IsFilling;
    public bool CanAutoFill => _matchCount > 0 && !IsFilling;
    public string AutoFillText => _matchCount > 0 ? $"Auto-fill {Fmt.Plural(_matchCount, "field")}" : "Filling…";

    // "Get data from X" card.
    public bool XFetching => _xState == 1;
    public bool XLoaded => _xState == 2;
    public bool CanFetchX => !XFetching;
    public string XTitle => XLoaded ? "X data loaded" : "Get data from X";
    public string XButtonText => XFetching ? "Fetching…" : "Get X data";

    public string XSubtitle
    {
        get
        {
            if (!XLoaded) return "Pull the latest test data from the X interface";
            int minutes = (int)(DateTime.Now - _xLoadedAt).TotalMinutes;
            return $"160 records · fetched {(minutes < 1 ? "just now" : $"{minutes} min ago")}";
        }
    }

    public async Task FetchXAsync()
    {
        if (XFetching) return;
        _xState = 1;
        Raise(nameof(XFetching), nameof(XLoaded), nameof(CanFetchX), nameof(XTitle), nameof(XSubtitle), nameof(XButtonText));
        await Task.Delay(1200); // stand-in for the call to the X interface
        _xState = 2;
        _xLoadedAt = DateTime.Now;
        Raise(nameof(XFetching), nameof(XLoaded), nameof(CanFetchX), nameof(XTitle), nameof(XSubtitle), nameof(XButtonText));
    }

    /// <summary>Reads the folder into the sidebar tree and fills every field it can match.</summary>
    public void OpenProject(string root)
    {
        _projectRoot = root;
        ProjectName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _tree.Clear();
        Scan(root, "");
        IndexTree();
        foreach (var slot in Slots) slot.Fill(null, "");
        foreach (var (slot, item) in Matches()) Assign(slot, item, notify: false);

        _expanded.Clear();
        if (Slots[0].Item?.Rel is { } tests) _expanded.Add(tests);
        SetupChanged();
    }

    /// <summary>The demo project from the design (acme-storefront). Single mode = only the Payments node.</summary>
    public void LoadSample(bool single)
    {
        _projectRoot = null;
        ProjectName = "acme-storefront";
        _sampleSingle = single;
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
        foreach (var f in "app cart checkout payments refunds money locale format queue worker jobs mailer webhook config logger db http auth session user product price tax coupon invoice receipt search cache metrics health".Split(' '))
            Add($"src/{f}.ts", 900);
        Add(".github");
        Add(".github/workflows");
        Add(".github/workflows/ci.yml", 812);
        Add(".github/workflows/release.yml", 640);
        Add("Dockerfile", 240);
        Add("package.json", 264);
        Add("README.md", 2048);
        Add("tsconfig.json", 310);
        IndexTree();

        foreach (var slot in Slots) slot.Fill(null, "");
        foreach (var rel in new[] { "tests", "package.json", "src/index.ts", "Dockerfile" })
        {
            var item = FindItem(rel)!;
            Assign(SlotFor(item.Name, item.IsDir), item, notify: false);
        }
        _expanded.Clear();
        _expanded.UnionWith(["tests", "tests/node-18", "tests/node-20"]);
        SetupChanged();
    }

    /// <summary>Puts a file/folder into a field and builds its detail line ("Project manifest · 264 B").</summary>
    public void Assign(int slot, FileItem item, bool notify = true)
    {
        if (slot < 0 || slot >= Slots.Count) return;
        string detail;
        if (slot == 0)
        {
            SpecCount = item.Rel is not null && _tree.Any(t => t.Rel == item.Rel)
                ? _tree.Count(t => !t.IsDir && t.Rel.StartsWith(item.Rel + "/", StringComparison.Ordinal) && IsSpec(t.Rel))
                : CountSpecs(item.FullPath);
            detail = $"Test folder · {Fmt.Plural(SpecCount, "spec")}";
        }
        else
        {
            string folder = item.Rel is { } rel && rel.Contains('/') ? $"{rel[..rel.LastIndexOf('/')]}/ · " : "";
            detail = slot switch
            {
                2 => $"Source file · {folder}{Fmt.Size(item.Size)}",
                4 => $"Pipeline config · {folder}{Fmt.Size(item.Size)}",
                _ => $"{Slots[slot].Title} · {Fmt.Size(item.Size)}",
            };
        }
        Slots[slot].Fill(item, detail);
        if (notify) SetupChanged();
    }

    public void ToggleFolder(TreeRow row)
    {
        if (!row.IsDir) return;
        if (!_expanded.Remove(row.Rel)) _expanded.Add(row.Rel);
        RebuildTree();
    }

    /// <summary>Best file of the project tree for every field that is still empty (test folder, package.json, entry file, Dockerfile, workflow).</summary>
    public List<(int Slot, FileItem Item)> Matches()
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

        var result = new List<(int, FileItem)>();
        for (int i = 0; i < found.Length; i++)
            if (found[i] is { } e && !Slots[i].IsFilled) result.Add((i, ToItem(e)));
        return result;
    }

    /// <summary>An item of the open project by its relative path (drag &amp; drop from the sidebar).</summary>
    public FileItem? FindItem(string rel)
    {
        var e = _tree.FirstOrDefault(t => t.Rel == rel);
        return e.Rel is null ? null : ToItem(e);
    }

    /// <summary>A real file or folder (Explorer drop, file dialog).</summary>
    public FileItem External(string path)
    {
        bool dir = Directory.Exists(path);
        string? rel = _projectRoot is not null && path.StartsWith(_projectRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(_projectRoot, path).Replace('\\', '/') : null;
        return new FileItem(Path.GetFileName(path), dir, dir ? 0 : new FileInfo(path).Length, path, rel);
    }

    public string? ProjectRoot => _projectRoot;

    /// <summary>Which field a file belongs to: 0 test folder, 1 package.json, 2 source, 3 Dockerfile, 4 yml; -1 none.</summary>
    public static int SlotFor(string name, bool isDir)
    {
        if (isDir) return 0;
        name = FileName(name);
        if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase)) return 3;
        if (HasExtension(name, ".yml", ".yaml")) return 4;
        if (HasExtension(name, ".ts", ".tsx", ".js")) return 2;
        return -1;
    }

    /// <summary>After any change to project or fields: recount, rebuild the tree and refresh every binding.</summary>
    private void SetupChanged()
    {
        _matchCount = Matches().Count;
        _plannedTests = IsReady ? PlanNodes().Sum(n => n.Total) : 0;
        RebuildTree();
        Raise(string.Empty);
        ProgressChanged?.Invoke(this, EventArgs.Empty);
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
        SpecCount = 0;
        TreeFilter = "";
    }

    private FileItem ToItem(Entry e) => new(FileName(e.Rel), e.IsDir, e.Size, FullPath(e.Rel), e.Rel);

    private string? FullPath(string rel) => _projectRoot is null ? null : Path.Combine(_projectRoot, rel.Replace('/', Path.DirectorySeparatorChar));

    // ════════════════════════════════════════════════════════════════════════
    // Run (screens 3–6)
    // ════════════════════════════════════════════════════════════════════════

    private readonly List<LogLine> _log = [];
    private PipelineNode? _selectedNode;
    private TestFilter _stateFilter;
    private bool _showAll, _logsOpen;
    private int _runNo = 1041, _logTab, _exportState, _version = -1, _doneCount;
    private DateTime _startedAt;
    private TimeSpan _elapsed;

    public ObservableCollection<PipelineNode> Nodes { get; } = [];

    /// <summary>Finished tests of the selected node, newest first, filtered – rows are kept and moved, never rebuilt.</summary>
    public ObservableCollection<TestItem> VisibleTests { get; } = [];

    public ObservableCollection<LogLine> VisibleLog { get; } = [];
    public IReadOnlyList<string> LogTabs { get; private set; } = [];

    public bool IsMulti => Nodes.Count > 1;
    public string ModeText => IsMulti ? $"Multi mode · {Nodes.Count} nodes in parallel" : "Single mode · 1 node";
    public string StopText => IsMulti ? "Stop all" : "Stop";
    public string ElapsedText => Fmt.Clock(_elapsed);

    public bool LogsOpen
    {
        get => _logsOpen;
        set => Set(ref _logsOpen, value);
    }

    public PipelineNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            // The tab strip pushes null while its items are being replaced – keep the current node then.
            if (value is null || !Set(ref _selectedNode, value)) return;
            _stateFilter = TestFilter.All;
            _showAll = false;
            Raise(nameof(StateFilter), nameof(ShowAll));
            RefreshList();
        }
    }

    public TestFilter StateFilter
    {
        get => _stateFilter;
        set
        {
            if (!Set(ref _stateFilter, value)) return;
            _showAll = false;
            RefreshList();
        }
    }

    public bool ShowAll
    {
        get => _showAll;
        set { if (Set(ref _showAll, value)) RefreshList(); }
    }

    /// <summary>The highlighted "Running" row on top of the list (only in the All filter).</summary>
    public TestItem? RunningTest => StateFilter == TestFilter.All ? SelectedNode?.RunningTest : null;

    public bool HasMore => _doneCount > 5;
    public string ShowMoreText => ShowAll ? "Show less" : $"Show {_doneCount - 5} earlier tests";

    public string EmptyText => VisibleTests.Count > 0 || RunningTest is not null ? ""
        : StateFilter == TestFilter.All ? "Starting…" : "Nothing here.";

    public string QueueText => SelectedNode is not { } n ? ""
        : n.Stopped && n.Queued > 0 ? $"Stopped · {n.Queued} not run"
        : IsRunning ? $"{n.Queued} queued"
        : $"All {n.Done} tests ran";

    public bool CanStopSelected => IsRunning && IsMulti && SelectedNode is { Finished: false };
    public string StopSelectedText => $"Stop {SelectedNode?.Name}";

    // Summary line of the single-node card header.
    public int HeaderCount => SelectedNode is not { } n ? 0 : IsRunning ? n.Done : n.Passed;
    public string HeaderCountRest => SelectedNode is not { } n ? "" : IsRunning ? $" of {n.Total} tests" : $" of {n.Total} passed";

    public string HeaderRightText => IsRunning ? Eta(SelectedNode)
        : _exportState == 2 ? "Exported" : "Ready to export";

    // Logs (screen 4).
    public int LogTab
    {
        get => _logTab;
        set
        {
            if (value >= 0 && Set(ref _logTab, value)) RebuildLog();
        }
    }

    /// <summary>The "All" tab: every line gets its node name in front.</summary>
    public bool IsAllLogs => IsMulti && _logTab == Nodes.Count;

    // Export (screen 6).
    public bool CanExport => _exportState != 1;
    public bool IsExported => _exportState == 2;
    public string ExportText => _exportState switch { 1 => "Exporting…", 2 => "Exported to X", _ => "Export to X" };

    public int RunNo => _runNo;
    public int TotalTests => Nodes.Sum(n => n.Total);
    public int TotalDone => Nodes.Sum(n => n.Done);
    public int TotalFailed => Nodes.Sum(n => n.Failed);

    public void StartRun()
    {
        if (!IsSetup || !IsReady) return;
        var nodes = PlanNodes();
        _runNo++;
        _timer.Stop();
        Nodes.Clear();
        foreach (var n in nodes) Nodes.Add(n);
        _log.Clear();
        VisibleLog.Clear();
        _exportState = 0;
        _version = -1;
        _startedAt = DateTime.Now;
        _elapsed = TimeSpan.Zero;

        LogTabs = IsMulti ? [.. nodes.Select(n => n.Name), "All"] : [.. nodes.Select(n => n.Name)];
        Raise(nameof(LogTabs));
        _logTab = 0;
        Raise(nameof(LogTab));

        Screen = Screen.Running;
        _selectedNode = null;
        SelectedNode = nodes[0];
        _timer.Start();
        Raise(string.Empty);
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopAll()
    {
        if (!IsRunning) return;
        foreach (var n in Nodes) n.Stop();
        Finish();
    }

    public void StopSelected()
    {
        if (!IsRunning || SelectedNode is null) return;
        SelectedNode.Stop();
        if (Nodes.All(n => n.Finished)) Finish();
        else Changed();
    }

    public void NewRun()
    {
        _timer.Stop();
        LogsOpen = false;
        Screen = Screen.Setup;
        SetupChanged();
    }

    public void ToggleLogs()
    {
        if (!IsSetup) LogsOpen = !LogsOpen;
    }

    public async Task ExportToXAsync()
    {
        if (!IsFinished || _exportState == 1) return;
        _exportState = 1;
        Raise(nameof(CanExport), nameof(IsExported), nameof(ExportText));
        await Task.Delay(1000); // stand-in for the upload to X
        _exportState = 2;
        Raise(nameof(CanExport), nameof(IsExported), nameof(ExportText), nameof(HeaderRightText));
    }

    public void SaveJson(string path)
    {
        var data = new
        {
            run = _runNo,
            project = ProjectName,
            duration = Fmt.Clock(_elapsed),
            nodes = Nodes.Select(n => new
            {
                name = n.Name, runtime = n.Runtime, passed = n.Passed, failed = n.Failed, aborted = n.Aborted, notRun = n.Queued,
                tests = n.Tests.Select(t => new { no = t.No, name = t.Name, state = t.IsDone ? t.State.ToString().ToLowerInvariant() : "not run", note = t.Note, seconds = t.Secs }),
            }),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
    }

    public string Summary() => $"Conduit · Run #{_runNo} · {ProjectName}{Environment.NewLine}" + string.Join(Environment.NewLine,
        Nodes.Select(n => $"{n.Name}: {n.Passed}/{n.Total} passed, {n.Failed} failed, {n.Aborted} aborted"));

    public string LogText() => string.Join(Environment.NewLine, VisibleLog);

    private void Tick()
    {
        if (!IsRunning) return;
        _elapsed = DateTime.Now - _startedAt;
        string time = Fmt.Clock(_elapsed, withHundredths: true);
        for (int i = 0; i < Nodes.Count; i++)
        {
            int index = i;
            var node = Nodes[i];
            node.Tick(0.1, _rnd, (kind, text, dim) => AddLog(new LogLine(index, node.Name, time, kind, text, dim)));
        }
        if (Nodes.All(n => n.Finished))
        {
            Finish();
            return;
        }
        if (Nodes.Sum(n => n.Version) != _version) Changed();
        Raise(nameof(ElapsedText), nameof(HeaderRightText));
    }

    /// <summary>A test started/finished somewhere: refresh the list and the taskbar.</summary>
    private void Changed()
    {
        _version = Nodes.Sum(n => n.Version);
        RefreshList();
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Finish()
    {
        _timer.Stop();
        Screen = Screen.Finished;
        foreach (var line in VisibleLog) line.ShowCursor = false;
        RefreshList();
        Raise(string.Empty);
        ProgressChanged?.Invoke(this, EventArgs.Empty);
        RunFinished?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshList()
    {
        var node = SelectedNode;
        var done = node is null ? [] : node.Tests.Where(t => t.IsDone && Matches(t)).Reverse().ToList();
        _doneCount = done.Count;
        Sync(VisibleTests, ShowAll ? done : done.Take(5).ToList());
        Raise(nameof(RunningTest), nameof(HasMore), nameof(ShowMoreText), nameof(EmptyText), nameof(QueueText),
            nameof(CanStopSelected), nameof(StopSelectedText), nameof(HeaderCount), nameof(HeaderCountRest), nameof(HeaderRightText),
            nameof(TotalTests), nameof(TotalDone), nameof(TotalFailed));
    }

    private bool Matches(TestItem t) => StateFilter switch
    {
        TestFilter.Passed => t.State == TestState.Passed,
        TestFilter.Failed => t.State == TestState.Failed,
        TestFilter.Aborted => t.State == TestState.Aborted,
        _ => true,
    };

    private void AddLog(LogLine line)
    {
        _log.Add(line);
        if (_log.Count > 2000) _log.RemoveRange(0, 500);
        if (!IsAllLogs && line.Node != _logTab) return;
        if (VisibleLog.Count > 0) VisibleLog[^1].ShowCursor = false;
        line.ShowCursor = IsRunning;
        VisibleLog.Add(line);
        if (VisibleLog.Count > 300) VisibleLog.RemoveAt(0);
    }

    private void RebuildLog()
    {
        foreach (var line in VisibleLog) line.ShowCursor = false;
        VisibleLog.Clear();
        foreach (var line in _log.Where(l => IsAllLogs || l.Node == _logTab).TakeLast(300)) VisibleLog.Add(line);
        if (IsRunning && VisibleLog.Count > 0) VisibleLog[^1].ShowCursor = true;
        Raise(nameof(IsAllLogs));
    }

    /// <summary>Sample → nodes from the design. Real folder → one node per sub-folder of the test folder (multi mode), else one node.</summary>
    private List<PipelineNode> PlanNodes()
    {
        if (_projectRoot is null) return PipelineNode.Sample(_sampleSingle);
        string? tests = Slots[0].Item?.FullPath;
        if (tests is null || !Directory.Exists(tests)) return [PipelineNode.ForFolder("Tests", SpecCount, 1)];
        var subs = Directory.EnumerateDirectories(tests).Where(d => !IgnoredDirs.Contains(Path.GetFileName(d))).Order().ToList();
        if (subs.Count < 2) return [PipelineNode.ForFolder(Path.GetFileName(tests), SpecCount, 1)];
        return [.. subs.Take(6).Select((d, i) => PipelineNode.ForFolder(Path.GetFileName(d), CountSpecs(d), i + 1))];
    }

    private string Eta(PipelineNode? n)
    {
        if (n is null || n.Done == 0) return "estimating…";
        if (n.Finished) return n.Stopped ? "stopped" : "done";
        var left = TimeSpan.FromSeconds(_elapsed.TotalSeconds / n.Done * (n.Total - n.Done));
        return $"~{Fmt.Clock(left)} left";
    }

    /// <summary>Makes <paramref name="target"/> equal to <paramref name="source"/> with moves/inserts, so existing rows stay.</summary>
    private static void Sync<T>(ObservableCollection<T> target, List<T> source) where T : class
    {
        for (int i = 0; i < source.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], source[i])) continue;
            int existing = target.IndexOf(source[i]);
            if (existing > i) target.Move(existing, i);
            else target.Insert(i, source[i]);
        }
        while (target.Count > source.Count) target.RemoveAt(target.Count - 1);
    }

    // ── File helpers ───────────────────────────────────────────────────────

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
}

/// <summary>Culture-invariant text helpers (a German PC would otherwise write "2,1s").</summary>
internal static class Fmt
{
    public static string Inv(FormattableString s) => FormattableString.Invariant(s);

    public static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => Inv($"{bytes / 1024.0:0.#} KB"),
        _ => Inv($"{bytes / 1048576.0:0.#} MB"),
    };

    public static string Clock(TimeSpan t, bool withHundredths = false) => withHundredths
        ? Inv($"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}")
        : Inv($"{(int)t.TotalMinutes}:{t.Seconds:00}");
}
