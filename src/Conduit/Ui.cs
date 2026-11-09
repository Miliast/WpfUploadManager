using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

using Path = System.Windows.Shapes.Path;

namespace Conduit;

/// <summary>
/// Design tokens from the Paper file plus tiny factory methods, so the window code reads like the layout.
/// Every colour lives here: a later switch to the WPF Fluent theme (.NET 9+) only has to touch this file.
/// </summary>
internal static class Ui
{
    // ── Colours (1:1 from the Paper file) ──────────────────────────────────
    public static readonly SolidColorBrush Blue = Hex("#1F4BFF"), BlueSoft = Hex("#F3F6FF"), BlueChip = Hex("#EEF2FF"), BlueRing = Hex("#C9D4FF");
    public static readonly SolidColorBrush Ink = Hex("#0B0D12"), Black = Hex("#000000"), Body = Hex("#2A2F3A"), Muted = Hex("#5B6270");
    public static readonly SolidColorBrush Subtle = Hex("#8A909C"), Faint = Hex("#A0A6B1"), Faintest = Hex("#B4B9C3");
    public static readonly SolidColorBrush Line = Hex("#E6E8EC"), LineSoft = Hex("#F0F1F3"), Chip = Hex("#F2F3F5"), Page = Hex("#F6F6F7");
    public static readonly SolidColorBrush Track = Hex("#ECEDEF"), TrackDark = Hex("#DADDE2"), White = Hex("#FFFFFF"), Clear = Hex("#00FFFFFF");
    public static readonly SolidColorBrush Green = Hex("#12A150"), GreenSoft = Hex("#E4F5EB");
    public static readonly SolidColorBrush Red = Hex("#E5383B"), RedText = Hex("#C2262A"), RedSoft = Hex("#FDEEEE"), RedLine = Hex("#F3CACB");
    public static readonly SolidColorBrush Amber = Hex("#E89B0C"), AmberText = Hex("#9A6700"), AmberSoft = Hex("#FDF3E1");
    public static readonly SolidColorBrush WarnRow = Hex("#FFF7E8"), WarnTag = Hex("#B87A00"), WarnText = Hex("#7A5300");
    public static readonly SolidColorBrush Json = Hex("#E3A008"), Ts = Hex("#3178C6"), Docker = Hex("#0B8FD1"), Yml = Hex("#7C5CDB");

    public static readonly FontFamily Sans = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = new("JetBrains Mono, Cascadia Mono, Consolas");
    public static readonly FontWeight Regular = FontWeights.Normal, Medium = FontWeights.Medium, Semi = FontWeights.SemiBold, Bold = FontWeights.Bold;

    // ── Icon paths (14×14 unless noted) ────────────────────────────────────
    public const string TerminalIcon = "M3.5,2.5 H10.5 A2,2 0 0 1 12.5,4.5 V9.5 A2,2 0 0 1 10.5,11.5 H3.5 A2,2 0 0 1 1.5,9.5 V4.5 A2,2 0 0 1 3.5,2.5 Z M4,6 L5.8,7.5 L4,9 M7.5,9 H10";
    public const string PlayIcon = "M4.5,3 L11,7 L4.5,11 Z";
    public const string CopyIcon = "M6,4.5 H10 A1.5,1.5 0 0 1 11.5,6 V10 A1.5,1.5 0 0 1 10,11.5 H6 A1.5,1.5 0 0 1 4.5,10 V6 A1.5,1.5 0 0 1 6,4.5 Z M9.5,4.5 V3 Q9.5,2.5 9,2.5 H3 Q2.5,2.5 2.5,3 V9 Q2.5,9.5 3,9.5 H4.5";
    public const string CloseIcon = "M3.5,3.5 L10.5,10.5 M10.5,3.5 L3.5,10.5";
    public const string CheckIcon8 = "M1.5,4.2 L3.2,5.8 L6.5,2.4";
    public const string CrossIcon8 = "M2,2 L6,6 M6,2 L2,6";
    public const string SearchIcon = "M6,2 A4,4 0 1 1 6,10 A4,4 0 1 1 6,2 Z M9,9 L12,12";
    public const string ChevronDown = "M4,5.5 L7,8.5 L10,5.5";
    public const string ChevronRight = "M5.5,4 L8.5,7 L5.5,10";
    public const string UploadIcon = "M7,9 V2.5 M4.5,5 L7,2.5 L9.5,5 M2.5,9 V11 Q2.5,11.5 3,11.5 H11 Q11.5,11.5 11.5,11 V9";
    public const string DownloadIcon = "M7,2.5 V9 M4.5,6.5 L7,9 L9.5,6.5 M2.5,9 V11 Q2.5,11.5 3,11.5 H11 Q11.5,11.5 11.5,11 V9";
    public const string RefreshIcon = "M11.5,7 A4.5,4.5 0 1 1 10.2,3.8 M10.4,1.6 L10.3,4 L7.9,3.9";
    public const string CloudIcon = "M4.5,10.5 H4 A2.5,2.5 0 0 1 3.8,5.5 A3.5,3.5 0 0 1 10.5,5 A2.5,2.5 0 0 1 10,10.5 H9.5 M7,6.5 V11.5 M5.5,10 L7,11.5 L8.5,10";
    public const string InfoIcon = "M7,1.5 A5.5,5.5 0 1 1 7,12.5 A5.5,5.5 0 1 1 7,1.5 Z M7,6.5 V10 M7,4.3 V4.5";
    public const string FlaskIcon = "M5.5,1.5 H8.5 M6,1.5 V5.5 L2.8,11 Q2.4,12 3.5,12 H10.5 Q11.6,12 11.2,11 L8,5.5 V1.5";
    public const string DocIcon = "M3.5,1.5 H8.5 L11,4 V12.5 H3.5 Z M8.5,1.5 V4 H11";
    public const string FolderIcon = "M1.5,4 Q1.5,2.5 3,2.5 H5.5 L7,4 H11 Q12.5,4 12.5,5.5 V10 Q12.5,11.5 11,11.5 H3 Q1.5,11.5 1.5,10 Z";
    public const string BoxIcon = "M2,4.5 H12 V11.5 H2 Z M2,7.5 H12 M5,4.5 V2.5 H9 V4.5";

    // ── Text & layout ──────────────────────────────────────────────────────

    /// <summary>A text run. Without <paramref name="color"/> it inherits the foreground (so buttons can recolour it).</summary>
    public static TextBlock T(string text, double size = 13, Brush? color = null, FontWeight? weight = null, bool mono = false)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = size, FontWeight = weight ?? Regular, FontFamily = mono ? Mono : Sans,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (color is not null) t.Foreground = color;
        return t;
    }

    /// <summary>Text with a second, dimmer part ("201 Created  ch_3PqLx2 · 571ms").</summary>
    public static TextBlock Rich(string text, Brush color, string dim, Brush dimColor, double size = 12, bool mono = true)
    {
        var t = T("", size, null, null, mono);
        t.Inlines.Add(new Run(text) { Foreground = color });
        if (dim.Length > 0) t.Inlines.Add(new Run(" " + dim) { Foreground = dimColor });
        return t;
    }

    /// <summary>Marks an element as the one that takes the remaining width in <see cref="Row"/> (flex-grow).</summary>
    public static T1 Grow<T1>(T1 element) where T1 : FrameworkElement
    {
        element.Tag = GrowTag;
        return element;
    }

    private static readonly object GrowTag = new();

    /// <summary>Horizontal row with a gap. <c>null</c> is a flexible spacer, <see cref="Grow"/> marks a filling element.</summary>
    public static Grid Row(double gap, params UIElement?[] items)
    {
        var grid = new Grid();
        bool first = true;
        foreach (var item in items)
        {
            bool grow = item is null || (item is FrameworkElement { Tag: var tag } && ReferenceEquals(tag, GrowTag));
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = grow ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            if (item is null) continue;
            if (!first && item is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left + gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            first = false;
            Grid.SetColumn(item, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(item);
        }
        return grid;
    }

    /// <summary>Vertical stack with a gap.</summary>
    public static StackPanel Stack(double gap, params UIElement?[] items)
    {
        var panel = new StackPanel();
        foreach (var item in items)
        {
            if (item is null) continue;
            if (panel.Children.Count > 0 && item is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + gap, fe.Margin.Right, fe.Margin.Bottom);
            panel.Children.Add(item);
        }
        return panel;
    }

    /// <summary>A Border with the usual knobs (the "div" of this file).</summary>
    public static Border Box(UIElement? child = null, Brush? bg = null, double radius = 0, Thickness? pad = null,
        double w = double.NaN, double h = double.NaN, Brush? border = null, double borderWidth = 1) => new()
    {
        Child = child, Background = bg, CornerRadius = new CornerRadius(radius), Padding = pad ?? default, Width = w, Height = h,
        BorderBrush = border, BorderThickness = border is null ? default : new Thickness(borderWidth),
    };

    /// <summary>White card with hairline border and a soft shadow (shadow on a separate layer so text keeps ClearType).</summary>
    public static Grid Card(UIElement content, CornerRadius radius, bool shadowUp = false)
    {
        var body = new Border
        {
            Background = White, CornerRadius = radius, BorderBrush = Hex("#140B0D12"),
            BorderThickness = shadowUp ? new Thickness(1, 1, 1, 0) : new Thickness(1), Child = content,
        };
        // Border doesn't clip its children; clip to the rounded shape.
        body.SizeChanged += (_, _) => body.Clip = new RectangleGeometry(new Rect(body.RenderSize), radius.TopLeft, radius.TopLeft);
        var shadow = new Border
        {
            Background = White, CornerRadius = radius,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = shadowUp ? 6 : 4, Direction = shadowUp ? 90 : 270, Opacity = 0.07 },
        };
        return new Grid { Children = { shadow, body } };
    }

    public static Border Dot(double size, Brush color, double radius = -1) =>
        Box(bg: color, radius: radius < 0 ? size / 2 : radius, w: size, h: size);

    public static Border Spacer(double w) => new() { Width = w };

    // ── Buttons ────────────────────────────────────────────────────────────

    public static readonly Style Primary = ButtonStyle(Blue, Hex("#1A40E0"), White, 10, disabledBg: Hex("#EEF0F3"), disabledFg: Faint);
    public static readonly Style Dark = ButtonStyle(Ink, Hex("#2A2E38"), White, 8, disabledBg: Chip, disabledFg: Faint);
    public static readonly Style ChipButton = ButtonStyle(Chip, Hex("#E7E9ED"), Black, 16);
    public static readonly Style Outline = ButtonStyle(White, Hex("#F6F7F9"), Black, 8, border: Line);
    public static readonly Style DangerSoft = ButtonStyle(RedSoft, Hex("#FBE1E1"), RedText, 15);
    public static readonly Style DangerOutline = ButtonStyle(Clear, RedSoft, RedText, 14, border: RedLine);
    public static readonly Style Ghost = ButtonStyle(Clear, Chip, Muted, 8, hoverFg: Black);
    public static readonly Style Tab = ButtonStyle(Clear, Hex("#F6F7F8"), Black, 10);
    public static readonly Style Link = ButtonStyle(Clear, Clear, Blue, 4, hoverFg: Hex("#1233C4"), weight: Medium);
    public static readonly Style LinkMuted = ButtonStyle(Clear, Clear, Subtle, 4, hoverFg: Black, weight: Regular);
    public static readonly Style PillDark = ButtonStyle(Ink, Hex("#2A2E38"), White, 15);
    public static readonly Style PillLight = ButtonStyle(Chip, Hex("#E7E9ED"), Ink, 15);
    public static readonly Style SquareOutline = ButtonStyle(White, Hex("#F6F7F9"), Ink, 10, border: Line);
    public static readonly Style OnDark = ButtonStyle(Clear, Hex("#262A33"), White, 0);

    public static Button Btn(object content, Style style, Action? click, double h = double.NaN, Thickness? pad = null, string? tip = null)
    {
        var b = new Button { Content = content, Style = style, Height = h, Padding = pad ?? new Thickness(12, 0, 12, 0), ToolTip = tip };
        if (click is not null) b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Flat button: rounded background, hover/disabled colours from triggers, no default Windows chrome.</summary>
    public static Style ButtonStyle(Brush bg, Brush hover, Brush fg, double radius, Brush? border = null, Brush? hoverFg = null,
        Brush? disabledBg = null, Brush? disabledFg = null, FontWeight? weight = null)
    {
        var box = new FrameworkElementFactory(typeof(Border));
        box.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        box.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        box.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        box.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        box.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        box.AppendChild(presenter);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = box }));
        style.Setters.Add(new Setter(Control.BackgroundProperty, bg));
        style.Setters.Add(new Setter(Control.ForegroundProperty, fg));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, border ?? Clear));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(border is null ? 0 : 1)));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, Sans));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Setter(Control.FontWeightProperty, weight ?? Semi));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));

        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Control.BackgroundProperty, hover));
        if (hoverFg is not null) over.Setters.Add(new Setter(Control.ForegroundProperty, hoverFg));
        style.Triggers.Add(over);

        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.8));
        style.Triggers.Add(pressed);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Control.BackgroundProperty, disabledBg ?? bg));
        disabled.Setters.Add(new Setter(Control.ForegroundProperty, disabledFg ?? Faint));
        disabled.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Arrow));
        style.Triggers.Add(disabled);
        return style;
    }

    // ── Icons & small graphics ─────────────────────────────────────────────

    /// <summary>Stroke icon from SVG-like path data. Without a colour it follows the parent's Foreground (e.g. a button's).</summary>
    public static FrameworkElement Icon(string data, Brush? color, double size = 14, double stroke = 1.3, double box = 14, bool filled = false)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data), StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        var target = filled ? Shape.FillProperty : Shape.StrokeProperty;
        if (color is not null) path.SetValue(target, color);
        else path.SetBinding(target, new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self });
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = box, Height = box, Children = { path } } };
    }

    /// <summary>16 px status circle of a test row: passed / failed / aborted / queued.</summary>
    public static FrameworkElement Status(char state) => state switch
    {
        'p' => Box(Icon(CheckIcon8, Green, 8, 1.5, 8), GreenSoft, 8, w: 16, h: 16),
        'f' => Box(Icon(CrossIcon8, Red, 8, 1.5, 8), RedSoft, 8, w: 16, h: 16),
        'a' => Box(Box(bg: Amber, radius: 1, w: 7, h: 1.5), AmberSoft, 8, w: 16, h: 16),
        _ => Box(null, Chip, 8, w: 16, h: 16),
    };

    /// <summary>Green circle with a white check (finished node).</summary>
    public static FrameworkElement CheckCircle(double size = 14) =>
        Box(Icon(CheckIcon8, White, size * 0.6, 1.6, 8), Green, size / 2, w: size, h: size);

    /// <summary>Blue arc spinning forever (running test).</summary>
    public static FrameworkElement Spinner(double size = 16)
    {
        double r = (size / 2) - 1;
        var ring = new Ellipse { Width = size, Height = size, Stroke = BlueRing, StrokeThickness = 2 };
        var arc = new Path
        {
            Data = Geometry.Parse(Inv($"M{size / 2},1 A{r},{r} 0 0 1 {size - 1},{size / 2}")),
            Stroke = Blue, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        var rotate = new RotateTransform();
        var grid = new Grid { Width = size, Height = size, Children = { ring, arc }, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = rotate };
        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        return grid;
    }

    /// <summary>Pie-in-a-ring progress icon (node tabs); optional status dot top-right.</summary>
    public static FrameworkElement Ring(double fraction, Brush color, Brush? dot = null, Brush? dotRing = null)
    {
        var canvas = new Canvas { Width = 14, Height = 14 };
        var outer = new Ellipse { Width = 12, Height = 12, Stroke = color, StrokeThickness = 1.5 };
        Canvas.SetLeft(outer, 1);
        Canvas.SetTop(outer, 1);
        canvas.Children.Add(outer);
        if (fraction >= 0.999)
        {
            var full = new Ellipse { Width = 8, Height = 8, Fill = color };
            Canvas.SetLeft(full, 3);
            Canvas.SetTop(full, 3);
            canvas.Children.Add(full);
        }
        else if (fraction > 0.005)
        {
            double a = fraction * 2 * Math.PI;
            string large = fraction > 0.5 ? "1" : "0";
            canvas.Children.Add(new Path { Fill = color, Data = Geometry.Parse(Inv($"M7,7 L7,3 A4,4 0 {large} 1 {7 + (4 * Math.Sin(a))},{7 - (4 * Math.Cos(a))} Z")) });
        }
        if (dot is not null) canvas.Children.Add(StatusDot(dot, dotRing ?? White));
        return canvas;
    }

    /// <summary>Small red/amber "problem" dot that sits on the top-right corner of a 14 px icon.</summary>
    public static Ellipse StatusDot(Brush color, Brush ring)
    {
        var dot = new Ellipse { Width = 8, Height = 8, Fill = color, Stroke = ring, StrokeThickness = 1.5 };
        Canvas.SetLeft(dot, 9);
        Canvas.SetTop(dot, -3);
        return dot;
    }

    /// <summary>Segmented progress bar: green passed, red failed, amber aborted, rest is track.</summary>
    public static Grid Bar(int passed, int failed, int aborted, int total, double height, Brush track)
    {
        var grid = new Grid { Height = height, Background = track };
        void Segment(double value, Brush? fill)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(value, 0), GridUnitType.Star) });
            if (fill is null) return;
            var rect = new Rectangle { Fill = fill };
            Grid.SetColumn(rect, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(rect);
        }
        Segment(passed, Green);
        Segment(failed, Red);
        Segment(aborted, Amber);
        Segment(total - passed - failed - aborted, null);
        return grid;
    }

    /// <summary>40 px document glyph with a coloured type tag (JSON, TS…); dashed when the slot is still empty.</summary>
    public static FrameworkElement FileGlyph(string tag, Brush tagColor, bool empty)
    {
        var page = new Rectangle
        {
            Width = 30, Height = 36, RadiusX = 5, RadiusY = 5, Fill = White, StrokeThickness = 1,
            Stroke = empty ? Faintest : Hex("#E3E5E8"), StrokeDashArray = empty ? new DoubleCollection { 3, 2 } : null,
        };
        var lines = Stack(4, Box(bg: Track, radius: 1, w: 12, h: 1.5), Box(bg: Track, radius: 1, w: 8, h: 1.5));
        lines.Margin = new Thickness(0, 9, 0, 0);
        lines.HorizontalAlignment = HorizontalAlignment.Center;
        lines.VerticalAlignment = VerticalAlignment.Top;
        var label = Box(T(tag, 7.5, empty ? Subtle : White, Bold), empty ? Line : tagColor, 3, new Thickness(4, 1, 4, 1));
        label.HorizontalAlignment = HorizontalAlignment.Left;
        label.VerticalAlignment = VerticalAlignment.Bottom;
        label.Margin = new Thickness(0, 0, 0, 5);
        return new Grid { Width = 46, Height = 40, Children = { page, lines, label } };
    }

    /// <summary>40 px folder glyph (blue gradient when set, dashed outline when empty).</summary>
    public static FrameworkElement FolderGlyph(bool empty)
    {
        var tab = new Rectangle { Width = 16, Height = 10, RadiusX = 3, RadiusY = 3 };
        var body = new Rectangle { Width = 40, Height = 30, RadiusX = 7, RadiusY = 7, Margin = new Thickness(0, 6, 0, 0) };
        if (empty)
        {
            foreach (var r in new[] { tab, body })
            {
                r.Fill = White;
                r.Stroke = Faintest;
                r.StrokeDashArray = new DoubleCollection { 3, 2 };
            }
        }
        else
        {
            tab.Fill = Hex("#7D97FF");
            body.Fill = new LinearGradientBrush(Color.FromRgb(0x5B, 0x80, 0xFF), Color.FromRgb(0x1F, 0x4B, 0xFF), 90);
        }
        tab.HorizontalAlignment = HorizontalAlignment.Left;
        tab.VerticalAlignment = body.VerticalAlignment = VerticalAlignment.Top;
        return new Grid { Width = 46, Height = 40, Margin = new Thickness(0, 4, 0, 0), Children = { tab, body } };
    }

    /// <summary>24 px input glyph for the run sidebar (blue folder, or page with coloured corner tag).</summary>
    public static FrameworkElement InputGlyph(bool folder, Brush tagColor)
    {
        var g = new Grid { Width = 24, Height = 24 };
        if (folder)
        {
            g.Children.Add(new Rectangle { Width = 11, Height = 6, RadiusX = 3, RadiusY = 3, Fill = Hex("#7D97FF"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
            g.Children.Add(new Rectangle { Width = 24, Height = 17, RadiusX = 5, RadiusY = 5, Fill = Blue, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) });
            return g;
        }
        g.Children.Add(new Rectangle { Width = 18, Height = 23, RadiusX = 4, RadiusY = 4, Fill = White, Stroke = Hex("#E3E5E8"), StrokeThickness = 1 });
        g.Children.Add(new Rectangle { Width = 8, Height = 5, RadiusX = 2, RadiusY = 2, Fill = tagColor, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) });
        return g;
    }

    // ── Images for the window icon and the Windows taskbar ─────────────────

    /// <summary>The Conduit logo (black rounded square with a blue dot) as window/taskbar icon.</summary>
    public static ImageSource Logo() => Draw(32, dc =>
    {
        dc.DrawRoundedRectangle(Ink, null, new Rect(2, 2, 28, 28), 8, 8);
        dc.DrawEllipse(Blue, null, new Point(16, 16), 5.5, 5.5);
    });

    /// <summary>Red count badge for the taskbar overlay (failures).</summary>
    public static ImageSource BadgeOverlay(int count) => Draw(16, dc =>
    {
        dc.DrawEllipse(Red, new Pen(White, 1.5), new Point(8, 8), 7.2, 7.2);
        var text = new FormattedText(count > 9 ? "9+" : count.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface(Sans, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), count > 9 ? 8 : 10, White, 1.0);
        dc.DrawText(text, new Point(8 - (text.Width / 2), 8 - (text.Height / 2)));
    });

    /// <summary>Green check for the taskbar overlay (run finished).</summary>
    public static ImageSource CheckOverlay() => Draw(16, dc =>
    {
        dc.DrawEllipse(Green, new Pen(White, 1.5), new Point(8, 8), 7.2, 7.2);
        dc.DrawGeometry(null, RoundPen(White, 1.8), Geometry.Parse("M4.6,8.3 L7,10.5 L11.4,5.8"));
    });

    /// <summary>Variant A from the design: one bar per node in the overlay, red/amber cap when that node has problems.</summary>
    public static ImageSource BarsOverlay(IReadOnlyList<PipelineNode> nodes) => Draw(16, dc =>
    {
        dc.DrawRoundedRectangle(White, null, new Rect(0, 0, 16, 16), 3, 3);
        double w = 12.0 / nodes.Count;
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var lane = new Rect(2 + (i * w) + 0.5, 2, w - 1, 12);
            dc.DrawRectangle(TrackDark, null, lane);
            double h = 12 * n.Fraction;
            dc.DrawRectangle(Blue, null, new Rect(lane.X, 14 - h, lane.Width, h));
            if (n.Failed + n.Aborted > 0 && h > 0) dc.DrawRectangle(n.Failed > 0 ? Red : Amber, null, new Rect(lane.X, 14 - h, lane.Width, Math.Min(2.5, h)));
        }
    });

    /// <summary>Icon for a thumbnail-toolbar button in the taskbar preview (Stop / Logs / Export).</summary>
    public static ImageSource ThumbIcon(string data, Brush color, bool filled = false) => Draw(14, dc =>
    {
        var geometry = Geometry.Parse(data);
        if (filled) dc.DrawGeometry(color, null, geometry);
        else dc.DrawGeometry(null, RoundPen(color, 1.4), geometry);
    });

    private static Pen RoundPen(Brush brush, double width) =>
        new(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

    private static DrawingImage Draw(double size, Action<DrawingContext> paint)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size)); // fixes the bounds
            paint(dc);
        }
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Culture-invariant formatting (a German PC would otherwise write "2,1s" and break path data).</summary>
    public static string Inv(FormattableString s) => FormattableString.Invariant(s);

    public static SolidColorBrush Hex(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
