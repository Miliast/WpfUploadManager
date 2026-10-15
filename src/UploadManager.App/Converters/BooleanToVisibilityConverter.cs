using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UploadManager.App.Converters;

/// <summary>
/// Converts <see langword="true"/> to <see cref="Visibility.Visible"/>, otherwise <see cref="Visibility.Collapsed"/>.
/// Set <see cref="IsInverted"/> to swap the result.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public bool IsInverted { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ IsInverted ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is Visibility.Visible) ^ IsInverted;
}
