using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Conduit;

/// <summary>
/// The taskbar hover preview. <see cref="Render"/> fills it from the current run and returns a picture of it,
/// scaled to the size Windows allows for a thumbnail. The look is in TaskbarPreview.xaml.
/// </summary>
public partial class TaskbarPreview : UserControl
{
    private const double SectionGap = 4; // space between two node sections

    public TaskbarPreview() => InitializeComponent();

    public BitmapSource Render(Workspace ws, int maxWidth, int maxHeight)
    {
        Show(ws);
        // Not part of any window, so layout has to be run by hand.
        Measure(new Size(Width, double.PositiveInfinity));
        Arrange(new Rect(DesiredSize));
        UpdateLayout();

        // Rendering at a different DPI scales the vectors and text themselves – sharp at any thumbnail size.
        double scale = Math.Min(maxWidth / ActualWidth, maxHeight / ActualHeight);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)(ActualWidth * scale)), Math.Max(1, (int)(ActualHeight * scale)),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(this);
        return bitmap;
    }

    private void Show(Workspace ws)
    {
        int done = ws.TotalDone, total = ws.TotalTests, passed = ws.Nodes.Sum(n => n.Passed);
        bool running = ws.IsRunning;

        // Header
        RunText.Text = $"Run #{ws.RunNo}";
        ElapsedText.Text = ws.ElapsedText;
        LiveDot.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        // Count row: while running "72 of 160 done", afterwards "158 of 160 passed".
        CountText.Text = (running ? done : passed).ToString(System.Globalization.CultureInfo.InvariantCulture);
        UnitText.Text = running ? $"of {total} done" : $"of {total} passed";
        NoteText.Text = running ? TimeLeft(ws, done, total)
            : ws.Nodes.Any(n => n.Stopped) ? "Stopped" : "Finished";

        ShowSections(ws);

        // One node: there is room to name the test in flight.
        var only = ws.Nodes.Count == 1 ? ws.Nodes[0] : null;
        NowText.Text = running && only?.RunningTest is { } test ? test.Name : "";
        NowText.Visibility = NowText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Rebuilds the section row: one star column per node, weighted by its number of tests, with a fixed gap column
    /// between two nodes. Building it anew on every render keeps the numbers exactly those of this moment.
    /// </summary>
    private void ShowSections(Workspace ws)
    {
        Sections.Children.Clear();
        Sections.ColumnDefinitions.Clear();
        var template = (DataTemplate)FindResource("NodeSection");

        for (int i = 0; i < ws.Nodes.Count; i++)
        {
            var node = ws.Nodes[i];
            if (i > 0) Sections.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SectionGap) });
            Sections.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, node.Total), GridUnitType.Star) });

            var section = new ContentControl { Content = node, ContentTemplate = template, Focusable = false };
            Grid.SetColumn(section, Sections.ColumnDefinitions.Count - 1);
            Sections.Children.Add(section);
        }
    }

    /// <summary>"about 0:34 left" from the pace so far; empty until the first test finished.</summary>
    private static string TimeLeft(Workspace ws, int done, int total)
    {
        if (done == 0) return "estimating…";
        var left = TimeSpan.FromSeconds(ws.Elapsed.TotalSeconds / done * (total - done));
        return $"about {Fmt.Clock(left)} left";
    }
}
