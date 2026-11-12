using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Conduit;

/// <summary>
/// Two-option pill switch. The look is in ModeSwitch.xaml; this file holds the selection and the motion:
/// the thumb's two edges are animated separately, so the edge facing the new side leaves first and the other
/// one catches up – the thumb stretches like a drop and settles. The chosen side also opens an icon slot (one page
/// for the first option, a fan of three for the second); the thumb follows the segments as they resize. A wide aurora wash fades in and out over the
/// whole track at the same time (opacity only, never growing from a point).
/// Left/Right arrows switch sides. With Windows animations turned off, the thumb simply jumps.
/// </summary>
internal sealed partial class ModeSwitch : UserControl
{
    // ── Tuning ─────────────────────────────────────────────────────────────
    private const double LeadMs = 300;     // the edge facing the new side
    private const double TrailMs = 480;    // the edge that catches up – the gap between the two is the stretch
    private const double WashInMs = 380;   // aurora wash: rise …
    private const double WashOutMs = 820;  // … and fall
    private const double WashPeak = 1.0;   // strongest opacity of the wash (its colours are already faint)
    private const double WashDrift = 0.18; // how far the wash colours drift towards the chosen side (fraction of the width)

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(ModeSwitch),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((ModeSwitch)d).OnSelectedIndexChanged(),
            (_, value) => (int)value == 1 ? 1 : 0));

    public static readonly DependencyProperty FirstTextProperty = DependencyProperty.Register(
        nameof(FirstText), typeof(string), typeof(ModeSwitch), new PropertyMetadata("First"));

    public static readonly DependencyProperty SecondTextProperty = DependencyProperty.Register(
        nameof(SecondText), typeof(string), typeof(ModeSwitch), new PropertyMetadata("Second"));

    // How far each edge of the thumb has travelled from the first segment (0) to the second (1), animated on
    // their own. The pixel position is worked out from the live layout in PlaceThumb, so the thumb stays glued
    // to the segments while their icon slots open and close mid-flight.
    private static readonly DependencyProperty LeftTravelProperty = DependencyProperty.Register(
        "LeftTravel", typeof(double), typeof(ModeSwitch), new PropertyMetadata(0d, (d, _) => ((ModeSwitch)d).PlaceThumb()));

    private static readonly DependencyProperty RightTravelProperty = DependencyProperty.Register(
        "RightTravel", typeof(double), typeof(ModeSwitch), new PropertyMetadata(0d, (d, _) => ((ModeSwitch)d).PlaceThumb()));

    private bool _syncing;

    public ModeSwitch()
    {
        InitializeComponent();
        SyncSegments();
        Loaded += (_, _) => MoveThumb(animate: false);
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>0 = first (left) option, 1 = second (right) option. Binds two-way by default.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public string FirstText
    {
        get => (string)GetValue(FirstTextProperty);
        set => SetValue(FirstTextProperty, value);
    }

    public string SecondText
    {
        get => (string)GetValue(SecondTextProperty);
        set => SetValue(SecondTextProperty, value);
    }

    /// <summary>Raised after the selection changed (by click, keyboard, code or binding).</summary>
    public event EventHandler? SelectedIndexChanged;

    private RadioButton Selected => SelectedIndex == 1 ? Second : First;

    // ── Selection ──────────────────────────────────────────────────────────

    private void OnSegmentChecked(object sender, RoutedEventArgs e)
    {
        if (!_syncing)
            SelectedIndex = ReferenceEquals(sender, Second) ? 1 : 0;
    }

    private void OnSelectedIndexChanged()
    {
        SyncSegments();
        MoveThumb(animate: IsLoaded);
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncSegments()
    {
        _syncing = true;
        First.IsChecked = SelectedIndex == 0;
        Second.IsChecked = SelectedIndex == 1;
        _syncing = false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right))
            return;

        var forward = (e.Key == Key.Right) != (FlowDirection == FlowDirection.RightToLeft);
        SelectedIndex = forward ? 1 : 0;
        Selected.Focus();
        e.Handled = true;
    }

    // A segment got wider or narrower (its icon slot is opening or closing, or the text changed): keep the thumb on it.
    private void OnSegmentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsLoaded)
            PlaceThumb();
    }

    // ── Motion ─────────────────────────────────────────────────────────────

    private void MoveThumb(bool animate)
    {
        double target = SelectedIndex;

        if (!animate || !SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(LeftTravelProperty, null);
            BeginAnimation(RightTravelProperty, null);
            SetValue(LeftTravelProperty, target);
            SetValue(RightTravelProperty, target);
            PlaceThumb();
            return;
        }

        // The edge facing the new side leads, the other one trails – that gap is the stretch.
        var movingRight = SelectedIndex == 1;
        BeginAnimation(LeftTravelProperty, Glide(target, movingRight ? TrailMs : LeadMs));
        BeginAnimation(RightTravelProperty, Glide(target, movingRight ? LeadMs : TrailMs));

        PlayWash(movingRight);
    }

    // Starts from wherever the edge is right now, so a second click mid-flight turns around smoothly.
    private static DoubleAnimation Glide(double to, double ms) => new()
    {
        To = to,
        Duration = TimeSpan.FromMilliseconds(ms),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
    };

    private void PlaceThumb()
    {
        if (!IsLoaded)
            return;

        var firstLeft = First.TranslatePoint(new Point(0, 0), ThumbLayer).X;
        var secondLeft = Second.TranslatePoint(new Point(0, 0), ThumbLayer).X;
        var firstRight = firstLeft + First.ActualWidth;
        var secondRight = secondLeft + Second.ActualWidth;

        var leftTravel = (double)GetValue(LeftTravelProperty);
        var rightTravel = (double)GetValue(RightTravelProperty);
        var left = firstLeft + (secondLeft - firstLeft) * leftTravel;
        var right = firstRight + (secondRight - firstRight) * rightTravel;

        Canvas.SetLeft(Thumb, left);
        Thumb.Width = Math.Max(0, right - left);
    }

    // The wash is full-width from its first frame; only its opacity rises and falls, slow-in and slow-out.
    private void PlayWash(bool movingRight)
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var total = WashInMs + WashOutMs;

        var breathe = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(total) };
        breathe.KeyFrames.Add(new EasingDoubleKeyFrame(WashPeak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(WashInMs)), ease));
        breathe.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(total)), ease));
        Wash.BeginAnimation(OpacityProperty, breathe);

        var direction = movingRight ? 1 : -1;
        WashShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            From = -direction * WashDrift,
            To = direction * WashDrift,
            Duration = TimeSpan.FromMilliseconds(total),
            EasingFunction = ease,
        });
    }
}
