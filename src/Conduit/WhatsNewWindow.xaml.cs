using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Conduit;

/// <summary>
/// The "What's new" window. It reads Changelog.json (compiled into the app) and hands the releases to the
/// templates in WhatsNewWindow.xaml. To announce a release, edit Changelog.json – not this file.
/// Pictures and photos live in the Changelog folder next to it and are compiled in as well.
/// </summary>
public partial class WhatsNewWindow : Window
{
    private const int ShownAtFirst = 3;    // the rest waits behind "Show … and earlier"
    private const double PinTop = 24;      // a pinned date rests this far below the top edge of the window
    private const double ReleaseGap = 80;  // empty space under every release (the bottom margin of "Body" in the template)

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly List<Release> _releases;
    private readonly ObservableCollection<Release> _shown = [];
    private int _current = -1; // the release whose dot is filled right now

    public WhatsNewWindow()
    {
        InitializeComponent();
        _releases = Load();

        var version = typeof(WhatsNewWindow).Assembly.GetName().Version;
        CurrentVersion.Text = $"You're on {version?.ToString(3)}";

        foreach (var release in _releases.Take(ShownAtFirst)) _shown.Add(release);
        Timeline.ItemsSource = _shown;
        if (_releases.Count > ShownAtFirst) OlderText.Text = $"Show {_releases[ShownAtFirst].Version} and earlier";
        else OlderRow.Visibility = Visibility.Collapsed;

        Loaded += (_, _) => UpdatePins();
    }

    // ── Loading ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Reads Changelog.json. The file lists the newest release first; that one starts with the filled dot.</summary>
    private static List<Release> Load()
    {
        using Stream json = Application.GetResourceStream(new Uri("Changelog.json", UriKind.Relative)).Stream;
        var releases = JsonSerializer.Deserialize<List<Release>>(json, JsonOptions) ?? [];
        if (releases.Count > 0) releases[0].IsLatest = true;

        // A typo such as "fix", or a picture that was never added, would make something vanish without a trace –
        // better to hear about it the first time the window is opened.
        foreach (var release in releases)
        {
            if (release.Picture is { } cover && !Exists(cover))
                throw new InvalidDataException($"Changelog.json, {release.Version}: the picture \"{release.Image}\" is not in the Changelog folder.");

            foreach (var change in release.Items)
            {
                if (change.Type is not ("new" or "improved" or "fixed"))
                    throw new InvalidDataException($"Changelog.json, {release.Version}, \"{change.Title}\": type is \"{change.Type}\" – use new, improved or fixed.");
                if (change.Picture is { } picture && !Exists(picture))
                    throw new InvalidDataException($"Changelog.json, {release.Version}, \"{change.Title}\": the picture \"{change.Image}\" is not in the Changelog folder.");
            }

            // A photo is optional: without Changelog\people\<name>.png the circle shows the first letter of the name.
            release.Authors = release.By
                .Select(name => new Author(name, Exists(Author.PhotoOf(name)) ? Author.PhotoOf(name) : null))
                .ToList();
        }
        return releases;
    }

    /// <summary>True when the picture behind this pack address was compiled into the app.</summary>
    private static bool Exists(string address)
    {
        try
        {
            Application.GetResourceStream(new Uri(address)).Stream.Dispose();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // ── The date that stays ────────────────────────────────────────────────────────────────────────────────
    // WPF has no "sticky". While a release is being read, its date block, its dot and a blue trail on the rail
    // are pushed down by exactly the distance the release has scrolled out at the top. Only render transforms
    // are touched, so scrolling never causes a new layout pass.

    private void OnScrolled(object sender, ScrollChangedEventArgs e) => UpdatePins();

    private void UpdatePins()
    {
        int count = Timeline.Items.Count;
        var rows = new ContentPresenter?[count];
        var tops = new double[count];
        int current = 0;

        // Where is every release right now? The last one that has reached the pin line is the one being read.
        for (int i = 0; i < count; i++)
        {
            if (Timeline.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter { IsLoaded: true } row) continue;
            rows[i] = row;
            tops[i] = row.TranslatePoint(new Point(), Scroller).Y;
            if (tops[i] <= PinTop) current = i;
        }

        for (int i = 0; i < count; i++)
        {
            if (rows[i] is not { } row || Part<FrameworkElement>(row, "Pin") is not { } pin) continue;

            double offset = PinOffset(tops[i], row.ActualHeight, pin.Margin.Top + pin.ActualHeight);
            pin.RenderTransform = new TranslateTransform(0, offset);
            if (Part<UIElement>(row, "Node") is { } node) node.RenderTransform = new TranslateTransform(0, offset);
            if (Part<UIElement>(row, "Trail") is { } trail) trail.RenderTransform = new ScaleTransform(1, offset); // 1 px tall, stretched to the dot
        }

        // The filled dot moves to the release being read – as a soft cross-fade, not a jump.
        if (current < count && current != _current && rows[current] is { } now)
        {
            if (_current >= 0 && _current < count && rows[_current] is { } before) Fade(before, 0);
            Fade(now, 1);
            _current = current;
        }
    }

    /// <summary>
    /// How far a date has to slide down: nothing until its release reaches the pin line, then pixel for pixel
    /// with the scrolling, and never further than the end of its own release (so it cannot run into the next date).
    /// </summary>
    internal static double PinOffset(double top, double rowHeight, double pinHeight)
    {
        double room = Math.Max(0, rowHeight - ReleaseGap - pinHeight);
        return Math.Round(Math.Clamp(PinTop - top, 0, room));
    }

    private static void Fade(ContentPresenter row, double to)
    {
        if (Part<UIElement>(row, "NodeOn") is { } dot)
            dot.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(180)));
    }

    /// <summary>A named element inside one release of the timeline (see the template "Release").</summary>
    private static T? Part<T>(ContentPresenter row, string name) where T : class =>
        row.ContentTemplate?.FindName(name, row) as T;

    // ── Keys and buttons ───────────────────────────────────────────────────────────────────────────────────

    private void OnShowOlder(object sender, RoutedEventArgs e)
    {
        foreach (var release in _releases.Skip(ShownAtFirst)) _shown.Add(release);
        OlderRow.Visibility = Visibility.Collapsed;
        Dispatcher.BeginInvoke(new Action(UpdatePins), DispatcherPriority.Loaded); // once the new releases are laid out
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }
}

// ── Data ───────────────────────────────────────────────────────────────────────────────────────────────────
// Everything below is plain data: no window, no brushes. The upper properties of a class are typed in
// Changelog.json; what follows under "Worked out" is derived from them, so nothing can get out of step.

/// <summary>One line of a release in Changelog.json. <see cref="Type"/> is "new", "improved" or "fixed".</summary>
public sealed class Change
{
    public string Type { get; set; } = "new";
    public string Title { get; set; } = "";
    public string? Text { get; set; }
    public string? Image { get; set; }  // optional: a file in the Changelog folder, e.g. "mode-switch.png"

    // ── Worked out ─────────────────────────────────────────────────────────────────────────────────────────
    public string? Picture => Release.PictureOf(Image);

    /// <summary>A "new" item that brings a text or a picture gets its own block; all others become list lines.</summary>
    public bool IsFeature => Type == "new" && (!string.IsNullOrEmpty(Text) || !string.IsNullOrEmpty(Image));
}

/// <summary>A small label under the date. <see cref="Kind"/> picks the colour: "feature", "improvement", "fix" or "tag" (grey).</summary>
public sealed record Badge(string Text, string Kind);

/// <summary>Somebody named under "by". <see cref="Photo"/> is null when there is no picture of them in Changelog\people.</summary>
public sealed record Author(string Name, string? Photo)
{
    public string Initial => Name.Trim() is { Length: > 0 } name ? name[..1].ToUpperInvariant() : "?";

    /// <summary>"Anna Maria" → the address of Changelog\people\anna-maria.png.</summary>
    public static string PhotoOf(string name) => Release.PictureOf("people/" + name.Trim().Replace(' ', '-') + ".png")!;
}

/// <summary>One release = one block in Changelog.json.</summary>
public sealed class Release
{
    public string Version { get; set; } = "";
    public string Date { get; set; } = "";      // 2026-11-12
    public string Headline { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? Image { get; set; }          // optional: a picture under the summary
    public List<string> By { get; set; } = [];   // optional: who shipped it – "Shipped by …" at the end
    public List<string> Tags { get; set; } = []; // optional: extra badges besides the automatic ones
    public List<Change> Items { get; set; } = [];

    // ── Worked out ─────────────────────────────────────────────────────────────────────────────────────────
    public bool IsLatest { get; set; }
    public IReadOnlyList<Author> Authors { get; set; } = []; // filled while loading: it needs to look for the photos

    public string DateText =>
        DateOnly.ParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    public string? Picture => PictureOf(Image);

    /// <summary>"new" items with a text or picture: title, paragraph and picture each.</summary>
    public IReadOnlyList<Change> Features => Items.Where(c => c.IsFeature).ToList();

    /// <summary>The other "new" items, as a list. It is called "Also new" when features stand above it.</summary>
    public IReadOnlyList<Change> NewList => Of("new").Where(c => !c.IsFeature).ToList();
    public string NewLabel => Features.Count > 0 ? "Also new" : "New";

    public IReadOnlyList<Change> Improvements => Of("improved");
    public IReadOnlyList<Change> Fixes => Of("fixed");

    /// <summary>
    /// Feature / Improvement / Bug fixes appear by themselves as soon as the release has such an item,
    /// so a badge can never contradict the text next to it. Words from "tags" follow in grey.
    /// </summary>
    public IReadOnlyList<Badge> Badges
    {
        get
        {
            var badges = new List<Badge>();
            if (Of("new").Count > 0) badges.Add(new Badge("Feature", "feature"));
            if (Of("improved").Count > 0) badges.Add(new Badge("Improvement", "improvement"));
            if (Of("fixed").Count > 0) badges.Add(new Badge("Bug fixes", "fix"));
            foreach (string tag in Tags)
                if (!badges.Any(b => string.Equals(b.Text, tag, StringComparison.OrdinalIgnoreCase)))
                    badges.Add(new Badge(tag, "tag"));
            return badges;
        }
    }

    /// <summary>"Mili", "Mili and Lena", "Mili, Lena and Tom".</summary>
    public string ByText => By.Count switch
    {
        0 => "",
        1 => By[0],
        _ => string.Join(", ", By.Take(By.Count - 1)) + " and " + By[^1],
    };

    private List<Change> Of(string type) => Items.Where(c => c.Type == type).ToList();

    /// <summary>
    /// File name → the address WPF loads it from. Compiled-in files are stored in lower case, hence ToLowerInvariant.
    /// </summary>
    internal static string? PictureOf(string? file) =>
        string.IsNullOrWhiteSpace(file) ? null : "pack://application:,,,/changelog/" + file.Trim().ToLowerInvariant().Replace(" ", "%20");
}
