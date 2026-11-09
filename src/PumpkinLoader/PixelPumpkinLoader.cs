using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace PumpkinLoader;

/// <summary>
/// Dark "Loading..." pill with a pixel pumpkin that assembles itself from flying tiles,
/// glows for a moment and scatters again, in a single class (no XAML, no NuGet packages).
/// <para>
/// Usage: <c>new PixelPumpkinLoader { Text = "Uploading" }</c>, or bind <see cref="IsActive"/>.
/// When inactive, or when Windows animations are turned off, it shows the finished pumpkin without motion.
/// </para>
/// </summary>
public sealed class PixelPumpkinLoader : Border
{
#if LOADER_DEMO
    [STAThread]
    public static void Main() => new Application().Run(new Window
    {
        Title = "Pixel Pumpkin Loader",
        Width = 680,
        Height = 340,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF5)),
        Content = new PixelPumpkinLoader
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    });
#endif

    // ── Pumpkin bitmap: s = stem, o = skin, y = glowing face, . = empty ──
    private static readonly string[] Map =
    [
        "...s...",
        ".ooooo.",
        "ooyoyoo",
        "ooooooo",
        "oyyyyyo",
        "ooyyyoo",
        ".ooooo.",
        "..ooo..",
    ];

    // ── Look ──
    private const double TileSize = 10;
    private const double TileGap = 2.5;
    private const double Step = TileSize + TileGap;

    private static readonly Color PillColor = Color.FromRgb(0x22, 0x22, 0x24);
    private static readonly Color StemColor = Color.FromRgb(0x7D, 0xB8, 0x78);
    private static readonly Color GhostColor = Color.FromRgb(0x5E, 0x35, 0x16); // dark brown motion trail
    private static readonly Color FaceDim = Color.FromRgb(0xEE, 0xC8, 0x6A);
    private static readonly Color FaceBright = Color.FromRgb(0xFF, 0xEE, 0x9E);
    private static readonly Color[] SkinColors =
    [
        Color.FromRgb(0xE3, 0xA2, 0x58),
        Color.FromRgb(0xDA, 0x95, 0x4C),
        Color.FromRgb(0xE9, 0xAE, 0x63),
    ];
    private static readonly Brush DotOn = Brushes.White;

    // ── Timeline of one loop (seconds) ──
    private const double Cycle = 4.4;
    private const double AssembleSpread = 0.9;  // delay between the first and the last tile flying in
    private const double FlyTime = 0.75;        // flight time of one tile
    private const double WaveStart = 1.75;      // shimmer that runs over the finished pumpkin
    private const double DisperseStart = 3.2;
    private const double DisperseSpread = 0.45;
    private const double DisperseTime = 0.55;
    private const double GhostLag = 0.07;       // the trail shows where the tile was this long ago
    private const double DotStep = 0.4;         // ".", "..", "..." then back to none

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(PixelPumpkinLoader),
        new PropertyMetadata(true, (d, _) => ((PixelPumpkinLoader)d).UpdateClock()));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(PixelPumpkinLoader),
        new PropertyMetadata("Loading", (d, e) => ((PixelPumpkinLoader)d)._label.Text = (string?)e.NewValue ?? ""));

    /// <summary>Runs the animation while true; shows a still pumpkin while false.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>The word before the animated dots.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private sealed class Pixel
    {
        public required Border Tile;
        public required Border Ghost;
        public required TranslateTransform TileMove;
        public required ScaleTransform TileScale;
        public required TranslateTransform GhostMove;
        public required ScaleTransform GhostScale;
        public required SolidColorBrush Fill;
        public required double Order;   // 0 = first to arrive/leave, 1 = last
        public required bool IsFace;
        public required double Phase;   // per-tile offset for the candle flicker
        public Vector From;             // where it flies in from (relative to its home)
        public Vector To;               // where it flies off to
    }

    private readonly List<Pixel> _pixels = [];
    private readonly Run _label = new("Loading");
    private readonly Run[] _dots = [new("."), new("."), new(".")];
    private readonly Brush _dotOff = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
    private readonly Random _random = new();
    private readonly Stopwatch _clock = new();
    private TimeSpan _lastFrame;
    private double _lastCycleTime = double.MaxValue;
    private bool _running;

    public PixelPumpkinLoader()
    {
        _dotOff.Freeze();
        Background = new SolidColorBrush(PillColor);
        Padding = new Thickness(36, 22, 52, 22);
        SnapsToDevicePixels = true;

        var pumpkin = BuildPumpkin();
        pumpkin.Margin = new Thickness(0, 0, 34, 0);
        pumpkin.VerticalAlignment = VerticalAlignment.Center;

        var text = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 46,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        };
        text.Inlines.Add(_label);
        text.Inlines.AddRange(_dots);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(pumpkin);
        row.Children.Add(text);
        Child = row;

        // Fully rounded ends, and clip so incoming tiles appear from "inside" the pill.
        SizeChanged += (_, _) =>
        {
            double radius = ActualHeight / 2;
            CornerRadius = new CornerRadius(radius);
            Clip = new RectangleGeometry(new Rect(RenderSize), radius, radius);
        };

        Loaded += (_, _) => UpdateClock();
        Unloaded += (_, _) => UpdateClock();
        IsVisibleChanged += (_, _) => UpdateClock();
        ShowStill();
    }

    private Canvas BuildPumpkin()
    {
        int rows = Map.Length, cols = Map[0].Length;
        var canvas = new Canvas { Width = cols * Step - TileGap, Height = rows * Step - TileGap };

        // Ghosts go in first so they are drawn underneath every real tile.
        var ghosts = new List<Border>();
        var tiles = new List<Border>();

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                char kind = Map[r][c];
                if (kind == '.')
                    continue;

                Color color = kind switch
                {
                    's' => StemColor,
                    'y' => FaceDim,
                    _ => SkinColors[(r * 3 + c * 7) % SkinColors.Length],
                };

                var (tile, tileMove, tileScale) = MakeTile(new SolidColorBrush(color), c, r);
                var ghostBrush = new SolidColorBrush(GhostColor);
                ghostBrush.Freeze();
                var (ghost, ghostMove, ghostScale) = MakeTile(ghostBrush, c, r);

                tiles.Add(tile);
                ghosts.Add(ghost);
                _pixels.Add(new Pixel
                {
                    Tile = tile,
                    Ghost = ghost,
                    TileMove = tileMove,
                    TileScale = tileScale,
                    GhostMove = ghostMove,
                    GhostScale = ghostScale,
                    Fill = (SolidColorBrush)tile.Background,
                    // Top-left arrives first, bottom-right last; the stem leads the way.
                    Order = kind == 's' ? 0 : (r + c * 0.45) / (rows - 1 + (cols - 1) * 0.45),
                    IsFace = kind == 'y',
                    Phase = _random.NextDouble() * Math.PI * 2,
                });
            }
        }

        foreach (var g in ghosts)
            canvas.Children.Add(g);
        foreach (var t in tiles)
            canvas.Children.Add(t);

        Reshuffle(canvas.Width, canvas.Height);
        return canvas;
    }

    private static (Border Tile, TranslateTransform Move, ScaleTransform Scale) MakeTile(Brush fill, int col, int row)
    {
        var move = new TranslateTransform();
        var scale = new ScaleTransform();
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(move);

        var tile = new Border
        {
            Width = TileSize,
            Height = TileSize,
            CornerRadius = new CornerRadius(2.5),
            Background = fill,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = group,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(tile, col * Step);
        Canvas.SetTop(tile, row * Step);
        return (tile, move, scale);
    }

    /// <summary>New random flight paths, so no two loops look the same.</summary>
    private void Reshuffle(double width, double height)
    {
        var center = new Point(width / 2, height / 2);
        foreach (var p in _pixels)
        {
            // Fly in mostly from below and the lower left (screen y points down: 40°..200°).
            double inAngle = (40 + _random.NextDouble() * 160) * Math.PI / 180;
            double inDistance = 30 + _random.NextDouble() * 35;
            p.From = new Vector(Math.Cos(inAngle), Math.Sin(inAngle)) * inDistance;

            // Fly off away from the middle, a little scattered and drifting upwards.
            var home = new Point(Canvas.GetLeft(p.Tile) + TileSize / 2, Canvas.GetTop(p.Tile) + TileSize / 2);
            Vector outward = home - center;
            if (outward.Length < 0.1)
                outward = new Vector(0, -1);
            outward.Normalize();
            double spin = (_random.NextDouble() - 0.5) * 1.2;
            var dir = new Vector(
                outward.X * Math.Cos(spin) - outward.Y * Math.Sin(spin),
                outward.X * Math.Sin(spin) + outward.Y * Math.Cos(spin));
            p.To = dir * (18 + _random.NextDouble() * 30) + new Vector(0, -10);
        }
    }

    // ── Clock ──

    private void UpdateClock()
    {
        bool shouldRun = IsActive && IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation;
        if (shouldRun == _running)
            return;

        _running = shouldRun;
        if (shouldRun)
        {
            _clock.Restart();
            _lastCycleTime = double.MaxValue;
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
            _clock.Stop();
            ShowStill();
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can fire more than once per frame; only draw once per new frame.
        if (e is RenderingEventArgs args)
        {
            if (args.RenderingTime == _lastFrame)
                return;
            _lastFrame = args.RenderingTime;
        }

        double now = _clock.Elapsed.TotalSeconds;
        double t = now % Cycle;
        if (t < _lastCycleTime && Child is StackPanel { Children: [Canvas canvas, ..] })
            Reshuffle(canvas.Width, canvas.Height); // a new loop just started
        _lastCycleTime = t;

        foreach (var p in _pixels)
        {
            var (offset, scale, opacity, flying) = Evaluate(p, t);
            Apply(p.Tile, p.TileMove, p.TileScale, offset, scale, opacity);

            if (flying)
            {
                // A darker copy trailing behind gives the "smear" of fast pixels.
                var (gOffset, gScale, gOpacity, _) = Evaluate(p, t - GhostLag);
                Apply(p.Ghost, p.GhostMove, p.GhostScale, gOffset, gScale * 0.9, gOpacity * 0.55);
            }
            else
            {
                p.Ghost.Opacity = 0;
            }

            if (p.IsFace)
                p.Fill.Color = Flicker(p, now);
        }

        int litDots = (int)(now / DotStep) % 4;
        for (int i = 0; i < _dots.Length; i++)
            _dots[i].Foreground = i < litDots ? DotOn : _dotOff;
    }

    /// <summary>Where a tile is at time <paramref name="t"/> within one loop.</summary>
    private static (Vector Offset, double Scale, double Opacity, bool Flying) Evaluate(Pixel p, double t)
    {
        double arrive = (t - p.Order * AssembleSpread) / FlyTime;
        double leave = (t - DisperseStart - p.Order * DisperseSpread) / DisperseTime;

        if (arrive <= 0)
            return (p.From, 0.2, 0, false);

        if (arrive < 1)
        {
            double e = EaseOutCubic(arrive);
            return (p.From * (1 - e), 0.25 + 0.75 * EaseOutBack(arrive), Math.Min(1, arrive * 3), true);
        }

        if (leave <= 0)
        {
            // Resting: a soft bump runs diagonally over the pumpkin once.
            double w = (t - WaveStart - p.Order * 0.5) / 0.3;
            double bump = w is > 0 and < 1 ? Math.Sin(Math.PI * w) : 0;
            return (default, 1 + 0.14 * bump, 1, false);
        }

        if (leave < 1)
        {
            double e = EaseInCubic(leave);
            return (p.To * e, 1 - 0.8 * e, 1 - leave * leave, true);
        }

        return (p.To, 0.2, 0, false);
    }

    private static void Apply(Border tile, TranslateTransform move, ScaleTransform scale, Vector offset, double size, double opacity)
    {
        move.X = offset.X;
        move.Y = offset.Y;
        scale.ScaleX = scale.ScaleY = size;
        tile.Opacity = opacity;
    }

    /// <summary>Candle-like glow: two slow sines plus a fast one, different per tile.</summary>
    private static Color Flicker(Pixel p, double time)
    {
        double f = 0.55
                   + 0.25 * Math.Sin(time * 2.1 + p.Phase)
                   + 0.15 * Math.Sin(time * 5.3 + p.Phase * 1.7)
                   + 0.05 * Math.Sin(time * 13.0 + p.Phase * 0.4);
        return Lerp(FaceDim, FaceBright, Math.Clamp(f, 0, 1));
    }

    /// <summary>Finished pumpkin, no motion: used when inactive or when Windows animations are off.</summary>
    private void ShowStill()
    {
        foreach (var p in _pixels)
        {
            Apply(p.Tile, p.TileMove, p.TileScale, default, 1, 1);
            p.Ghost.Opacity = 0;
            if (p.IsFace)
                p.Fill.Color = Lerp(FaceDim, FaceBright, 0.6);
        }

        foreach (var dot in _dots)
            dot.Foreground = DotOn;
    }

    // ── Math helpers ──

    private static double EaseOutCubic(double x) => 1 - Math.Pow(1 - x, 3);

    private static double EaseInCubic(double x) => x * x * x;

    private static double EaseOutBack(double x)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
