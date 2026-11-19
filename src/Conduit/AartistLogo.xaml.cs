using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Conduit;

/// <summary>
/// The AARTist wordmark that folds into its first "A" and back – e.g. full while a run is set up, folded while it runs:
/// <c>&lt;local:AartistLogo LogoHeight="22" IsCollapsed="{Binding IsRun}" /&gt;</c>.
/// Folding: i, s, t slip behind the T stem one after another, then the letters telescope – the second A under the
/// first, the R under that A, the T with its speed line under the R, almost at the same time – until only the first A
/// is left. Unfolding runs the other way round: the speed line shoots out first, the letters follow, and "ist" slides
/// out as one word while the T is still gliding to rest.
/// While a letter travels it leans a little further forward and takes the aurora colour of the place it is passing;
/// soft lights follow the movement and everything settles back to the plain blue logo.
/// The look (artwork, letter regions, colours, lights) is in AartistLogo.xaml; this file is the timeline. The control
/// changes its own Width while it moves, so whatever stands next to it (breadcrumb, buttons) follows by itself.
/// With Windows animations turned off, or while it is not on screen, it simply jumps to the other state.
/// </summary>
internal sealed partial class AartistLogo : Canvas
{
    // ── Tuning ─────────────────────────────────────────────────────────────
    private const double CollapseMs = 1400;  // full logo → mark
    private const double ExpandMs = 1500;    // mark → full logo
    private const double Lean = 9;           // extra slant of a travelling letter: degrees per (logo unit per ms)
    private const double TintAt = 0.3;       // speed (logo units per ms) at which a letter is fully aurora-coloured
    private const double LayoutLag = 0.05;   // the Width trails the logo's right end on the way in and leads it on the way out
    private const double Nod = 3.2;          // how far the mark dips to the left when the letters have landed (logo units)
    private const double LightPeak = 0.9;    // strongest opacity of a light
    private const double LightReach = 150;   // how far around the moving end the lights are lit (logo units)

    // ── Artwork (logo units; the traced logo is 656 × 90) ──────────────────
    private const double ArtHeight = 90;
    private const double TipAtRest = 652;    // right end of the speed line
    private const double MarkEdge = 175.7;   // right stem of the first A – where the folded mark ends
    private const double EdgeMargin = 4;     // room to the right of the logo, inside the control

    /// <summary>
    /// One moving part: how far it has to slide until it is fully behind its neighbour, and when it moves – as a share
    /// of the run, for folding (In) and unfolding (Out). Soft parts use the calm curve from GlowSweep.
    /// </summary>
    private readonly record struct Track(double Travel, double TravelOut, double InFrom, double InTo, double OutFrom, double OutTo, bool Soft);

    private const int SecondA = 0, LetterR = 1, LetterT = 2, SmallI = 3, SmallS = 4, SmallT = 5;

    private static readonly Track[] Tracks =
    [
        new(137, 137, 0.07, 0.68, 0.14, 0.74, false), // second A under the first
        new(152, 152, 0.11, 0.72, 0.09, 0.69, false), // R under the second A
        new(190, 190, 0.15, 0.76, 0.04, 0.64, false), // T and the speed line under the R – out first, in last
        new(56, 132, 0.00, 0.11, 0.30, 0.46, true),   // i  ┐ in: one after another, each just far enough to hide
        new(92, 132, 0.03, 0.15, 0.30, 0.46, true),   // s  │ out: together as one word, so they never overlap
        new(132, 132, 0.06, 0.19, 0.30, 0.46, true),  // t  ┘
    ];

    // Where each light rests (centre, logo units), right → left: orange far out, indigo next to the first A.
    private static readonly double[] LightX = [600, 500, 410, 300, 190];

    public static readonly DependencyProperty IsCollapsedProperty = DependencyProperty.Register(
        nameof(IsCollapsed), typeof(bool), typeof(AartistLogo), new PropertyMetadata(false, (d, _) => ((AartistLogo)d).OnCollapsedChanged()));

    public static readonly DependencyProperty LogoHeightProperty = DependencyProperty.Register(
        nameof(LogoHeight), typeof(double), typeof(AartistLogo), new PropertyMetadata(22.0, (d, _) => ((AartistLogo)d).Render()), v => v is double h && h > 0);

    public static readonly DependencyProperty AuroraProperty = DependencyProperty.Register(
        nameof(Aurora), typeof(bool), typeof(AartistLogo), new PropertyMetadata(true, (d, _) => ((AartistLogo)d).Render()));

    /// <summary>False = the whole wordmark, true = only the first A. Changing it plays the fold or unfold.</summary>
    public bool IsCollapsed { get => (bool)GetValue(IsCollapsedProperty); set => SetValue(IsCollapsedProperty, value); }

    /// <summary>Height of the logo in pixels; the width follows (about 7.3 × the height when unfolded, 2 × when folded).</summary>
    public double LogoHeight { get => (double)GetValue(LogoHeightProperty); set => SetValue(LogoHeightProperty, value); }

    /// <summary>Colour and lights while it moves. Off = the same movement in plain blue.</summary>
    public bool Aurora { get => (bool)GetValue(AuroraProperty); set => SetValue(AuroraProperty, value); }

    private readonly Ellipse[] _lights;
    private readonly Brush _bloomIn, _bloomOut;

    private double _p;              // 0 = whole wordmark … 1 = folded to the first A
    private bool _expanding;        // which of the two timelines _p is read with
    private bool _running;
    private TimeSpan? _startedAt;   // set by the first frame of a run
    private TimeSpan _lastFrame;

    public AartistLogo()
    {
        InitializeComponent();
        _lights = [Light0, Light1, Light2, Light3, Light4];
        _bloomIn = (Brush)Resources["LightViolet"];
        _bloomOut = (Brush)Resources["LightPink"];
        Loaded += (_, _) => Snap();
        Unloaded += (_, _) => Snap();
        Snap();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Clock
    // ════════════════════════════════════════════════════════════════════════

    private void OnCollapsedChanged()
    {
        if (_running) return; // the run in flight finishes first; OnRendering then starts the way back
        if (!IsLoaded || !IsVisible || !SystemParameters.ClientAreaAnimation)
        {
            Snap();
            return;
        }
        Start();
    }

    /// <summary>Stops any run and shows the state <see cref="IsCollapsed"/> asks for, without motion.</summary>
    private void Snap()
    {
        Stop();
        _expanding = false;
        _p = IsCollapsed ? 1 : 0;
        Render();
    }

    private void Start()
    {
        if ((_p >= 1) == IsCollapsed) return; // already there
        _expanding = !IsCollapsed;
        _startedAt = null;
        _running = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Stop()
    {
        if (_running) CompositionTarget.Rendering -= OnRendering;
        _running = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // The frame's own timestamp is the clock (as in PixelLogoLoader); Rendering can fire twice per frame.
        var frame = ((RenderingEventArgs)e).RenderingTime;
        if (frame == _lastFrame) return;
        _lastFrame = frame;
        _startedAt ??= frame;

        double x = Math.Clamp((frame - _startedAt.Value).TotalMilliseconds / (_expanding ? ExpandMs : CollapseMs), 0, 1);
        _p = _expanding ? 1 - x : x;
        Render();
        if (x < 1) return;

        Stop();
        Start(); // only does something if IsCollapsed changed again while this run was in flight
    }

    // ════════════════════════════════════════════════════════════════════════
    // One frame
    // ════════════════════════════════════════════════════════════════════════

    private void Render()
    {
        bool expanding = _expanding;
        double p = _p;
        double x = expanding ? 1 - p : p; // how far this run has got, 0 → 1
        double duration = expanding ? ExpandMs : CollapseMs;

        // Where every part is now, and a moment before and after – the difference is its speed.
        Span<double> now = stackalloc double[Tracks.Length];
        Span<double> ahead = stackalloc double[Tracks.Length];
        Span<double> behind = stackalloc double[Tracks.Length];
        double pa = Math.Min(p + 0.004, 1), pb = Math.Max(p - 0.004, 0);
        Travelled(now, p, expanding);
        Travelled(ahead, pa, expanding);
        Travelled(behind, pb, expanding);
        double perMs = (expanding ? -1 : 1) / ((pa - pb) * duration); // logo units per ms, + = travelling left
        double speedA = (ahead[SecondA] - behind[SecondA]) * perMs;
        double speedR = (ahead[LetterR] - behind[LetterR]) * perMs;
        double speedT = (ahead[LetterT] - behind[LetterT]) * perMs;

        // Size: the artwork is scaled to LogoHeight; the Width follows the right end of the logo with a small lag.
        double zoom = LogoHeight / ArtHeight;
        Zoom.ScaleX = Zoom.ScaleY = zoom;
        Travelled(behind, Math.Clamp(p - LayoutLag, 0, 1), expanding);
        Width = (EdgeMargin + Math.Max(Tip(behind), MarkEdge)) * zoom;
        Height = ArtHeight * zoom;

        // Letters: each layer slides inside the one before it and leans with its own speed.
        SlideA2.X = -now[SecondA];
        SlideR.X = -now[LetterR];
        SlideT.X = -now[LetterT];
        LeanA2.AngleX = -Lean * speedA;
        LeanR.AngleX = -Lean * speedR;
        LeanT.AngleX = -Lean * speedT;

        // After the letters have landed, the mark dips once to the left and comes back (folding only).
        double nod = expanding ? 0 : -Ring(Seg(p, 0.70, 1), Nod);
        SlideMark.X = nod;
        LeanMark.AngleX = nod * 0.7;

        // i, s, t: fade out over the last stretch before they are fully behind the T stem.
        Place(GlyphI, SlideSmallI, now[SmallI], Tracks[SmallI], expanding);
        Place(GlyphS, SlideSmallS, now[SmallS], Tracks[SmallS], expanding);
        Place(GlyphT, SlideSmallT, now[SmallT], Tracks[SmallT], expanding);

        // Colour: a letter is tinted by how fast it travels across the control; the gradient itself stays where it is
        // (each brush is shifted back by what its letter has travelled), so the hue changes as the letter passes.
        bool aurora = Aurora;
        double travelA = now[SecondA], travelR = travelA + now[LetterR], travelT = travelR + now[LetterT];
        TintShiftA2.X = travelA;
        TintShiftR.X = travelR;
        TintShiftT.X = travelT;
        TintA2.Opacity = aurora ? Tint(speedA) : 0;
        TintR.Opacity = aurora ? Tint(speedA + speedR) : 0;
        TintT.Opacity = aurora ? Tint(speedA + speedR + speedT) : 0;
        // The first A takes a soft charge of colour while its neighbour arrives or leaves.
        double charge = expanding ? Seg(x, 0, 0.55) : Seg(p, 0.22, 0.9);
        TintA1.Opacity = aurora ? 0.7 * Math.Pow(Math.Sin(Math.PI * charge), 1.3) : 0;

        // Lights: lit around the moving end of the logo, already large – only their opacity rises and falls.
        Glow.Visibility = aurora ? Visibility.Visible : Visibility.Collapsed;
        if (!aurora) return;
        double front = Tip(now) - 30;
        double energy = Math.Pow(Math.Sin(Math.PI * Seg(x, 0.02, 0.92)), 0.8);
        for (int i = 0; i < _lights.Length; i++)
        {
            double d = (front - LightX[i]) / LightReach;
            _lights[i].Opacity = LightPeak * energy * Math.Exp(-d * d);
            SetLeft(_lights[i], LightX[i] + (0.18 * (front - LightX[i])) - (_lights[i].Width / 2));
        }

        // Bloom: one wide light where the movement ends – around the mark when folding, around "ist" when unfolding.
        double bloom = expanding ? Seg(x, 0.28, 0.85) : Seg(p, 0.5, 1);
        Bloom.Fill = expanding ? _bloomOut : _bloomIn;
        Bloom.Opacity = (expanding ? 0.55 : 0.75) * Math.Pow(Math.Sin(Math.PI * bloom), 1.2);
        BloomScale.ScaleX = BloomScale.ScaleY = 0.88 + (0.24 * bloom);
        SetLeft(Bloom, (expanding ? 585 : 100) - (Bloom.Width / 2));
        SetTop(Bloom, (expanding ? 52 : 45) - (Bloom.Height / 2));
    }

    private static void Place(UIElement glyph, TranslateTransform slide, double travelled, Track track, bool expanding)
    {
        slide.X = -travelled;
        glyph.Opacity = 1 - Seg(travelled / (expanding ? track.TravelOut : track.Travel), 0.6, 1);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Timeline maths
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>How far every part has slid at <paramref name="p"/> (0 = whole wordmark, 1 = folded).</summary>
    private static void Travelled(Span<double> into, double p, bool expanding)
    {
        double x = expanding ? 1 - p : p;
        for (int i = 0; i < Tracks.Length; i++)
        {
            var track = Tracks[i];
            double local = expanding ? Seg(x, track.OutFrom, track.OutTo) : Seg(x, track.InFrom, track.InTo);
            double eased = track.Soft ? Bezier(local, 0.45, 0, 0.55, 1) : Bezier(local, 0.5, 0, 0.1, 1);
            into[i] = expanding ? track.TravelOut * (1 - eased) : track.Travel * eased;
        }
    }

    /// <summary>Right end of the speed line (logo units) for the given slides.</summary>
    private static double Tip(ReadOnlySpan<double> travelled) =>
        TipAtRest - travelled[SecondA] - travelled[LetterR] - travelled[LetterT];

    private static double Tint(double speed) => Math.Pow(Math.Clamp(Math.Abs(speed) / TintAt, 0, 1), 0.85);

    /// <summary>0 before <paramref name="from"/>, 1 after <paramref name="to"/>, linear in between.</summary>
    private static double Seg(double value, double from, double to) => Math.Clamp((value - from) / (to - from), 0, 1);

    /// <summary>One short, damped swing that starts and ends at 0.</summary>
    private static double Ring(double t, double amplitude) =>
        amplitude * Math.Exp(-4.5 * t) * Math.Sin(2 * Math.PI * 1.25 * t) * (1 - t);

    /// <summary>CSS-style cubic-bezier(x1, y1, x2, y2) – the same thing as a WPF KeySpline.</summary>
    private static double Bezier(double x, double x1, double y1, double x2, double y2)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double lo = 0, hi = 1, t = x;
        for (int i = 0; i < 30; i++)
        {
            double at = Curve(t, x1, x2);
            if (Math.Abs(at - x) < 1e-6) break;
            if (at < x) lo = t; else hi = t;
            t = (lo + hi) / 2;
        }
        return Curve(t, y1, y2);

        static double Curve(double s, double a, double b)
        {
            double c1 = 3 * a, c2 = (3 * (b - a)) - c1, c3 = 1 - c1 - c2;
            return ((((c3 * s) + c2) * s) + c1) * s;
        }
    }
}
