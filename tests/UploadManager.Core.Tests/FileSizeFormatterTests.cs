using System.Globalization;
using UploadManager.Core.Formatting;

namespace UploadManager.Core.Tests;

public sealed class FileSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void Formats_human_readable_sizes(long bytes, string expected) =>
        Assert.Equal(expected, FileSizeFormatter.Format(bytes, CultureInfo.InvariantCulture));

    [Fact]
    public void Uses_culture_specific_decimal_separator() =>
        Assert.Equal("1,5 KB", FileSizeFormatter.Format(1536, CultureInfo.GetCultureInfo("de-DE")));
}
