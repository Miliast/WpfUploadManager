using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Conduit;

/// <summary>
/// Square progress mark of a node. The look is in NodeProgressIcon.xaml; this file turns the four inputs
/// (<see cref="Progress"/>, <see cref="IsComplete"/>, <see cref="HasFailed"/>, <see cref="HasAborted"/>) into the
/// pie-shaped clip of the fill, the green "done" state and the problem dot – and moves between them softly.
/// </summary>
public partial class NodeProgressIcon : UserControl
{
    // ── Tuning (milliseconds) ──────────────────────────────────────────────
    private const double SweepMs = 450;    // fill: glide to a new progress value
    private const double FadeMs = 260;     // outline/fill ↔ green square, dot fade-in
    private const double DrawDelayMs = 140; // the check starts a moment after the green square
    private const double DrawMs = 320;     // time to draw the check
    private const double DotFrom = 0.8;    // start size of the dot – close to full size, never a point

    // Geometry of the fill, in the coordinates of the Fill rectangle (8.2 × 8.2).
    private const double FillSize = 8.2;
    private static readonly Point FillCentre = new(FillSize / 2, FillSize / 2);
    private const double ClipRadius = 8;   // larger than the shape, so the slice always covers its corners

    // Check: path length 8.9 units at 1.7 stroke → one dash that covers it, see StatusIcon for the idea.
    private const double CheckDash = (8.9 / 1.7) + 1;

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(NodeProgressIcon),
        new PropertyMetadata(0d, (d, _) => ((NodeProgressIcon)d).OnProgressChanged()));

    public static readonly DependencyProperty IsCompleteProperty = DependencyProperty.Register(
        nameof(IsComplete), typeof(bool), typeof(NodeProgressIcon),
        new PropertyMetadata(false, (d, _) => ((NodeProgressIcon)d).ShowDone(animate: true)));

    public static readonly DependencyProperty HasFailedProperty = DependencyProperty.Register(
        nameof(HasFailed), typeof(bool), typeof(NodeProgressIcon),
        new PropertyMetadata(false, (d, _) => ((NodeProgressIcon)d).ShowDot(animate: true)));

    public static readonly DependencyProperty HasAbortedProperty = DependencyProperty.Register(
        nameof(HasAborted), typeof(bool), typeof(NodeProgressIcon),
        new PropertyMetadata(false, (d, _) => ((NodeProgressIcon)d).ShowDot(animate: true)));

    public static readonly DependencyProperty DotRingProperty = DependencyProperty.Register(
        nameof(DotRing), typeof(Brush), typeof(NodeProgressIcon), new PropertyMetadata(Brushes.White));

    // The part of the fill that is currently revealed (0…1). Animated; every change rebuilds the clip.
    private static readonly DependencyProperty SweepProperty = DependencyProperty.Register(
        "Sweep", typeof(double), typeof(NodeProgressIcon),
        new PropertyMetadata(0d, (d, _) => ((NodeProgressIcon)d).UpdateClip()));

    private bool _dotShown;

    public NodeProgressIcon()
    {
        InitializeComponent();
        DoneCheck.StrokeDashArray = new DoubleCollection { CheckDash, CheckDash };
        UpdateClip();
        Loaded += (_, _) =>
        {
            // First appearance: show the current values as they are, without motion.
            MoveSweep(animate: false);
            ShowDone(animate: false);
            ShowDot(animate: false);
        };
    }

    /// <summary>Finished part of the node, 0…1. Values outside are clamped.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Every test ran: the icon becomes the green square with a check.</summary>
    public bool IsComplete
    {
        get => (bool)GetValue(IsCompleteProperty);
        set => SetValue(IsCompleteProperty, value);
    }

    /// <summary>At least one test failed: red dot (wins over amber).</summary>
    public bool HasFailed
    {
        get => (bool)GetValue(HasFailedProperty);
        set => SetValue(HasFailedProperty, value);
    }

    /// <summary>At least one test was aborted: amber dot.</summary>
    public bool HasAborted
    {
        get => (bool)GetValue(HasAbortedProperty);
        set => SetValue(HasAbortedProperty, value);
    }

    /// <summary>Colour of the thin ring around the dot – the colour of the surface behind the icon.</summary>
    public Brush DotRing
    {
        get => (Brush)GetValue(DotRingProperty);
        set => SetValue(DotRingProperty, value);
    }

    private bool CanAnimate => IsLoaded && SystemParameters.ClientAreaAnimation;

    // ── Progress: the pie-shaped clip ──────────────────────────────────────

    private void OnProgressChanged() => MoveSweep(animate: true);

    private void MoveSweep(bool animate)
    {
        double target = Math.Clamp(Progress, 0, 1);
        if (!animate || !CanAnimate)
        {
            BeginAnimation(SweepProperty, null);
            SetValue(SweepProperty, target);
            return;
        }
        // Starts from wherever the sweep is right now, so quick successive results glide on without a jump.
        BeginAnimation(SweepProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(SweepMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private void UpdateClip()
    {
        double sweep = (double)GetValue(SweepProperty);
        if (sweep <= 0.001)
        {
            Fill.Clip = Geometry.Empty; // nothing finished yet: only the outline shows
            return;
        }
        if (sweep >= 0.999)
        {
            Fill.Clip = null;           // everything revealed
            return;
        }

        // A slice from 12 o'clock, clockwise, around the centre of the fill.
        double angle = sweep * 2 * Math.PI;
        var start = new Point(FillCentre.X, FillCentre.Y - ClipRadius);
        var end = new Point(FillCentre.X + (ClipRadius * Math.Sin(angle)), FillCentre.Y - (ClipRadius * Math.Cos(angle)));
        var figure = new PathFigure(FillCentre,
        [
            new LineSegment(start, false),
            new ArcSegment(end, new Size(ClipRadius, ClipRadius), 0, sweep > 0.5, SweepDirection.Clockwise, false),
        ], closed: true);
        var slice = new PathGeometry([figure]);
        slice.Freeze();
        Fill.Clip = slice;
    }

    // ── Done: outline + fill hand over to the green square, the check draws itself ──

    private void ShowDone(bool animate)
    {
        bool done = IsComplete;
        animate &= CanAnimate;

        Fade(Outline, done ? 0 : 1, animate);
        Fade(Fill, done ? 0 : 1, animate);
        Fade(DoneFill, done ? 1 : 0, animate);
        Fade(DoneCheck, done ? 1 : 0, animate);

        if (!done || !animate)
        {
            DoneCheck.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            DoneCheck.StrokeDashOffset = 0;
            return;
        }
        DoneCheck.StrokeDashOffset = CheckDash; // hidden until the drawing starts
        DoneCheck.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(CheckDash, 0, TimeSpan.FromMilliseconds(DrawMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(DrawDelayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    // ── Dot: red after a failure, amber after an abort ─────────────────────

    private void ShowDot(bool animate)
    {
        bool show = HasFailed || HasAborted;
        animate &= CanAnimate;
        if (show) Dot.Fill = (Brush)FindResource(HasFailed ? "Red" : "Amber"); // a colour change (amber → red) is immediate

        bool appears = show && !_dotShown;
        _dotShown = show;

        Fade(Dot, show ? 1 : 0, animate);
        if (appears && animate)
        {
            var settle = new DoubleAnimation(DotFrom, 1, TimeSpan.FromMilliseconds(FadeMs + 80))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            DotScale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
            DotScale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
        }
    }

    private static void Fade(UIElement element, double to, bool animate)
    {
        if (!animate)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = to;
            return;
        }
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(FadeMs))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }
}
