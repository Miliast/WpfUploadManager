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
/// "Loading…" pill whose logo is built from pixel tiles. Three phases: the tiles drift in softly once, then the logo
/// stays whole while a calm wave of light crosses it now and then, and on stop it comes to rest and raises
/// <see cref="Landed"/>. Any <see cref="ImageSource"/> works (PNG/JPG/ICO via <see cref="FromFile"/>, or a XAML
/// DrawingImage); transparent PNGs look best, a plain background is removed.
/// <para>
/// Usage: <c>new PixelLogoLoader { Source = PixelLogoLoader.FromFile("logo.png"), Text = "Uploading" }</c>, or in XAML
/// <c>&lt;ll:PixelLogoLoader Source="Assets/logo.png" IsActive="{Binding IsBusy}" /&gt;</c>.
/// Setting <see cref="IsActive"/> to false lets the logo land first and then stops. With Windows animations turned off
/// it shows the finished logo without motion. Without a <see cref="Source"/> it shows a pixel pumpkin.
/// </para>
/// </summary>
public sealed class PixelLogoLoader : Border
{
    // ── Timeline (seconds since the loader started) ────────────────────────
    // 1. Build-up, once: the tiles drift in softly, bottom-left first.
    // 2. Calm: the logo stays whole; now and then one soft wave of light runs over it.
    // 3. Landing: when IsActive goes off, the build-up/wave finishes, everything rests, Landed is raised.
    private const double AssembleSpread = 0.7;   // first → last tile setting off
    private const double Jitter = 0.08;          // random extra delay per tile, so the sweep isn't a ruler-straight line
    private const double FadeIn = 0.45;          // a tile appears this slowly – it is seen travelling instead of popping up
    private const double Settle = 0.9;           // after this long a tile rests exactly on its pixels
    private const double IntroEnd = AssembleSpread + Jitter + Settle;
    private const double WaveFirst = IntroEnd + 0.35;
    private const double WaveEvery = 2.8;        // one wave, then stillness
    private const double WaveTime = 1.2;
    private const double WaveWidth = 0.16;       // width of the band of light (share of the logo diagonal)

    // ── Soft arrival: a spring that barely overshoots (damping 0.9 ≈ 0.2 %) ─
    private const double MoveFrequency = 1.3, MoveDamping = 0.9;
    private const double StartScale = 0.82;      // tiles start almost full size – no burst from a point

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
        nameof(IsActive), typeof(bool), typeof(PixelLogoLoader), new PropertyMetadata(true, (d, _) => ((PixelLogoLoader)d).OnActiveChanged()));

    /// <summary>
    /// Raised after <see cref="IsActive"/> went off and the logo has come to rest (at once if nothing was moving).
    /// Hide the loader in response to this, not to your own busy flag – then it never disappears half built.
    /// </summary>
    public event EventHandler? Landed;

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
        public required Rect Rest;           // where it rests, on whole device pixels (crisp edges)
        public required Brush Brush;
        public required double Diagonal;     // 0 = bottom-left corner of the logo, 1 = top-right (build-up order)
        public required double Sweep;        // 0 = top-left, 1 = bottom-right (path of the wave)
        public double In;                    // when it sets off
        public Vector From;                  // where it drifts in from, new on every start
        public double Spin;                  // degrees, small
    }

    private static readonly Pose Resting = new(default, 1, 0, 1, 0);

    private readonly DrawingGroup _drawing = new();   // the tiles; refilled per frame without a layout pass
    private readonly TileLayer _layer;
    private readonly TextBlock _text = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Run _label = new("Loading");
    private readonly Run[] _dots = [new("."), new("."), new(".")];
    private readonly SolidColorBrush[] _dotBrushes = [new(), new(), new()];
    private readonly Brush _glow = Frozen(Colors.White);
    private readonly Random _random = new();
    private List<Tile> _tiles = [];
    private bool _prefersDark = true, _running, _stopRequested, _still;
    private Color _textColor = Colors.White;
    private double _t, _dpi = 1;
    private TimeSpan? _startedAt;
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
        UseLayoutRounding = true; // the tile layer starts on a whole pixel, so the resting tiles stay sharp

        // Fully rounded ends; the clip keeps drifting tiles inside the pill.
        SizeChanged += (_, _) =>
        {
            double radius = ActualHeight / 2;
            CornerRadius = new CornerRadius(radius);
            Clip = new RectangleGeometry(new Rect(RenderSize), radius, radius);
        };
        Loaded += (_, _) =>
        {
            if (VisualTreeHelper.GetDpi(this).DpiScaleX != _dpi) Rebuild(); // now the real monitor scaling is known
            UpdateClock();
        };
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

        // Everything in whole device pixels: grid pitch, tile edge and tile position. Otherwise the resting logo looks soft.
        double dpi = _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double pitchPx = Math.Max(2, Math.Round(LogoSize / Math.Max(cols, rows) * dpi));
        double fullPx = Math.Max(1, Math.Min(Math.Round(pitchPx * TileFill), pitchPx - 1)); // always at least 1 px gap
        double pitch = pitchPx / dpi;
        double width = cols * pitch, height = rows * pitch;
        _layer.Width = width;
        _layer.Height = height;

        var tiles = new List<Tile>(cells.Count);
        double darkScore = 0, lightScore = 0;
        foreach (var cell in cells)
        {
            // Partly covered edge cells get smaller tiles: the pixel version of anti-aliasing.
            double sizePx = Math.Max(1, Math.Round(fullPx * Math.Clamp(Math.Sqrt(cell.Coverage / 0.8), 0.45, 1)));
            double insetPx = Math.Round((pitchPx - sizePx) / 2);
            var rest = new Rect(((cell.Col * pitchPx) + insetPx) / dpi, ((cell.Row * pitchPx) + insetPx) / dpi, sizePx / dpi, sizePx / dpi);
            double x = (rest.X + (rest.Width / 2)) / width, y = (rest.Y + (rest.Height / 2)) / height;
            tiles.Add(new Tile
            {
                Rest = rest,
                Brush = Frozen(cell.Color),
                Diagonal = (x + (1 - y)) / 2,
                Sweep = (x + y) / 2,
            });
            double lum = Luminance(cell.Color), weight = cell.Coverage;
            darkScore += weight * Math.Log(Contrast(lum, Luminance(DarkPill)));
            lightScore += weight * Math.Log(Contrast(lum, Luminance(LightPill)));
        }
        _tiles = tiles;
        _prefersDark = darkScore >= lightScore;

        Shuffle();
        ApplyTheme();
        ApplyLayout();
        Redraw();
        UpdateClock();
    }

    /// <summary>Moving the window to a monitor with another scaling: put the tiles on that monitor's pixels.</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Rebuild();
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

    /// <summary>New drift paths on every start, so no two build-ups look the same. Distances scale with the logo.</summary>
    private void Shuffle()
    {
        double k = LogoSize / 88;
        foreach (var tile in _tiles)
        {
            // A sweep from the bottom-left; each tile drifts a short way in from the lower left and turns only a little.
            tile.In = (tile.Diagonal * AssembleSpread) + (_random.NextDouble() * Jitter);
            tile.From = Direction(120 + (_random.NextDouble() * 50)) * (10 + (_random.NextDouble() * 10)) * k;
            tile.Spin = (_random.NextDouble() < 0.5 ? -1 : 1) * (4 + (_random.NextDouble() * 6));
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Motion
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Where a tile is <paramref name="t"/> seconds after the start. <paramref name="lift"/> = how far the wave raises a tile.</summary>
    private static Pose Evaluate(Tile tile, double t, double lift)
    {
        double since = t - tile.In;
        if (since <= 0) return default; // not there yet (opacity 0)

        if (since >= Settle)
        {
            // Calm: exactly on its pixels, unless the wave is passing – then it brightens, rises and grows a touch.
            double glow = Wave(tile, t);
            return glow < 0.004 ? Resting : new Pose(new Vector(0, -lift * glow), 1 + (0.06 * glow), 0, 1, glow);
        }

        // Build-up: drift, turn and size on one soft spring; the slow fade-in lets the eye follow the whole way.
        double s = Spring(since, MoveFrequency, MoveDamping);
        return new Pose(tile.From * (1 - s), StartScale + ((1 - StartScale) * s), tile.Spin * (1 - s), SmoothStep(0, FadeIn, since), 0);
    }

    /// <summary>Progress 0…1 of the wave that is running at <paramref name="t"/>, or -1 between waves.</summary>
    private static double WaveProgress(double t)
    {
        if (t < WaveFirst) return -1;
        double p = ((t - WaveFirst) % WaveEvery) / WaveTime;
        return p < 1 ? p : -1;
    }

    /// <summary>A soft diagonal band of light that crosses the resting logo (0…1 brightness at this tile).</summary>
    private static double Wave(Tile tile, double t)
    {
        double p = WaveProgress(t);
        if (p < 0) return 0;
        double band = -0.35 + (1.7 * EaseInOut(p)); // starts and ends outside the logo, slow in and slow out
        double d = (tile.Sweep - band) / WaveWidth;
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

    private void OnActiveChanged()
    {
        UpdateClock();
        if (!IsActive && !_running) Landed?.Invoke(this, EventArgs.Empty); // nothing was moving – no need to wait
    }

    private void UpdateClock()
    {
        bool canRun = IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation && _tiles.Count > 0;
        if (!canRun || (!IsActive && !_running))
        {
            bool waiting = _stopRequested; // someone is waiting for the landing, but the loader got hidden first
            Stop();
            if (waiting) Landed?.Invoke(this, EventArgs.Empty);
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
        _startedAt = null; // set by the first frame
        _t = 0;
        _still = false;
        Shuffle();
        CompositionTarget.Rendering += OnRendering;
        Redraw(); // empty until the first tiles set off – never a flash of the finished logo
    }

    private void Stop()
    {
        if (_running) CompositionTarget.Rendering -= OnRendering;
        _running = _stopRequested = false;
        for (int i = 0; i < _dotBrushes.Length; i++) _dotBrushes[i].Color = _textColor;
        Redraw();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // The frame's own timestamp is the clock: it advances by exactly one screen refresh per frame, however late
        // this handler happens to run. (A stopwatch read here would jitter.) Rendering can also fire twice per frame.
        var frame = ((RenderingEventArgs)e).RenderingTime;
        if (frame == _lastFrame) return;
        _lastFrame = frame;
        _startedAt ??= frame;
        _t = (frame - _startedAt.Value).TotalSeconds;

        bool moving = _t < IntroEnd || WaveProgress(_t) >= 0;
        if (_stopRequested && !moving)
        {
            Stop(); // landed: whole logo, nothing in motion
            Landed?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Dots: a soft wave instead of on/off blinking.
        for (int i = 0; i < _dotBrushes.Length; i++)
        {
            double v = 0.5 + (0.5 * Math.Sin((_t * 2 * Math.PI / 1.25) - (i * 0.9)));
            _dotBrushes[i].Color = Color.FromArgb(ToByte(255 * (0.25 + (0.75 * v * v))), _textColor.R, _textColor.G, _textColor.B);
        }

        // Between waves nothing changes: draw the resting logo once, then leave it alone.
        if (moving || !_still) Redraw();
        _still = !moving;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Drawing
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Refills the drawing the layer shows. The layer itself is never invalidated, so a frame costs no layout pass –
    /// WPF just sends the new drawing to the render thread.
    /// </summary>
    private void Redraw()
    {
        double lift = 1.5 * LogoSize / 88;
        using var dc = _drawing.Open();
        foreach (var tile in _tiles) DrawTile(dc, tile, _running ? Evaluate(tile, _t, lift) : Resting);
    }

    private void DrawTile(DrawingContext dc, Tile tile, Pose pose)
    {
        if (pose.Opacity < 0.004) return;
        var rect = tile.Rest;
        bool turned = false;
        if (pose != Resting)
        {
            double size = rect.Width * pose.Scale;
            var centre = new Point(rect.X + (rect.Width / 2) + pose.Offset.X, rect.Y + (rect.Height / 2) + pose.Offset.Y);
            rect = new Rect(centre.X - (size / 2), centre.Y - (size / 2), size, size);
            if (pose.Angle != 0)
            {
                dc.PushTransform(new RotateTransform(pose.Angle, centre.X, centre.Y));
                turned = true;
            }
        }
        bool faded = pose.Opacity < 1;
        if (faded) dc.PushOpacity(pose.Opacity);

        double radius = rect.Width * CornerRatio;
        dc.DrawRoundedRectangle(tile.Brush, null, rect, radius, radius);
        if (pose.Glow > 0.01)
        {
            dc.PushOpacity(pose.Glow * 0.45);
            dc.DrawRoundedRectangle(_glow, null, rect, radius, radius);
            dc.Pop();
        }

        if (faded) dc.Pop();
        if (turned) dc.Pop();
    }

    /// <summary>Shows the tile drawing; sized to the logo, but tiles may drift outside it (the pill clips them).</summary>
    private sealed class TileLayer(PixelLogoLoader owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawingContext) => drawingContext.DrawDrawing(owner._drawing);
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
        _text.FontSize = s * 0.4; // the font family is inherited from the app, so the loader speaks its typeface
        _text.Margin = new Thickness(0, 0, 0, s * 0.04);
        _layer.Margin = new Thickness(0, 0, hasText ? s * 0.32 : 0, 0);
        Padding = hasText ? new Thickness(s * 0.38, s * 0.26, s * 0.52, s * 0.26) : new Thickness(s * 0.4);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════════

    private static Vector Direction(double degrees) => new(Math.Cos(degrees * Math.PI / 180), Math.Sin(degrees * Math.PI / 180));

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
