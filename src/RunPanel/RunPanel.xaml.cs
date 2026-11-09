using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace TestRunUi;

// ════════════════════════════════════════════════════════════════════════════
// RunPanel – the two boxes of a test run as one control.
//
//   <local:RunPanel x:Name="Panel" />
//
//   var run = Panel.Session;                       // fill it from your own runner, on the UI thread:
//   var payments = run.AddNode("Payments", "Node 18");
//   var test = payments.AddTest("charges a saved card");   // add all tests first …
//   run.Start();                                   // … then start
//   test.Start();  run.Log(payments, LogKind.Run, "#1 charges a saved card");
//   test.Pass();   run.Log(payments, LogKind.Pass, "#1 charges a saved card", "0.8s");
//   test.Fail("expected 200, received 500");       // or test.Abort("runner lost connection")
//
//   One node = a summary line, several nodes = tabs. The run ends by itself when every node has finished.
//   run.StopRequested fires when the user clicks "Stop <node>" – cancel your own work there.
//
// The file has three parts: RunPanel (the control) · RunSession/RunNode/RunTest/RunLine (the data you fill) · helpers.
// ════════════════════════════════════════════════════════════════════════════

/// <summary>The test card (node tabs or summary line, state filters, test list) and the log panel, bound to <see cref="Session"/>.</summary>
public partial class RunPanel : UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(RunSession), typeof(RunPanel), new PropertyMetadata(null, OnSessionChanged));

    /// <summary>What the panel shows. A fresh, empty session exists from the start; assign your own to swap it.</summary>
    public RunSession? Session { get => (RunSession?)GetValue(SessionProperty); set => SetValue(SessionProperty, value); }

    public RunPanel()
    {
        InitializeComponent();
        LogScroll.IsVisibleChanged += (_, _) => ScrollLogToEnd();
        Session = new RunSession();
    }

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (RunPanel)d;
        if (e.OldValue is RunSession old)
        {
            old.VisibleLog.CollectionChanged -= panel.OnLogChanged;
            old.PropertyChanged -= panel.OnSessionChanged;
        }
        if (e.NewValue is RunSession session)
        {
            session.VisibleLog.CollectionChanged += panel.OnLogChanged;
            session.PropertyChanged += panel.OnSessionChanged;
        }
        panel.DataContext = e.NewValue;
    }

    // Scroll behaviour: the test list jumps to the top when you switch node or filter,
    // the log follows new lines only while you are at its end (so you can read in peace).

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RunSession.SelectedNode) or nameof(RunSession.StateFilter)) TestScroll.ScrollToTop();
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        bool atEnd = LogScroll.VerticalOffset >= LogScroll.ScrollableHeight - 24;
        if (atEnd || e.Action == NotifyCollectionChangedAction.Reset) ScrollLogToEnd();
    }

    private void ScrollLogToEnd() => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(LogScroll.ScrollToEnd));

    private void OnStopSelected(object sender, RoutedEventArgs e) => Session?.StopSelected();

    private void OnShowMore(object sender, RoutedEventArgs e)
    {
        if (Session is { } session) session.ShowAll = !session.ShowAll;
    }

    private void OnCopyLog(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session) return;
        try { Clipboard.SetText(session.LogText()); }
        catch (COMException) { /* another app holds the clipboard – the user can simply retry */ }
    }

    private void OnCloseLogs(object sender, RoutedEventArgs e)
    {
        if (Session is { } session) session.LogsOpen = false;
    }
}

// ════════════════════════════════════════════════════════════════════════════
// Small building blocks the XAML needs
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Stroke icon from SVG path data. <see cref="Box"/> is the SVG viewBox size, the element can be any size.
/// Without its own <see cref="Foreground"/> it takes the parent's – inside a button it follows the button's colour.
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

    public Geometry? Data { get => (Geometry?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public double Box { get => (double)GetValue(BoxProperty); set => SetValue(BoxProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Box, Box);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Data is null || Box <= 0) return;
        drawingContext.PushTransform(new ScaleTransform(ActualWidth / Box, ActualHeight / Box));
        var pen = new Pen(Foreground, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        drawingContext.DrawGeometry(null, pen, Data);
        drawingContext.Pop();
    }
}

/// <summary>Attached properties for the templates.</summary>
public static class Look
{
    /// <summary>Corner radius of the flat button template (Button itself has none).</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Look), new FrameworkPropertyMetadata(default(CornerRadius)));

    /// <summary>Clips a Border's content to its rounded corners (WPF borders don't clip). Square bottom corners stay unclipped.</summary>
    public static readonly DependencyProperty ClipToCornersProperty = DependencyProperty.RegisterAttached(
        "ClipToCorners", typeof(bool), typeof(Look), new PropertyMetadata(false, OnClipToCornersChanged));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    public static bool GetClipToCorners(DependencyObject element) => (bool)element.GetValue(ClipToCornersProperty);

    public static void SetClipToCorners(DependencyObject element, bool value) => element.SetValue(ClipToCornersProperty, value);

    private static void OnClipToCornersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border) return;
        border.SizeChanged -= UpdateClip;
        border.Clip = null;
        if ((bool)e.NewValue) border.SizeChanged += UpdateClip;
    }

    private static void UpdateClip(object sender, SizeChangedEventArgs e)
    {
        var border = (Border)sender;
        var r = border.CornerRadius;
        double extra = r.BottomLeft == 0 && r.BottomRight == 0 ? r.TopLeft : 0; // top-only rounding: push the bottom corners out of view
        border.Clip = new RectangleGeometry(new Rect(0, 0, border.ActualWidth, border.ActualHeight + extra), r.TopLeft, r.TopLeft);
    }
}

/// <summary>Visible when the value is "truthy": true, non-null, non-empty text, non-zero number, non-empty list.</summary>
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
            double d => d != 0,
            ICollection c => c.Count > 0,
            _ => true,
        };
        return on != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Number → star column width, for the segmented progress bars (passed | failed | aborted | rest).</summary>
public sealed class StarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new GridLength(Math.Max(0, System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture)), GridUnitType.Star);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>0…1 → pie slice inside the 14 px node ring (centre 7,7, radius 5), starting at 12 o'clock.</summary>
public sealed class PieConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double f = Math.Clamp(System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture), 0, 1);
        if (f < 0.005) return Geometry.Empty;
        if (f > 0.995) return new EllipseGeometry(new Point(7, 7), 5, 5);
        double angle = f * 2 * Math.PI;
        var end = new Point(7 + (5 * Math.Sin(angle)), 7 - (5 * Math.Cos(angle)));
        var figure = new PathFigure(new Point(7, 7),
            [new LineSegment(new Point(7, 2), false), new ArcSegment(end, new Size(5, 5), 0, f > 0.5, SweepDirection.Clockwise, false)], closed: true);
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Value == parameter → true. For radio buttons bound to an enum (ConvertBack returns the parameter when checked).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}

/// <summary>Culture-invariant text helpers (a German PC would otherwise write "2,1s").</summary>
internal static class Fmt
{
    public static string Inv(FormattableString s) => FormattableString.Invariant(s);

    public static string Clock(TimeSpan t, bool withHundredths = false) => withHundredths
        ? Inv($"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}")
        : Inv($"{(int)t.TotalMinutes}:{t.Seconds:00}");
}

// ════════════════════════════════════════════════════════════════════════════
// The data you fill (everything is bound by the XAML; call it on the UI thread)
// ════════════════════════════════════════════════════════════════════════════

public enum TestState { Queued, Running, Passed, Failed, Aborted }

public enum LogKind { Run, Pass, Fail, Warn, Request, Response, Waiting }

public enum TestFilter { All, Passed, Failed, Aborted }

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises the given properties; <c>Raise(string.Empty)</c> refreshes every binding on the object.</summary>
    protected void Raise(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name!);
        return true;
    }
}

/// <summary>One test of a node. Its row in the list updates in place when the state changes.</summary>
public sealed class RunTest : Observable
{
    private readonly RunNode _node;
    private long _startedAt;

    internal RunTest(RunNode node, int no, string name)
    {
        _node = node;
        No = no;
        Name = name;
    }

    public int No { get; }
    public string Name { get; }
    public TestState State { get; private set; }
    public string Note { get; private set; } = "";
    public double Secs { get; private set; }
    public bool IsDone => State is TestState.Passed or TestState.Failed or TestState.Aborted;
    public string TimeText => Fmt.Inv($"{Secs:0.0}s");

    /// <summary>Seconds the test has been running, or its final duration once it is done.</summary>
    public double Elapsed => State == TestState.Running ? Stopwatch.GetElapsedTime(_startedAt).TotalSeconds : Secs;

    /// <summary>The test starts running (one running test per node is shown).</summary>
    public void Start() => Move(TestState.Running, "", null);

    /// <summary>Finished green. <paramref name="secs"/> = duration; without it the measured running time is used.</summary>
    public void Pass(double? secs = null) => Move(TestState.Passed, "", secs);

    /// <summary>Finished red, with the reason shown behind the name ("expected 200, received 500").</summary>
    public void Fail(string note, double? secs = null) => Move(TestState.Failed, note, secs);

    /// <summary>Finished amber: the test could not complete (runner lost, timeout …).</summary>
    public void Abort(string note, double? secs = null) => Move(TestState.Aborted, note, secs);

    internal int FinishOrder { get; private set; }

    internal void Reset() => Move(TestState.Queued, "", null);

    private void Move(TestState state, string note, double? secs)
    {
        double ran = State == TestState.Running ? Stopwatch.GetElapsedTime(_startedAt).TotalSeconds : 0;
        if (state == TestState.Running) _startedAt = Stopwatch.GetTimestamp();
        State = state;
        Note = note;
        if (IsDone)
        {
            Secs = secs ?? ran;
            FinishOrder = _node.NextFinishOrder();
        }
        Raise(string.Empty);
        _node.Changed();
    }
}

/// <summary>One line of the log panel ("00:46.05  RUN  #25 charges a saved card").</summary>
public sealed class RunLine(int node, string nodeName, string time, LogKind kind, string text, string dim) : Observable
{
    private bool _showCursor;

    public int Node { get; } = node;
    public string NodePrefix { get; } = $"[{nodeName}] ";
    public string Time { get; } = time;
    public LogKind Kind { get; } = kind;
    public string Text { get; } = text;
    public string Dim { get; } = dim;
    public string DimText => Dim.Length > 0 ? " " + Dim : "";

    public string Tag => Kind switch
    {
        LogKind.Pass => "PASS",
        LogKind.Run => "RUN",
        LogKind.Fail => "FAIL",
        LogKind.Warn => "WARN",
        LogKind.Request => "→",
        LogKind.Response => "←",
        _ => "…",
    };

    /// <summary>Blinking cursor behind the newest line while the run is live.</summary>
    public bool ShowCursor { get => _showCursor; set => Set(ref _showCursor, value); }

    public override string ToString() => $"{Time}  {Tag,-5} {Text} {Dim}".TrimEnd();
}

/// <summary>One pipeline node (e.g. "Payments" on Node 18) with its tests – a tab in multi mode.</summary>
public sealed class RunNode : Observable
{
    private readonly RunSession _session;
    private readonly List<RunTest> _tests = [];
    private int _passed, _failed, _aborted, _queued, _finishCounter;
    private RunTest? _running;

    internal RunNode(RunSession session, string name, string runtime)
    {
        _session = session;
        Name = name;
        Runtime = runtime;
    }

    public string Name { get; }
    public string Runtime { get; }
    public IReadOnlyList<RunTest> Tests => _tests;
    public bool Stopped { get; private set; }

    public int Passed => _passed;
    public int Failed => _failed;
    public int Aborted => _aborted;
    public int Queued => _queued;
    public int Done => _passed + _failed + _aborted;
    public int Total => _tests.Count;
    public int Rest => Total - Done;

    /// <summary>All tests ran, or the node was stopped.</summary>
    public bool Finished => Stopped || (Total > 0 && Done == Total);

    public double Fraction => Total == 0 ? 0 : (double)Done / Total;

    /// <summary>All tests ran (check icon); a stopped node keeps its progress ring.</summary>
    public bool IsComplete => Finished && !Stopped;

    public bool HasFailed => Failed > 0;
    public bool HasAborted => Aborted > 0;
    public string ProgressText => $"{Done}/{Total}";
    public RunTest? RunningTest => _running;
    public string RunningForText => Fmt.Inv($"{_running?.Elapsed ?? 0:0.0}s");

    /// <summary>Adds a test (numbered 1, 2, 3 … in the order added). Add them all before <see cref="RunSession.Start"/>.</summary>
    public RunTest AddTest(string name)
    {
        var test = new RunTest(this, _tests.Count + 1, name);
        _tests.Add(test);
        Changed();
        return test;
    }

    /// <summary>Stops the node: the running test goes back to the queue and the rest is never run.</summary>
    public void Stop()
    {
        if (Finished) return;
        _running?.Reset();
        Stopped = true;
        Changed();
    }

    internal int NextFinishOrder() => ++_finishCounter;

    internal void Tick() => Raise(nameof(RunningForText));

    internal void Changed()
    {
        int passed = 0, failed = 0, aborted = 0, queued = 0;
        RunTest? running = null;
        foreach (var t in _tests)
        {
            switch (t.State)
            {
                case TestState.Passed: passed++; break;
                case TestState.Failed: failed++; break;
                case TestState.Aborted: aborted++; break;
                case TestState.Running: running ??= t; break;
                default: queued++; break;
            }
        }
        (_passed, _failed, _aborted, _queued, _running) = (passed, failed, aborted, queued, running);
        Raise(string.Empty);
        _session.NodeChanged();
    }
}

/// <summary>
/// Everything the panel shows: the nodes, the selected node and filter, the log. Fill it from your runner;
/// the panel only reads it (the few things the user can do come back as methods/events).
/// </summary>
public sealed class RunSession : Observable
{
    private const int Preview = 5;         // finished tests shown before "Show N earlier tests"
    private const int MaxVisibleLog = 300; // lines kept in the panel
    private const int MaxLog = 2000;       // lines kept in memory

    private readonly ObservableCollection<RunNode> _nodes = [];
    private readonly List<RunLine> _log = [];
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer;
    private RunNode? _selected;
    private TestFilter _filter;
    private bool _showAll, _logsOpen, _running;
    private int _logTab, _doneCount;

    public RunSession()
    {
        Nodes = new ReadOnlyObservableCollection<RunNode>(_nodes);
        // Keeps "Running 2.1s" and the "~0:12 left" text moving between test events.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) =>
        {
            foreach (var node in _nodes) node.Tick();
            Raise(nameof(HeaderRightText));
        };
    }

    // ── What the host does ─────────────────────────────────────────────────

    /// <summary>Raised when the user stops a node ("Stop Payments"). The node is already marked stopped – cancel your own work.</summary>
    public event EventHandler<RunNode>? StopRequested;

    /// <summary>Raised once, when every node has finished (or <see cref="Finish"/> was called).</summary>
    public event EventHandler? RunFinished;

    /// <summary>Adds a node. One node shows a summary line, several show tabs and an "All" log tab.</summary>
    public RunNode AddNode(string name, string runtime = "")
    {
        var node = new RunNode(this, name, runtime);
        _nodes.Add(node);
        _selected ??= node;
        LogTabs = IsMulti ? [.. _nodes.Select(n => n.Name), "All"] : [.. _nodes.Select(n => n.Name)];
        Raise(nameof(LogTabs), nameof(LogTab), nameof(IsMulti), nameof(ModeText), nameof(SelectedNode));
        RefreshList();
        return node;
    }

    /// <summary>Starts the clock and the "Live" state. Add the nodes and tests first.</summary>
    public void Start()
    {
        if (_running) return;
        _running = true;
        _clock.Restart();
        _timer.Start();
        RebuildLog();
        Raise(nameof(IsRunning), nameof(HeaderRightText));
        RefreshList();
    }

    /// <summary>Ends the run (called by itself when every node has finished).</summary>
    public void Finish()
    {
        if (!_running) return;
        _running = false;
        _clock.Stop();
        _timer.Stop();
        foreach (var line in VisibleLog) line.ShowCursor = false;
        Raise(string.Empty);
        RefreshList();
        RunFinished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Empties the panel for the next run.</summary>
    public void Clear()
    {
        _running = false;
        _timer.Stop();
        _clock.Reset();
        _nodes.Clear();
        _log.Clear();
        _selected = null;
        _filter = TestFilter.All;
        _showAll = false;
        _logTab = 0;
        LogTabs = [];
        RebuildLog();
        Raise(string.Empty);
        RefreshList();
    }

    /// <summary>Adds a line to the log. <paramref name="dim"/> is the grey part behind the text (a duration, an id).</summary>
    public void Log(RunNode node, LogKind kind, string text, string dim = "")
    {
        int index = _nodes.IndexOf(node);
        if (index < 0) throw new ArgumentException("The node does not belong to this session.", nameof(node));
        var line = new RunLine(index, node.Name, Fmt.Clock(_clock.Elapsed, withHundredths: true), kind, text, dim);
        _log.Add(line);
        if (_log.Count > MaxLog) _log.RemoveRange(0, 500);
        if (!IsAllLogs && line.Node != _logTab) return;
        if (VisibleLog.Count > 0) VisibleLog[^1].ShowCursor = false;
        line.ShowCursor = _running;
        VisibleLog.Add(line);
        if (VisibleLog.Count > MaxVisibleLog) VisibleLog.RemoveAt(0);
    }

    /// <summary>The visible log as plain text (what the copy button puts on the clipboard).</summary>
    public string LogText() => string.Join(Environment.NewLine, VisibleLog);

    /// <summary>Stops the selected node; also what the red "Stop Payments" button does.</summary>
    public void StopSelected()
    {
        if (_selected is not { Finished: false } node) return;
        node.Stop();
        StopRequested?.Invoke(this, node);
    }

    // ── What the panel binds to ────────────────────────────────────────────

    public ReadOnlyObservableCollection<RunNode> Nodes { get; }

    /// <summary>Finished tests of the selected node, newest first, filtered – rows are kept and moved, never rebuilt.</summary>
    public ObservableCollection<RunTest> VisibleTests { get; } = [];

    public ObservableCollection<RunLine> VisibleLog { get; } = [];
    public IReadOnlyList<string> LogTabs { get; private set; } = [];

    public bool IsRunning => _running;
    public bool IsMulti => _nodes.Count > 1;
    public string ModeText => IsMulti ? $"Multi mode · {_nodes.Count} nodes in parallel" : "Single mode · 1 node";

    /// <summary>Whether the log panel under the test card is open (the panel's close button sets it to false).</summary>
    public bool LogsOpen { get => _logsOpen; set => Set(ref _logsOpen, value); }

    public RunNode? SelectedNode
    {
        get => _selected;
        set
        {
            // The tab strip pushes null while its items are being replaced – keep the current node then.
            if (value is null || !Set(ref _selected, value)) return;
            _filter = TestFilter.All;
            _showAll = false;
            Raise(nameof(StateFilter), nameof(ShowAll));
            RefreshList();
        }
    }

    public TestFilter StateFilter
    {
        get => _filter;
        set
        {
            if (!Set(ref _filter, value)) return;
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
    public RunTest? RunningTest => StateFilter == TestFilter.All ? _selected?.RunningTest : null;

    public bool HasMore => _doneCount > Preview;
    public string ShowMoreText => ShowAll ? "Show less" : $"Show {_doneCount - Preview} earlier tests";

    public string EmptyText => VisibleTests.Count > 0 || RunningTest is not null ? ""
        : !_running && _selected?.Total == 0 ? "No tests."
        : StateFilter == TestFilter.All ? "Starting…" : "Nothing here.";

    public string QueueText => _selected is not { } n ? ""
        : n.Stopped && n.Queued > 0 ? $"Stopped · {n.Queued} not run"
        : _running ? $"{n.Queued} queued"
        : $"All {n.Done} tests ran";

    public bool CanStopSelected => _running && IsMulti && _selected is { Finished: false };
    public string StopSelectedText => $"Stop {_selected?.Name}";

    // Summary line of the single-node card header.
    public int HeaderCount => _selected is not { } n ? 0 : _running ? n.Done : n.Passed;
    public string HeaderCountRest => _selected is not { } n ? "" : _running ? $" of {n.Total} tests" : $" of {n.Total} passed";

    public string HeaderRightText => _running ? Eta(_selected) : _selected is { Stopped: true } ? "Stopped" : "Finished";

    /// <summary>Selected log tab: a node's index, or the last one ("All") in multi mode.</summary>
    public int LogTab
    {
        get => _logTab;
        set
        {
            if (value >= 0 && Set(ref _logTab, value)) RebuildLog();
        }
    }

    /// <summary>The "All" tab: every line gets its node name in front.</summary>
    public bool IsAllLogs => IsMulti && _logTab == _nodes.Count;

    // ── Internals ──────────────────────────────────────────────────────────

    /// <summary>A test started/finished somewhere in a node: refresh the list, and end the run when all nodes are done.</summary>
    internal void NodeChanged()
    {
        RefreshList();
        if (_running && _nodes.All(n => n.Finished)) Finish();
    }

    private void RefreshList()
    {
        var done = _selected is null ? [] : _selected.Tests.Where(t => t.IsDone && Matches(t)).OrderByDescending(t => t.FinishOrder).ToList();
        _doneCount = done.Count;
        Sync(VisibleTests, ShowAll ? done : done.Take(Preview).ToList());
        Raise(nameof(RunningTest), nameof(HasMore), nameof(ShowMoreText), nameof(EmptyText), nameof(QueueText),
            nameof(CanStopSelected), nameof(StopSelectedText), nameof(HeaderCount), nameof(HeaderCountRest), nameof(HeaderRightText));
    }

    private bool Matches(RunTest t) => StateFilter switch
    {
        TestFilter.Passed => t.State == TestState.Passed,
        TestFilter.Failed => t.State == TestState.Failed,
        TestFilter.Aborted => t.State == TestState.Aborted,
        _ => true,
    };

    private void RebuildLog()
    {
        foreach (var line in VisibleLog) line.ShowCursor = false;
        VisibleLog.Clear();
        foreach (var line in _log.Where(l => IsAllLogs || l.Node == _logTab).TakeLast(MaxVisibleLog)) VisibleLog.Add(line);
        if (_running && VisibleLog.Count > 0) VisibleLog[^1].ShowCursor = true;
        Raise(nameof(IsAllLogs));
    }

    private string Eta(RunNode? n)
    {
        if (n is null || n.Done == 0) return "estimating…";
        if (n.Finished) return n.Stopped ? "stopped" : "done";
        var left = TimeSpan.FromSeconds(_clock.Elapsed.TotalSeconds / n.Done * (n.Total - n.Done));
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
}

#if RUNPANEL_DEMO
// ════════════════════════════════════════════════════════════════════════════
// Demo (only in this project): three nodes with a fake runner, so you can see every state of the panel.
// ════════════════════════════════════════════════════════════════════════════

internal static class Demo
{
    private sealed class FakeRunner(RunNode node, double speed)
    {
        public RunNode Node { get; } = node;
        public double Speed { get; } = speed;
        public double Target { get; set; }
    }

    [STAThread]
    public static void Main()
    {
        var app = new Application(); // must exist before the first XAML control (it registers the pack:// resources)
        var panel = new RunPanel();
        var run = panel.Session!;
        var rnd = new Random(7);

        string[] verbs = ["creates a", "updates a", "validates the", "cancels a", "lists every"];
        string[] nouns = ["customer", "invoice", "payout", "subscription", "coupon", "dispute", "tax rate", "refund", "price", "mandate"];
        var runners = new List<FakeRunner>();
        foreach (var (name, runtime, count, speed) in new[] { ("Payments", "Node 18", 50, 1.0), ("Formatting", "Node 20", 60, 1.4), ("Queue worker", "Node 22", 50, 0.8) })
        {
            var node = run.AddNode(name, runtime);
            for (int i = 0; i < count; i++) node.AddTest($"{verbs[(i / nouns.Length) % verbs.Length]} {nouns[i % nouns.Length]}");
            runners.Add(new FakeRunner(node, speed));
        }

        run.LogsOpen = true;
        run.Start();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            foreach (var r in runners)
            {
                if (r.Node.Finished) continue;
                if (r.Node.RunningTest is { } current)
                {
                    if (current.Elapsed < r.Target) continue;
                    string label = $"#{current.No} {current.Name}";
                    if (current.No % 17 == 0)
                    {
                        current.Fail("expected 200, received 500");
                        run.Log(r.Node, LogKind.Fail, label, "expected 200, received 500");
                    }
                    else if (current.No % 23 == 0)
                    {
                        current.Abort("runner lost connection", secs: 30);
                        run.Log(r.Node, LogKind.Warn, $"{label} aborted: runner lost connection");
                    }
                    else
                    {
                        current.Pass();
                        run.Log(r.Node, LogKind.Pass, label, current.TimeText);
                    }
                    if (r.Node.Finished) continue;
                }
                if (r.Node.Tests.FirstOrDefault(t => t.State == TestState.Queued) is not { } next) continue;
                next.Start();
                r.Target = (0.3 + (rnd.NextDouble() * 1.4)) / r.Speed;
                run.Log(r.Node, LogKind.Run, $"#{next.No} {next.Name}");
                if (next.No % 6 == 0) run.Log(r.Node, LogKind.Request, "POST /v1/charges", "{ amount: 1299, currency: \"eur\" }");
            }
        };
        run.RunFinished += (_, _) => timer.Stop();
        timer.Start();

        app.Run(new Window { Title = "RunPanel", Width = 1000, Height = 780, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel });
    }
}
#endif
