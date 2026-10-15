using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UploadManager.App.Converters;

/// <summary>
/// Converts a tree depth (0, 1, 2, …) into a left margin: <c>Offset + depth * Indent</c>.
/// Used for the row content as well as for the vertical indent guide lines.
/// </summary>
[ValueConversion(typeof(int), typeof(Thickness))]
public sealed class TreeIndentConverter : IValueConverter
{
    public double Indent { get; set; } = 16;

    public double Offset { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var depth = value is int d ? Math.Max(d, 0) : 0;
        return new Thickness(Offset + (depth * Indent), 0, 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
