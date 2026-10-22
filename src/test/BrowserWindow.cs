using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;

namespace WpfBrowserDemo;

/// <summary>
/// Arc-style browser shell in a single class (no XAML, no NuGet packages):
/// a frosted Mica sidebar on the left (Windows 11 22H2+) and an opaque white content card on the right.
/// </summary>
public sealed class BrowserWindow : Window
{
    [STAThread]
    public static void Main() => new Application().Run(new BrowserWindow());

    // ── Win32: Mica backdrop and rounded corners via the Desktop Window Manager ──
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_MAINWINDOW = 2; // 2 = Mica, 3 = Acrylic (frosted glass), 4 = Mica Alt

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ── Look & feel ──
    private const double CaptionHeight = 54;
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly SolidColorBrush TextBrush = Hex("#1F1F1F");
    private static readonly SolidColorBrush MutedBrush = Hex("#8A8F98");
    private static readonly SolidColorBrush SelectedTabBrush = Hex("#BFFFFFFF");

    private static readonly Style SidebarButton = ButtonStyle(Brushes.Transparent, Hex("#55FFFFFF"), new CornerRadius(8));
    private static readonly Style PinnedTileButton = ButtonStyle(Hex("#66FFFFFF"), Hex("#A6FFFFFF"), new CornerRadius(10));
    private static readonly Style ToolbarButton = ButtonStyle(Brushes.Transparent, Hex("#0F000000"), new CornerRadius(6));
    private static readonly Style CaptionButton = ButtonStyle(Brushes.Transparent, Hex("#12000000"), new CornerRadius(0));
    private static readonly Style CloseButton = ButtonStyle(Brushes.Transparent, Hex("#C42B1C"), new CornerRadius(0), Brushes.White);

    private sealed record Tab(string Title, string Domain, string PageTitle, string Icon, Brush IconBackground);

    private readonly List<Tab> _tabs = new()
    {
        new("Weekly Ad | Michaels", "michaels.com", "Weekly Ad | Michaels", "M", Hex("#111111")),
        new("Once declared locally extinct", "cbc.ca",
            "Once declared locally extinct, Vancouver Island's western bluebirds are making a comeback | CBC News", "✱", Hex("#E60505")),
    };

    private readonly Grid _root = new();
    private readonly StackPanel _tabList = new();
    private readonly TextBlock _domain = new() { FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _pageTitle = new()
    {
        FontSize = 15, Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock _maximizeGlyph = Glyph("", 10);

    public BrowserWindow()
    {
        Title = "Browser";
        Width = 1280;
        Height = 820;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 14;
        Foreground = TextBrush;
        UseLayoutRounding = true;
        Background = Hex("#DCE8E6"); // fallback tint; replaced by real Mica in OnSourceInitialized

        // Remove the standard title bar but keep native resizing, snapping and window dragging.
        // GlassFrameThickness = -1 lets Windows draw the backdrop behind the *whole* window.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = CaptionHeight,
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(6),
            UseAeroCaptionButtons = false,
        });

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(272) });
        _root.ColumnDefinitions.Add(new ColumnDefinition());
        _root.Children.Add(BuildSidebar());
        var content = BuildContentArea();
        Grid.SetColumn(content, 1);
        _root.Children.Add(content);
        Content = _root;

        // A maximized chrome-less window hangs ~7px over the screen edges; pad it back in.
        StateChanged += (_, _) =>
        {
            bool max = WindowState == WindowState.Maximized;
            _root.Margin = max ? new Thickness(7) : new Thickness(0);
            _maximizeGlyph.Text = max ? "" : "";
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control) AddTab();
        };

        RenderTabs();
        SelectTab(1);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        if (Environment.OSVersion.Version.Build < 22621) return; // system backdrops need Windows 11 22H2

        int lightMode = 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref lightMode, sizeof(int));
        int backdrop = DWMSBT_MAINWINDOW;
        if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) != 0) return;

        // Everything WPF leaves transparent now shows the Mica material.
        Background = Brushes.Transparent;
        if (HwndSource.FromHwnd(hwnd) is { } source) source.CompositionTarget.BackgroundColor = Colors.Transparent;
    }

    // ── Left: frosted sidebar ─────────────────────────────────────────────

    private UIElement BuildSidebar()
    {
        var sidebar = new DockPanel { Margin = new Thickness(10, 8, 8, 10) };

        var profile = new DockPanel();
        var chevron = Glyph("", 10);
        DockPanel.SetDock(chevron, Dock.Right);
        profile.Children.Add(chevron);
        profile.Children.Add(new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 14, 0),
            Background = Hex("#40000000"), Child = Glyph("", 11, Brushes.White),
        });
        profile.Children.Add(new TextBlock { Text = "life", FontSize = 15, VerticalAlignment = VerticalAlignment.Center });

        var profileButton = new Button
        {
            Content = profile, Height = 40, Padding = new Thickness(12, 0, 12, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Style = SidebarButton,
        };
        // It sits inside the draggable caption strip, so it has to opt in to receiving clicks.
        WindowChrome.SetIsHitTestVisibleInChrome(profileButton, true);
        AddTop(sidebar, profileButton);

        var pinned = new UniformGrid { Columns = 3, Margin = new Thickness(-3, 8, -3, 10) };
        pinned.Children.Add(PinnedTile(Glyph("", 18, Hex("#1A73E8"))));
        pinned.Children.Add(PinnedTile(Letter("M", Hex("#EA4335"))));
        pinned.Children.Add(PinnedTile(Letter("N", Hex("#E50914"))));
        AddTop(sidebar, pinned);

        AddTop(sidebar, _tabList);

        var newTab = SidebarItem(Glyph("", 12, MutedBrush), "New Tab");
        newTab.Foreground = MutedBrush;
        newTab.Click += (_, _) => AddTab();
        AddTop(sidebar, newTab);

        sidebar.Children.Add(new Border()); // fills the rest
        return sidebar;
    }

    private void RenderTabs()
    {
        _tabList.Children.Clear();
        for (int i = 0; i < _tabs.Count; i++)
        {
            int index = i;
            var tab = _tabs[i];
            var favicon = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Background = tab.IconBackground,
                Child = new TextBlock
                {
                    Text = tab.Icon, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var item = SidebarItem(favicon, tab.Title);
            item.Click += (_, _) => SelectTab(index);
            _tabList.Children.Add(item);
        }
    }

    private void SelectTab(int index)
    {
        for (int i = 0; i < _tabList.Children.Count; i++)
        {
            var item = (Button)_tabList.Children[i];
            if (i == index)
            {
                item.Background = SelectedTabBrush; // a local value wins over the style's hover trigger
                item.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.08 };
            }
            else
            {
                item.ClearValue(BackgroundProperty);
                item.Effect = null;
            }
        }

        var tab = _tabs[index];
        _domain.Text = tab.Domain;
        _pageTitle.Text = tab.Domain.Length > 0 ? $" / {tab.PageTitle}" : tab.PageTitle;
    }

    private void AddTab()
    {
        _tabs.Add(new Tab("New Tab", "", "Search or enter address", "+", Hex("#9AA0A6")));
        RenderTabs();
        SelectTab(_tabs.Count - 1);
    }

    // ── Right: opaque white content card ──────────────────────────────────

    private UIElement BuildContentArea()
    {
        var toolbar = new DockPanel { Height = 48, Margin = new Thickness(8, 0, 0, 0), LastChildFill = true };
        AddLeft(toolbar, ChromeButton(ToolbarButton, Glyph("", 13), 40, 36, null));
        AddLeft(toolbar, ChromeButton(ToolbarButton, Glyph("", 13, MutedBrush), 40, 36, null));
        AddLeft(toolbar, ChromeButton(ToolbarButton, Glyph("", 13), 40, 36, null));

        AddRight(toolbar, ChromeButton(CloseButton, Glyph("", 10), 46, 48, Close));
        AddRight(toolbar, ChromeButton(CaptionButton, _maximizeGlyph, 46, 48,
            () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        AddRight(toolbar, ChromeButton(CaptionButton, Glyph("", 10), 46, 48, () => WindowState = WindowState.Minimized));

        var chat = new StackPanel { Orientation = Orientation.Horizontal };
        chat.Children.Add(new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(7.5), Background = Hex("#1DB954"), Margin = new Thickness(0, 0, 8, 0) });
        chat.Children.Add(new TextBlock { Text = "Chat", FontSize = 15 });
        var chatButton = ChromeButton(ToolbarButton, chat, double.NaN, 36, null);
        chatButton.Padding = new Thickness(10, 0, 10, 0);
        chatButton.Margin = new Thickness(0, 0, 14, 0);
        AddRight(toolbar, chatButton);

        // The address text is not hit-test visible, so the window can be dragged by it like a title bar.
        var address = new DockPanel { Margin = new Thickness(14, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        AddLeft(address, _domain);
        address.Children.Add(_pageTitle);
        toolbar.Children.Add(address);

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(BuildPagePlaceholder());

        var card = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            BorderBrush = Hex("#12000000"),
            BorderThickness = new Thickness(1),
            Child = layout,
        };
        // Border doesn't clip its children, so clip to the rounded shape (keeps the red close button inside).
        card.SizeChanged += (_, _) =>
            layout.Clip = new RectangleGeometry(new Rect(0, 0, layout.ActualWidth, layout.ActualHeight), 7, 7);

        // The shadow lives on a separate layer: an Effect on the card itself would turn off ClearType for all its text.
        var shadow = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.12 },
        };

        var area = new Grid { Margin = new Thickness(0, 6, 6, 6) };
        area.Children.Add(shadow);
        area.Children.Add(card);
        return area;
    }

    /// <summary>Stand-in for the web page. A real browser would put a WebView2 control here.</summary>
    private static UIElement BuildPagePlaceholder()
    {
        var listenText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        listenText.Children.Add(new TextBlock { Text = "Listen to this article", FontSize = 20, FontWeight = FontWeights.SemiBold });
        listenText.Children.Add(new TextBlock { Text = "Estimated 3 minutes", Foreground = MutedBrush, Margin = new Thickness(0, 2, 0, 0) });

        var listenRow = new DockPanel();
        AddLeft(listenRow, new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Background = Hex("#333333"),
            Margin = new Thickness(0, 0, 20, 0), Child = Glyph("", 16, Brushes.White),
        });
        listenRow.Children.Add(listenText);

        var article = new StackPanel { MaxWidth = 1100, Margin = new Thickness(48, 4, 48, 40) };
        article.Children.Add(new Border
        {
            Background = Hex("#F4F7F9"), CornerRadius = new CornerRadius(6), Padding = new Thickness(24, 16, 24, 16),
            Margin = new Thickness(0, 0, 0, 24), Child = listenRow,
        });
        article.Children.Add(new Border
        {
            Height = 520,
            Background = new LinearGradientBrush(Color.FromRgb(0x6B, 0x78, 0x45), Color.FromRgb(0x2E, 0x38, 0x22), 90),
            Child = new TextBlock
            {
                Text = "Page content / WebView2 goes here", Foreground = Hex("#CCFFFFFF"), FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        article.Children.Add(new TextBlock
        {
            Text = "A population of bluebirds that were revived in Washington state have become a source of \"donor\" birds for the recovery on Vancouver Island.",
            TextWrapping = TextWrapping.Wrap, FontSize = 18, LineHeight = 30, Margin = new Thickness(0, 24, 0, 0), Foreground = Hex("#444444"),
        });

        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = article };
    }

    // ── Small building blocks ─────────────────────────────────────────────

    private static Button SidebarItem(UIElement icon, string text)
    {
        var row = new DockPanel();
        var iconHost = new Border { Width = 18, Margin = new Thickness(0, 0, 14, 0), Child = icon };
        AddLeft(row, iconHost);
        row.Children.Add(new TextBlock { Text = text, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

        return new Button
        {
            Content = row, Height = 44, Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(12, 0, 12, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Style = SidebarButton,
        };
    }

    private static Button PinnedTile(UIElement icon) =>
        new() { Content = icon, Height = 56, Margin = new Thickness(3), Style = PinnedTileButton };

    private static Button ChromeButton(Style style, UIElement content, double width, double height, Action? onClick)
    {
        var button = new Button { Content = content, Width = width, Height = height, Style = style };
        if (onClick is not null) button.Click += (_, _) => onClick();
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        return button;
    }

    /// <summary>Flat button: rounded background, hover colour from a style trigger, no default Windows chrome.</summary>
    private static Style ButtonStyle(Brush normal, Brush hover, CornerRadius radius, Brush? hoverForeground = null)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        border.SetValue(Border.CornerRadiusProperty, radius);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, new TemplateBindingExtension(HorizontalContentAlignmentProperty));
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = border }));
        style.Setters.Add(new Setter(BackgroundProperty, normal));
        style.Setters.Add(new Setter(ForegroundProperty, TextBrush));
        style.Setters.Add(new Setter(FocusVisualStyleProperty, null));

        var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(BackgroundProperty, hover));
        if (hoverForeground is not null) hoverTrigger.Setters.Add(new Setter(ForegroundProperty, hoverForeground));
        style.Triggers.Add(hoverTrigger);

        var pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(OpacityProperty, 0.75));
        style.Triggers.Add(pressedTrigger);
        return style;
    }

    private static TextBlock Glyph(string glyph, double size, Brush? color = null)
    {
        var text = new TextBlock
        {
            Text = glyph, FontFamily = IconFont, FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        if (color is not null) text.Foreground = color; // otherwise inherit from the button (e.g. white on red close hover)
        return text;
    }

    private static TextBlock Letter(string letter, Brush color) => new()
    {
        Text = letter, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = color,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    private static void AddTop(DockPanel panel, UIElement child) { DockPanel.SetDock(child, Dock.Top); panel.Children.Add(child); }
    private static void AddLeft(DockPanel panel, UIElement child) { DockPanel.SetDock(child, Dock.Left); panel.Children.Add(child); }
    private static void AddRight(DockPanel panel, UIElement child) { DockPanel.SetDock(child, Dock.Right); panel.Children.Add(child); }

    private static SolidColorBrush Hex(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
