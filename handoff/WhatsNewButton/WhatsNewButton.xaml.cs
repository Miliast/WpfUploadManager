// WhatsNewButton – logic of the What's new button (the look is in WhatsNewButton.xaml). Self-contained: these two
// files are the whole control. Requirements: WPF, net8.0-windows or later, C# 12, no NuGet packages.
// Rename the namespace here and in WhatsNewButton.xaml (x:Class, xmlns:local) together if your project uses another one.
//
// Put it in the top bar right after the product name:
//
//     <whatsNew:WhatsNewButton x:Name="WhatsNew" Version="0.1.0" HasUnread="True" Click="OnWhatsNew" />
//
// Three properties decide what it shows (BackTo wins over HasUnread):
//   · Version    "0.1.0" – the version of the newest release in the changelog
//   · HasUnread  true after an update, until What's new was opened once      → "✿ New in 0.1.0"
//   · BackTo     None while What's new is closed                                → "✿ 0.1.0"
//                Setup / Run / Results while it is open                         → "← Back to setup / run / results"
//
// The host does the page switching; the control only says what was clicked. A typical handler:
//
//     private void OnWhatsNew(object sender, RoutedEventArgs e)
//     {
//         if (WhatsNew.BackTo == WhatsNewBack.None)
//         {
//             ShowWhatsNewPage();                                   // in place of the white page
//             WhatsNew.BackTo = IsRunning ? WhatsNewBack.Run : IsFinished ? WhatsNewBack.Results : WhatsNewBack.Setup;
//             WhatsNew.HasUnread = false;
//             Settings.LastOpenedVersion = WhatsNew.Version;         // so the "New" chip stays away until the next release
//         }
//         else
//         {
//             ShowPreviousPage();                                    // same tab, same scroll position as before
//             WhatsNew.BackTo = WhatsNewBack.None;
//         }
//     }
//
// When a run finishes while What's new is open, set BackTo = Results. On start-up:
//     WhatsNew.HasUnread = WhatsNewButton.IsUnread(WhatsNew.Version, Settings.LastOpenedVersion);
//
// Keys: call WhatsNew.HandleKey(e) first thing in the window's PreviewKeyDown – F1 opens (or closes) What's new,
// Esc goes back while it is open. Both raise Click, so the handler above covers them too.

#nullable enable

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Conduit.WhatsNew;

/// <summary>Where the back button leads. <see cref="None"/> = What's new is closed.</summary>
public enum WhatsNewBack { None, Setup, Run, Results }

/// <summary>The look the control shows – worked out from the properties, read by the XAML.</summary>
public enum WhatsNewLook { Version, New, Back }

public partial class WhatsNewButton : UserControl
{
    // ── Properties the host sets ─────────────────────────────────────────────────────────────────────

    public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(
        nameof(Version), typeof(string), typeof(WhatsNewButton), new PropertyMetadata("", OnStateChanged));

    public static readonly DependencyProperty HasUnreadProperty = DependencyProperty.Register(
        nameof(HasUnread), typeof(bool), typeof(WhatsNewButton), new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty BackToProperty = DependencyProperty.Register(
        nameof(BackTo), typeof(WhatsNewBack), typeof(WhatsNewButton), new PropertyMetadata(WhatsNewBack.None, OnStateChanged));

    /// <summary>Version of the newest release, e.g. "0.1.0".</summary>
    public string Version { get => (string)GetValue(VersionProperty); set => SetValue(VersionProperty, value); }

    /// <summary>True after an update until What's new was opened: shows the white "New in …" chip.</summary>
    public bool HasUnread { get => (bool)GetValue(HasUnreadProperty); set => SetValue(HasUnreadProperty, value); }

    /// <summary>Set while What's new is open: the control becomes "← Back to …".</summary>
    public WhatsNewBack BackTo { get => (WhatsNewBack)GetValue(BackToProperty); set => SetValue(BackToProperty, value); }

    // ── Worked out (read-only, for the XAML) ─────────────────────────────────────────────────────────

    private static readonly DependencyPropertyKey LookKey = DependencyProperty.RegisterReadOnly(
        nameof(Look), typeof(WhatsNewLook), typeof(WhatsNewButton), new PropertyMetadata(WhatsNewLook.Version));
    public static readonly DependencyProperty LookProperty = LookKey.DependencyProperty;

    private static readonly DependencyPropertyKey BackTextKey = DependencyProperty.RegisterReadOnly(
        nameof(BackText), typeof(string), typeof(WhatsNewButton), new PropertyMetadata(""));
    public static readonly DependencyProperty BackTextProperty = BackTextKey.DependencyProperty;

    /// <summary>Which of the three looks is showing.</summary>
    public WhatsNewLook Look => (WhatsNewLook)GetValue(LookProperty);

    /// <summary>"Back to setup", "Back to run" or "Back to results".</summary>
    public string BackText => (string)GetValue(BackTextProperty);

    // ── Click ───────────────────────────────────────────────────────────────────────────────────────

    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(
        nameof(Click), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(WhatsNewButton));

    /// <summary>Raised by a click, F1 or Esc (see <see cref="HandleKey"/>). The host opens What's new or goes back.</summary>
    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    public WhatsNewButton()
    {
        InitializeComponent();
        Refresh();
    }

    /// <summary>
    /// Call first thing in the window's PreviewKeyDown. F1 always acts (opens What's new, or goes back while it is
    /// open); Esc only goes back. Returns true and marks the key handled when it used it.
    /// </summary>
    public bool HandleKey(KeyEventArgs e)
    {
        if (e.Handled || !IsEnabled || !IsVisible || Keyboard.Modifiers != ModifierKeys.None) return false;
        bool use = e.Key == Key.F1 || (e.Key == Key.Escape && BackTo != WhatsNewBack.None);
        if (!use) return false;
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        return true;
    }

    /// <summary>
    /// True when <paramref name="current"/> is newer than the version What's new was last opened for
    /// (<paramref name="lastOpened"/> null or empty = never opened). Versions like "0.1.0" or "1.2".
    /// </summary>
    public static bool IsUnread(string current, string? lastOpened) =>
        System.Version.TryParse(current, out var now)
        && (!System.Version.TryParse(lastOpened, out var seen) || now > seen);

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // the button's own Click stops here; the host gets ours
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WhatsNewButton)d).Refresh();

    private void Refresh()
    {
        var look = BackTo != WhatsNewBack.None ? WhatsNewLook.Back
                 : HasUnread ? WhatsNewLook.New
                 : WhatsNewLook.Version;
        SetValue(LookKey, look);
        SetValue(BackTextKey, BackTo == WhatsNewBack.None ? "" : "Back to " + BackTo.ToString().ToLowerInvariant());

        // Screen readers hear what the button does, not just "0.1.0".
        if (Chip != null)
            AutomationProperties.SetName(Chip, look switch
            {
                WhatsNewLook.Back => BackText,
                WhatsNewLook.New => $"What's new – new in {Version}",
                _ => $"What's new – version {Version}",
            });
    }
}
