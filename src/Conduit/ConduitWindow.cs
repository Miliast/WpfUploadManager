using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

using Microsoft.Win32;

using static Conduit.Ui;

namespace Conduit;

/// <summary>
/// Conduit – the whole app in one window, built in code (no XAML). Screens from the Paper design:
/// 1–2 Setup (empty / filled), 3–6 Running and Finished (multi or single mode, optional log panel), 7 taskbar progress.
/// Each Render* method rebuilds its region from the state fields below; nothing else touches the visuals.
/// </summary>
public sealed class ConduitWindow : Window
{
    [STAThread]
    public static void Main() => new Application().Run(new ConduitWindow());

    private enum Screen { Setup, Running, Finished }

    // The five inputs of a run (screen 1/2).
    private static readonly (string Title, string Hint, string Tag, Brush Color, bool Folder)[] Slots =
    [
        ("Test folder", "Specs for all nodes", "", Blue, true),
        ("Project manifest", "package.json", "JSON", Json, false),
        ("Source file", ".ts, .tsx or .js", "TS", Ts, false),
        ("Container", "Dockerfile", "DOCKER", Docker, false),
        ("Pipeline config", ".yml or .yaml", "YML", Yml, false),
    ];

    private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", "bin", "obj", ".vs", ".idea", "dist" };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ── State ──────────────────────────────────────────────────────────────
    private Screen _screen = Screen.Setup;

    // Setup: project tree in the sidebar + the five slots.
    private string? _projectName, _projectRoot;              // root == null → sample project (virtual files)
    private bool _sampleSingle;
    private readonly List<(string Rel, bool Dir, long Size)> _tree = [];
    private readonly Dictionary<string, List<(string Rel, bool Dir, long Size)>> _kids = new();
    private readonly HashSet<string> _expanded = [];
    private string _filter = "";
    private readonly (string Name, string Detail, string? Full, string? Rel)?[] _slotValues = new (string, string, string?, string?)?[5];
    private int _specCount;
    private int _xState;                                      // 0 none, 1 loading, 2 loaded
    private DateTime _xLoadedAt;

    // Run.
    private List<PipelineNode> _nodes = [];
    private int _runNo = 1041, _sel, _logTab, _exportState, _renderedVersion = -1;
    private char _stateFilter = '*';
    private bool _showAll, _logsOpen, _flashed;
    private DateTime _startedAt;
    private TimeSpan _elapsed;
    private readonly List<(int Node, string Time, char Kind, string Text, string Dim)> _log = [];
    private readonly Random _rnd = new(7);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string _overlayKey = "";

    // Regions + live labels that update without a rebuild.
    private readonly Border _header = new() { Height = 56, Background = White, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(20, 0, 16, 0) };
    private readonly Border _sidebar = new() { Width = 300, Background = White, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 1, 0), AllowDrop = true };
    private readonly Border _content = new() { Background = Page, AllowDrop = true };
    private readonly ScrollViewer _treeHost = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly FrameworkElement _spinner = Spinner();   // reused so its animation doesn't restart on every re-render
    private TextBlock? _timerText, _runningTime, _etaText;
    private ScrollViewer? _listScroll;
    private double _listOffset;

    public ConduitWindow()
    {
        Title = "Conduit";
        Icon = Logo();
        Width = 1280;
        Height = 840;
        MinWidth = 1000;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Sans;
        FontSize = 13;
        Foreground = Black;
        Background = White;
        UseLayoutRounding = true;
        TaskbarItemInfo = new TaskbarItemInfo();

        var body = new DockPanel();
        DockPanel.SetDock(_sidebar, Dock.Left);
        body.Children.Add(_sidebar);
        body.Children.Add(_content);
        var root = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        root.Children.Add(_header);
        root.Children.Add(body);
        Content = root;

        // Dropping anywhere on the page fills the matching fields; a folder on the empty sidebar opens it as project.
        _content.DragOver += (_, e) => { e.Effects = _screen == Screen.Setup && Dropped(e.Data).Any(d => SlotFor(d.Name, d.Dir) >= 0) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        _content.Drop += (_, e) => { if (_screen == Screen.Setup) foreach (var d in Dropped(e.Data)) Assign(SlotFor(d.Name, d.Dir), d); };
        _sidebar.DragOver += (_, e) => { e.Effects = _screen == Screen.Setup && Dropped(e.Data).Any(d => d.Dir && d.Full is not null) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        _sidebar.Drop += (_, e) => { var d = Dropped(e.Data).FirstOrDefault(x => x.Dir && x.Full is not null); if (_screen == Screen.Setup && d.Full is not null) OpenProject(d.Full); };

        PreviewKeyDown += OnKey;
        _timer.Tick += (_, _) => OnTick();
        RenderAll();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Windows 11: white title bar + hairline border so the native chrome blends into the white header.
        var hwnd = new WindowInteropHelper(this).Handle;
        int caption = 0x00FFFFFF, border = 0x00ECE8E6; // COLORREF = 0x00BBGGRR
        _ = DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.O && _screen == Screen.Setup) PickProjectFolder();
        else if (e.Key == Key.Enter && _screen == Screen.Setup && MissingCount() == 0) StartRun();
        else if (e.Key is Key.L or Key.Oem3 && _screen != Screen.Setup) ToggleLogs();
        else return;
        e.Handled = true;
    }

    private void RenderAll()
    {
        RenderHeader();
        RenderSidebar();
        RenderContent();
        UpdateTaskbar();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Header
    // ════════════════════════════════════════════════════════════════════════

    private void RenderHeader()
    {
        var logo = Box(Dot(8, Blue, 2), Ink, 6, w: 22, h: 22);
        var brand = Btn(Row(10, logo, T("Conduit", 15, Black, Bold)), LinkMuted, _screen == Screen.Finished ? NewRun : null, pad: new Thickness(0),
            tip: _screen == Screen.Finished ? "Start a new run" : null);
        var crumb = T(_screen == Screen.Setup ? "New run" : $"Run #{_runNo}", 13, Black, Semi);

        var items = new List<UIElement?> { brand, T("/", 13, Faintest), crumb, null };
        if (_screen == Screen.Setup)
        {
            int missing = MissingCount();
            items.Add(missing > 0
                ? T($"{missing} file{(missing == 1 ? "" : "s")} missing", 13, Subtle)
                : Row(0, T("Ready", 13, Green, Semi), T($" · {PlanNodes().Sum(n => n.Total)} tests", 13, Subtle)));
            var run = Btn(Row(8, Ui.Icon(PlayIcon, null, 10, 0, 14, filled: true), T("Run pipelines")), Primary, StartRun, 32, new Thickness(14, 0, 16, 0), "Ctrl+Enter");
            run.IsEnabled = missing == 0;
            items.Add(run);
        }
        else
        {
            if (_screen == Screen.Running)
            {
                _timerText = T(Clock(_elapsed), 12, Black, Medium, mono: true);
                items.Add(Box(Row(8, Dot(6, Blue), _timerText), Chip, 15, new Thickness(12, 0, 12, 0), h: 30));
            }
            var terminal = Btn(Ui.Icon(TerminalIcon, null, 15), _logsOpen ? PillDark : _screen == Screen.Running ? PillLight : SquareOutline, ToggleLogs,
                _screen == Screen.Running ? 30 : 34, new Thickness(0), "Logs (Ctrl+L)");
            terminal.Width = 34;
            items.Add(terminal);

            if (_screen == Screen.Running)
                items.Add(Btn(Row(7, Dot(8, RedText, 2), T(_nodes.Count > 1 ? "Stop all" : "Stop")), DangerSoft, StopAll, 30, new Thickness(14, 0, 14, 0)));
            else
                items.Add(ExportButton());
        }
        _header.Child = Row(14, items.ToArray());
    }

    /// <summary>Dark split button "Export to X | ▾" (screen 6).</summary>
    private Border ExportButton()
    {
        string label = _exportState switch { 1 => "Exporting…", 2 => "Exported to X", _ => "Export to X" };
        var main = Btn(Row(8, Ui.Icon(_exportState == 2 ? CheckIcon8 : UploadIcon, null, 13, 1.4, _exportState == 2 ? 8 : 14), T(label)), OnDark, ExportToX, 34, new Thickness(12, 0, 12, 0));
        main.IsEnabled = _exportState != 1;
        var more = Btn(Ui.Icon(ChevronDown, null, 12, 1.5), OnDark, null, 34, new Thickness(8, 0, 8, 0), "More export options");
        more.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
            menu.Items.Add(MakeMenuItem("Save results as JSON…", SaveJson));
            menu.Items.Add(MakeMenuItem("Copy summary", () => CopyText(Summary())));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("New run", NewRun));
            menu.IsOpen = true;
        };
        var split = Row(0, main, Box(bg: Hex("#33FFFFFF"), w: 1, h: 18), more);
        var box = Box(split, Ink, 8, h: 34);
        box.SizeChanged += (_, _) => box.Clip = new RectangleGeometry(new Rect(box.RenderSize), 8, 8);
        return box;
    }

    private static MenuItem MakeMenuItem(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Sidebar
    // ════════════════════════════════════════════════════════════════════════

    private void RenderSidebar()
    {
        if (_screen != Screen.Setup) _sidebar.Child = RunSidebar();
        else if (_projectName is null) _sidebar.Child = EmptySidebar();
        else _sidebar.Child = ProjectSidebar();
    }

    /// <summary>Screen 1: no project yet.</summary>
    private Grid EmptySidebar()
    {
        var label = SectionLabel("Project");
        label.Margin = new Thickness(20, 22, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Top;

        // Skeleton of a file tree as illustration.
        var art = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 26) };
        foreach (var (indent, width) in new[] { (0, 70), (16, 84), (16, 60), (0, 52), (16, 76) })
        {
            var line = Row(6, Box(bg: Line, radius: 2, w: 12, h: 10), Box(bg: Line, radius: 3, w: width, h: 6));
            line.Margin = new Thickness(indent, 4, 0, 4);
            art.Children.Add(line);
        }

        var desc = T("Browse its files here, then drag them onto the fields.", 13, Muted);
        desc.TextWrapping = TextWrapping.Wrap;
        desc.TextAlignment = TextAlignment.Center;
        desc.MaxWidth = 230;
        var kbd = Box(T("Ctrl O", 11, White, Semi), Hex("#33FFFFFF"), 4, new Thickness(5, 1, 5, 1));
        var open = Btn(Row(10, T("Open folder"), kbd), Dark, PickProjectFolder, 36, new Thickness(14, 0, 8, 0));
        open.HorizontalAlignment = HorizontalAlignment.Center;
        var sample = Btn(T("Try a sample project"), Link, null, pad: new Thickness(4), tip: "Shift-click for single mode");
        sample.Click += (_, _) => LoadSample(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        sample.HorizontalAlignment = HorizontalAlignment.Center;

        var title = T("Open your project", 15, Black, Semi);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var center = Stack(10, art, title, desc, open, sample);
        center.Margin = new Thickness(24, 0, 24, 40);
        center.VerticalAlignment = VerticalAlignment.Center;
        open.Margin = new Thickness(0, 16, 0, 0);

        return new Grid { Background = White, Children = { label, center } };
    }

    /// <summary>Screen 2: project tree with filter; items can be dragged onto the fields.</summary>
    private DockPanel ProjectSidebar()
    {
        int files = _tree.Count(t => !t.Dir);
        var head = Row(10, Ui.Icon(FolderIcon, Blue, 15, 1.2), T(_projectName!, 14, Black, Bold), T($"{files} files", 12, Subtle, mono: true), null,
            Btn(T("Replace", 13, null, Medium), Link, PickProjectFolder, pad: new Thickness(2)));
        head.Margin = new Thickness(18, 16, 14, 10);

        var input = new TextBox { Text = _filter, BorderThickness = new Thickness(0), Background = Clear, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0) };
        var placeholder = T("Filter files", 13, Faint);
        placeholder.IsHitTestVisible = false;
        placeholder.Visibility = _filter.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        input.TextChanged += (_, _) =>
        {
            _filter = input.Text.Trim();
            placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _treeHost.Content = BuildTree();
        };
        var field = new Grid { Children = { placeholder, input } };
        var filter = Box(Row(8, Ui.Icon(SearchIcon, Subtle, 13), Grow(field)), White, 8, new Thickness(10, 0, 10, 0), h: 32, border: Line);
        filter.Margin = new Thickness(14, 0, 14, 12);

        var hint = Box(Row(10, Ui.Icon(UploadIcon, Muted, 13), T("Drag files from here onto a field", 12, Muted)), pad: new Thickness(20, 12, 20, 12), border: Line);
        hint.BorderThickness = new Thickness(0, 1, 0, 0);

        _treeHost.Content = BuildTree();
        var panel = new DockPanel();
        foreach (var top in new FrameworkElement[] { head, filter }) { DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top); }
        DockPanel.SetDock(hint, Dock.Bottom);
        panel.Children.Add(hint);
        panel.Children.Add(_treeHost);
        return panel;
    }

    private StackPanel BuildTree()
    {
        var panel = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };
        if (_filter.Length > 0)
        {
            foreach (var e in _tree.Where(t => !t.Dir && t.Rel.Contains(_filter, StringComparison.OrdinalIgnoreCase)).Take(300))
                panel.Children.Add(TreeRow(e, 0, showPath: true));
            if (panel.Children.Count == 0) panel.Children.Add(new Border { Padding = new Thickness(12, 8, 0, 0), Child = T("No matching files", 13, Subtle) });
            return panel;
        }
        void Level(string parent, int depth)
        {
            if (!_kids.TryGetValue(parent, out var kids)) return;
            foreach (var k in kids)
            {
                panel.Children.Add(TreeRow(k, depth, showPath: false));
                if (k.Dir && _expanded.Contains(k.Rel)) Level(k.Rel, depth + 1);
            }
        }
        Level("", 0);
        return panel;
    }

    private Border TreeRow((string Rel, bool Dir, long Size) entry, int depth, bool showPath)
    {
        string name = showPath ? entry.Rel : FileName(entry.Rel);
        int slot = Array.FindIndex(_slotValues, v => v?.Rel == entry.Rel);
        bool open = _expanded.Contains(entry.Rel);

        UIElement chevron = entry.Dir ? Ui.Icon(open ? ChevronDown : ChevronRight, Subtle, 11, 1.4) : Spacer(11);
        UIElement icon = entry.Dir ? Ui.Icon(FolderIcon, slot == 0 ? Blue : Ink, 14, 1.2, filled: slot == 0)
            : IsSpec(name) ? Ui.Icon(FlaskIcon, Yml, 14, 1.2)
            : SlotFor(name, false) == 3 ? Ui.Icon(BoxIcon, Docker, 14, 1.2)
            : Ui.Icon(DocIcon, Green, 14, 1.2);
        var right = slot >= 0 ? T(Slots[slot].Title, 11, Subtle)
            : entry.Dir ? T(_kids.TryGetValue(entry.Rel, out var k) ? k.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "", 11, Subtle, mono: true)
            : null;

        var row = Box(Row(8, chevron, icon, Grow(T(name, 13, Black, entry.Dir ? Medium : Regular)), right), Clear, 6,
            new Thickness(8 + (depth * 18), 0, 10, 0), h: 29);
        row.Cursor = entry.Dir ? Cursors.Hand : Cursors.SizeAll;
        row.MouseEnter += (_, _) => row.Background = Chip;
        row.MouseLeave += (_, _) => row.Background = Clear;

        // Click toggles folders; dragging starts a drag & drop onto the fields (also works into Explorer for real files).
        Point? down = null;
        row.MouseLeftButtonDown += (_, e) => down = e.GetPosition(row);
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (down is null || !entry.Dir) return;
            down = null;
            if (!_expanded.Remove(entry.Rel)) _expanded.Add(entry.Rel);
            _treeHost.Content = BuildTree();
        };
        row.MouseMove += (_, e) =>
        {
            if (down is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(row) - start;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            down = null;
            var data = new DataObject();
            data.SetData(RelFormat, entry.Rel);
            if (_projectRoot is not null) data.SetData(DataFormats.FileDrop, new[] { FullPath(entry.Rel)! });
            DragDrop.DoDragDrop(row, data, DragDropEffects.Copy);
        };
        return row;
    }

    /// <summary>Screens 3–6: summary of the run + its inputs.</summary>
    private StackPanel RunSidebar()
    {
        string mode = _nodes.Count > 1 ? $"Multi mode · {_nodes.Count} nodes in parallel" : "Single mode · 1 node";
        var summary = Stack(6, SectionLabel("This run"), T(_projectName ?? "Project", 14, Black, Semi), T(mode, 13, Subtle));
        summary.Margin = new Thickness(4, 0, 4, 0);

        var inputs = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
        var label = SectionLabel("Inputs");
        label.Margin = new Thickness(4, 0, 0, 8);
        inputs.Children.Add(label);
        for (int i = 0; i < Slots.Length; i++)
        {
            var row = Row(12, InputGlyph(Slots[i].Folder, Slots[i].Color), Grow(T(_slotValues[i]?.Name ?? "–", 13, Black, Medium)),
                i == 0 ? T($"{_specCount} specs", 12, Subtle) : null);
            row.Margin = new Thickness(4, 7, 4, 7);
            inputs.Children.Add(row);
        }
        var panel = Stack(0, summary, inputs);
        panel.Margin = new Thickness(16, 20, 16, 18);
        return panel;
    }

    private static TextBlock SectionLabel(string text) => T(text.ToUpperInvariant(), 11, Muted, Semi);

    // ════════════════════════════════════════════════════════════════════════
    // Content
    // ════════════════════════════════════════════════════════════════════════

    private void RenderContent()
    {
        if (_listScroll is not null) _listOffset = _listScroll.VerticalOffset;
        _runningTime = _etaText = null;
        _content.Child = _screen == Screen.Setup ? SetupPage() : RunPage();
        _renderedVersion = _nodes.Sum(n => n.Version);
    }

    // ── Setup (screens 1, 2) ───────────────────────────────────────────────

    private Grid SetupPage()
    {
        int filled = 5 - MissingCount();
        var title = T("Set up the run.", 34, Black, Bold);
        var sub = T(filled == 5 ? "Everything is in place. Swap any file by dropping a new one on it." : "Add the five files below, or drop them anywhere on this page.", 14, Muted);

        var segments = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < 5; i++)
        {
            var seg = Box(bg: i < filled ? Blue : Line, radius: 2, w: 28, h: 4);
            seg.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
            segments.Children.Add(seg);
        }
        var count = Row(0, T($"{filled}", 12, Black, Semi, mono: true), T(" / 5 files", 12, Subtle, mono: true));
        count.HorizontalAlignment = HorizontalAlignment.Right;
        var counter = Stack(10, count, segments);
        counter.VerticalAlignment = VerticalAlignment.Bottom;
        counter.Margin = new Thickness(0, 0, 0, 2);
        var head = Row(16, Grow(Stack(6, title, sub)), counter);

        var rows = new StackPanel { Margin = new Thickness(6) };
        for (int i = 0; i < Slots.Length; i++)
        {
            if (i > 0) rows.Children.Add(Box(bg: LineSoft, h: 1, pad: null));
            rows.Children.Add(SlotRow(i));
        }
        foreach (var sep in rows.Children.OfType<Border>().Where(b => b.Height == 1)) sep.Margin = new Thickness(16, 0, 16, 0);

        var column = Stack(16, head, Card(rows, new CornerRadius(20)), Card(XDataRow(), new CornerRadius(16)));
        ((FrameworkElement)column.Children[1]).Margin = new Thickness(0, 22, 0, 0);
        column.MaxWidth = 900;
        column.Margin = new Thickness(72, 44, 72, 24);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = column };
        var page = new Grid();
        page.RowDefinitions.Add(new RowDefinition());
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.Children.Add(scroll);
        if (_projectName is null)
        {
            var tip = Row(8, Ui.Icon(InfoIcon, Subtle, 13), T("Tip: open the whole project folder in the sidebar and we'll fill matching fields for you.", 13, Muted));
            tip.Margin = new Thickness(72, 0, 72, 40);
            tip.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(tip, 1);
            page.Children.Add(tip);
        }
        return page;
    }

    /// <summary>One input field. Highlights while something droppable is dragged over it ("Drop to replace ci.yml").</summary>
    private Border SlotRow(int i)
    {
        var def = Slots[i];
        var value = _slotValues[i];
        var glyph = def.Folder ? FolderGlyph(value is null) : FileGlyph(def.Tag, def.Color, value is null);
        var title = T(value?.Name ?? def.Title, 14, Black, Semi);
        var sub = T(value?.Detail ?? def.Hint, 13, Subtle);
        var action = value is null
            ? Btn(T(def.Folder ? "Add folder" : "Add file"), ChipButton, () => Browse(i), 32, new Thickness(14, 0, 14, 0))
            : Btn(T("Replace", 13, null, Regular), LinkMuted, () => Browse(i), pad: new Thickness(4));

        var row = new Border
        {
            Height = 68, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), BorderBrush = Clear, Background = Clear,
            Padding = new Thickness(14, 0, 14, 0), AllowDrop = true, Child = Row(14, glyph, Grow(Stack(2, title, sub)), action),
        };
        void Highlight(bool on)
        {
            row.Background = on ? BlueSoft : Clear;
            row.BorderBrush = on ? Blue : Clear;
            title.Text = !on ? value?.Name ?? def.Title : value is null ? $"Drop to add {def.Title.ToLowerInvariant()}" : $"Drop to replace {value.Value.Name}";
            title.Foreground = on ? Blue : Black;
        }
        row.DragOver += (_, e) =>
        {
            bool ok = Dropped(e.Data).Any(d => SlotFor(d.Name, d.Dir) == i);
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            Highlight(ok);
            e.Handled = true;
        };
        row.DragLeave += (_, _) => Highlight(false);
        row.Drop += (_, e) =>
        {
            Highlight(false);
            foreach (var d in Dropped(e.Data))
                if (SlotFor(d.Name, d.Dir) == i) { Assign(i, d); break; }
            e.Handled = true;
        };
        return row;
    }

    /// <summary>"Get data from X" card under the fields.</summary>
    private Grid XDataRow()
    {
        var icon = new Grid { Width = 40, Height = 40, Children = { Box(Ui.Icon(CloudIcon, Blue, 16), BlueChip, 10) } };
        if (_xState == 2)
        {
            var badge = Box(Ui.Icon(CheckIcon8, White, 8, 1.6, 8), Green, 8, w: 16, h: 16, border: White, borderWidth: 2);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Bottom;
            badge.Margin = new Thickness(0, 0, -5, -5);
            icon.Children.Add(badge);
        }
        int minutes = (int)(DateTime.Now - _xLoadedAt).TotalMinutes;
        string ago = minutes < 1 ? "just now" : $"{minutes} min ago";
        var title = T(_xState == 2 ? "X data loaded" : "Get data from X", 14, Black, Semi);
        var sub = T(_xState == 2 ? $"160 records · fetched {ago}" : "Pull the latest test data from the X interface", 13, Subtle);
        var button = _xState == 2
            ? Btn(Row(8, Ui.Icon(RefreshIcon, null, 13), T("Get X data")), Outline, FetchX, 32, new Thickness(12, 0, 12, 0))
            : Btn(Row(8, Ui.Icon(DownloadIcon, null, 13), T(_xState == 1 ? "Fetching…" : "Get X data")), Dark, FetchX, 32, new Thickness(12, 0, 12, 0));
        button.IsEnabled = _xState != 1;
        var row = Row(16, icon, Grow(Stack(2, title, sub)), button);
        row.Margin = new Thickness(22, 14, 22, 14);
        return row;
    }

    // ── Running / Finished (screens 3–6) ───────────────────────────────────

    private Grid RunPage()
    {
        var page = new Grid { Margin = new Thickness(40, 24, 40, _logsOpen ? 0 : 24) };
        page.RowDefinitions.Add(new RowDefinition());
        page.RowDefinitions.Add(new RowDefinition { Height = _logsOpen ? new GridLength(306) : new GridLength(0) });
        page.Children.Add(Card(NodeCard(), new CornerRadius(20)));
        if (_logsOpen)
        {
            var logs = Card(LogPanel(), new CornerRadius(18, 18, 0, 0), shadowUp: true);
            logs.Margin = new Thickness(0, 16, 0, 0);
            Grid.SetRow(logs, 1);
            page.Children.Add(logs);
        }
        return page;
    }

    private Grid NodeCard()
    {
        var node = _nodes[_sel];
        bool running = _screen == Screen.Running;

        // Header: node tabs (multi) or the single node's summary line.
        UIElement header;
        if (_nodes.Count > 1)
        {
            var tabs = new UniformGrid { Rows = 1, Columns = _nodes.Count, Margin = new Thickness(8) };
            for (int i = 0; i < _nodes.Count; i++) tabs.Children.Add(NodeTab(i));
            header = tabs;
        }
        else
        {
            var stats = new List<UIElement?>
            {
                NodeIcon(node, Blue, White), T(node.Name, 15, Black, Semi), node.Runtime.Length > 0 ? T(node.Runtime, 13, Subtle) : null, T("•", 10, Faintest),
                Row(0, T($"{(running ? node.Done : node.Passed)}", 13, Black, Semi), T(running ? $" of {node.Total} tests" : $" of {node.Total} passed", 13, Muted)),
            };
            if (node.Failed > 0) stats.Add(T($"{node.Failed} failed", 13, RedText, Semi));
            if (node.Aborted > 0) stats.Add(T($"{node.Aborted} aborted", 13, AmberText, Semi));
            stats.Add(null);
            _etaText = T(running ? Eta(node) : _exportState == 2 ? "Exported" : "Ready to export", 13, Subtle);
            stats.Add(_etaText);
            var bar = Bar(node.Passed, node.Failed, node.Aborted, node.Total, 3, Track);
            bar.Margin = new Thickness(0, 12, 0, 0);
            header = new Border { Padding = new Thickness(20, 16, 20, 16), Child = Stack(0, Row(10, stats.ToArray()), bar) };
        }

        // Filter row: All / Passed / Failed / Aborted + queue info.
        var filters = new StackPanel { Orientation = Orientation.Horizontal };
        filters.Children.Add(FilterButton("All", node.Done, Subtle, '*'));
        filters.Children.Add(FilterButton("Passed", node.Passed, Faint, 'p'));
        filters.Children.Add(FilterButton("Failed", node.Failed, RedText, 'f'));
        filters.Children.Add(FilterButton("Aborted", node.Aborted, AmberText, 'a'));
        var right = new List<UIElement?>();
        if (running || (node.Stopped && node.Queued > 0)) right.Add(T(node.Stopped ? $"Stopped · {node.Queued} not run" : $"{node.Queued} queued", 12, Subtle));
        else right.Add(T($"All {node.Done} tests ran", 12, Subtle));
        if (running && _nodes.Count > 1 && !node.Finished)
            right.Add(Btn(Row(6, Dot(7, RedText, 2), T($"Stop {node.Name}", 12)), DangerOutline, () => { node.Stop(); OnTick(); }, 28, new Thickness(12, 0, 12, 0)));
        var filterRow = Row(6, filters, null, Row(12, right.ToArray()));
        filterRow.Margin = new Thickness(16, 14, 16, 14);

        // Test list, newest first.
        var list = new StackPanel { Margin = new Thickness(6) };
        int ri = node.RunningIndex;
        if (ri >= 0 && _stateFilter == '*') list.Children.Add(RunningRow(node, node.Tests[ri]));
        var done = node.Tests.Where(t => t.State is 'p' or 'f' or 'a' && (_stateFilter == '*' || t.State == _stateFilter)).Reverse().ToList();
        foreach (var t in done.Take(_showAll ? done.Count : 5)) list.Children.Add(TestRow(t));
        if (done.Count > 5)
        {
            var more = Btn(T(_showAll ? "Show less" : $"Show {done.Count - 5} earlier tests", 13, null, Regular), LinkMuted, () => { _showAll = !_showAll; RenderContent(); }, 40, new Thickness(54, 0, 0, 0));
            more.HorizontalAlignment = HorizontalAlignment.Left;
            list.Children.Add(more);
        }
        if (list.Children.Count == 0)
            list.Children.Add(new Border { Padding = new Thickness(54, 12, 0, 12), Child = T(_stateFilter == '*' ? "Starting…" : "Nothing here.", 13, Subtle) });

        _listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
        var scroll = _listScroll;
        double offset = _listOffset;
        scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(offset);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var headerBox = new Border { Child = header, BorderBrush = LineSoft, BorderThickness = new Thickness(0, 0, 0, 1) };
        var filterBox = new Border { Child = filterRow, BorderBrush = LineSoft, BorderThickness = new Thickness(0, 0, 0, 1) };
        Grid.SetRow(filterBox, 1);
        Grid.SetRow(scroll, 2);
        grid.Children.Add(headerBox);
        grid.Children.Add(filterBox);
        grid.Children.Add(scroll);
        return grid;
    }

    private Button NodeTab(int i)
    {
        var n = _nodes[i];
        bool sel = i == _sel;
        var counts = new List<UIElement?> { NodeIcon(n, sel ? Blue : Faintest, sel ? LineSoft : White), Grow(T(n.Name, 13, sel ? Black : Muted, sel ? Semi : Medium)) };
        if (n.Failed > 0) counts.Add(T($"{n.Failed}", 11, RedText, Semi));
        if (n.Aborted > 0) counts.Add(T($"{n.Aborted}", 11, AmberText, Semi));
        counts.Add(T($"{n.Done}/{n.Total}", 12, sel ? Subtle : Faint));
        var tab = Btn(Stack(8, Row(8, counts.ToArray()), Bar(n.Passed, n.Failed, n.Aborted, n.Total, 2, sel ? TrackDark : Track)), Tab,
            () => { _sel = i; _showAll = false; _listOffset = 0; RenderContent(); }, pad: new Thickness(14, 9, 14, 9));
        tab.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        tab.Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0);
        if (sel) tab.Background = LineSoft; // local value wins over the hover trigger
        return tab;
    }

    /// <summary>Ring while running, green check when done; red/amber dot when the node has problems.</summary>
    private static FrameworkElement NodeIcon(PipelineNode n, Brush color, Brush dotRing)
    {
        Brush? dot = n.Failed > 0 ? Red : n.Aborted > 0 ? Amber : null;
        if (!n.Finished || n.Stopped) return Ring(n.Fraction, color, dot, dotRing);
        var canvas = new Canvas { Width = 14, Height = 14, Children = { CheckCircle() } };
        if (dot is not null) canvas.Children.Add(StatusDot(dot, dotRing));
        return canvas;
    }

    private Button FilterButton(string label, int count, Brush countColor, char state)
    {
        bool sel = _stateFilter == state;
        var b = Btn(Row(6, T(label, 13, sel ? Black : Muted, sel ? Semi : Medium), T($"{count}", 12, count == 0 && state != '*' ? Faint : countColor, Medium)), Ghost,
            () => { _stateFilter = state; _showAll = false; _listOffset = 0; RenderContent(); }, 30);
        if (sel) b.Background = LineSoft;
        b.Margin = new Thickness(0, 0, 2, 0);
        return b;
    }

    private Border RunningRow(PipelineNode node, (int No, string Name, char State, string Note, double Secs) t)
    {
        if (_spinner.Parent is Panel old) old.Children.Remove(_spinner);
        _runningTime = T(Inv($"{node.RunningFor:0.0}s"), 12, Blue, null, mono: true);
        _runningTime.HorizontalAlignment = HorizontalAlignment.Right;
        return Box(Row(14, Box(T($"{t.No}", 11, Blue, null, true), w: 28), _spinner, Grow(T(t.Name, 14, Black, Semi)), T("Running", 12, Blue, Semi), Box(_runningTime, w: 48)),
            BlueSoft, 12, new Thickness(12, 0, 12, 0), h: 46);
    }

    private static Border TestRow((int No, string Name, char State, string Note, double Secs) t)
    {
        var time = T(Inv($"{t.Secs:0.0}s"), 12, Subtle, null, mono: true);
        time.HorizontalAlignment = HorizontalAlignment.Right;
        var note = t.Note.Length > 0 ? T(t.Note, 12, t.State == 'f' ? RedText : AmberText, null, mono: true) : null;
        return Box(Row(14, Box(T($"{t.No}", 11, Subtle, null, true), w: 28), Status(t.State), Grow(T(t.Name, 14, Body)), note, Box(time, w: 48)),
            pad: new Thickness(12, 0, 12, 0), h: 42);
    }

    // ── Log panel (screen 4) ───────────────────────────────────────────────

    private DockPanel LogPanel()
    {
        var handle = Box(bg: TrackDark, radius: 2, w: 36, h: 4);
        handle.Margin = new Thickness(0, 8, 0, 0);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        var names = _nodes.Select(n => n.Name).ToList();
        if (_nodes.Count > 1) names.Add("All");
        for (int i = 0; i < names.Count; i++)
        {
            int index = i == _nodes.Count ? -1 : i;
            bool sel = _logTab == index;
            var b = Btn(T(names[i], 12, sel ? Black : Muted, sel ? Semi : Medium), Ghost, () => { _logTab = index; RenderContent(); }, 24, new Thickness(10, 0, 10, 0));
            if (sel) b.Background = White;
            if (sel) b.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Opacity = 0.08 };
            tabs.Children.Add(b);
        }
        var segmented = Box(tabs, Chip, 9, new Thickness(3));

        bool live = _screen == Screen.Running;
        var status = Box(Row(6, Dot(6, live ? Blue : Faint), T(live ? "Live" : "Done", 12, live ? Blue : Subtle, Semi)), live ? BlueChip : Chip, 13, new Thickness(10, 0, 10, 0), h: 26);
        var bar = Row(12, Row(8, Ui.Icon(TerminalIcon, Ink, 14), T("Logs", 13, Black, Semi)), _nodes.Count > 1 ? segmented : null, null, status,
            Btn(Ui.Icon(CopyIcon, null, 13), Ghost, () => CopyText(string.Join(Environment.NewLine, VisibleLog().Select(l => $"{l.Time}  {LogTag(l.Kind).Text,-5} {l.Text} {l.Dim}".TrimEnd()))), 28, new Thickness(0), "Copy"),
            Btn(Ui.Icon(CloseIcon, null, 12, 1.4), Ghost, ToggleLogs, 28, new Thickness(0), "Close"));
        foreach (var b in bar.Children.OfType<Button>()) b.Width = 28;
        var barBox = new Border { Child = bar, Padding = new Thickness(20, 8, 14, 12), BorderBrush = LineSoft, BorderThickness = new Thickness(0, 0, 0, 1) };

        var lines = new StackPanel { Margin = new Thickness(8) };
        var visible = VisibleLog().TakeLast(300).ToList();
        for (int i = 0; i < visible.Count; i++)
        {
            var l = visible[i];
            var (tag, tagColor) = LogTag(l.Kind);
            bool warn = l.Kind == 'W', fail = l.Kind == 'F';
            var tagText = T(tag, 12, tagColor, l.Kind is 'P' or 'R' or 'F' or 'W' ? Semi : Regular, mono: true);
            var text = Rich((_logTab == -1 ? $"[{_nodes[l.Node].Name}] " : "") + l.Text, warn ? WarnText : Body, l.Dim, Subtle);
            UIElement message = text;
            if (live && i == visible.Count - 1)
            {
                var cursor = Box(bg: Blue, radius: 1, w: 7, h: 14);
                cursor.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.6)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
                message = Row(6, text, cursor);
            }
            lines.Children.Add(Box(Row(14, T(l.Time, 12, Faint, null, mono: true), Box(tagText, w: 40), Grow((FrameworkElement)message)),
                warn ? WarnRow : fail ? RedSoft : null, 6, new Thickness(12, 3, 12, 3)));
        }
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = lines };
        scroll.Loaded += (_, _) => scroll.ScrollToEnd();

        var panel = new DockPanel();
        DockPanel.SetDock(handle, Dock.Top);
        DockPanel.SetDock(barBox, Dock.Top);
        panel.Children.Add(handle);
        panel.Children.Add(barBox);
        panel.Children.Add(scroll);
        return panel;
    }

    private IEnumerable<(int Node, string Time, char Kind, string Text, string Dim)> VisibleLog() =>
        _log.Where(l => _logTab == -1 || l.Node == _logTab);

    private static (string Text, Brush Color) LogTag(char kind) => kind switch
    {
        'P' => ("PASS", Green),
        'R' => ("RUN", Blue),
        'F' => ("FAIL", RedText),
        'W' => ("WARN", WarnTag),
        '>' => ("→", Faint),
        '<' => ("←", Faint),
        _ => ("…", Faint),
    };

    // ════════════════════════════════════════════════════════════════════════
    // Actions
    // ════════════════════════════════════════════════════════════════════════

    private void PickProjectFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Open project folder" };
        if (dialog.ShowDialog(this) == true) OpenProject(dialog.FolderName);
    }

    /// <summary>Reads the folder into the sidebar tree and fills every field it can match.</summary>
    private void OpenProject(string root)
    {
        _projectRoot = root;
        _projectName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        _tree.Clear();
        Scan(root, "");
        IndexTree();
        Array.Clear(_slotValues);

        var tests = _tree.FirstOrDefault(t => t.Dir && t.Rel.Count(c => c == '/') < 2 && FileName(t.Rel).ToLowerInvariant() is "tests" or "test" or "__tests__" or "spec" or "e2e");
        if (tests.Rel is not null) Assign(0, Item(tests), render: false);
        foreach (var rel in new[] { "package.json", "src/index.ts", "src/index.tsx", "src/index.js", "index.ts", "index.js", "Dockerfile" })
        {
            var e = _tree.FirstOrDefault(t => !t.Dir && t.Rel.Equals(rel, StringComparison.OrdinalIgnoreCase));
            if (e.Rel is not null && _slotValues[SlotFor(FileName(e.Rel), false)] is null) Assign(SlotFor(FileName(e.Rel), false), Item(e), render: false);
        }
        var yml = _tree.FirstOrDefault(t => !t.Dir && t.Rel.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase) && SlotFor(t.Rel, false) == 4);
        if (yml.Rel is not null) Assign(4, Item(yml), render: false);

        _expanded.Clear();
        if (tests.Rel is not null) _expanded.Add(tests.Rel);
        RenderAll();
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
                _tree.Add((r, true, 0));
                Scan(d, r);
            }
            foreach (var f in Directory.EnumerateFiles(dir).Order(StringComparer.OrdinalIgnoreCase))
                _tree.Add((rel.Length == 0 ? Path.GetFileName(f) : $"{rel}/{Path.GetFileName(f)}", false, new FileInfo(f).Length));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Unreadable folders are simply left out of the tree.
        }
    }

    /// <summary>The demo project from the design (acme-storefront, 42 files).</summary>
    private void LoadSample(bool single)
    {
        _projectRoot = null;
        _projectName = "acme-storefront";
        _sampleSingle = single;
        _tree.Clear();
        void Add(string rel, long size = -1) => _tree.Add((rel, size < 0, Math.Max(size, 0))); // no size = folder
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

        Array.Clear(_slotValues);
        Assign(0, Item(_tree[0]), render: false);
        foreach (var rel in new[] { "package.json", "src/index.ts", "Dockerfile" })
        {
            var e = _tree.First(t => t.Rel == rel);
            Assign(SlotFor(FileName(rel), false), Item(e), render: false);
        }
        _expanded.Clear();
        _expanded.UnionWith(["tests", "tests/node-18", "tests/node-20"]);
        RenderAll();
    }

    private void IndexTree()
    {
        _kids.Clear();
        foreach (var e in _tree.OrderBy(e => !e.Dir).ThenBy(e => FileName(e.Rel), StringComparer.OrdinalIgnoreCase))
        {
            int cut = e.Rel.LastIndexOf('/');
            string parent = cut < 0 ? "" : e.Rel[..cut];
            if (!_kids.TryGetValue(parent, out var list)) _kids[parent] = list = [];
            list.Add(e);
        }
        _specCount = 0;
    }

    private void Browse(int slot)
    {
        string? path;
        if (Slots[slot].Folder)
        {
            var dialog = new OpenFolderDialog { Title = "Choose the test folder", InitialDirectory = _projectRoot ?? "" };
            path = dialog.ShowDialog(this) == true ? dialog.FolderName : null;
        }
        else
        {
            string filter = slot switch
            {
                1 => "package.json|package.json",
                2 => "Source files|*.ts;*.tsx;*.js",
                3 => "Dockerfile|Dockerfile*",
                _ => "Pipeline config|*.yml;*.yaml",
            };
            var dialog = new OpenFileDialog { Title = $"Choose {Slots[slot].Title.ToLowerInvariant()}", Filter = filter, InitialDirectory = _projectRoot ?? "" };
            path = dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }
        if (path is not null) Assign(slot, External(path));
    }

    /// <summary>Puts a file/folder into a field and builds its detail line ("Project manifest · 264 B").</summary>
    private void Assign(int slot, (string Name, bool Dir, long Size, string? Full, string? Rel) item, bool render = true)
    {
        if (slot < 0) return;
        string detail;
        if (slot == 0)
        {
            _specCount = item.Rel is not null && _tree.Any(t => t.Rel == item.Rel)
                ? _tree.Count(t => !t.Dir && t.Rel.StartsWith(item.Rel + "/", StringComparison.Ordinal) && IsSpec(t.Rel))
                : CountSpecs(item.Full);
            detail = $"Test folder · {_specCount} specs";
        }
        else
        {
            string folder = item.Rel is { } rel && rel.Contains('/') ? $"{rel[..rel.LastIndexOf('/')]}/ · " : "";
            detail = slot switch
            {
                2 => $"Source file · {folder}{Size(item.Size)}",
                4 => $"Pipeline config · {folder}{Size(item.Size)}",
                _ => $"{Slots[slot].Title} · {Size(item.Size)}",
            };
        }
        _slotValues[slot] = (item.Name, detail, item.Full, item.Rel);
        if (render) RenderAll();
    }

    private async void FetchX()
    {
        _xState = 1;
        RenderContent();
        await Task.Delay(1200); // stand-in for the call to the X interface
        _xState = 2;
        _xLoadedAt = DateTime.Now;
        RenderContent();
    }

    private void StartRun()
    {
        if (MissingCount() > 0) return;
        _nodes = PlanNodes();
        _runNo++;
        _sel = _exportState = 0;
        _logTab = 0;
        _stateFilter = '*';
        _showAll = _flashed = false;
        _listOffset = 0;
        _log.Clear();
        _startedAt = DateTime.Now;
        _elapsed = TimeSpan.Zero;
        _screen = Screen.Running;
        _timer.Start();
        RenderAll();
    }

    /// <summary>Sample → nodes from the design. Real folder → one node per sub-folder of the test folder (multi mode), else one node.</summary>
    private List<PipelineNode> PlanNodes()
    {
        if (_projectRoot is null) return PipelineNode.Sample(_sampleSingle);
        string? tests = _slotValues[0]?.Full;
        if (tests is null || !Directory.Exists(tests)) return [PipelineNode.ForFolder("Tests", _specCount, 1)];
        var subs = Directory.EnumerateDirectories(tests).Where(d => !IgnoredDirs.Contains(Path.GetFileName(d))).Order().ToList();
        if (subs.Count < 2) return [PipelineNode.ForFolder(Path.GetFileName(tests), _specCount, 1)];
        return subs.Take(6).Select((d, i) => PipelineNode.ForFolder(Path.GetFileName(d), CountSpecs(d), i + 1)).ToList();
    }

    private void OnTick()
    {
        if (_screen != Screen.Running) return;
        _elapsed = DateTime.Now - _startedAt;
        string time = Clock(_elapsed, withHundredths: true);
        for (int i = 0; i < _nodes.Count; i++)
        {
            int node = i;
            _nodes[i].Tick(0.1, _rnd, (kind, text, dim) => _log.Add((node, time, kind, text, dim)));
        }
        if (_log.Count > 2000) _log.RemoveRange(0, 500);

        if (_nodes.All(n => n.Finished))
        {
            Finish();
            return;
        }
        if (_nodes.Sum(n => n.Version) != _renderedVersion)
        {
            RenderContent();
            UpdateTaskbar();
        }
        // Live labels without a rebuild.
        if (_timerText is not null) _timerText.Text = Clock(_elapsed);
        if (_runningTime is not null) _runningTime.Text = Inv($"{_nodes[_sel].RunningFor:0.0}s");
        if (_etaText is not null) _etaText.Text = Eta(_nodes[_sel]);
    }

    private void StopAll()
    {
        foreach (var n in _nodes) n.Stop();
        Finish();
    }

    private void Finish()
    {
        _timer.Stop();
        _screen = Screen.Finished;
        RenderAll();
        if (!IsActive && !_flashed) Flash();
        _flashed = true;
    }

    private void NewRun()
    {
        _timer.Stop();
        _screen = Screen.Setup;
        _nodes = [];
        _logsOpen = false;
        RenderAll();
    }

    private void ToggleLogs()
    {
        if (_screen == Screen.Setup) return;
        _logsOpen = !_logsOpen;
        RenderHeader();
        RenderContent();
    }

    private async void ExportToX()
    {
        _exportState = 1;
        RenderHeader();
        await Task.Delay(1000); // stand-in for the upload to X
        _exportState = 2;
        RenderHeader();
        RenderContent();
    }

    private void SaveJson()
    {
        var dialog = new SaveFileDialog { FileName = $"run-{_runNo}.json", Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        var data = new
        {
            run = _runNo, project = _projectName, duration = Clock(_elapsed),
            nodes = _nodes.Select(n => new
            {
                name = n.Name, runtime = n.Runtime, passed = n.Passed, failed = n.Failed, aborted = n.Aborted, notRun = n.Queued,
                tests = n.Tests.Select(t => new { no = t.No, name = t.Name, state = t.State switch { 'p' => "passed", 'f' => "failed", 'a' => "aborted", _ => "not run" }, note = t.Note, seconds = t.Secs }),
            }),
        };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(data, JsonOptions));
    }

    private string Summary() => $"Conduit · Run #{_runNo} · {_projectName}{Environment.NewLine}" + string.Join(Environment.NewLine,
        _nodes.Select(n => $"{n.Name}: {n.Passed}/{n.Total} passed, {n.Failed} failed, {n.Aborted} aborted"));

    private static void CopyText(string text)
    {
        try { Clipboard.SetText(text); }
        catch (COMException) { /* clipboard busy – user can retry */ }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Windows taskbar (screen 7): native progress bar, overlay icon, preview buttons, flash
    // ════════════════════════════════════════════════════════════════════════

    private ThumbButtonInfo? _thumbStop, _thumbLogs, _thumbExport;

    private void UpdateTaskbar()
    {
        var tb = TaskbarItemInfo;
        if (_thumbStop is null)
        {
            _thumbStop = new ThumbButtonInfo { ImageSource = ThumbIcon("M3,3 H11 V11 H3 Z", RedText, filled: true), Description = "Stop" };
            _thumbLogs = new ThumbButtonInfo { ImageSource = ThumbIcon(TerminalIcon, Ink), Description = "Logs" };
            _thumbExport = new ThumbButtonInfo { ImageSource = ThumbIcon(UploadIcon, Ink), Description = "Export to X" };
            _thumbStop.Click += (_, _) => StopAll();
            _thumbLogs.Click += (_, _) => { Activate(); if (!_logsOpen) ToggleLogs(); };
            _thumbExport.Click += (_, _) => ExportToX();
            tb.ThumbButtonInfos = new ThumbButtonInfoCollection { _thumbStop, _thumbLogs, _thumbExport };
        }
        _thumbStop.Visibility = _screen == Screen.Running ? Visibility.Visible : Visibility.Collapsed;
        _thumbLogs!.Visibility = _screen == Screen.Setup ? Visibility.Collapsed : Visibility.Visible;
        _thumbExport!.Visibility = _screen == Screen.Finished ? Visibility.Visible : Visibility.Collapsed;

        if (_screen == Screen.Setup)
        {
            tb.ProgressState = TaskbarItemProgressState.None;
            SetOverlay("", null);
            return;
        }
        int failed = _nodes.Sum(n => n.Failed);
        if (_screen == Screen.Finished)
        {
            tb.ProgressState = TaskbarItemProgressState.None;
            SetOverlay($"done{failed}", failed > 0 ? BadgeOverlay(failed) : CheckOverlay());
            return;
        }
        // Native bar = total progress. Multi: per-node bars in the overlay (variant A). Single: overlay only on failures.
        tb.ProgressState = TaskbarItemProgressState.Normal;
        tb.ProgressValue = (double)_nodes.Sum(n => n.Done) / Math.Max(1, _nodes.Sum(n => n.Total));
        if (_nodes.Count > 1)
            SetOverlay("bars" + string.Join(",", _nodes.Select(n => $"{(int)(n.Fraction * 12)}{n.Failed + n.Aborted}")), BarsOverlay(_nodes));
        else
            SetOverlay($"fail{failed}", failed > 0 ? BadgeOverlay(failed) : null);
    }

    /// <summary>Only swaps the overlay when it really changed – Windows redraws it on every set.</summary>
    private void SetOverlay(string key, ImageSource? image)
    {
        if (key == _overlayKey) return;
        _overlayKey = key;
        TaskbarItemInfo.Overlay = image;
    }

    private void Flash()
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = new WindowInteropHelper(this).Handle,
            dwFlags = 2 | 12, // FLASHW_TRAY | FLASHW_TIMERNOFG: flash the taskbar button until the window is focused
        };
        _ = FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ════════════════════════════════════════════════════════════════════════
    // Small helpers
    // ════════════════════════════════════════════════════════════════════════

    private const string RelFormat = "Conduit.RelativePath";

    private int MissingCount() => _slotValues.Count(v => v is null);

    /// <summary>Which field a file belongs to: 0 test folder, 1 package.json, 2 source, 3 Dockerfile, 4 yml; -1 none.</summary>
    private static int SlotFor(string name, bool dir)
    {
        if (dir) return 0;
        name = FileName(name);
        if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) return 4;
        if (name.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) return 2;
        return -1;
    }

    /// <summary>Everything in a drag: our own tree items and/or real files from Explorer.</summary>
    private List<(string Name, bool Dir, long Size, string? Full, string? Rel)> Dropped(IDataObject data)
    {
        var items = new List<(string, bool, long, string?, string?)>();
        if (data.GetData(RelFormat) is string rel)
        {
            var e = _tree.FirstOrDefault(t => t.Rel == rel);
            if (e.Rel is not null) items.Add(Item(e));
        }
        else if (data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            items.AddRange(paths.Select(External));
        }
        return items;
    }

    private (string Name, bool Dir, long Size, string? Full, string? Rel) Item((string Rel, bool Dir, long Size) e) =>
        (FileName(e.Rel), e.Dir, e.Size, FullPath(e.Rel), e.Rel);

    private (string Name, bool Dir, long Size, string? Full, string? Rel) External(string path)
    {
        bool dir = Directory.Exists(path);
        string? rel = _projectRoot is not null && path.StartsWith(_projectRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(_projectRoot, path).Replace('\\', '/') : null;
        return (Path.GetFileName(path), dir, dir ? 0 : new FileInfo(path).Length, path, rel);
    }

    private string? FullPath(string rel) => _projectRoot is null ? null : Path.Combine(_projectRoot, rel.Replace('/', Path.DirectorySeparatorChar));

    private static int CountSpecs(string? dir)
    {
        if (dir is null || !Directory.Exists(dir)) return 0;
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Take(5000).Count(IsSpec); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return 0; }
    }

    private static bool IsSpec(string path) =>
        path.Contains(".spec.", StringComparison.OrdinalIgnoreCase) || path.Contains(".test.", StringComparison.OrdinalIgnoreCase);

    private static string FileName(string rel) => rel[(rel.LastIndexOfAny(['/', '\\']) + 1)..];

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => Inv($"{bytes / 1024.0:0.#} KB"),
        _ => Inv($"{bytes / 1048576.0:0.#} MB"),
    };

    private static string Clock(TimeSpan t, bool withHundredths = false) => withHundredths
        ? Inv($"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}")
        : Inv($"{(int)t.TotalMinutes}:{t.Seconds:00}");

    private string Eta(PipelineNode n)
    {
        if (n.Done == 0) return "estimating…";
        var left = TimeSpan.FromSeconds(_elapsed.TotalSeconds / n.Done * (n.Total - n.Done));
        return $"~{Clock(left)} left";
    }
}
