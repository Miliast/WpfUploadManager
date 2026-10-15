using System.Globalization;

namespace UploadManager.Core.Formatting;

public static class FileSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// Formats a byte count as a human readable size, e.g. <c>1,5 MB</c>.
    /// </summary>
    public static string Format(long bytes, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;

        if (bytes < 1024)
        {
            return string.Create(provider, $"{Math.Max(bytes, 0)} B");
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(provider, $"{value:0.#} {Units[unit]}");
    }
}
