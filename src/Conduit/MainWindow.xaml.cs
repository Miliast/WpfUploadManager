using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

using LogoLoader;

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
    private IntPtr _hwnd;
    private bool _ownThumbnail;
    private TaskbarPreview? _preview;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _ws;
        _ws.ProgressChanged += (_, _) => UpdateTaskbar();
        _ws.RunFinished += (_, _) => { if (!IsActive) Flash(); };
        _ws.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Workspace.IsBusy) && _ws.IsBusy) ShowBusy(); };
        Loader.Landed += (_, _) => HideBusy();
        ApplyOwnLogo();
        UpdateTaskbar();
    }

    /// <summary>
    /// Assets\logo.png (next to the exe) replaces the Conduit mark everywhere it appears: loader, top bar, taskbar.
    /// A missing or unreadable file simply keeps the built-in mark.
    /// </summary>
    private void ApplyOwnLogo()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
        if (!File.Exists(path)) return;
        try
        {
            var logo = PixelLogoLoader.FromFile(path);
            Loader.Source = logo;
            BrandImage.Source = logo;
            BrandImage.Visibility = Visibility.Visible;
            BrandMark.Visibility = Visibility.Collapsed;
            Icon = logo;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or FormatException)
        {
            // Not a usable image – keep the built-in logo.
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Windows 11: white title bar + hairline border, so the native chrome blends into the white top bar.
        var hwnd = new WindowInteropHelper(this).Handle;
        int caption = 0x00FFFFFF, border = 0x00ECE8E6; // COLORREF = 0x00BBGGRR
        _ = DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));

        // Taskbar hover preview: Windows asks this window for the picture instead of shrinking the whole window.
        _hwnd = hwnd;
        HwndSource.FromHwnd(hwnd)?.AddHook(TaskbarHook);
        UpdatePreview();
    }

    // ── Busy overlay: fades in softly, and out only after the loader's logo has landed ──

    private void ShowBusy()
    {
        BusyOverlay.Visibility = Visibility.Visible; // the loader starts as soon as it is visible
        BusyOverlay.BeginAnimation(OpacityProperty, Fade(1, 0.45));
    }

    private void HideBusy()
    {
        if (BusyOverlay.Visibility != Visibility.Visible) return;
        var fade = Fade(0, 0.4);
        fade.Completed += (_, _) => { if (!_ws.IsBusy) BusyOverlay.Visibility = Visibility.Collapsed; };
        BusyOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private static DoubleAnimation Fade(double to, double seconds) =>
        new(to, TimeSpan.FromSeconds(seconds)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (BusyOverlay.IsVisible)
        {
            // The overlay stops the mouse; this stops the keyboard (Tab + Enter would still reach the page underneath).
            // Alt+F4 and other system keys arrive as SystemKey and stay untouched.
            if (e.Key != Key.System) e.Handled = true;
            return;
        }
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
        UpdatePreview();

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

    // ── Hover preview ("Detail on demand"): all nodes in multi mode, the one job in single mode ──

    /// <summary>
    /// During and after a run the thumbnail is our own card (<see cref="TaskbarPreview"/>); on the setup screen it stays
    /// the normal picture of the window. Windows keeps the last card and only asks again after it was marked as outdated.
    /// </summary>
    private void UpdatePreview()
    {
        if (_hwnd == IntPtr.Zero) return;
        Title = _ws.IsSetup ? "Conduit" : $"Conduit · {_ws.Crumb}"; // the title line above the card
        bool own = !_ws.IsSetup;
        if (own != _ownThumbnail)
        {
            _ownThumbnail = own;
            int on = own ? 1 : 0;
            _ = DwmSetWindowAttribute(_hwnd, 7, ref on, sizeof(int));  // DWMWA_FORCE_ICONIC_REPRESENTATION
            _ = DwmSetWindowAttribute(_hwnd, 10, ref on, sizeof(int)); // DWMWA_HAS_ICONIC_BITMAP
        }
        if (own) _ = DwmInvalidateIconicBitmaps(_hwnd);
    }

    private IntPtr TaskbarHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_ownThumbnail) return IntPtr.Zero;
        if (msg == 0x0323) // WM_DWMSENDICONICTHUMBNAIL: the mouse is over the taskbar button
        {
            long size = lParam.ToInt64();
            int maxWidth = (int)((size >> 16) & 0xFFFF), maxHeight = (int)(size & 0xFFFF);
            if (maxWidth > 0 && maxHeight > 0)
            {
                _preview ??= new TaskbarPreview();
                SendBitmap(_preview.Render(_ws, maxWidth, maxHeight), hbmp => DwmSetIconicThumbnail(hwnd, hbmp, 0));
            }
            handled = true;
        }
        else if (msg == 0x0326 && Content is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } root)
        {
            // WM_DWMSENDICONICLIVEPREVIEWBITMAP: the mouse is over the card itself – Windows "peeks" at the real window.
            var dpi = VisualTreeHelper.GetDpi(this);
            var area = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            var picture = new DrawingVisual();
            using (var dc = picture.RenderOpen())
            {
                dc.DrawRectangle(Background, null, area);
                dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None }, null, area);
            }
            var bitmap = new RenderTargetBitmap((int)(area.Width * dpi.DpiScaleX), (int)(area.Height * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(picture);
            SendBitmap(bitmap, hbmp => DwmSetIconicLivePreviewBitmap(hwnd, hbmp, IntPtr.Zero, 1)); // 1 = draw the window frame around it
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>DWM wants a 32-bit GDI bitmap: copy the WPF pixels into one, hand it over, free it.</summary>
    private static void SendBitmap(BitmapSource source, Func<IntPtr, int> send)
    {
        int width = source.PixelWidth, height = source.PixelHeight;
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // negative = rows from top to bottom, like WPF
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr hbmp = CreateDIBSection(IntPtr.Zero, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (hbmp == IntPtr.Zero) return;
        try
        {
            source.CopyPixels(Int32Rect.Empty, bits, width * 4 * height, width * 4);
            _ = send(hbmp);
        }
        finally
        {
            _ = DeleteObject(hbmp);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetIconicThumbnail(IntPtr hwnd, IntPtr hbmp, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetIconicLivePreviewBitmap(IntPtr hwnd, IntPtr hbmp, IntPtr clientOffset, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmInvalidateIconicBitmaps(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

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
