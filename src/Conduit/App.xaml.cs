using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace Conduit;

/// <summary>The app plus the few small building blocks the XAML needs (icon element, attached properties, converters).</summary>
public partial class App : Application
{
    /// <summary>Clipboard write that survives another app holding the clipboard open.</summary>
    public static void CopyText(string text)
    {
        try { Clipboard.SetText(text); }
        catch (COMException) { /* clipboard busy – the user can simply retry */ }
    }
}

/// <summary>
/// Stroke icon from SVG path data (the icons in App.xaml). <see cref="Box"/> is the SVG viewBox size, the element can be
/// any size. Without its own <see cref="Foreground"/> it takes the parent's – inside a button it follows the button's colour.
/// </summary>
public sealed class PathIcon : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(PathIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BoxProperty = DependencyProperty.Register(
        nameof(Box), typeof(double), typeof(PathIcon), new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(PathIcon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(PathIcon), new FrameworkPropertyMetadata(1.3, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(bool), typeof(PathIcon), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Data { get => (Geometry?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public double Box { get => (double)GetValue(BoxProperty); set => SetValue(BoxProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Box, Box);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Data is null || Box <= 0) return;
        drawingContext.PushTransform(new ScaleTransform(ActualWidth / Box, ActualHeight / Box));
        if (Filled)
        {
            drawingContext.DrawGeometry(Foreground, null, Data);
        }
        else
        {
            var pen = new Pen(Foreground, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            drawingContext.DrawGeometry(null, pen, Data);
        }
        drawingContext.Pop();
    }
}

/// <summary>
/// One line of path text that never hides the file name: when the path is wider than the element, folders from the middle
/// give way to "…" – <c>\\corp-fs01\platform\…\node-ci\2026-q4\Dockerfile</c>. The root (server + share, drive + first
/// folder) and as many trailing parts as fit stay. Font and colour come from the parent like a TextBlock's.
/// </summary>
public sealed class CompactPath : FrameworkElement
{
    private const string Gap = "…";

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CompactPath), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(CompactPath), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(CompactPath), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(CompactPath), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    /// <summary>Takes whatever width it is given; height of one line.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(0, Format("M").Height);

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.WidthChanged) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (string.IsNullOrEmpty(Text) || ActualWidth <= 0) return;
        var text = Format(Fit(Text, ActualWidth));
        drawingContext.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        drawingContext.DrawText(text, new Point(0, (ActualHeight - text.Height) / 2));
        drawingContext.Pop();
    }

    private string Fit(string path, double width)
    {
        if (Fits(path, width)) return path;

        // Split into parts, each with the separator in front of it: "\\corp-fs01" "\platform" "\shared" … "\Dockerfile".
        var parts = new List<string>();
        int start = 0;
        while (start < path.Length && path[start] is '\\' or '/') start++; // leading "\\" of a network path belongs to the first part
        for (int i = start; i <= path.Length; i++)
        {
            if (i < path.Length && path[i] is not ('\\' or '/')) continue;
            if (i > start) parts.Add(path[(parts.Count == 0 ? 0 : start)..i]);
            start = i;
        }
        if (parts.Count < 2) return path; // a single long name: the clip cuts it

        string sep = path.Contains('\\') ? "\\" : "/";
        string head = string.Concat(parts.Take(Math.Min(2, parts.Count - 1)));
        string tail = parts[^1];
        for (int i = parts.Count - 2; i >= 2; i--)
        {
            if (!Fits(head + sep + Gap + parts[i] + tail, width)) break;
            tail = parts[i] + tail;
        }
        if (Fits(head + sep + Gap + tail, width)) return head + sep + Gap + tail;
        return Gap + tail; // not even the root fits next to the file name
    }

    private bool Fits(string text, double width) => Format(text).WidthIncludingTrailingWhitespace <= width;

    private FormattedText Format(string text) => new(
        text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}

/// <summary>Attached properties for the shared templates.</summary>
public static class Look
{
    /// <summary>Corner radius of the flat button template (Button itself has none).</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Look), new FrameworkPropertyMetadata(default(CornerRadius)));

    /// <summary>Clips a Border's content to its rounded corners (WPF borders don't clip). Square bottom corners stay unclipped.</summary>
    public static readonly DependencyProperty ClipToCornersProperty = DependencyProperty.RegisterAttached(
        "ClipToCorners", typeof(bool), typeof(Look), new PropertyMetadata(false, OnClipToCornersChanged));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    public static bool GetClipToCorners(DependencyObject element) => (bool)element.GetValue(ClipToCornersProperty);

    public static void SetClipToCorners(DependencyObject element, bool value) => element.SetValue(ClipToCornersProperty, value);

    private static void OnClipToCornersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border) return;
        border.SizeChanged -= UpdateClip;
        border.Clip = null;
        if ((bool)e.NewValue) border.SizeChanged += UpdateClip;
    }

    private static void UpdateClip(object sender, SizeChangedEventArgs e)
    {
        var border = (Border)sender;
        var r = border.CornerRadius;
        double extra = r.BottomLeft == 0 && r.BottomRight == 0 ? r.TopLeft : 0; // top-only rounding: push the bottom corners out of view
        border.Clip = new RectangleGeometry(new Rect(0, 0, border.ActualWidth, border.ActualHeight + extra), r.TopLeft, r.TopLeft);
    }
}

/// <summary>Visible when the value is "truthy": true, non-null, non-empty text, non-zero number, non-empty list.</summary>
public sealed class VisibleWhen : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool on = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int i => i != 0,
            double d => d != 0,
            ICollection c => c.Count > 0,
            _ => true,
        };
        return on != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Number → star column width, for the segmented progress bars (passed | failed | aborted | rest).</summary>
public sealed class StarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new GridLength(Math.Max(0, System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture)), GridUnitType.Star);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>0…1 → pie slice inside the 14 px node ring (centre 7,7, radius 5), starting at 12 o'clock.</summary>
public sealed class PieConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double f = Math.Clamp(System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture), 0, 1);
        if (f < 0.005) return Geometry.Empty;
        if (f > 0.995) return new EllipseGeometry(new Point(7, 7), 5, 5);
        double angle = f * 2 * Math.PI;
        var end = new Point(7 + (5 * Math.Sin(angle)), 7 - (5 * Math.Cos(angle)));
        var figure = new PathFigure(new Point(7, 7),
            [new LineSegment(new Point(7, 2), false), new ArcSegment(end, new Size(5, 5), 0, f > 0.5, SweepDirection.Clockwise, false)], closed: true);
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Value == parameter → true. For radio buttons bound to an enum (ConvertBack returns the parameter when checked).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}
