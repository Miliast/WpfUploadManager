using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Conduit;

/// <summary>
/// Status mark of one test: dashed ring (queued), spinner (running), or a solid disc with a check, cross or dash.
/// The look is in StatusIcon.xaml; this file picks the layer for the current <see cref="State"/> and plays the
/// hand-over from spinner to result. Everything is soft: layers cross-fade, the disc only settles by a few percent,
/// the glyph is drawn along its own path and the halo changes opacity only.
/// </summary>
public partial class StatusIcon : UserControl
{
    // ── Tuning (milliseconds) ──────────────────────────────────────────────
    private const double SpinMs = 900;      // one full turn of the spinner
    private const double FadeMs = 220;      // cross-fade between layers
    private const double SettleMs = 340;    // disc: 86 % → 100 %
    private const double DrawDelayMs = 110; // the glyph starts a moment after the disc
    private const double DrawMs = 300;      // time to draw the glyph
    private const double HaloMs = 900;      // one halo pulse (failed / aborted)
    private const double HaloPeak = 0.26;   // strongest halo opacity
    private const double SettleFrom = 0.86; // start size of the disc – close to full size, never a point

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(TestState), typeof(StatusIcon),
        new PropertyMetadata(TestState.Queued, (d, e) => ((StatusIcon)d).OnStateChanged((TestState)e.OldValue)));

    public static readonly DependencyProperty IsFreshProperty = DependencyProperty.Register(
        nameof(IsFresh), typeof(bool), typeof(StatusIcon), new PropertyMetadata(false));

    public StatusIcon()
    {
        InitializeComponent();
        Loaded += (_, _) => Show(State, animate: IsFresh);
        Unloaded += (_, _) => Spin.BeginAnimation(RotateTransform.AngleProperty, null); // a hidden spinner must not keep ticking
    }

    /// <summary>What the icon shows.</summary>
    public TestState State
    {
        get => (TestState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>
    /// True when the result is brand new (the test finished a moment ago): the icon then plays its arrival
    /// when it first appears. Rows that were finished long ago leave it false and simply show their result.
    /// </summary>
    public bool IsFresh
    {
        get => (bool)GetValue(IsFreshProperty);
        set => SetValue(IsFreshProperty, value);
    }

    private void OnStateChanged(TestState old)
    {
        if (!IsLoaded) return; // Loaded shows the first state
        // A result that replaces the spinner (or the queued ring) in place is always worth the motion.
        Show(State, animate: old is TestState.Running or TestState.Queued);
    }

    // ── Layers ─────────────────────────────────────────────────────────────

    private void Show(TestState state, bool animate)
    {
        animate &= SystemParameters.ClientAreaAnimation;
        bool done = state is TestState.Passed or TestState.Failed or TestState.Aborted;

        Fade(Queued, state == TestState.Queued ? 1 : 0, animate);
        Fade(Spinner, state == TestState.Running ? 1 : 0, animate);
        Turn(state == TestState.Running);

        if (!done)
        {
            Fade(Disc, 0, animate);
            Halo.BeginAnimation(OpacityProperty, null);
            return;
        }

        // Colour + glyph of the result. Path lengths are in icon units; WPF measures dashes in stroke widths.
        var (brushKey, data, thickness, length) = state switch
        {
            TestState.Passed => ("Green", "M4.9,8.2 L7,10.3 L11.1,6.1", 1.7, 8.9),
            TestState.Failed => ("Red", "M5.7,5.7 L10.3,10.3 M10.3,5.7 L5.7,10.3", 1.7, 6.6),
            _ => ("Amber", "M5.2,8 H10.8", 1.8, 5.6),
        };
        var brush = (Brush)FindResource(brushKey);
        DiscFill.Fill = brush;
        Halo.Fill = brush;
        Glyph.Data = Geometry.Parse(data);
        Glyph.StrokeThickness = thickness;

        // One dash as long as the whole glyph (plus a little), followed by an equally long gap:
        // sliding the pattern from "gap first" to "dash first" draws the stroke from its start to its end.
        double dash = (length / thickness) + 1;
        Glyph.StrokeDashArray = new DoubleCollection { dash, dash };

        if (!animate)
        {
            Fade(Disc, 1, false);
            Set(DiscScale, 1);
            Glyph.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            Glyph.StrokeDashOffset = 0;
            Halo.BeginAnimation(OpacityProperty, null);
            return;
        }

        // Disc: fades in and settles to full size.
        Fade(Disc, 1, true);
        var settle = Ease(SettleFrom, 1, SettleMs, EasingMode.EaseOut);
        DiscScale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
        DiscScale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);

        // Glyph: writes itself shortly after.
        var draw = Ease(dash, 0, DrawMs, EasingMode.EaseInOut);
        draw.BeginTime = TimeSpan.FromMilliseconds(DrawDelayMs);
        Glyph.StrokeDashOffset = dash; // hidden until the animation starts
        Glyph.BeginAnimation(Shape.StrokeDashOffsetProperty, draw);

        // Halo: one slow pulse for the results that need attention.
        if (state == TestState.Passed)
        {
            Halo.BeginAnimation(OpacityProperty, null);
            return;
        }
        var sine = new SineEase { EasingMode = EasingMode.EaseInOut };
        var pulse = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(HaloMs) };
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(HaloPeak, KeyTime.FromPercent(0.4), sine));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), sine));
        Halo.BeginAnimation(OpacityProperty, pulse);
    }

    /// <summary>Starts or stops the endless rotation of the spinner.</summary>
    private void Turn(bool on)
    {
        if (!on)
        {
            Spin.BeginAnimation(RotateTransform.AngleProperty, null);
            return;
        }
        Spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(SpinMs))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void Fade(UIElement layer, double to, bool animate)
    {
        if (!animate)
        {
            layer.BeginAnimation(OpacityProperty, null);
            layer.Opacity = to;
            return;
        }
        layer.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(FadeMs))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private static void Set(ScaleTransform scale, double value)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = scale.ScaleY = value;
    }

    private static DoubleAnimation Ease(double from, double to, double ms, EasingMode mode) => new(from, to, TimeSpan.FromMilliseconds(ms))
    {
        EasingFunction = new CubicEase { EasingMode = mode },
    };
}
