using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

using Microsoft.Win32;

namespace Conduit;

/// <summary>
/// Top bar actions, keyboard shortcuts and the Windows taskbar (screen 7: native progress, overlay icon, preview buttons, flash).
/// Everything else is bound in XAML to <see cref="Workspace"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly Workspace _ws = new();
    private string _overlayKey = "";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _ws;
        _ws.ProgressChanged += (_, _) => UpdateTaskbar();
        _ws.RunFinished += (_, _) => { if (!IsActive) Flash(); };
        UpdateTaskbar();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Windows 11: white title bar + hairline border, so the native chrome blends into the white top bar.
        var hwnd = new WindowInteropHelper(this).Handle;
        int caption = 0x00FFFFFF, border = 0x00ECE8E6; // COLORREF = 0x00BBGGRR
        _ = DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.O && _ws.IsSetup) Setup.PickProjectFolder();
        else if (e.Key == Key.Enter && _ws.IsSetup) _ws.StartRun();
        else if (e.Key is Key.L or Key.Oem3 && _ws.IsRun) _ws.ToggleLogs();
        else return;
        e.Handled = true;
    }

    // ── Top bar ────────────────────────────────────────────────────────────

    private void OnBrandClick(object sender, RoutedEventArgs e)
    {
        if (_ws.IsFinished) _ws.NewRun();
    }

    private void OnRunClick(object sender, RoutedEventArgs e) => _ws.StartRun();

    private void OnToggleLogs(object sender, RoutedEventArgs e) => _ws.ToggleLogs();

    private void OnStopAll(object sender, RoutedEventArgs e) => _ws.StopAll();

    private void OnExport(object sender, EventArgs e) => _ = _ws.ExportToXAsync();

    private void OnExportMore(object sender, RoutedEventArgs e)
    {
        var menu = ExportMore.ContextMenu;
        menu.PlacementTarget = ExportMore;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnSaveJson(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = $"run-{_ws.RunNo}.json", Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) == true) _ws.SaveJson(dialog.FileName);
    }

    private void OnCopySummary(object sender, RoutedEventArgs e) => App.CopyText(_ws.Summary());

    private void OnNewRun(object sender, RoutedEventArgs e) => _ws.NewRun();

    private void OnThumbStop(object sender, EventArgs e) => _ws.StopAll();

    private void OnThumbLogs(object sender, EventArgs e)
    {
        Activate();
        if (!_ws.LogsOpen) _ws.ToggleLogs();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Windows taskbar (screen 7)
    // ════════════════════════════════════════════════════════════════════════

    private void UpdateTaskbar()
    {
        var taskbar = TaskbarItemInfo;
        ThumbStop.Visibility = _ws.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        ThumbLogs.Visibility = _ws.IsRun ? Visibility.Visible : Visibility.Collapsed;
        ThumbExport.Visibility = _ws.IsFinished ? Visibility.Visible : Visibility.Collapsed;

        if (_ws.IsSetup)
        {
            taskbar.ProgressState = TaskbarItemProgressState.None;
            SetOverlay("", null);
            return;
        }
        int failed = _ws.TotalFailed;
        if (_ws.IsFinished)
        {
            // Bar gone, check or red count on the icon (the button flashes when the window is in the background).
            taskbar.ProgressState = TaskbarItemProgressState.None;
            SetOverlay($"done{failed}", failed > 0 ? BadgeOverlay(failed) : (ImageSource)FindResource("Overlay.Check"));
            return;
        }
        // Native bar = total progress. Multi: one bar per node in the overlay (variant A). Single: overlay only on failures.
        taskbar.ProgressState = TaskbarItemProgressState.Normal;
        taskbar.ProgressValue = (double)_ws.TotalDone / Math.Max(1, _ws.TotalTests);
        if (_ws.IsMulti)
            SetOverlay("bars" + string.Join(",", _ws.Nodes.Select(n => $"{(int)(n.Fraction * 12)}{n.Failed + n.Aborted}")), BarsOverlay(_ws.Nodes));
        else
            SetOverlay($"fail{failed}", failed > 0 ? BadgeOverlay(failed) : null);
    }

    /// <summary>Only swaps the overlay when it really changed – Windows redraws it on every set.</summary>
    private void SetOverlay(string key, ImageSource? image)
    {
        if (key == _overlayKey) return;
        _overlayKey = key;
        TaskbarItemInfo.Overlay = image;
    }

    /// <summary>Red count badge (failures).</summary>
    private static DrawingImage BadgeOverlay(int count) => Draw(16, dc =>
    {
        dc.DrawEllipse(Brush("Red"), new Pen(Brushes.White, 1.5), new Point(8, 8), 7.2, 7.2);
        var text = new FormattedText(count > 9 ? "9+" : count.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            count > 9 ? 8 : 10, Brushes.White, 1.0);
        dc.DrawText(text, new Point(8 - (text.Width / 2), 8 - (text.Height / 2)));
    });

    /// <summary>Variant A from the design: one bar per node, red/amber cap when that node has problems.</summary>
    private static DrawingImage BarsOverlay(IReadOnlyList<PipelineNode> nodes) => Draw(16, dc =>
    {
        dc.DrawRoundedRectangle(Brushes.White, null, new Rect(0, 0, 16, 16), 3, 3);
        double w = 12.0 / nodes.Count;
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var lane = new Rect(2 + (i * w) + 0.5, 2, w - 1, 12);
            dc.DrawRectangle(Brush("TrackDark"), null, lane);
            double h = 12 * n.Fraction;
            dc.DrawRectangle(Brush("Blue"), null, new Rect(lane.X, 14 - h, lane.Width, h));
            if (n.Failed + n.Aborted > 0 && h > 0)
                dc.DrawRectangle(Brush(n.Failed > 0 ? "Red" : "Amber"), null, new Rect(lane.X, 14 - h, lane.Width, Math.Min(2.5, h)));
        }
    });

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

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private void Flash()
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = new WindowInteropHelper(this).Handle,
            dwFlags = 2 | 12, // FLASHW_TRAY | FLASHW_TIMERNOFG: flash the taskbar button until the window is focused
        };
        _ = FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
