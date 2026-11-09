using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Conduit;

/// <summary>
/// Aurora glow for auto-fill: a soft violet → pink → orange light bursts out of the sidebar button, washes across
/// the page towards the fields and "lands" on each field as its file drops in.
/// The look (colours, brushes, shapes, fixed storyboards) is in GlowSweep.xaml; this file only places the shapes
/// and animates what depends on the window (start point, field positions, sizes).
/// Pure overlay: it never takes clicks and removes every shape when its animation ends, so the page underneath
/// can re-render freely while the glow is still playing. Skipped entirely when Windows animations are turned off.
/// </summary>
internal sealed partial class GlowSweep : Canvas
{
    /// <summary>Four-point sparkle (14×14) for the button that starts the glow.</summary>
    public const string SparkleIcon = "M7,1 Q7.6,6.4 13,7 Q7.6,7.6 7,13 Q6.4,7.6 1,7 Q6.4,6.4 7,1 Z";

    // Where each light blob drifts to (fraction of the overlay), its diameter (fraction of the height) and its brush in the XAML.
    private static readonly (double X, double Y, double Size, string Brush)[] Blobs =
    [
        (0.42, 0.30, 0.85, "BlobViolet"),
        (0.58, 0.62, 0.95, "BlobPink"),
        (0.78, 0.40, 0.80, "BlobOrange"),
        (0.95, 0.75, 0.90, "BlobViolet"),
        (0.66, 0.12, 0.70, "BlobIndigo"),
        (0.88, 0.95, 0.75, "BlobPink"),
    ];

    public GlowSweep() => InitializeComponent();

    private bool CanPlay => SystemParameters.ClientAreaAnimation && ActualWidth > 0 && ActualHeight > 0;

    /// <summary>Plays the sweep starting at <paramref name="origin"/> (overlay coordinates, e.g. the button's centre).</summary>
    public void Play(Point origin)
    {
        if (!CanPlay) return;
        double w = ActualWidth, h = ActualHeight;
        var easeOut = (IEasingFunction)Resources["EaseOut"];

        // 1. Flare on the button – fully described in the XAML.
        var flare = Spawn<Ellipse>("Flare", origin.X - 120, origin.Y - 120);
        Run("FlareIn", flare);

        // 2. Wash: from the sidebar to the right edge.
        double band = Math.Max(w * 0.45, 320);
        var wash = Spawn<Rectangle>("Wash", 0, -h * 0.2);
        wash.Width = band;
        wash.Height = h * 1.4;
        var washMove = (TranslateTransform)((TransformGroup)wash.RenderTransform).Children[1];
        washMove.BeginAnimation(TranslateTransform.XProperty,
            Anim(origin.X - (band / 2), w + (band * 0.3), Ms(80), Ms(1200), (IEasingFunction)Resources["EaseInOut"]));
        Fade(wash, Ms(80), Ms(1200), (0.2, 1), (0.7, 1), (1, 0));

        // 3. Aurora: big soft blobs that bloom out of the button and drift over the fields.
        for (int i = 0; i < Blobs.Length; i++)
        {
            var (fx, fy, fs, brush) = Blobs[i];
            double size = Math.Max(h, 360) * fs;
            var blob = Spawn<Ellipse>("Blob", 0, 0);
            blob.Width = blob.Height = size;
            blob.Fill = (Brush)Resources[brush];
            var parts = ((TransformGroup)blob.RenderTransform).Children;
            var scale = (ScaleTransform)parts[0];
            var move = (TranslateTransform)parts[1];

            var begin = Ms(i * 55);
            var duration = Ms(1500 + (i * 40));
            move.X = origin.X - (size / 2);
            move.Y = origin.Y - (size / 2);
            move.BeginAnimation(TranslateTransform.XProperty, Anim(move.X, (fx * w) - (size / 2), begin, duration, easeOut));
            move.BeginAnimation(TranslateTransform.YProperty, Anim(move.Y, (fy * h) - (size / 2), begin, duration, easeOut));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.15, 1, begin, duration, easeOut));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.15, 1, begin, duration, easeOut));
            Fade(blob, begin, duration, (0.18, 0.9), (0.6, 0.65), (1, 0));
        }
    }

    /// <summary>Glowing outline on a field (overlay coordinates) – the moment a file lands in it.</summary>
    public void Land(Rect target, double radius = 12)
    {
        if (!CanPlay || target.IsEmpty) return;
        var ring = Spawn<Border>("LandRing", target.X, target.Y);
        ring.Width = target.Width;
        ring.Height = target.Height;
        ring.CornerRadius = new CornerRadius(radius);
        Run("LandIn", ring);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>A fresh copy of a template from the XAML (x:Shared="False"), placed on the overlay.</summary>
    private T Spawn<T>(string key, double left, double top) where T : FrameworkElement
    {
        var element = (T)Resources[key];
        SetLeft(element, left);
        SetTop(element, top);
        Children.Add(element);
        return element;
    }

    /// <summary>Plays a storyboard from the XAML on <paramref name="element"/> and removes the element afterwards.</summary>
    private void Run(string key, FrameworkElement element)
    {
        var storyboard = (Storyboard)Resources[key];
        Storyboard.SetTarget(storyboard, element);
        storyboard.Completed += (_, _) => Children.Remove(element);
        storyboard.Begin();
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

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);
}
