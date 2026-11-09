using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Conduit;

/// <summary>
/// Aurora glow for auto-fill: a calm violet → pink → orange wave that starts as a wide, faint light around the
/// sidebar button, flows across the page towards the fields and "lands" softly on each field as its file drops in.
/// Nothing bursts out of a point: every light is large from its first frame, only its opacity rises slowly, and
/// the lights fade in one after another from left to right – that staggering is what makes it read as a wave.
/// The look (colours, brushes, shapes, fixed storyboards) is in GlowSweep.xaml; this file only places the shapes
/// and animates what depends on the window (start point, field positions, sizes).
/// Pure overlay: it never takes clicks and removes every shape when its animation ends, so the page underneath
/// can re-render freely while the glow is still playing. Skipped entirely when Windows animations are turned off.
/// </summary>
internal sealed partial class GlowSweep : Canvas
{
    /// <summary>Four-point sparkle (14×14) for the button that starts the glow.</summary>
    public const string SparkleIcon = "M7,1 Q7.6,6.4 13,7 Q7.6,7.6 7,13 Q6.4,7.6 1,7 Q6.4,6.4 7,1 Z";

    // ── Tuning ─────────────────────────────────────────────────────────────
    private const double LightMs = 2200;   // lifetime of one light (fade in, hold, fade out)
    private const double SpreadMs = 700;   // how much later the light at the far right starts than the first one
    private const double Drift = 0.2;      // how far a light travels: it starts this fraction of the way back to the button
    private const double Peak = 0.85;      // strongest opacity of a light

    // Where each light rests (fraction of the overlay), its height (fraction of the overlay height) and its brush
    // in the XAML. Ordered left → right: violet/indigo near the sidebar, pink in the middle, orange at the far edge.
    private static readonly (double X, double Y, double Size, string Brush)[] Blobs =
    [
        (0.28, 0.52, 0.95, "BlobViolet"),
        (0.44, 0.22, 0.80, "BlobIndigo"),
        (0.56, 0.68, 1.00, "BlobPink"),
        (0.72, 0.34, 0.90, "BlobPink"),
        (0.86, 0.72, 0.95, "BlobOrange"),
        (0.98, 0.30, 0.85, "BlobOrange"),
    ];

    public GlowSweep() => InitializeComponent();

    private bool CanPlay => SystemParameters.ClientAreaAnimation && ActualWidth > 0 && ActualHeight > 0;

    /// <summary>Plays the wave starting around <paramref name="origin"/> (overlay coordinates, e.g. the button's centre).</summary>
    public void Play(Point origin)
    {
        if (!CanPlay) return;
        double w = ActualWidth, h = ActualHeight;
        double reach = Math.Max(w - origin.X, 1);
        var soft = (IEasingFunction)Resources["EaseSoft"];

        // 1. Bloom: wide, faint light around the button – fully described in the XAML.
        var bloom = Spawn<Ellipse>("Bloom", 0, 0);
        SetLeft(bloom, origin.X - (bloom.Width / 2));
        SetTop(bloom, origin.Y - (bloom.Height / 2));
        Run("BloomIn", bloom);

        // 2. Wash: a very wide band that slowly travels from the sidebar to the right edge.
        double band = Math.Max(w * 0.75, 520);
        var wash = Spawn<Rectangle>("Wash", 0, -h * 0.2);
        wash.Width = band;
        wash.Height = h * 1.4;
        var washMove = (TranslateTransform)((TransformGroup)wash.RenderTransform).Children[1];
        washMove.BeginAnimation(TranslateTransform.XProperty,
            Anim(origin.X - (band * 0.6), w - (band * 0.25), Ms(150), Ms(LightMs), soft));
        Fade(wash, Ms(150), Ms(LightMs), (0.4, 1), (0.6, 1), (1, 0));

        // 3. Aurora: large lights that are already (almost) full size, fade in one after another from left to
        //    right and drift a little further away from the button while they glow.
        foreach (var (fx, fy, fs, brush) in Blobs)
        {
            double height = Math.Max(h, 420) * fs;
            double width = height * 1.3; // wider than tall, so neighbours overlap into one wash
            var blob = Spawn<Ellipse>("Blob", 0, 0);
            blob.Width = width;
            blob.Height = height;
            blob.Fill = (Brush)Resources[brush];
            var parts = ((TransformGroup)blob.RenderTransform).Children;
            var scale = (ScaleTransform)parts[0];
            var move = (TranslateTransform)parts[1];

            // Rest position (centre) and a start position a short way back towards the button.
            double restX = fx * w, restY = fy * h;
            double startX = restX + ((origin.X - restX) * Drift);
            double startY = restY + ((origin.Y - restY) * Drift);

            // The further right a light rests, the later it starts – this is the wave.
            var begin = Ms(120 + (SpreadMs * Math.Clamp((restX - origin.X) / reach, 0, 1)));
            var duration = Ms(LightMs);

            move.X = startX - (width / 2);
            move.Y = startY - (height / 2);
            move.BeginAnimation(TranslateTransform.XProperty, Anim(move.X, restX - (width / 2), begin, duration, soft));
            move.BeginAnimation(TranslateTransform.YProperty, Anim(move.Y, restY - (height / 2), begin, duration, soft));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.86, 1.08, begin, duration, soft));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.86, 1.08, begin, duration, soft));
            Fade(blob, begin, duration, (0.45, Peak), (0.6, Peak), (1, 0));
        }
    }

    /// <summary>Soft glowing outline on a field (overlay coordinates) – the moment a file lands in it.</summary>
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

    /// <summary>
    /// Opacity keyframes (time as fraction of <paramref name="duration"/>), each reached on a slow-in / slow-out
    /// curve so the light never pops; the element is removed afterwards.
    /// </summary>
    private void Fade(UIElement element, TimeSpan begin, TimeSpan duration, params (double At, double Value)[] keys)
    {
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = duration };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        foreach (var (at, value) in keys) fade.KeyFrames.Add(new SplineDoubleKeyFrame(value, KeyTime.FromPercent(at), new KeySpline(0.45, 0, 0.55, 1)));
        fade.Completed += (_, _) => Children.Remove(element);
        element.BeginAnimation(OpacityProperty, fade);
    }

    private static DoubleAnimation Anim(double from, double to, TimeSpan begin, TimeSpan duration, IEasingFunction ease) =>
        new(from, to, duration) { BeginTime = begin, EasingFunction = ease };

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);
}
