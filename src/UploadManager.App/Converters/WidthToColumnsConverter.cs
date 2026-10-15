using System.Globalization;
using System.Windows.Data;

namespace UploadManager.App.Converters;

/// <summary>
/// Responsive layout helper: returns 1 column below <see cref="Breakpoint"/>, otherwise <see cref="Columns"/>.
/// </summary>
[ValueConversion(typeof(double), typeof(int))]
public sealed class WidthToColumnsConverter : IValueConverter
{
    public double Breakpoint { get; set; } = 760;

    public int Columns { get; set; } = 2;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double width && width > 0 && width < Breakpoint ? 1 : Columns;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
