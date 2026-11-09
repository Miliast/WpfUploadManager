using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LogoLoader;

public enum LoaderTheme
{
    /// <summary>Dark or light pill, whichever gives the logo more contrast.</summary>
    Auto,
    Dark,
    Light,
}

/// <summary>
/// "Loading…" pill whose logo is built from pixel tiles: they fly in on springs along curved paths, a light sweep runs
/// over the finished logo, then the tiles drift off and the loop starts again. Any <see cref="ImageSource"/> works
/// (PNG/JPG/ICO via <see cref="FromFile"/>, or a XAML DrawingImage); transparent PNGs look best, a plain background is removed.
/// <para>
/// Usage: <c>new PixelLogoLoader { Source = PixelLogoLoader.FromFile("logo.png"), Text = "Uploading" }</c>, or in XAML
/// <c>&lt;ll:PixelLogoLoader Source="Assets/logo.png" IsActive="{Binding IsBusy}" /&gt;</c>.
/// Setting <see cref="IsActive"/> to false lets the logo land first and then stops. With Windows animations turned off
/// it shows the finished logo without motion. Without a <see cref="Source"/> it shows a pixel pumpkin.
/// </para>
/// </summary>
public sealed class PixelLogoLoader : Border
{
    // ── Timeline of one loop (seconds) ─────────────────────────────────────
    private const double AssembleSpread = 0.8;   // first → last tile launching
    private const double ShimmerStart = 1.5;     // light sweep over the finished logo
    private const double ShimmerTime = 0.85;
    private const double StopFrom = 2.35;        // a requested stop lands here: assembled, sweep done, nothing moving
    private const double ExitStart = 2.7;
    private const double ExitSpread = 0.55;
    private const double ExitTime = 0.62;
    private const double Cycle = 4.2;            // leaves a short empty beat before the next loop
    private const double GhostLag = 0.022;       // motion-blur copies trail this far behind (×1, ×2)

    // ── Springs: frequency (Hz) and damping ratio (< 1 overshoots) ─────────
    private const double MoveFrequency = 1.7, MoveDamping = 0.62;   // ~8 % overshoot, settles in ~0.6 s
    private const double PopFrequency = 2.2, PopDamping = 0.45;     // size: a livelier ~20 % pop

    // ── Look ───────────────────────────────────────────────────────────────
    private const double TileFill = 0.8;         // tile edge / grid pitch (the rest is the gap)
    private const double CornerRatio = 0.26;     // corner radius / tile edge
    private const int Supersample = 8;           // image pixels sampled per tile edge
    private const double MinCoverage = 0.14;     // cells that are less covered than this stay empty

    private static readonly Color DarkPill = Color.FromRgb(0x1C, 0x1C, 0x1F);
    private static readonly Color LightPill = Colors.White;
    private static readonly Color LightBorder = Color.FromRgb(0xE6, 0xE8, 0xEC);
    private static readonly Color LightText = Color.FromRgb(0x0B, 0x0D, 0x12);

    // Fallback when no Source is set: s = stem, o = skin, y = face.
    private static readonly string[] Pumpkin = ["...s...", ".ooooo.", "ooyoyoo", "ooooooo", "oyyyyyo", "ooyyyoo", ".ooooo.", "..ooo.."];

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(PixelLogoLoader), new PropertyMetadata(null, OnLogoChanged));

    public static readonly DependencyProperty ResolutionProperty = DependencyProperty.Register(
        nameof(Resolution), typeof(int), typeof(PixelLogoLoader), new PropertyMetadata(12, OnLogoChanged), v => v is >= 3 and <= 48);

    public static readonly DependencyProperty LogoSizeProperty = DependencyProperty.Register(
        nameof(LogoSize), typeof(double), typeof(PixelLogoLoader), new PropertyMetadata(88.0, OnLogoChanged), v => v is double d && d > 0);

    public static readonly DependencyProperty RemoveBackgroundProperty = DependencyProperty.Register(
        nameof(RemoveBackground), typeof(bool), typeof(PixelLogoLoader), new PropertyMetadata(true, OnLogoChanged));

    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(LoaderTheme), typeof(PixelLogoLoader), new PropertyMetadata(LoaderTheme.Auto, (d, _) => ((PixelLogoLoader)d).ApplyTheme()));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(PixelLogoLoader), new PropertyMetadata("Loading", (d, _) => ((PixelLogoLoader)d).ApplyLayout()));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(PixelLogoLoader), new PropertyMetadata(true, (d, _) => ((PixelLogoLoader)d).UpdateClock()));

    /// <summary>The logo. Transparent PNGs work best; a uniform background colour is removed (see <see cref="RemoveBackground"/>).</summary>
    public ImageSource? Source { get => (ImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <summary>Tiles along the longer side of the logo (3–48). Fewer = chunkier pixel look, more = more detail.</summary>
    public int Resolution { get => (int)GetValue(ResolutionProperty); set => SetValue(ResolutionProperty, value); }

    /// <summary>Size of the logo's longer side in pixels; the text and pill scale with it.</summary>
    public double LogoSize { get => (double)GetValue(LogoSizeProperty); set => SetValue(LogoSizeProperty, value); }

    /// <summary>Treat an opaque, uniform background (e.g. white around a JPG logo) as transparent.</summary>
    public bool RemoveBackground { get => (bool)GetValue(RemoveBackgroundProperty); set => SetValue(RemoveBackgroundProperty, value); }

    public LoaderTheme Theme { get => (LoaderTheme)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }

    /// <summary>The word before the animated dots. Empty = logo only.</summary>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>Animates while true. Switching it off lets the logo assemble/land first, then stops.</summary>
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    /// <summary>Loads an image file fully into memory (the file isn't kept locked) – e.g. right after a user uploaded a logo.</summary>
    public static BitmapImage FromFile(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(Path.GetFullPath(path));
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.EndInit();
        image.Freeze();
        return image;
    }

    // ════════════════════════════════════════════════════════════════════════
    // State
    // ════════════════════════════════════════════════════════════════════════

    private readonly record struct Cell(int Col, int Row, Color Color, double Coverage);

    private readonly record struct Pose(Vector Offset, double Scale, double Angle, double Opacity, double Glow);

    private sealed class Tile
    {
        public required Point Home;          // centre in layer coordinates
        public required double Size;         // edge length when resting
        public required Brush Brush;
        public required double Diagonal;     // 0 = bottom-left corner of the logo, 1 = top-right
        public double In, Out;               // launch times within the loop
        public Vector From, Arc, To, Curl;   // flight paths, new every loop
        public double SpinIn, SpinOut;       // degrees
    }

    private readonly TileLayer _layer;
    private readonly TextBlock _text = new() { FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Run _label = new("Loading");
    private readonly Run[] _dots = [new("."), new("."), new(".")];
    private readonly SolidColorBrush[] _dotBrushes = [new(), new(), new()];
    private readonly Brush _glow = Frozen(Colors.White);
    private readonly Random _random = new();
    private readonly Stopwatch _clock = new();
    private List<Tile> _tiles = [];
    private Pose[] _poses = [];
    private bool _prefersDark = true, _running, _stopRequested;
    private Color _textColor = Colors.White;
    private double _t, _lastT = double.MaxValue;
    private TimeSpan _lastFrame;

    public PixelLogoLoader()
    {
        _layer = new TileLayer(this) { VerticalAlignment = VerticalAlignment.Center };
        _text.Inlines.Add(_label);
        for (int i = 0; i < _dots.Length; i++)
        {
            _dots[i].Foreground = _dotBrushes[i];
            _text.Inlines.Add(_dots[i]);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_layer);
        row.Children.Add(_text);
        Child = row;
        SnapsToDevicePixels = true;

        // Fully rounded ends; the clip makes incoming tiles appear from inside the pill.
        SizeChanged += (_, _) =>
        {
            double radius = ActualHeight / 2;
            CornerRadius = new CornerRadius(radius);
            Clip = new RectangleGeometry(new Rect(RenderSize), radius, radius);
        };
        Loaded += (_, _) => UpdateClock();
        Unloaded += (_, _) => UpdateClock();
        IsVisibleChanged += (_, _) => UpdateClock();

        Rebuild();
    }

    private static void OnLogoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PixelLogoLoader)d).Rebuild();

    // ════════════════════════════════════════════════════════════════════════
    // Logo → tiles
    // ════════════════════════════════════════════════════════════════════════

    private void Rebuild()
    {
        // A web image still downloading: build again once it's there.
        if (Source is BitmapSource { IsDownloading: true } downloading)
        {
            downloading.DownloadCompleted += (_, _) => Rebuild();
            return;
        }

        var (cells, cols, rows) = Source is { } source ? Sample(source, Resolution, RemoveBackground) : ([], 0, 0);
        if (cells.Count == 0) (cells, cols, rows) = PumpkinCells();

        double pitch = LogoSize / Math.Max(cols, rows);
        double width = cols * pitch, height = rows * pitch;
        _layer.Width = width;
        _layer.Height = height;

        var tiles = new List<Tile>(cells.Count);
        double darkScore = 0, lightScore = 0;
        foreach (var cell in cells)
        {
            var home = new Point((cell.Col + 0.5) * pitch, (cell.Row + 0.5) * pitch);
            tiles.Add(new Tile
            {
                Home = home,
                // Partly covered edge cells get smaller tiles: the pixel version of anti-aliasing.
                Size = pitch * TileFill * Math.Clamp(Math.Sqrt(cell.Coverage / 0.8), 0.45, 1),
                Brush = Frozen(cell.Color),
                Diagonal = ((home.X / width) + (1 - (home.Y / height))) / 2,
            });
            double lum = Luminance(cell.Color), weight = cell.Coverage;
            darkScore += weight * Math.Log(Contrast(lum, Luminance(DarkPill)));
            lightScore += weight * Math.Log(Contrast(lum, Luminance(LightPill)));
        }
        _tiles = tiles;
        _poses = new Pose[tiles.Count];
        _prefersDark = darkScore >= lightScore;

        Reshuffle();
        ApplyTheme();
        ApplyLayout();
        _layer.InvalidateVisual();
        UpdateClock();
    }

    /// <summary>Renders the image small (×8 per tile), then averages each cell: colour weighted by alpha, coverage = mean alpha.</summary>
    private static (List<Cell> Cells, int Cols, int Rows) Sample(ImageSource source, int resolution, bool removeBackground)
    {
        double w = source.Width, h = source.Height;
        if (!(w > 0 && h > 0)) return ([], 0, 0);
        int cols = w >= h ? resolution : Math.Max(1, (int)Math.Round(resolution * w / h));
        int rows = w >= h ? Math.Max(1, (int)Math.Round(resolution * h / w)) : resolution;
        int pw = cols * Supersample, ph = rows * Supersample;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) dc.DrawImage(source, new Rect(0, 0, pw, ph));
        var bitmap = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var px = new byte[pw * ph * 4];
        bitmap.CopyPixels(px, pw * 4, 0);

        if (removeBackground) StripBackground(px, pw, ph);

        var cells = new List<Cell>();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                double a = 0, r = 0, g = 0, b = 0;
                for (int y = row * Supersample; y < (row + 1) * Supersample; y++)
                {
                    for (int x = col * Supersample; x < (col + 1) * Supersample; x++)
                    {
                        int i = ((y * pw) + x) * 4; // premultiplied BGRA
                        b += px[i];
                        g += px[i + 1];
                        r += px[i + 2];
                        a += px[i + 3];
                    }
                }
                double coverage = a / (255.0 * Supersample * Supersample);
                if (coverage < MinCoverage) continue;
                // Σ premultiplied / Σ alpha = alpha-weighted average of the straight colour.
                var color = Color.FromRgb(ToByte(r / a * 255), ToByte(g / a * 255), ToByte(b / a * 255));
                cells.Add(new Cell(col, row, color, coverage));
            }
        }
        return (cells, cols, rows);
    }

    /// <summary>If all four corners share one opaque colour, fade out every pixel close to it (soft edge, no hard halo).</summary>
    private static void StripBackground(byte[] px, int width, int height)
    {
        int[] corners = [0, (width - 1) * 4, (width * (height - 1)) * 4, ((width * height) - 1) * 4];
        if (corners.Any(i => px[i + 3] < 250)) return; // already transparent
        double br = corners.Average(i => px[i + 2]), bg = corners.Average(i => px[i + 1]), bb = corners.Average(i => px[i]);
        if (corners.Any(i => Math.Abs(px[i + 2] - br) > 24 || Math.Abs(px[i + 1] - bg) > 24 || Math.Abs(px[i] - bb) > 24)) return;

        for (int i = 0; i < px.Length; i += 4)
        {
            double distance = Math.Max(Math.Abs(px[i + 2] - br), Math.Max(Math.Abs(px[i + 1] - bg), Math.Abs(px[i] - bb)));
            double keep = SmoothStep(18, 64, distance);
            for (int k = 0; k < 4; k++) px[i + k] = ToByte(px[i + k] * keep); // premultiplied: scale colour and alpha together
        }
    }

    private static (List<Cell> Cells, int Cols, int Rows) PumpkinCells()
    {
        Color[] skin = [Color.FromRgb(0xE3, 0xA2, 0x58), Color.FromRgb(0xDA, 0x95, 0x4C), Color.FromRgb(0xE9, 0xAE, 0x63)];
        var cells = new List<Cell>();
        for (int r = 0; r < Pumpkin.Length; r++)
        {
            for (int c = 0; c < Pumpkin[r].Length; c++)
            {
                Color? color = Pumpkin[r][c] switch
                {
                    's' => Color.FromRgb(0x7D, 0xB8, 0x78),
                    'y' => Color.FromRgb(0xF6, 0xDA, 0x82),
                    'o' => skin[((r * 3) + (c * 7)) % skin.Length],
                    _ => null,
                };
                if (color is { } value) cells.Add(new Cell(c, r, value, 1));
            }
        }
        return (cells, Pumpkin[0].Length, Pumpkin.Length);
    }

    /// <summary>New flight paths every loop, so no two loops look the same. Distances scale with the logo.</summary>
    private void Reshuffle()
    {
        double k = LogoSize / 88;
        foreach (var tile in _tiles)
        {
            // In: a sweep from the bottom-left; each tile comes from the lower left on a slight arc.
            tile.In = (tile.Diagonal * AssembleSpread) + (_random.NextDouble() * 0.08);
            var from = Direction(110 + (_random.NextDouble() * 90)) * (34 + (_random.NextDouble() * 30)) * k;
            tile.From = from;
            tile.Arc = Perpendicular(from) * (_random.NextDouble() < 0.5 ? -1 : 1) * (0.18 + (_random.NextDouble() * 0.22));
            tile.SpinIn = (_random.NextDouble() < 0.5 ? -1 : 1) * (25 + (_random.NextDouble() * 45));

            // Out: the leading edge (top-right) leaves first, drifting up and to the right like a stream.
            tile.Out = ExitStart + ((1 - tile.Diagonal) * ExitSpread) + (_random.NextDouble() * 0.06);
            var to = Direction(-80 + (_random.NextDouble() * 75)) * (30 + (_random.NextDouble() * 40)) * k;
            tile.To = to;
            tile.Curl = Perpendicular(to) * (_random.NextDouble() < 0.5 ? -1 : 1) * (0.1 + (_random.NextDouble() * 0.2));
            tile.SpinOut = (_random.NextDouble() < 0.5 ? -1 : 1) * (40 + (_random.NextDouble() * 60));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Motion
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Where a tile is at time <paramref name="t"/> of the loop. Pure function of time, so motion blur can sample the past.</summary>
    private static Pose Evaluate(Tile tile, double t, Size logo)
    {
        double since = t - tile.In;
        if (since <= 0) return new Pose(tile.From, 0.15, tile.SpinIn, 0, 0);

        double leaving = t - tile.Out;
        if (leaving <= 0)
        {
            // Arrive: position and rotation on one spring, size on a bouncier one; the arc bulges mid-flight.
            double s = Spring(since, MoveFrequency, MoveDamping);
            double pop = Spring(since, PopFrequency, PopDamping);
            var offset = (tile.From * (1 - s)) + (tile.Arc * tile.From.Length * Math.Sin(Math.PI * Math.Clamp(s, 0, 1)));
            double glow = Shimmer(tile, t, logo);
            return new Pose(offset, (0.15 + (0.85 * pop)) * (1 + (0.07 * glow)), tile.SpinIn * (1 - s), SmoothStep(0, 0.16, since), glow);
        }

        // Leave: accelerate away (ease-in), curl sideways, shrink and fade at the very end.
        double e = Math.Min(leaving / ExitTime, 1);
        double travel = e * e * e;
        var drift = (tile.To * travel) + (tile.Curl * tile.To.Length * Math.Sin(Math.PI * e) * 0.5);
        return new Pose(drift, 1 - (0.7 * e * e), tile.SpinOut * travel, 1 - SmoothStep(0.45, 1, e), 0);
    }

    /// <summary>A soft diagonal band of light that sweeps over the resting logo once per loop (0…1 brightness).</summary>
    private static double Shimmer(Tile tile, double t, Size logo)
    {
        double p = (t - ShimmerStart) / ShimmerTime;
        if (p <= 0 || p >= 1) return 0;
        double band = -0.25 + (1.5 * EaseInOut(p));                               // band centre travels past both corners
        double here = ((tile.Home.X / logo.Width) + (tile.Home.Y / logo.Height)) / 2; // top-left → bottom-right diagonal
        double d = (here - band) / 0.13;
        return Math.Exp(-d * d);
    }

    /// <summary>Damped spring from 0 to 1 after <paramref name="t"/> seconds (analytic, so it can be evaluated at any time).</summary>
    private static double Spring(double t, double frequency, double damping)
    {
        double w = 2 * Math.PI * frequency;
        double wd = w * Math.Sqrt(1 - (damping * damping));
        return 1 - (Math.Exp(-damping * w * t) * (Math.Cos(wd * t) + (damping * w / wd * Math.Sin(wd * t))));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Clock
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateClock()
    {
        bool canRun = IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation && _tiles.Count > 0;
        if (!canRun || (!IsActive && !_running))
        {
            Stop();
            return;
        }
        if (IsActive)
        {
            _stopRequested = false;
            if (!_running) Start();
        }
        else
        {
            _stopRequested = true; // keep going until the logo has landed (see OnRendering)
        }
    }

    private void Start()
    {
        _running = true;
        _clock.Restart();
        _lastT = double.MaxValue;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Stop()
    {
        if (_running) CompositionTarget.Rendering -= OnRendering;
        _running = _stopRequested = false;
        _clock.Stop();
        for (int i = 0; i < _dotBrushes.Length; i++) _dotBrushes[i].Color = _textColor;
        _layer.InvalidateVisual();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can fire more than once per frame; only work once per new frame.
        if (e is RenderingEventArgs args)
        {
            if (args.RenderingTime == _lastFrame) return;
            _lastFrame = args.RenderingTime;
        }

        double now = _clock.Elapsed.TotalSeconds;
        _t = now % Cycle;
        if (_t < _lastT) Reshuffle(); // a new loop just started – every tile is still hidden
        _lastT = _t;

        if (_stopRequested && _t is >= StopFrom and < ExitStart)
        {
            Stop();
            return;
        }

        // Dots: a soft wave instead of on/off blinking.
        for (int i = 0; i < _dotBrushes.Length; i++)
        {
            double v = 0.5 + (0.5 * Math.Sin((now * 2 * Math.PI / 1.25) - (i * 0.9)));
            _dotBrushes[i].Color = Color.FromArgb(ToByte(255 * (0.25 + (0.75 * v * v))), _textColor.R, _textColor.G, _textColor.B);
        }
        _layer.InvalidateVisual();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Drawing
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>All tiles in one element: motion-blur copies first, then the tiles on top.</summary>
    private void Render(DrawingContext dc)
    {
        if (!_running)
        {
            foreach (var tile in _tiles) DrawTile(dc, tile, new Pose(default, 1, 0, 1, 0), 1);
            return;
        }

        var logo = new Size(_layer.Width, _layer.Height);
        for (int i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i];
            var pose = _poses[i] = Evaluate(tile, _t, logo);
            if (pose.Opacity <= 0) continue;
            for (int k = 2; k >= 1; k--)
            {
                var past = Evaluate(tile, _t - (k * GhostLag), logo);
                double speed = (pose.Offset - past.Offset).Length / k; // px per lag step
                double strength = Math.Clamp((speed - 1.5) / 6, 0, 1);
                if (strength > 0) DrawTile(dc, tile, past with { Scale = past.Scale * 0.92, Glow = 0 }, past.Opacity * strength * (k == 1 ? 0.3 : 0.15));
            }
        }
        for (int i = 0; i < _tiles.Count; i++) DrawTile(dc, _tiles[i], _poses[i], _poses[i].Opacity);
    }

    private void DrawTile(DrawingContext dc, Tile tile, Pose pose, double opacity)
    {
        if (opacity < 0.004 || pose.Scale < 0.02) return;
        double size = tile.Size * pose.Scale, radius = size * CornerRatio;
        var rect = new Rect(-size / 2, -size / 2, size, size);
        var matrix = Matrix.Identity;
        matrix.Rotate(pose.Angle);
        matrix.Translate(tile.Home.X + pose.Offset.X, tile.Home.Y + pose.Offset.Y);

        dc.PushTransform(new MatrixTransform(matrix));
        dc.PushOpacity(Math.Min(opacity, 1));
        dc.DrawRoundedRectangle(tile.Brush, null, rect, radius, radius);
        if (pose.Glow > 0.01)
        {
            dc.PushOpacity(pose.Glow * 0.55);
            dc.DrawRoundedRectangle(_glow, null, rect, radius, radius);
            dc.Pop();
        }
        dc.Pop();
        dc.Pop();
    }

    /// <summary>Hosts the tiles; sized to the logo, but tiles may fly outside it (the pill clips them).</summary>
    private sealed class TileLayer(PixelLogoLoader owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawingContext) => owner.Render(drawingContext);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Theme & layout
    // ════════════════════════════════════════════════════════════════════════

    private void ApplyTheme()
    {
        bool dark = Theme switch { LoaderTheme.Dark => true, LoaderTheme.Light => false, _ => _prefersDark };
        Background = Frozen(dark ? DarkPill : LightPill);
        BorderBrush = dark ? null : Frozen(LightBorder);
        BorderThickness = new Thickness(dark ? 0 : 1);
        _textColor = dark ? Colors.White : LightText;
        _text.Foreground = Frozen(_textColor);
        foreach (var brush in _dotBrushes) brush.Color = _textColor;
    }

    private void ApplyLayout()
    {
        double s = LogoSize;
        bool hasText = !string.IsNullOrEmpty(Text);
        _label.Text = Text ?? "";
        _text.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        _text.FontSize = s * 0.52;
        _text.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        _text.Margin = new Thickness(0, 0, 0, s * 0.05);
        _layer.Margin = new Thickness(0, 0, hasText ? s * 0.38 : 0, 0);
        Padding = hasText ? new Thickness(s * 0.42, s * 0.26, s * 0.6, s * 0.26) : new Thickness(s * 0.4);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════════

    private static Vector Direction(double degrees) => new(Math.Cos(degrees * Math.PI / 180), Math.Sin(degrees * Math.PI / 180));

    private static Vector Perpendicular(Vector v)
    {
        var p = new Vector(-v.Y, v.X);
        if (p.Length > 0) p.Normalize();
        return p;
    }

    private static double SmoothStep(double from, double to, double x)
    {
        double t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - (2 * t));
    }

    private static double EaseInOut(double x) => x < 0.5 ? 4 * x * x * x : 1 - (Math.Pow((-2 * x) + 2, 3) / 2);

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>WCAG relative luminance.</summary>
    private static double Luminance(Color c)
    {
        static double Linear(byte v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return (0.2126 * Linear(c.R)) + (0.7152 * Linear(c.G)) + (0.0722 * Linear(c.B));
    }

    private static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);

#if LOADER_DEMO
    // ════════════════════════════════════════════════════════════════════════
    // Demo: choose or drop a logo, change resolution, toggle running
    // ════════════════════════════════════════════════════════════════════════

    [STAThread]
    public static void Main()
    {
        var loader = new PixelLogoLoader { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var choose = new Button { Content = "Choose logo…", Padding = new Thickness(14, 6, 14, 6) };
        var resolution = new Slider { Minimum = 6, Maximum = 32, Value = loader.Resolution, Width = 160, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        var resolutionLabel = new TextBlock { Text = $"{loader.Resolution} tiles", Width = 60, VerticalAlignment = VerticalAlignment.Center };
        var running = new CheckBox { Content = "Running", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        var hint = new TextBlock { Text = "…or drop an image anywhere", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };

        void Load(string path)
        {
            try { loader.Source = FromFile(path); }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or FormatException)
            {
                MessageBox.Show($"This file can't be used as a logo:\n{ex.Message}", "Logo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        choose.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff" };
            if (dialog.ShowDialog() == true) Load(dialog.FileName);
        };
        resolution.ValueChanged += (_, e) =>
        {
            loader.Resolution = (int)e.NewValue;
            resolutionLabel.Text = $"{loader.Resolution} tiles";
        };
        running.Checked += (_, _) => loader.IsActive = true;
        running.Unchecked += (_, _) => loader.IsActive = false;

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 24) };
        foreach (var element in new FrameworkElement[] { choose, hint, resolution, resolutionLabel, running })
        {
            element.Margin = new Thickness(8, 0, 8, 0);
            bar.Children.Add(element);
        }
        var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF5)) };
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(loader);

        var window = new Window
        {
            Title = "Pixel Logo Loader",
            Width = 760,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            AllowDrop = true,
            Content = root,
        };
        window.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) Load(files[0]);
        };
        new Application().Run(window);
    }
#endif
}
