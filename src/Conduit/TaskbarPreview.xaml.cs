using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Conduit;

/// <summary>
/// The taskbar hover preview. <see cref="Render"/> fills it from the current run and returns a picture of it,
/// scaled to the size Windows allows for a thumbnail.
/// </summary>
public partial class TaskbarPreview : UserControl
{
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
        int done = ws.TotalDone, total = ws.TotalTests, failed = ws.TotalFailed, aborted = ws.Nodes.Sum(n => n.Aborted);

        CountText.Text = $"{done} of {total}";
        FailedText.Text = failed > 0 ? $"{failed} failed" : "";
        FailedText.Visibility = failed > 0 ? Visibility.Visible : Visibility.Collapsed;

        TotalDone.Width = new GridLength(done, GridUnitType.Star);
        TotalRest.Width = new GridLength(Math.Max(0, total - done), GridUnitType.Star);
        TotalFill.Fill = (Brush)FindResource(failed > 0 ? "Red" : aborted > 0 ? "Amber" : "Green");

        bool multi = ws.IsMulti;
        NodeRows.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        SinglePanel.Visibility = multi ? Visibility.Collapsed : Visibility.Visible;

        if (multi)
        {
            NoteText.Text = aborted > 0 ? $"{aborted} aborted" : ws.IsFinished ? $"in {ws.ElapsedText}" : "";
            // Setting the list again re-reads every node, so the rows always show the numbers of this moment.
            NodeRows.ItemsSource = null;
            NodeRows.ItemsSource = ws.Nodes;
        }
        else if (ws.Nodes.Count > 0)
        {
            var node = ws.Nodes[0];
            NoteText.Text = ws.IsRunning ? ws.HeaderRightText : $"in {ws.ElapsedText}";
            SingleTitle.Text = $"{node.Name} · {(ws.IsRunning ? "now running" : node.Stopped ? "stopped" : "finished")}";
            SingleSub.Text = ws.IsRunning && node.RunningTest is { } test ? test.Name
                : aborted > 0 ? $"{aborted} aborted"
                : failed > 0 ? "see the failed tests in the report"
                : "all tests passed";
        }
    }
}
