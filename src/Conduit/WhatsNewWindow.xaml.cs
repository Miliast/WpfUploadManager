using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Conduit;

/// <summary>
/// The "What's new" window. It reads Changelog.json (compiled into the app) and hands the releases to the
/// templates in WhatsNewWindow.xaml. To announce a release, edit Changelog.json – not this file.
/// Pictures and photos live in the Changelog folder next to it and are compiled in as well.
/// </summary>
public partial class WhatsNewWindow : Window
{
    private const int ShownAtFirst = 3; // the rest waits behind "Show … and earlier"

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly List<Release> _releases;
    private readonly ObservableCollection<Release> _shown = [];
    private bool _viewing; // the large view of a picture is open

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

    }

    // ── Loading ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Reads Changelog.json. The file lists the newest release first; that one starts with its lists open.</summary>
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

                change.Stamp = release.Stamp; // the preview card of a line says which release it belongs to
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

    // ── The large view of a picture ────────────────────────────────────────────────────────────────────────
    // A click on a picture – or on a list line that has one – shows it large, on a panel over the page.
    // The page behind goes out of focus (blur) and pale. Everything eases in; nothing pops.

    private void OnPictureClicked(object sender, MouseButtonEventArgs e)
    {
        Shot? shot = (sender as FrameworkElement)?.DataContext switch
        {
            Change change => change.Shot,
            Release release => release.Shot,
            _ => null,
        };
        if (shot is null) return; // a list line without a picture

        var bitmap = new BitmapImage(new Uri(shot.Picture));
        ViewerImage.Source = bitmap;
        ViewerImage.MaxWidth = bitmap.PixelWidth;                  // never blown up beyond its own pixels
        ViewerImage.MaxHeight = Math.Min(bitmap.PixelHeight, 520);
        Viewer.DataContext = shot;
        Viewer.Visibility = Visibility.Visible;
        _viewing = true;

        var time = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var blur = new BlurEffect();
        var grow = new ScaleTransform();
        Scroller.Effect = blur;
        ViewerPanel.RenderTransform = grow;
        blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, 18, time) { EasingFunction = ease });
        grow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, time) { EasingFunction = ease });
        grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, time) { EasingFunction = ease });
        Viewer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, time));
    }

    private void OnViewerClose(object sender, RoutedEventArgs e) => CloseViewer();

    private void CloseViewer()
    {
        if (!_viewing) return;
        _viewing = false;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) =>
        {
            if (_viewing) return; // opened again while it was fading out
            Viewer.Visibility = Visibility.Collapsed;
            Scroller.Effect = null;
            ViewerImage.Source = null;
        };
        Viewer.BeginAnimation(OpacityProperty, fade);
    }

    // ── Keys and buttons ───────────────────────────────────────────────────────────────────────────────────

    private void OnShowOlder(object sender, RoutedEventArgs e)
    {
        foreach (var release in _releases.Skip(ShownAtFirst)) _shown.Add(release);
        OlderRow.Visibility = Visibility.Collapsed;
    }

    /// <summary>Escape closes the large view if one is open, otherwise the window.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape) return;
        if (_viewing) CloseViewer();
        else Close();
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
    public string? Text { get; set; }   // optional: a sentence or two more
    public string? Image { get; set; }  // optional: a file in the Changelog folder, e.g. "mode-switch.png"

    // ── Worked out ─────────────────────────────────────────────────────────────────────────────────────────
    public string Stamp { get; set; } = ""; // "0.1.0 · NOV 12", filled while loading: a line does not know its release

    public string? Picture => Release.PictureOf(Image);
    public bool HasPicture => Picture is not null;

    /// <summary>A "new" item that brings a text or a picture gets its own block; all others become list lines.</summary>
    public bool IsFeature => Type == "new" && (!string.IsNullOrEmpty(Text) || !string.IsNullOrEmpty(Image));

    /// <summary>The small word on top of the preview card.</summary>
    public string Label => Type switch { "new" => "NEW", "improved" => "IMPROVEMENT", "fixed" => "FIX", _ => Type.ToUpperInvariant() };

    /// <summary>What the large view shows for this item; null without a picture.</summary>
    public Shot? Shot => Picture is { } picture ? new Shot(picture, Title, Text, $"{Label} · {Stamp}") : null;
}

/// <summary>A picture in the large view: the picture, a title, an optional text and the small line on the right.</summary>
public sealed record Shot(string Picture, string Title, string? Text, string Caption);

/// <summary>A small label under the date. <see cref="Kind"/> picks the colour of its dot: "feature", "improvement", "fix" or "tag" (grey).</summary>
public sealed record Badge(string Text, string Kind);

/// <summary>
/// One of the lists of a release ("Improvements 3"). It can be folded; <see cref="Open"/> is how it starts.
/// <see cref="First"/> is the top one of its release – it has no dividing line above it.
/// </summary>
public sealed record Fold(string Title, IReadOnlyList<Change> Items, bool Open, bool First)
{
    public int Count => Items.Count;
}

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
    public string? Image { get; set; }          // optional: a picture under the headline
    public List<string> By { get; set; } = [];   // optional: who shipped it – "Shipped by …" at the end
    public List<string> Tags { get; set; } = []; // optional: extra badges besides the automatic ones
    public List<Change> Items { get; set; } = [];

    // ── Worked out ─────────────────────────────────────────────────────────────────────────────────────────
    public bool IsLatest { get; set; }
    public IReadOnlyList<Author> Authors { get; set; } = []; // filled while loading: it needs to look for the photos

    private DateOnly Day => DateOnly.ParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string DateText => Day.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>"0.1.0 · NOV 12" – the small line on the preview card and in the large view.</summary>
    public string Stamp => $"{Version} · {Day.ToString("MMM d", CultureInfo.InvariantCulture).ToUpperInvariant()}";

    public string? Picture => PictureOf(Image);

    /// <summary>What the large view shows for the picture of the release; null without one.</summary>
    public Shot? Shot => Picture is { } picture ? new Shot(picture, Headline, Summary, Stamp) : null;

    /// <summary>"new" items with a text or picture: title, paragraph and picture each.</summary>
    public IReadOnlyList<Change> Features => Items.Where(c => c.IsFeature).ToList();

    /// <summary>The other "new" items, as a list. It is called "Also new" when features stand above it.</summary>
    public IReadOnlyList<Change> NewList => Of("new").Where(c => !c.IsFeature).ToList();
    public string NewLabel => Features.Count > 0 ? "Also new" : "New";

    public IReadOnlyList<Change> Improvements => Of("improved");
    public IReadOnlyList<Change> Fixes => Of("fixed");

    /// <summary>
    /// The lists that have something in them, in their fixed order. In the newest release they start open,
    /// in older ones folded – there the headline and picture are enough at first sight.
    /// </summary>
    public IReadOnlyList<Fold> Folds
    {
        get
        {
            (string Title, IReadOnlyList<Change> Items)[] lists = [(NewLabel, NewList), ("Improvements", Improvements), ("Fixes", Fixes)];
            return lists.Where(list => list.Items.Count > 0)
                        .Select((list, index) => new Fold(list.Title, list.Items, IsLatest, index == 0))
                        .ToList();
        }
    }

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
