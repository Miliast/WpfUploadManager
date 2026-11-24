// StemScroller – the scrolling area of the "What's new" page: a ScrollViewer that (1) lets the stem of the sunflower
// grow while you scroll and keeps the date of the release being read in view, and (2) makes the mouse wheel glide
// instead of jump. The page it scrolls is written inside it in WhatsNewWindow.xaml, like inside any ScrollViewer.
//
// One file on purpose, no StemScroller.xaml: the page inside has named parts (Timeline, CurrentVersion …), and WPF
// refuses names inside a control that has its own XAML file (error MC3093). So the few settings a XAML file would
// hold are set in the constructor below.
//
// Everything it needs is in this file. The window only has to use it instead of a ScrollViewer:
//     <local:StemScroller x:Name="Scroller"> … the page … </local:StemScroller>
// It finds the list of releases by itself (the ItemsControl named "Timeline" inside it) and updates the stem by
// itself: when the window has loaded, on every scroll step and when releases are added or change their height.
// In the template of a release it looks for four named parts: Pin (date block), Node (leaf), NodeOn (green leaf),
// Trail (green piece of the rail) – see the template "Release" in WhatsNewWindow.xaml.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Conduit;

public class StemScroller : ScrollViewer
{
    private readonly List<StemParts> _stems = []; // the moving parts of every release in the timeline, in order

    /// <summary>The list of releases whose stem this scroller moves. Found by its name when not set.</summary>
    public ItemsControl? Timeline { get; set; }

    private bool _retryQueued; // a release was not laid out yet; one more try is already waiting

    public StemScroller()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Focusable = false;

        ScrollChanged += OnScrolled;             // moves the stem: pinned date, travelling leaf, green trail
        PreviewMouseWheel += OnWheel;            // the wheel glides instead of jumping 48 units per notch
        PreviewMouseDown += OnScrollerPressed;   // a click (e.g. on the scroll bar) takes over from a running glide
        Unloaded += OnUnloaded;
        Loaded += (_, _) => UpdateStem();
    }

    // ── The stem that grows ────────────────────────────────────────────────────────────────────────────────
    // The rail on the left is the stem of the sunflower in the page header. While you read downwards it turns
    // green behind you, and every release you have reached gets a green leaf. The date of the release being read
    // stays in view (WPF has no "sticky"): its date block and its leaf are pushed down by exactly the distance
    // the release has scrolled out at the top.
    //
    // What keeps this calm and cheap while the page moves:
    //   · Only render transforms are touched, so scrolling never causes a new layout pass.
    //   · The parts of a release and their transforms are looked up ONCE (StemParts). A scroll step then only
    //     writes two numbers per release, and only when they changed – nothing is created per step.
    //   · Nothing is rounded here. The page is already laid out on whole screen pixels (UseLayoutRounding), so
    //     the distances below land on screen pixels by themselves – on a 100 % screen and on a 125, 150 or
    //     250 % one. Rounding them to whole units would push date and leaf off that grid on scaled screens.
    //   · The green ends at ONE line for the whole timeline (StemTip), so it grows evenly with the scrolling
    //     and never jumps from one release to the next.

    private void OnScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (_gliding && Math.Abs(e.VerticalOffset - _glideAt) > 0.5) StopGlide(); // somebody else moved the page
        UpdateStem();
    }

    /// <summary>Puts date, leaf and green trail of every release where they belong. Runs by itself (see the top of the file).</summary>
    public void UpdateStem()
    {
        Timeline ??= (Content as FrameworkElement)?.FindName("Timeline") as ItemsControl;
        if (Timeline is null) return;

        // Releases are only ever added at the end ("Show … and earlier"), so the list of parts only grows.
        for (int i = _stems.Count; i < Timeline.Items.Count; i++)
        {
            if (Timeline.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter { IsLoaded: true } row
                || StemParts.Of(row) is not { } found)
            {
                // Just added ("Show … and earlier") and not laid out yet: try once more when it is.
                if (!_retryQueued)
                {
                    _retryQueued = true;
                    Dispatcher.BeginInvoke(() => { _retryQueued = false; UpdateStem(); }, System.Windows.Threading.DispatcherPriority.Loaded);
                }
                break;
            }
            _stems.Add(found);
        }
        if (_stems.Count == 0) return;

        // One measurement for the whole timeline: where its first release is in the window. Every other release
        // follows from its laid-out place in the list (GetOffset is a stored number, not a walk through the tree).
        var first = _stems[0].Row;
        double firstTop = first.TranslatePoint(new Point(), this).Y;
        double listTop = firstTop - VisualTreeHelper.GetOffset(first).Y;
        double tip = StemTip(firstTop);

        foreach (var parts in _stems)
        {
            double top = listTop + VisualTreeHelper.GetOffset(parts.Row).Y;
            double height = parts.Row.ActualHeight;

            // Date and leaf share one transform, so they can never be a pixel apart.
            double slide = PinOffset(top, height, parts.Pin.Margin.Top + parts.Pin.ActualHeight);
            if (parts.Slide.Y != slide) parts.Slide.Y = slide;

            // The green piece of the rail is stretched from the height it was REALLY drawn with. "Height 1" in the
            // XAML is rounded to whole screen pixels – that is 0.8 units on a 125 % or 250 % screen and 1.33 on a
            // 150 % one, and a stretch that assumes 1 ends a fifth short of the leaf or a third beyond it.
            double drawn = parts.Trail.ActualHeight;
            double grow = drawn > 0 ? StemLength(tip, top, height) / drawn : 0;
            if (parts.Grow.ScaleY != grow) parts.Grow.ScaleY = grow;

            // A leaf turns green when the green line reaches it and grey again when you scroll back above it –
            // as a soft cross-fade, not a jump.
            bool green = top + LeafY <= tip + Hair;
            if (parts.IsGreen == green) continue;
            parts.IsGreen = green;
            parts.GreenLeaf.BeginAnimation(OpacityProperty, new DoubleAnimation(green ? 1 : 0, TimeSpan.FromMilliseconds(220)));
        }
    }

    // ── Arithmetic of the stem (no WPF in here) ────────────────────────────────────────────────────────────
    // All distances are measured downwards from the top edge of the scrolling area.
    private const double PinTop = 24;      // a pinned date rests this far below the top edge of the window
    private const double ReleaseGap = 80;  // empty space under every release (the bottom margin of the body in the template)
    private const double LeafY = 20;       // a leaf joins the stem this far below the top of its release
    private const double Hair = 0.01;      // far less than a screen pixel: absorbs the last digit of an addition

    /// <summary>
    /// How far a date has to slide down: nothing until its release reaches the pin line, then exactly as far
    /// as the page has scrolled on, and never further than the end of its own release (so it cannot run into
    /// the next date).
    /// </summary>
    internal static double PinOffset(double top, double rowHeight, double pinHeight) =>
        Math.Clamp(PinTop - top, 0, Math.Max(0, rowHeight - ReleaseGap - pinHeight));

    /// <summary>
    /// Where the green ends. At first that is the leaf of the newest release – the stem of the flower runs
    /// into it. Once that leaf is pinned the green ends at the pin line and stays there: the page scrolls
    /// through it, so the stem grows by exactly what you scroll. <paramref name="firstTop"/> = top of the
    /// newest release.
    /// </summary>
    internal static double StemTip(double firstTop) => Math.Max(PinTop, firstTop) + LeafY;

    /// <summary>
    /// How much of one release's piece of the rail is green: what lies above the tip. Added up over all
    /// releases that is one unbroken green line from the flower down to the tip.
    /// </summary>
    internal static double StemLength(double tip, double top, double rowHeight) =>
        Math.Clamp(tip - top, 0, Math.Max(0, rowHeight));
    // ── end of the arithmetic ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The moving parts of one release (template "Release"), looked up once. The transforms are made here and
    /// kept; a scroll step only writes a number into them. (A transform written inside a template is shared
    /// by every release and read-only, so it cannot be used for this.)
    /// </summary>
    private sealed class StemParts
    {
        public ContentPresenter Row { get; }
        public FrameworkElement Pin { get; }               // date, version and badges
        public FrameworkElement Trail { get; }             // the green piece of the rail
        public UIElement GreenLeaf { get; }                // "NodeOn", faded in over the grey leaf
        public TranslateTransform Slide { get; } = new();  // moves the date block AND the leaf
        public ScaleTransform Grow { get; } = new(1, 0);   // stretches the trail downwards from the top of the release
        public bool IsGreen { get; set; }

        private StemParts(ContentPresenter row, FrameworkElement pin, UIElement leaf, UIElement greenLeaf, FrameworkElement trail)
        {
            Row = row;
            Pin = pin;
            Trail = trail;
            GreenLeaf = greenLeaf;
            pin.RenderTransform = Slide;
            leaf.RenderTransform = Slide;
            trail.RenderTransform = Grow;
        }

        /// <summary>Null while the release has not built its template yet.</summary>
        public static StemParts? Of(ContentPresenter row) =>
            row.ContentTemplate is { } template
            && template.FindName("Pin", row) is FrameworkElement pin
            && template.FindName("Node", row) is UIElement leaf
            && template.FindName("NodeOn", row) is UIElement greenLeaf
            && template.FindName("Trail", row) is FrameworkElement trail
                ? new StemParts(row, pin, leaf, greenLeaf, trail)
                : null;
    }

    // ── Gliding instead of jumping ─────────────────────────────────────────────────────────────────────────
    // A ScrollViewer moves the page a fixed 48 units per wheel event, in one frame, whatever the wheel reports:
    // a mouse makes it hop, a touchpad (many small events) makes it race. Here the wheel only says where the
    // page should end up; the page is then eased there, one step per screen refresh. The frame handler is
    // attached for those few frames only and takes itself off as soon as the page has arrived – nothing runs
    // while nobody scrolls. The stem follows through OnScrolled in the very same frame.
    // Without a graphics card (Remote Desktop, some Citrix setups) or with animations switched off in Windows
    // the ScrollViewer keeps its plain jump: sixty software-drawn frames a second would cost more than they give.
    // This block stands alone – delete it and the two Preview… lines in the constructor and the page
    // scrolls the stock way again; the stem above does not depend on it.

    private const double UnitsPerLine = 16;    // 3 lines per notch (the Windows default) = 48 units, as far as before
    private const double GlideSeconds = 0.09;  // in this time 63 % of the remaining way is done – about 0.3 s to arrive

    private double _glideAt;      // where the glide has put the page
    private double _glideTo;      // where the wheel wants it
    private bool _gliding;        // the frame handler is attached
    private TimeSpan _lastFrame;

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation || RenderCapability.Tier >> 16 == 0) return; // stock jump

        e.Handled = true;
        if (!_gliding) _glideAt = _glideTo = VerticalOffset; // scroll bar or keys may have moved the page since

        int lines = SystemParameters.WheelScrollLines; // -1 = "one screen at a time"
        double notch = lines < 0 ? ViewportHeight * 0.9 : lines * UnitsPerLine;
        double wanted = _glideTo - e.Delta / (double)Mouse.MouseWheelDeltaForOneLine * notch;
        _glideTo = Math.Clamp(wanted, 0, ScrollableHeight);

        if (_gliding || _glideTo == _glideAt) return;
        _gliding = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        TimeSpan now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return; // WPF can raise the event twice for one frame
        double seconds = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 0.1);
        _lastFrame = now;

        // The same share of the remaining way per unit of time: quick off the mark, a soft landing, and the same
        // pace on a 60 Hz and a 144 Hz screen.
        _glideAt += (_glideTo - _glideAt) * (1 - Math.Exp(-seconds / GlideSeconds));
        bool arrived = Math.Abs(_glideTo - _glideAt) < 0.5;
        if (arrived) _glideAt = _glideTo;

        ScrollToVerticalOffset(_glideAt);
        if (arrived) StopGlide();
    }

    private void StopGlide()
    {
        if (!_gliding) return;
        _gliding = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    /// <summary>A click on the scroll bar (or anywhere on the page) takes over from a glide that is still running.</summary>
    private void OnScrollerPressed(object sender, MouseButtonEventArgs e) => StopGlide();

    /// <summary>CompositionTarget.Rendering is static – a handler left on it would keep the window alive.</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e) => StopGlide();
}
