using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Conduit;

/// <summary>
/// Aurora glow for auto-fill: a soft violet → pink → orange light bursts out of the sidebar button, washes across
/// the page towards the fields and "lands" on each field as its file drops in.
/// Pure overlay: it never takes clicks and removes every shape when its animation ends, so the page underneath
/// can re-render freely while the glow is still playing. Skipped entirely when Windows animations are turned off.
/// </summary>
internal sealed class GlowSweep : Canvas
{
    /// <summary>Four-point sparkle (14×14) for the button that starts the glow.</summary>
    public const string SparkleIcon = "M7,1 Q7.6,6.4 13,7 Q7.6,7.6 7,13 Q6.4,7.6 1,7 Q6.4,6.4 7,1 Z";

    private static readonly Color Violet = Color.FromRgb(0x7C, 0x5C, 0xFF), Pink = Color.FromRgb(0xFF, 0x4F, 0xA3);
    private static readonly Color Orange = Color.FromRgb(0xFF, 0x92, 0x48), Indigo = Color.FromRgb(0x3D, 0x6B, 0xFF);

    // Where each light blob drifts to (fraction of the overlay), its diameter (fraction of the height) and colour.
    private static readonly (double X, double Y, double Size, Color Color)[] Blobs =
    [
        (0.42, 0.30, 0.85, Violet),
        (0.58, 0.62, 0.95, Pink),
        (0.78, 0.40, 0.80, Orange),
        (0.95, 0.75, 0.90, Violet),
        (0.66, 0.12, 0.70, Indigo),
        (0.88, 0.95, 0.75, Pink),
    ];

    private static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction InOut = new CubicEase { EasingMode = EasingMode.EaseInOut };

    public GlowSweep()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    private bool CanPlay => SystemParameters.ClientAreaAnimation && ActualWidth > 0 && ActualHeight > 0;

    /// <summary>Plays the sweep starting at <paramref name="origin"/> (overlay coordinates, e.g. the button's centre).</summary>
    public void Play(Point origin)
    {
        if (!CanPlay) return;
        double w = ActualWidth, h = ActualHeight;

        // 1. Flare: a bright pulse right on the button.
        var flare = Add(new Ellipse { Width = 240, Height = 240, Fill = Radial(Pink, 0.75) }, origin.X - 120, origin.Y - 120);
        var flareScale = Scale(flare, 0.2, 120, 120);
        Grow(flareScale, 0.2, 1.6, TimeSpan.Zero, Ms(700));
        Fade(flare, TimeSpan.Zero, Ms(700), (0.12, 1), (1, 0));

        // 2. Wash: a tilted band of colour that sweeps from the sidebar to the right edge.
        double band = Math.Max(w * 0.45, 320);
        var wash = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        wash.GradientStops.Add(new GradientStop(Alpha(Violet, 0), 0));
        wash.GradientStops.Add(new GradientStop(Alpha(Violet, 0.16), 0.3));
        wash.GradientStops.Add(new GradientStop(Alpha(Pink, 0.22), 0.55));
        wash.GradientStops.Add(new GradientStop(Alpha(Orange, 0.14), 0.75));
        wash.GradientStops.Add(new GradientStop(Alpha(Orange, 0), 1));
        wash.Freeze();
        var sheet = Add(new Rectangle { Width = band, Height = h * 1.4, Fill = wash }, 0, -h * 0.2);
        var sheetMove = new TranslateTransform(origin.X - band / 2, 0);
        sheet.RenderTransform = new TransformGroup { Children = { new SkewTransform(-14, 0, band / 2, h * 0.7), sheetMove } };
        sheetMove.BeginAnimation(TranslateTransform.XProperty, Anim(origin.X - band / 2, w + band * 0.3, Ms(80), Ms(1200), InOut));
        Fade(sheet, Ms(80), Ms(1200), (0.2, 1), (0.7, 1), (1, 0));

        // 3. Aurora: big soft blobs that bloom out of the button and drift over the fields.
        for (int i = 0; i < Blobs.Length; i++)
        {
            var (fx, fy, fs, color) = Blobs[i];
            double size = Math.Max(h, 360) * fs;
            var blob = Add(new Ellipse { Width = size, Height = size, Fill = Radial(color, 0.5) }, 0, 0);
            var move = new TranslateTransform(origin.X - size / 2, origin.Y - size / 2);
            var scale = new ScaleTransform(0.15, 0.15, size / 2, size / 2);
            blob.RenderTransform = new TransformGroup { Children = { scale, move } };

            var begin = Ms(i * 55);
            var duration = Ms(1500 + (i * 40));
            move.BeginAnimation(TranslateTransform.XProperty, Anim(origin.X - size / 2, (fx * w) - (size / 2), begin, duration, Out));
            move.BeginAnimation(TranslateTransform.YProperty, Anim(origin.Y - size / 2, (fy * h) - (size / 2), begin, duration, Out));
            Grow(scale, 0.15, 1, begin, duration);
            Fade(blob, begin, duration, (0.18, 0.9), (0.6, 0.65), (1, 0));
        }
    }

    /// <summary>Glowing outline on a field (overlay coordinates) – the moment a file lands in it.</summary>
    public void Land(Rect target, double radius = 12)
    {
        if (!CanPlay || target.IsEmpty) return;
        var stroke = new LinearGradientBrush(Violet, Orange, 0);
        stroke.GradientStops.Insert(1, new GradientStop(Pink, 0.5));
        stroke.Freeze();
        var ring = Add(new Border
        {
            Width = target.Width, Height = target.Height, CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(2), BorderBrush = stroke, Background = new SolidColorBrush(Alpha(Violet, 0.07)),
            Effect = new DropShadowEffect { Color = Pink, BlurRadius = 28, ShadowDepth = 0, Opacity = 0.7 },
        }, target.X, target.Y);
        Grow(Scale(ring, 0.97, target.Width / 2, target.Height / 2), 0.97, 1, TimeSpan.Zero, Ms(350));
        Fade(ring, TimeSpan.Zero, Ms(950), (0.15, 1), (0.45, 1), (1, 0));
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private T Add<T>(T element, double left, double top) where T : FrameworkElement
    {
        element.Opacity = 0;
        SetLeft(element, left);
        SetTop(element, top);
        Children.Add(element);
        return element;
    }

    private static ScaleTransform Scale(UIElement element, double start, double cx, double cy)
    {
        var scale = new ScaleTransform(start, start, cx, cy);
        element.RenderTransform = scale;
        return scale;
    }

    private static void Grow(ScaleTransform scale, double from, double to, TimeSpan begin, TimeSpan duration)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(from, to, begin, duration, Out));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(from, to, begin, duration, Out));
    }

    /// <summary>Opacity keyframes (time as fraction of <paramref name="duration"/>); the element is removed afterwards.</summary>
    private void Fade(UIElement element, TimeSpan begin, TimeSpan duration, params (double At, double Value)[] keys)
    {
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = duration };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        foreach (var (at, value) in keys) fade.KeyFrames.Add(new SplineDoubleKeyFrame(value, KeyTime.FromPercent(at), new KeySpline(0.4, 0, 0.2, 1)));
        fade.Completed += (_, _) => Children.Remove(element);
        element.BeginAnimation(OpacityProperty, fade);
    }

    private static DoubleAnimation Anim(double from, double to, TimeSpan begin, TimeSpan duration, IEasingFunction ease) =>
        new(from, to, duration) { BeginTime = begin, EasingFunction = ease };

    /// <summary>Soft light: full colour in the middle, fading to nothing at the rim.</summary>
    private static RadialGradientBrush Radial(Color color, double strength)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Alpha(color, strength), 0));
        brush.GradientStops.Add(new GradientStop(Alpha(color, strength * 0.45), 0.4));
        brush.GradientStops.Add(new GradientStop(Alpha(color, 0), 1));
        brush.Freeze();
        return brush;
    }

    private static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);
}
